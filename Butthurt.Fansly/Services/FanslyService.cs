using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Fansly.Models;

namespace Butthurt.Fansly.Services;

public enum FanslyState
{
    NotSetUp,
    Offline,
    Connecting,
    Connected,
    Error
}

/// <summary>A Fansly creator as the public account lookup returns it.</summary>
public record FanslyAccount(string Id, string Username, string DisplayName, string ChatRoomId, List<string> Tiers);

/// <summary>
/// Reads a creator's Fansly livestream chat. Fansly has no public developer API, so this talks to the same
/// endpoints the Fansly website uses (reverse engineered, not documented by Fansly, may change):
///
///   GET apiv3.fansly.com/api/v1/account?usernames=…   → account id, display name, streaming.channel.chatRoomId
///   wss://chatws.fansly.com/?v=3
///     out  {"t":1,"d":"{\"token\":\"…\"}"}            optional auth
///     out  {"t":46001,"d":"{\"chatRoomId\":\"…\"}"}  join the chatroom
///     out  "p" every 10s                             keep-alive
///     in   {"t":10000,"d":"{\"serviceId\":46,\"event\":\"{\\\"type\\\":10,\\\"chatRoomMessage\\\":{…}}\"}"}
///     in   {"t":10001,"d":[…]}                       several of the above
///     in   {"t":0,"d":"{\"code\":401,…}"}            error
/// "d" and "event" are JSON sent as strings. Tips are chat messages with a tip attachment (1/1000 dollar),
/// subscriptions are event type 53.
/// </summary>
public static class FanslyService
{
    private const string ApiBase = "https://apiv3.fansly.com/api/v1/";
    private static readonly Uri SocketUri = new("wss://chatws.fansly.com/?v=3");
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";

    private const int FrameTypeError = 0;
    private const int FrameTypeAuth = 1;
    private const int FrameTypeEvent = 10000;
    private const int FrameTypeBulkEvent = 10001;
    private const int FrameTypeJoinChatRoom = 46001;

    private const int EventTypeChatMessage = 10;
    private const int EventTypeSubAlert = 53;
    private const int AttachmentTypeTip = 7;

    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);

    /// <summary>Messages older than this when they arrive (e.g. history sent on join) never trigger anything.</summary>
    private static readonly TimeSpan MaxEventAge = TimeSpan.FromSeconds(60);

    private static readonly HttpClient Http = CreateClient();

    private static CancellationTokenSource? _socketCts;
    private static readonly SemaphoreSlim SendLock = new(1, 1);

    // Fansly can send the same message twice (bulk frames, reconnects). Nobody wants a double zap.
    private static readonly HashSet<string> SeenIds = new();
    private static readonly Queue<string> SeenOrder = new();

    public static FanslyState State { get; private set; } = FanslyState.Offline;
    public static string? LastError { get; private set; }

    /// <summary>Raised when the state changes. May fire on any thread.</summary>
    public static event EventHandler? StateChanged;

    /// <summary>Every event from the chat, matching a rule or not. May fire on any thread.</summary>
    public static event EventHandler<FanslyEvent>? EventReceived;

    private static FanslySettings Settings => FanslyPlugin.Settings;

    public static bool IsSetUp => !string.IsNullOrWhiteSpace(Settings.Username);
    public static string ChannelName => string.IsNullOrWhiteSpace(Settings.DisplayName) ? Settings.Username : Settings.DisplayName!;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(ApiBase), Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("Origin", "https://fansly.com");
        client.DefaultRequestHeaders.Referrer = new Uri("https://fansly.com/");
        return client;
    }

    #region Account

    /// <summary>Looks a creator up by username. Null when there's no such account or Fansly didn't answer.</summary>
    public static async Task<FanslyAccount?> LookUp(string username, CancellationToken token = default)
    {
        username = username.Trim().TrimStart('@');
        if (username.Length == 0) return null;

        using var response = await Http.GetAsync($"account?usernames={Uri.EscapeDataString(username)}&ngsw-bypass=true", token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Fansly answered {(int)response.StatusCode}");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("response", out var accounts) || accounts.ValueKind != JsonValueKind.Array) return null;

        var all = accounts.EnumerateArray().ToList();
        var account = all.FirstOrDefault(a => string.Equals(String(a, "username"), username, StringComparison.OrdinalIgnoreCase));
        if (account.ValueKind != JsonValueKind.Object)
        {
            if (all.Count == 0) return null;
            account = all[0];
        }

        var chatRoomId = "";
        if (account.TryGetProperty("streaming", out var streaming) && streaming.ValueKind == JsonValueKind.Object &&
            streaming.TryGetProperty("channel", out var channel) && channel.ValueKind == JsonValueKind.Object)
        {
            chatRoomId = Id(channel, "chatRoomId");
        }

        var tiers = new List<string>();
        if (account.TryGetProperty("subscriptionTiers", out var tierList) && tierList.ValueKind == JsonValueKind.Array)
        {
            tiers.AddRange(tierList.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.Object)
                .OrderBy(t => t.TryGetProperty("pos", out var pos) && pos.TryGetInt32(out var p) ? p : 0)
                .Select(t => String(t, "name").Trim())
                .Where(n => n.Length > 0));
        }

        var name = String(account, "username");
        var display = String(account, "displayName");
        return new FanslyAccount(Id(account, "id"), name, display.Length > 0 ? display : name, chatRoomId, tiers);
    }

    /// <summary>
    /// Saves the creator to listen to and looks up their chatroom. Returns an error message, or null when it worked.
    /// </summary>
    public static async Task<string?> SetChannel(string username)
    {
        username = username.Trim().TrimStart('@');
        if (username.Length == 0) return "Type a Fansly username first.";

        FanslyAccount? account;
        try
        {
            account = await LookUp(username);
        }
        catch (Exception e)
        {
            return $"Couldn't reach Fansly: {e.Message}";
        }

        if (account == null) return $"There's no Fansly account called {username}.";
        if (account.ChatRoomId.Length == 0) return $"{account.DisplayName} has no livestream chat yet. Set up streaming on Fansly once, then try again.";

        var changed = !string.Equals(Settings.ChatRoomId, account.ChatRoomId, StringComparison.Ordinal);
        Settings.Username = account.Username;
        Settings.AccountId = account.Id;
        Settings.ChatRoomId = account.ChatRoomId;
        Settings.DisplayName = account.DisplayName;
        Settings.KnownTiers = account.Tiers;
        SettingsService.Save();

        if (changed && _socketCts != null)
        {
            await Disconnect();
            await Connect();
        }
        else
        {
            SetState(State == FanslyState.NotSetUp ? FanslyState.Offline : State);
        }

        return null;
    }

    public static async Task ClearChannel()
    {
        await Disconnect();
        Settings.Username = "";
        Settings.AccountId = null;
        Settings.ChatRoomId = null;
        Settings.DisplayName = null;
        Settings.KnownTiers = new();
        SettingsService.Save();
        SetState(FanslyState.NotSetUp);
    }

    public static bool HasToken => !string.IsNullOrEmpty(Secrets.Unprotect(Settings.Token));

    public static void SetToken(string? token)
    {
        token = token?.Trim();
        Settings.Token = string.IsNullOrEmpty(token) ? null : Secrets.Protect(token);
        SettingsService.Save();
    }

    #endregion

    #region Chat socket

    public static Task Connect()
    {
        if (!IsSetUp || _socketCts != null) return Task.CompletedTask;

        var cts = _socketCts = new CancellationTokenSource();
        return Task.Run(() => SocketLoop(cts.Token));
    }

    public static Task Disconnect()
    {
        _socketCts?.Cancel();
        _socketCts = null;
        if (State is FanslyState.Connected or FanslyState.Connecting) SetState(FanslyState.Offline);
        return Task.CompletedTask;
    }

    private static async Task SocketLoop(CancellationToken token)
    {
        var failures = 0;

        while (!token.IsCancellationRequested)
        {
            SetState(FanslyState.Connecting);
            var connectedAt = DateTime.UtcNow;

            try
            {
                var roomId = await EnsureChatRoom(token);
                if (roomId == null) break;

                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Origin", "https://fansly.com");
                socket.Options.SetRequestHeader("User-Agent", UserAgent);
                await socket.ConnectAsync(SocketUri, token);

                if (Secrets.Unprotect(Settings.Token) is { Length: > 0 } session)
                {
                    await SendFrame(socket, FrameTypeAuth, new { token = session }, token);
                }

                await SendFrame(socket, FrameTypeJoinChatRoom, new { chatRoomId = roomId }, token);

                ActivityLog.Add($"Listening to {ChannelName}'s Fansly chat", LogLevel.Success);
                SetState(FanslyState.Connected);

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                var ping = PingLoop(socket, linked.Token);
                try
                {
                    await ReceiveLoop(socket, roomId, linked.Token);
                }
                finally
                {
                    linked.Cancel();
                    try { await ping; } catch { /* stopped with the socket */ }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                ActivityLog.Add($"Fansly connection problem: {e.Message}", LogLevel.Warning);
            }

            if (token.IsCancellationRequested) break;

            // A connection that stayed up a while starts the backoff over.
            failures = DateTime.UtcNow - connectedAt > TimeSpan.FromMinutes(5) ? 1 : failures + 1;
            SetState(FanslyState.Connecting, "Reconnecting…");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 << Math.Min(failures, 4), 30)), token).ContinueWith(_ => { });
        }
    }

    /// <summary>The chatroom id, looked up again when it's missing.</summary>
    private static async Task<string?> EnsureChatRoom(CancellationToken token)
    {
        if (!string.IsNullOrEmpty(Settings.ChatRoomId)) return Settings.ChatRoomId;

        var account = await LookUp(Settings.Username, token);
        if (account == null || account.ChatRoomId.Length == 0)
        {
            SetError(account == null
                ? $"There's no Fansly account called {Settings.Username}."
                : $"{account.DisplayName} has no livestream chat yet.");
            _socketCts = null;
            return null;
        }

        Settings.AccountId = account.Id;
        Settings.ChatRoomId = account.ChatRoomId;
        Settings.DisplayName = account.DisplayName;
        Settings.KnownTiers = account.Tiers;
        SettingsService.Save();
        return account.ChatRoomId;
    }

    private static async Task PingLoop(ClientWebSocket socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(PingInterval, token);
            await SendRaw(socket, "p", token);
        }
    }

    private static async Task ReceiveLoop(ClientWebSocket socket, string roomId, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            try
            {
                HandleFrame(text, roomId);
            }
            catch (Exception e)
            {
                ActivityLog.Add($"Couldn't read a Fansly message: {e.Message}", LogLevel.Warning);
            }
        }
    }

    private static Task SendFrame(ClientWebSocket socket, int type, object payload, CancellationToken token) =>
        SendRaw(socket, JsonSerializer.Serialize(new { t = type, d = JsonSerializer.Serialize(payload) }), token);

    private static async Task SendRaw(ClientWebSocket socket, string text, CancellationToken token)
    {
        await SendLock.WaitAsync(token);
        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, token);
        }
        finally
        {
            SendLock.Release();
        }
    }

    #endregion

    #region Reading frames

    /// <summary>Turns one websocket frame into events. Public so it can be fed recorded frames in tests.</summary>
    public static void HandleFrame(string text, string roomId)
    {
        // Keep-alive replies and anything else that isn't a JSON object.
        if (text.Length == 0 || text[0] != '{') return;

        using var frame = JsonDocument.Parse(text);
        var root = frame.RootElement;
        if (!root.TryGetProperty("t", out var typeEl) || !typeEl.TryGetInt32(out var type)) return;
        if (!root.TryGetProperty("d", out var data)) return;

        switch (type)
        {
            case FrameTypeEvent:
                HandleServiceEvent(data, roomId);
                break;

            case FrameTypeBulkEvent:
                using (var bulk = ParseMaybeString(data))
                {
                    if (bulk.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in bulk.RootElement.EnumerateArray()) HandleServiceEvent(item, roomId);
                    }
                }
                break;

            case FrameTypeError:
                var error = data.ToString();
                var unauthorized = error.Contains("401");
                var message = unauthorized && !HasToken
                    ? "Fansly wants a login to read this chat. Add your Fansly session token on the Fansly page."
                    : unauthorized ? "Fansly didn't accept the session token. Copy a fresh one from fansly.com."
                    : $"Fansly chat error: {Shorten(error)}";
                ActivityLog.Add(message, LogLevel.Warning);
                LastError = message;
                StateChanged?.Invoke(null, EventArgs.Empty);
                break;
        }
    }

    private static void HandleServiceEvent(JsonElement data, string roomId)
    {
        using var service = ParseMaybeString(data);
        if (service.RootElement.ValueKind != JsonValueKind.Object || !service.RootElement.TryGetProperty("event", out var eventEl)) return;

        using var evt = ParseMaybeString(eventEl);
        var root = evt.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeEl) || !typeEl.TryGetInt32(out var type)) return;

        switch (type)
        {
            case EventTypeChatMessage when root.TryGetProperty("chatRoomMessage", out var chat):
                HandleChatMessage(chat, roomId);
                break;

            case EventTypeSubAlert when root.TryGetProperty("subAlert", out var sub):
                HandleSubAlert(sub, roomId);
                break;
        }
    }

    private static void HandleChatMessage(JsonElement chat, string roomId)
    {
        if (Id(chat, "chatRoomId") != roomId || IsOld(chat) || !FirstTime(String(chat, "id"))) return;

        var name = UserName(chat);
        var content = String(chat, "content").Trim();
        var level = ChatterLevelOf(chat, out var tierName);
        var tip = TipAmount(chat);

        if (tip > 0)
        {
            Handle(new FanslyEvent(FanslyTrigger.Tip, name)
            {
                TipDollars = Math.Round(tip / 1000.0, 2),
                Message = content,
                TierName = tierName,
                ChatterLevel = level
            });
        }

        // Like on Twitch, a message with a tip can also be a chat command.
        if (content.Length > 0)
        {
            Handle(new FanslyEvent(FanslyTrigger.ChatCommand, name) { Message = content, TierName = tierName, ChatterLevel = level });
        }
    }

    private static void HandleSubAlert(JsonElement sub, string roomId)
    {
        if (Id(sub, "chatRoomId") != roomId || IsOld(sub)) return;

        var id = String(sub, "id");
        if (!FirstTime(id.Length > 0 ? "sub:" + id : "")) return;

        Handle(new FanslyEvent(FanslyTrigger.Subscription, UserName(sub))
        {
            TierName = String(sub, "subscriptionTierName"),
            Months = sub.TryGetProperty("subscriptionStreak", out var s) && s.TryGetInt32(out var streak) ? Math.Max(streak, 1) : 1,
            ChatterLevel = FanslyChatterLevel.Subscribers
        });
    }

    /// <summary>
    /// chatRoomMessage.metadata is JSON in a string, e.g. {"senderIsCreator":false,"senderIsFollowing":true,
    /// "senderSubscription":{"tierName":"Plus"}}. senderSubscription is left out for non-subscribers.
    /// </summary>
    private static FanslyChatterLevel ChatterLevelOf(JsonElement chat, out string tierName)
    {
        tierName = "";
        var isCreator = Settings.AccountId is { Length: > 0 } owner && Id(chat, "senderId") == owner;

        if (chat.TryGetProperty("metadata", out var metaEl) &&
            metaEl.ValueKind is JsonValueKind.Object or JsonValueKind.String &&
            !(metaEl.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(metaEl.GetString())))
        {
            try
            {
                using var meta = ParseMaybeString(metaEl);
                var root = meta.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("senderSubscription", out var subEl) && subEl.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                    {
                        using var sub = ParseMaybeString(subEl);
                        if (sub.RootElement.ValueKind == JsonValueKind.Object) tierName = String(sub.RootElement, "tierName");
                    }

                    if (Bool(root, "senderIsCreator")) isCreator = true;
                    if (isCreator) return FanslyChatterLevel.Creator;
                    if (tierName.Length > 0) return FanslyChatterLevel.Subscribers;
                    if (Bool(root, "senderIsFollowing")) return FanslyChatterLevel.Followers;
                }
            }
            catch (JsonException)
            {
                // Unknown metadata, treat as an ordinary viewer.
            }
        }

        return isCreator ? FanslyChatterLevel.Creator : FanslyChatterLevel.Everyone;
    }

    /// <summary>Tip amount in Fansly's units (1/1000 dollar), 0 when the message has no tip.</summary>
    private static long TipAmount(JsonElement chat)
    {
        if (!chat.TryGetProperty("attachments", out var attachments) || attachments.ValueKind != JsonValueKind.Array) return 0;

        foreach (var attachment in attachments.EnumerateArray())
        {
            if (!attachment.TryGetProperty("contentType", out var ct) || !ct.TryGetInt32(out var contentType) ||
                contentType != AttachmentTypeTip || !attachment.TryGetProperty("metadata", out var metaEl))
            {
                continue;
            }

            using var meta = ParseMaybeString(metaEl);
            if (meta.RootElement.ValueKind == JsonValueKind.Object && meta.RootElement.TryGetProperty("amount", out var amountEl))
            {
                if (amountEl.TryGetInt64(out var amount)) return amount;
                if (amountEl.ValueKind == JsonValueKind.String && long.TryParse(amountEl.GetString(), out amount)) return amount;
            }
        }

        return 0;
    }

    private static bool IsOld(JsonElement element) =>
        element.TryGetProperty("createdAt", out var value) && value.TryGetInt64(out var ms) && ms > 0 &&
        DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime > MaxEventAge;

    private static bool FirstTime(string id)
    {
        if (id.Length == 0) return true;

        lock (SeenIds)
        {
            if (!SeenIds.Add(id)) return false;
            SeenOrder.Enqueue(id);
            while (SeenOrder.Count > 500) SeenIds.Remove(SeenOrder.Dequeue());
            return true;
        }
    }

    private static string UserName(JsonElement element)
    {
        var display = String(element, "displayname");
        if (display.Length > 0) return display;
        var user = String(element, "username");
        return user.Length > 0 ? user : "Anonymous";
    }

    #endregion

    #region Rules

    /// <summary>Runs an event through the rules. Public so tests (and the Test buttons) can feed events in.</summary>
    public static void Handle(FanslyEvent e)
    {
        var rules = Settings.Rules ?? new List<FanslyRule>();
        // Vibration rules sit out while the Toys plugin is off.
        var matching = PluginManager.ToysEnabled
            ? rules.Where(r => FanslyRules.Matches(r, e) && FanslyRules.TryUseCooldown(r)).ToList()
            : new List<FanslyRule>();

        foreach (var rule in matching)
        {
            e.ReportResult(ButthurtService.IsConnected);
            _ = ButthurtService.Vibrate(rule.Intensity, FanslyRules.DurationFor(rule, e));
        }

        // Other plugins (e.g. PiShock) run their own rules here and report back through e.ReportResult.
        EventReceived?.Invoke(null, e);

        // Chat is noisy, only log messages that actually did something.
        if (e.Type != FanslyTrigger.ChatCommand || e.AnySucceeded || e.OnlyFailed)
        {
            ActivityLog.Add($"Fansly: {FanslyRules.Describe(e)}", LogLevel.Event);
        }
    }

    /// <summary>Plays a rule as if it matched, for the Test button.</summary>
    public static void Test(FanslyRule rule)
    {
        ActivityLog.Add($"Test: {rule.Name}", LogLevel.Event);
        _ = ButthurtService.Vibrate(rule.Intensity, FanslyRules.DurationFor(rule, FanslyRules.SampleFor(rule)));
    }

    #endregion

    #region Helpers

    /// <summary>Fansly nests JSON as strings; accept either a string holding JSON or the JSON itself.</summary>
    private static JsonDocument ParseMaybeString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? JsonDocument.Parse(element.GetString() is { Length: > 0 } s ? s : "null")
            : JsonDocument.Parse(element.GetRawText());

    private static string String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Fansly ids are 64-bit snowflakes, usually sent as strings.</summary>
    private static string Id(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number when value.TryGetUInt64(out var id) => id.ToString(),
            _ => ""
        };
    }

    private static string Shorten(string text) => text.Length > 160 ? text[..160] + "…" : text;

    private static void SetState(FanslyState state, string? error = null)
    {
        State = state;
        if (state == FanslyState.Connected) LastError = null;
        if (error != null) LastError = error;
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetError(string message)
    {
        ActivityLog.Add(message, LogLevel.Error);
        SetState(FanslyState.Error, message);
    }

    #endregion
}
