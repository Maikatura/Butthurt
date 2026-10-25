using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Butthurt.Contracts.Services;

/// <summary>One line of the overlay's queue. Kind picks the icon: grow, shrink, parameter, avatar.</summary>
public record OverlayQueueItem(string Label, string Detail, string Kind, long? ExpiresAtUnixMs);

/// <summary>
/// The stream overlay: a small web server on this PC (localhost only) serving a page for an OBS Browser
/// source. The page gets live updates (announcements and the queue) over server-sent events.
/// A plain TcpListener instead of HttpListener, so it needs no admin rights or URL reservations.
/// </summary>
public static class OverlayService
{
    private static readonly object _lock = new();
    private static readonly List<Stream> _clients = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static int _runningPort;
    private static IReadOnlyList<OverlayQueueItem> _queue = Array.Empty<OverlayQueueItem>();

    public static bool IsRunning => _listener != null;
    public static string? LastError { get; private set; }
    public static string Url => $"http://localhost:{SettingsService.Current.Overlay.Port}/";

    public static int ConnectedClients
    {
        get
        {
            lock (_lock) return _clients.Count;
        }
    }

    /// <summary>Raised when the server starts/stops or a browser source connects. May fire on any thread.</summary>
    public static event EventHandler? StatusChanged;

    /// <summary>Starts, stops or restarts the server to match the settings, and tells open pages about changes.</summary>
    public static void ApplySettings()
    {
        var settings = SettingsService.Current.Overlay;

        if (!settings.Enabled)
        {
            Stop();
            return;
        }

        if (IsRunning && _runningPort != settings.Port) Stop();
        if (!IsRunning) Start(settings.Port);

        Broadcast(SettingsMessage());
        Broadcast(QueueMessage());
    }

    /// <summary>Shows an announcement on the overlay (if it's on and announcements are shown).</summary>
    public static void Alert(string text)
    {
        var settings = SettingsService.Current.Overlay;
        if (!IsRunning || !settings.ShowAlerts || string.IsNullOrWhiteSpace(text)) return;

        Broadcast(JsonSerializer.Serialize(new { type = "alert", text, seconds = Math.Clamp(settings.AlertSeconds, 1, 60) }));
    }

    /// <summary>Replaces the queue shown on the overlay. Countdowns run in the page itself.</summary>
    public static void SetQueue(IEnumerable<OverlayQueueItem> items)
    {
        _queue = items.ToList();
        if (IsRunning) Broadcast(QueueMessage());
    }

    private static string SettingsMessage()
    {
        var s = SettingsService.Current.Overlay;
        return JsonSerializer.Serialize(new
        {
            type = "settings",
            showQueue = s.ShowQueue,
            showAlerts = s.ShowAlerts,
            alertPosition = Kebab(s.AlertPosition),
            queuePosition = Kebab(s.QueuePosition),
            scale = Math.Clamp(s.Scale, 0.3, 4)
        });
    }

    // TopCenter → "top-center", the names the page (and its URL options) use.
    private static string Kebab(Models.OverlayPosition position) => position switch
    {
        Models.OverlayPosition.TopLeft => "top-left",
        Models.OverlayPosition.TopCenter => "top-center",
        Models.OverlayPosition.TopRight => "top-right",
        Models.OverlayPosition.MiddleLeft => "middle-left",
        Models.OverlayPosition.Center => "center",
        Models.OverlayPosition.MiddleRight => "middle-right",
        Models.OverlayPosition.BottomLeft => "bottom-left",
        Models.OverlayPosition.BottomCenter => "bottom-center",
        _ => "bottom-right"
    };

    private static string QueueMessage() => JsonSerializer.Serialize(new
    {
        type = "queue",
        items = _queue.Select(i => new { label = i.Label, detail = i.Detail, kind = i.Kind, expiresAt = i.ExpiresAtUnixMs })
    });

    private static void Start(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _listener = listener;
            _runningPort = port;
            _cts = new CancellationTokenSource();
            LastError = null;

            _ = Task.Run(() => AcceptLoop(listener, _cts.Token));
            _ = Task.Run(() => KeepAliveLoop(_cts.Token));
            ActivityLog.Add($"Stream overlay running at {Url}", LogLevel.Success);
        }
        catch (Exception e)
        {
            _listener = null;
            LastError = e is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                ? $"Port {port} is already used by another program, pick another one."
                : e.Message;
            ActivityLog.Add($"Couldn't start the stream overlay: {LastError}", LogLevel.Error);
        }

        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Stop()
    {
        if (!IsRunning) return;

        _cts?.Cancel();
        _cts = null;
        _listener?.Stop();
        _listener = null;

        lock (_lock)
        {
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }

        ActivityLog.Add("Stream overlay stopped");
        StatusChanged?.Invoke(null, EventArgs.Empty);
    }

    private static async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClient(client, token), token);
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Stream overlay stopped accepting connections: {e.Message}", LogLevel.Warning);
        }
    }

    private static async Task HandleClient(TcpClient client, CancellationToken token)
    {
        var keepOpen = false;
        var stream = client.GetStream();

        try
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(token) ?? "";
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token)))
            {
                // Headers don't matter to us.
            }

            var parts = requestLine.Split(' ');
            var path = parts.Length > 1 ? parts[1].Split('?')[0] : "/";

            switch (path)
            {
                case "/":
                case "/index.html":
                    await WriteResponse(stream, "200 OK", "text/html; charset=utf-8", OverlayPage.Html, token);
                    break;

                case "/events":
                    await WriteHead(stream, "200 OK", "text/event-stream", null, token);

                    // Bring the new page up to date first, then it gets every broadcast.
                    await WriteEvent(stream, SettingsMessage(), token);
                    await WriteEvent(stream, QueueMessage(), token);
                    lock (_lock) _clients.Add(stream);
                    keepOpen = true;
                    StatusChanged?.Invoke(null, EventArgs.Empty);
                    break;

                default:
                    await WriteResponse(stream, "404 Not Found", "text/plain", "Not found", token);
                    break;
            }
        }
        catch
        {
            // Browser went away mid-request.
        }
        finally
        {
            if (!keepOpen) client.Dispose();
        }
    }

    private static async Task WriteResponse(Stream stream, string status, string contentType, string body, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        await WriteHead(stream, status, contentType, bytes.Length, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task WriteHead(Stream stream, string status, string contentType, int? length, CancellationToken token)
    {
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append("Cache-Control: no-cache\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append(length is { } l ? $"Content-Length: {l}\r\nConnection: close\r\n" : "Connection: keep-alive\r\n")
            .Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token);
        await stream.FlushAsync(token);
    }

    private static async Task WriteEvent(Stream stream, string json, CancellationToken token)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), token);
        await stream.FlushAsync(token);
    }

    private static void Broadcast(string json)
    {
        List<Stream> clients;
        lock (_lock) clients = _clients.ToList();

        var bytes = Encoding.UTF8.GetBytes(json.StartsWith(':') ? json : $"data: {json}\n\n");
        foreach (var client in clients)
        {
            try
            {
                lock (client)
                {
                    client.Write(bytes);
                    client.Flush();
                }
            }
            catch
            {
                // The browser source was closed (OBS scene switch, refresh…).
                lock (_lock) _clients.Remove(client);
                client.Dispose();
                StatusChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    // A comment line every 15s keeps idle connections from being dropped and finds closed ones.
    private static async Task KeepAliveLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token);
                Broadcast(": ping\n\n");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
