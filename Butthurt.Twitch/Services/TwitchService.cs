using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Butthurt.Contracts.Plugins;
using Butthurt.Contracts.Services;
using Butthurt.Twitch.Models;

namespace Butthurt.Twitch.Services;

public enum TwitchState
{
    LoggedOut,
    WaitingForLogin,
    Connecting,
    Connected,
    Error
}

public record TwitchLoginCode(string UserCode, string VerificationUri);

/// <summary>
/// Logs in to Twitch with the device code flow (no password ever touches the app) and listens to the
/// channel through EventSub over a websocket. Matching events are turned into vibrations by the user's rules.
/// </summary>
public static class TwitchService
{
    /// <summary>
    /// Client ID of the Butthurt Twitch app. Register a "Public" app at https://dev.twitch.tv/console/apps
    /// (redirect URL http://localhost) and paste its Client ID here so streamers don't need their own.
    /// It can also be set per user on the Twitch page.
    /// </summary>
    public const string DefaultClientId = "";

    private const string EventSubUrl = "wss://eventsub.wss.twitch.tv/ws";

    private static readonly string[] Scopes =
    {
        "bits:read",
        "channel:read:subscriptions",
        "channel:read:redemptions",
        // Lets Butthurt create its own channel point rewards and fulfil or refund their redemptions.
        "channel:manage:redemptions",
        // Lets rules post their announcements in chat (only when turned on).
        "user:write:chat",
        "moderator:read:followers",
        "user:read:chat"
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static CancellationTokenSource? _loginCts;
    private static CancellationTokenSource? _socketCts;

    public static TwitchState State { get; private set; } = TwitchState.LoggedOut;
    public static TwitchLoginCode? PendingLogin { get; private set; }
    public static string? LastError { get; private set; }

    /// <summary>Raised when the state changes. May fire on any thread.</summary>
    public static event EventHandler? StateChanged;

    /// <summary>Every event from the channel, matching a rule or not. May fire on any thread.</summary>
    public static event EventHandler<TwitchEvent>? EventReceived;

    private static TwitchSettings Settings => TwitchPlugin.Settings;

    public static string ClientId =>
        !string.IsNullOrWhiteSpace(Settings.ClientId) ? Settings.ClientId.Trim() : DefaultClientId;

    public static bool HasClientId => !string.IsNullOrWhiteSpace(ClientId);
    public static bool IsLoggedIn => !string.IsNullOrEmpty(Settings.AccessToken);
    public static string? ChannelName => Settings.Login;

    #region Login

    /// <summary>
    /// Starts the device code login. <see cref="PendingLogin"/> holds the code the user has to enter
    /// at twitch.tv/activate; this finishes once they approve (or the code expires).
    /// </summary>
    public static async Task<bool> Login()
    {
        if (!HasClientId)
        {
            SetError("No Twitch Client ID set. Add one on the Twitch page first.");
            return false;
        }

        CancelLogin();
        var cts = _loginCts = new CancellationTokenSource();

        try
        {
            using var device = await PostForm("https://id.twitch.tv/oauth2/device", new()
            {
                ["client_id"] = ClientId,
                ["scopes"] = string.Join(' ', Scopes)
            }, cts.Token);

            if (!device.Ok) throw new InvalidOperationException(device.Error);

            var root = device.Json!.RootElement;
            var deviceCode = root.GetProperty("device_code").GetString()!;
            var interval = root.GetProperty("interval").GetInt32();
            var expires = DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32());

            PendingLogin = new TwitchLoginCode(
                root.GetProperty("user_code").GetString()!,
                root.GetProperty("verification_uri").GetString()!);
            SetState(TwitchState.WaitingForLogin);

            while (DateTime.UtcNow < expires)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);

                using var token = await PostForm("https://id.twitch.tv/oauth2/token", new()
                {
                    ["client_id"] = ClientId,
                    ["scopes"] = string.Join(' ', Scopes),
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                }, cts.Token);

                if (token.Ok)
                {
                    StoreTokens(token.Json!.RootElement);
                    PendingLogin = null;

                    if (!await LoadUser(cts.Token)) return false;

                    ActivityLog.Add($"Logged in to Twitch as {Settings.Login}", LogLevel.Success);
                    _ = Connect();
                    return true;
                }

                if (token.Error.Contains("authorization_pending")) continue;
                if (token.Error.Contains("slow_down"))
                {
                    interval += 5;
                    continue;
                }

                throw new InvalidOperationException(token.Error);
            }

            throw new InvalidOperationException("The login code expired, please try again.");
        }
        catch (OperationCanceledException)
        {
            PendingLogin = null;
            SetState(IsLoggedIn ? State : TwitchState.LoggedOut);
            return false;
        }
        catch (Exception e)
        {
            PendingLogin = null;
            SetError($"Twitch login failed: {e.Message}");
            return false;
        }
    }

    public static void CancelLogin()
    {
        _loginCts?.Cancel();
        _loginCts = null;
    }

    public static async Task Logout()
    {
        CancelLogin();
        await Disconnect();

        var token = Unprotect(Settings.AccessToken);
        if (token != null)
        {
            try
            {
                using var _ = await PostForm("https://id.twitch.tv/oauth2/revoke", new()
                {
                    ["client_id"] = ClientId,
                    ["token"] = token
                }, CancellationToken.None);
            }
            catch
            {
                // Revoking is a courtesy, the token is forgotten either way.
            }
        }

        Settings.AccessToken = null;
        Settings.RefreshToken = null;
        Settings.UserId = null;
        Settings.Login = null;
        MissingScopes = Array.Empty<string>();
        SettingsService.Save();

        ActivityLog.Add("Logged out of Twitch");
        LastError = null;
        SetState(TwitchState.LoggedOut);
    }

    private static void StoreTokens(JsonElement root)
    {
        Settings.AccessToken = Protect(root.GetProperty("access_token").GetString());
        if (root.TryGetProperty("refresh_token", out var refresh))
        {
            Settings.RefreshToken = Protect(refresh.GetString());
        }

        SettingsService.Save();
    }

    /// <summary>Looks up who logged in. Doubles as a check that the token still works.</summary>
    private static async Task<bool> LoadUser(CancellationToken token)
    {
        var accessToken = await GetValidToken(token);
        if (accessToken == null) return false;

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {accessToken}");
        using var response = await Http.SendAsync(request, token);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));

        Settings.UserId = json.RootElement.GetProperty("user_id").GetString();
        Settings.Login = json.RootElement.GetProperty("login").GetString();
        SettingsService.Save();

        // Logins from older versions don't have every permission (e.g. managing redeems).
        var granted = json.RootElement.TryGetProperty("scopes", out var scopes) && scopes.ValueKind == JsonValueKind.Array
            ? scopes.EnumerateArray().Select(s => s.GetString()).ToHashSet()
            : new HashSet<string?>();
        MissingScopes = Scopes.Where(s => !granted.Contains(s)).ToList();
        if (MissingScopes.Count > 0)
        {
            ActivityLog.Add($"Twitch login is missing permissions ({string.Join(", ", MissingScopes)}). Log out and in again to fix it.", LogLevel.Warning);
        }

        return true;
    }

    /// <summary>Permissions Butthurt asks for that the current login doesn't have. Logging in again fixes it.</summary>
    public static IReadOnlyList<string> MissingScopes { get; private set; } = Array.Empty<string>();

    public static bool CanManageRedeems => IsLoggedIn && !MissingScopes.Contains("channel:manage:redemptions");

    public static bool CanSendChat => IsLoggedIn && !MissingScopes.Contains("user:write:chat");

    /// <summary>Posts a message in your own chat, as you. Returns an error message, or null when it worked.</summary>
    public static async Task<string?> SendChatMessage(string message)
    {
        if (!CanSendChat) return "Log out of Twitch and in again so Butthurt may post in chat.";
        if (Settings.UserId == null) return "Not connected to Twitch.";

        // Twitch's limit is 500 characters.
        if (message.Length > 500) message = message[..497] + "...";

        try
        {
            var body = JsonSerializer.Serialize(new { broadcaster_id = Settings.UserId, sender_id = Settings.UserId, message });
            using var response = await Helix(HttpMethod.Post, "chat/messages", body, CancellationToken.None);
            if (response.IsSuccessStatusCode) return null;
            return TwitchMessage(await response.Content.ReadAsStringAsync()) ?? $"Twitch said {(int)response.StatusCode}";
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Returns a working access token, refreshing it when Twitch says it expired.</summary>
    private static async Task<string?> GetValidToken(CancellationToken token)
    {
        var accessToken = Unprotect(Settings.AccessToken);
        if (accessToken == null) return null;

        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {accessToken}");
            using var response = await Http.SendAsync(request, token);
            if (response.IsSuccessStatusCode) return accessToken;
            if (response.StatusCode != HttpStatusCode.Unauthorized) return accessToken;
        }

        return await RefreshToken(token);
    }

    private static async Task<string?> RefreshToken(CancellationToken token)
    {
        var refreshToken = Unprotect(Settings.RefreshToken);
        if (refreshToken != null)
        {
            using var refreshed = await PostForm("https://id.twitch.tv/oauth2/token", new()
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }, token);

            if (refreshed.Ok)
            {
                StoreTokens(refreshed.Json!.RootElement);
                return Unprotect(Settings.AccessToken);
            }
        }

        Settings.AccessToken = null;
        Settings.RefreshToken = null;
        SettingsService.Save();
        SetError("Your Twitch login expired, please log in again.");
        return null;
    }

    #endregion

    #region EventSub

    public static Task Connect()
    {
        if (!IsLoggedIn || _socketCts != null) return Task.CompletedTask;

        var cts = _socketCts = new CancellationTokenSource();
        return Task.Run(() => SocketLoop(cts.Token));
    }

    public static Task Disconnect()
    {
        _socketCts?.Cancel();
        _socketCts = null;
        if (State is TwitchState.Connected or TwitchState.Connecting)
        {
            SetState(IsLoggedIn ? TwitchState.Error : TwitchState.LoggedOut, "Disconnected");
        }

        return Task.CompletedTask;
    }

    private static async Task SocketLoop(CancellationToken token)
    {
        var url = EventSubUrl;
        var failures = 0;

        while (!token.IsCancellationRequested)
        {
            SetState(TwitchState.Connecting);
            string? reconnectUrl = null;

            try
            {
                // Also refreshes which permissions the login has.
                if (failures == 0 && !await LoadUser(token)) break;

                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(url), token);

                // Twitch sends a keepalive at least this often, silence for longer means the connection died.
                var keepalive = TimeSpan.FromSeconds(30);

                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(keepalive + TimeSpan.FromSeconds(10));

                    var text = await ReceiveText(socket, timeout.Token);
                    if (text == null) break;

                    using var json = JsonDocument.Parse(text);
                    var metadata = json.RootElement.GetProperty("metadata");
                    var payload = json.RootElement.GetProperty("payload");

                    switch (metadata.GetProperty("message_type").GetString())
                    {
                        case "session_welcome":
                            var session = payload.GetProperty("session");
                            if (session.TryGetProperty("keepalive_timeout_seconds", out var seconds) &&
                                seconds.ValueKind == JsonValueKind.Number)
                            {
                                keepalive = TimeSpan.FromSeconds(seconds.GetInt32());
                            }

                            // After a session_reconnect the subscriptions move over by themselves.
                            if (url == EventSubUrl)
                            {
                                await Subscribe(session.GetProperty("id").GetString()!, token);
                            }

                            failures = 0;
                            ActivityLog.Add($"Listening to twitch.tv/{Settings.Login}", LogLevel.Success);
                            SetState(TwitchState.Connected);
                            RequestRewardSync();
                            break;

                        case "session_reconnect":
                            reconnectUrl = payload.GetProperty("session").GetProperty("reconnect_url").GetString();
                            break;

                        case "notification":
                            HandleNotification(payload);
                            break;

                        case "revocation":
                            var type = payload.GetProperty("subscription").GetProperty("type").GetString();
                            ActivityLog.Add($"Twitch stopped sending {type} events (permission removed?)", LogLevel.Warning);
                            break;
                    }

                    if (reconnectUrl != null) break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                failures++;
                ActivityLog.Add($"Twitch connection problem: {e.Message}", LogLevel.Warning);
            }

            if (token.IsCancellationRequested || !IsLoggedIn) break;

            if (reconnectUrl != null)
            {
                url = reconnectUrl;
                continue;
            }

            // Fresh connection: new session, so we have to subscribe again.
            url = EventSubUrl;
            SetState(TwitchState.Connecting, "Reconnecting…");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 << Math.Min(failures, 4), 30)), token)
                .ContinueWith(_ => { });
        }

        if (!IsLoggedIn) SetState(TwitchState.LoggedOut);
    }

    private static async Task<string?> ReceiveText(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    private static async Task Subscribe(string sessionId, CancellationToken token)
    {
        var broadcaster = Settings.UserId!;
        var subscriptions = new (string Type, string Version, object Condition)[]
        {
            ("channel.cheer", "1", new { broadcaster_user_id = broadcaster }),
            ("channel.subscribe", "1", new { broadcaster_user_id = broadcaster }),
            ("channel.subscription.message", "1", new { broadcaster_user_id = broadcaster }),
            ("channel.subscription.gift", "1", new { broadcaster_user_id = broadcaster }),
            ("channel.channel_points_custom_reward_redemption.add", "1", new { broadcaster_user_id = broadcaster }),
            ("channel.follow", "2", new { broadcaster_user_id = broadcaster, moderator_user_id = broadcaster }),
            ("channel.raid", "1", new { to_broadcaster_user_id = broadcaster }),
            ("channel.chat.message", "1", new { broadcaster_user_id = broadcaster, user_id = broadcaster }),
        };

        foreach (var (type, version, condition) in subscriptions)
        {
            var body = JsonSerializer.Serialize(new
            {
                type,
                version,
                condition,
                transport = new { method = "websocket", session_id = sessionId }
            });

            using var response = await Helix(HttpMethod.Post, "eventsub/subscriptions", body, token);
            if (!response.IsSuccessStatusCode)
            {
                // Channel points need affiliate/partner, the rest should always work.
                var reason = await response.Content.ReadAsStringAsync(token);
                ActivityLog.Add($"Couldn't listen to {type}: {(int)response.StatusCode} {Shorten(reason)}", LogLevel.Warning);
            }
        }
    }

    private static void HandleNotification(JsonElement payload)
    {
        var type = payload.GetProperty("subscription").GetProperty("type").GetString();
        var e = payload.GetProperty("event");

        TwitchEvent? twitchEvent = type switch
        {
            "channel.cheer" => new TwitchEvent(TwitchTrigger.Bits, UserOrAnonymous(e, "user_name"))
            {
                Amount = e.GetProperty("bits").GetInt32(),
                Message = String(e, "message")
            },
            // Gifted subs also arrive here once per recipient, the gift event covers those.
            "channel.subscribe" when !e.GetProperty("is_gift").GetBoolean() =>
                new TwitchEvent(TwitchTrigger.Subscription, UserOrAnonymous(e, "user_name"))
                {
                    Tier = Tier(e),
                    Amount = 1
                },
            "channel.subscription.message" => new TwitchEvent(TwitchTrigger.Subscription, UserOrAnonymous(e, "user_name"))
            {
                Tier = Tier(e),
                IsResub = true,
                Amount = e.TryGetProperty("cumulative_months", out var months) && months.ValueKind == JsonValueKind.Number
                    ? months.GetInt32()
                    : 0,
                Message = e.TryGetProperty("message", out var message) ? String(message, "text") : ""
            },
            "channel.subscription.gift" => new TwitchEvent(TwitchTrigger.GiftSub, UserOrAnonymous(e, "user_name"))
            {
                Tier = Tier(e),
                Amount = e.GetProperty("total").GetInt32()
            },
            "channel.channel_points_custom_reward_redemption.add" =>
                new TwitchEvent(TwitchTrigger.Redeem, UserOrAnonymous(e, "user_name"))
                {
                    RewardTitle = String(e.GetProperty("reward"), "title"),
                    RewardId = String(e.GetProperty("reward"), "id"),
                    RedemptionId = String(e, "id"),
                    Message = String(e, "user_input")
                },
            "channel.follow" => new TwitchEvent(TwitchTrigger.Follow, UserOrAnonymous(e, "user_name")),
            "channel.raid" => new TwitchEvent(TwitchTrigger.Raid, UserOrAnonymous(e, "from_broadcaster_user_name"))
            {
                Amount = e.GetProperty("viewers").GetInt32()
            },
            "channel.chat.message" => new TwitchEvent(TwitchTrigger.ChatCommand, UserOrAnonymous(e, "chatter_user_name"))
            {
                Message = String(e.GetProperty("message"), "text"),
                ChatterLevel = ChatterLevelFrom(e)
            },
            _ => null
        };

        if (twitchEvent != null) Handle(twitchEvent);
    }

    #endregion

    #region Rules

    /// <summary>Runs an event through the rules. Public so other sources (or tests) can feed events in.</summary>
    public static void Handle(TwitchEvent e)
    {
        var rules = Settings.Rules ?? new List<TwitchRule>();
        // The vibration rules sit out while toy features are off (they don't count for refunds either).
        var matching = PluginManager.ToysEnabled
            ? rules.Where(r => TwitchRules.Matches(r, e) && TwitchRules.TryUseCooldown(r)).ToList()
            : new List<TwitchRule>();

        foreach (var rule in matching)
        {
            e.ReportResult(ButthurtService.IsConnected);
            _ = ButthurtService.Vibrate(rule.Intensity, TwitchRules.DurationFor(rule, e));
        }

        // Other plugins (e.g. VRChat) run their own rules here and report back through e.ReportResult.
        EventReceived?.Invoke(null, e);

        // Chat is noisy, only log messages that actually did something.
        if (e.Type != TwitchTrigger.ChatCommand || e.AnySucceeded || e.OnlyFailed)
        {
            ActivityLog.Add($"Twitch: {TwitchRules.Describe(e)}", LogLevel.Event);
        }

        if (e.Type == TwitchTrigger.Redeem) _ = CompleteRedemption(e);
    }

    /// <summary>
    /// For rewards Butthurt created: fulfil the redemption when something reacted, refund it when everything
    /// that tried failed. Redemptions nothing reacted to stay in the queue for the streamer to handle.
    /// </summary>
    private static async Task CompleteRedemption(TwitchEvent e)
    {
        if (e.RedemptionId.Length == 0 || Settings.ManagedRewards.All(r => r.Id != e.RewardId)) return;
        if (!e.AnySucceeded && !e.OnlyFailed) return;

        var status = e.AnySucceeded ? "FULFILLED" : "CANCELED";
        try
        {
            using var response = await Helix(new HttpMethod("PATCH"),
                $"channel_points/custom_rewards/redemptions?broadcaster_id={Settings.UserId}&reward_id={e.RewardId}&id={e.RedemptionId}",
                JsonSerializer.Serialize(new { status }), CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                ActivityLog.Add($"Couldn't update the {e.RewardTitle} redemption: {(int)response.StatusCode}", LogLevel.Warning);
            }
            else if (status == "CANCELED")
            {
                ActivityLog.Add($"Refunded {e.UserName}'s {e.RewardTitle}, nothing could react to it", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            ActivityLog.Add($"Couldn't update the {e.RewardTitle} redemption: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>Plays a rule as if it matched, for the Test button.</summary>
    public static void Test(TwitchRule rule)
    {
        var sample = TwitchRules.SampleFor(rule);
        ActivityLog.Add($"Test: {rule.Name}", LogLevel.Event);
        _ = ButthurtService.Vibrate(rule.Intensity, TwitchRules.DurationFor(rule, sample));
    }

    /// <summary>
    /// Creates a channel point reward owned by Butthurt. Only rewards created this way can be fulfilled
    /// or refunded automatically (Twitch's rule, not ours). Returns an error message, or null when it worked.
    /// </summary>
    public static async Task<string?> CreateReward(string title, int cost, bool isEnabled = true, bool ownedByRules = false)
    {
        if (!CanManageRedeems) return "Log out and in again so Butthurt may manage redeems.";

        try
        {
            using var response = await Helix(HttpMethod.Post, $"channel_points/custom_rewards?broadcaster_id={Settings.UserId}",
                JsonSerializer.Serialize(new { title, cost, is_enabled = isEnabled }), CancellationToken.None);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return TwitchMessage(text) ?? $"Twitch said {(int)response.StatusCode}";

            using var json = JsonDocument.Parse(text);
            var id = json.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
            Settings.ManagedRewards.Add(new ManagedReward
            {
                Id = id, Title = title, Cost = cost, IsEnabled = isEnabled, OwnedByRules = ownedByRules
            });
            SettingsService.Save();
            ActivityLog.Add($"Created the channel point reward \"{title}\" ({cost} points)", LogLevel.Success);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Deletes one of Butthurt's rewards from the channel. Returns an error message, or null.</summary>
    public static async Task<string?> DeleteReward(ManagedReward reward)
    {
        try
        {
            using var response = await Helix(HttpMethod.Delete,
                $"channel_points/custom_rewards?broadcaster_id={Settings.UserId}&id={reward.Id}", null, CancellationToken.None);

            // 404: already deleted on Twitch, forget it here too.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                return TwitchMessage(await response.Content.ReadAsStringAsync()) ?? $"Twitch said {(int)response.StatusCode}";
            }

            Settings.ManagedRewards.Remove(reward);
            SettingsService.Save();
            ActivityLog.Add($"Deleted the channel point reward \"{reward.Title}\"");
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    /// <summary>Changes a reward's cost and whether viewers can see it. Returns an error message, or null.</summary>
    private static async Task<string?> UpdateReward(ManagedReward reward, int cost, bool isEnabled)
    {
        using var response = await Helix(new HttpMethod("PATCH"),
            $"channel_points/custom_rewards?broadcaster_id={Settings.UserId}&id={reward.Id}",
            JsonSerializer.Serialize(new { cost, is_enabled = isEnabled }), CancellationToken.None);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Deleted on Twitch by hand: forget it, the next sync creates it again.
            Settings.ManagedRewards.Remove(reward);
            SettingsService.Save();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return TwitchMessage(await response.Content.ReadAsStringAsync()) ?? $"Twitch said {(int)response.StatusCode}";
        }

        reward.Cost = cost;
        reward.IsEnabled = isEnabled;
        SettingsService.Save();
        return null;
    }

    private static string? TwitchMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Titles of the channel's custom channel point rewards, empty for channels without them.</summary>
    public static async Task<List<string>> GetRewardTitles()
    {
        if (!IsLoggedIn || Settings.UserId == null) return new List<string>();

        try
        {
            using var response = await Helix(HttpMethod.Get,
                $"channel_points/custom_rewards?broadcaster_id={Settings.UserId}", null, CancellationToken.None);
            if (!response.IsSuccessStatusCode) return new List<string>();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(r => String(r, "title"))
                .Where(t => t.Length > 0)
                .OrderBy(t => t)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    #endregion

    #region Reward sync

    private static readonly List<Func<IEnumerable<DesiredReward>>> RewardSources = new();
    private static readonly SemaphoreSlim SyncLock = new(1, 1);
    private static readonly Dictionary<string, string> LastSyncErrors = new(StringComparer.OrdinalIgnoreCase);
    private static Timer? _syncTimer;

    /// <summary>
    /// Plugins with redeem rules register which rewards they want on Twitch (rules with ManageReward on).
    /// Register in the plugin's constructor, so a turned off plugin still reports its rules (as hidden)
    /// instead of having its rewards deleted.
    /// </summary>
    public static void RegisterRewardSource(Func<IEnumerable<DesiredReward>> source)
    {
        lock (RewardSources) RewardSources.Add(source);
    }

    /// <summary>The rewards every rule asks for right now.</summary>
    public static List<DesiredReward> DesiredRewards()
    {
        lock (RewardSources)
        {
            return RewardSync.Merge(RewardSources.SelectMany(source =>
            {
                try
                {
                    return source().ToList();
                }
                catch
                {
                    return new List<DesiredReward>();
                }
            }));
        }
    }

    /// <summary>Syncs rewards a moment after things stop changing (typing a reward name fires on every key).</summary>
    public static void RequestRewardSync()
    {
        _syncTimer ??= new Timer(_ => _ = SyncRewards());
        _syncTimer.Change(1500, Timeout.Infinite);
    }

    /// <summary>Creates, updates and deletes rule rewards on Twitch until they match the rules.</summary>
    public static async Task SyncRewards()
    {
        if (State != TwitchState.Connected || !CanManageRedeems) return;
        if (!await SyncLock.WaitAsync(0))
        {
            // A sync is already running, check again once it's done.
            RequestRewardSync();
            return;
        }

        try
        {
            foreach (var change in RewardSync.Plan(DesiredRewards(), Settings.ManagedRewards.ToList()))
            {
                var error = change.Kind switch
                {
                    RewardChangeKind.Create => await CreateReward(change.Title, change.Cost, change.Enabled, ownedByRules: true),
                    RewardChangeKind.Update => await UpdateReward(change.Existing!, change.Cost, change.Enabled),
                    RewardChangeKind.Adopt => await Adopt(change),
                    RewardChangeKind.Delete => await DeleteReward(change.Existing!),
                    _ => null
                };

                // Report each problem once, not on every save while it keeps failing.
                if (error == null)
                {
                    LastSyncErrors.Remove(change.Title);
                }
                else if (!LastSyncErrors.TryGetValue(change.Title, out var last) || last != error)
                {
                    LastSyncErrors[change.Title] = error;
                    ActivityLog.Add($"Couldn't sync the reward \"{change.Title}\": {error}", LogLevel.Warning);
                }
            }
        }
        catch (Exception e)
        {
            ActivityLog.Add($"Reward sync failed: {e.Message}", LogLevel.Warning);
        }
        finally
        {
            SyncLock.Release();
            RewardsChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Raised after a reward sync. May fire on any thread.</summary>
    public static event EventHandler? RewardsChanged;

    /// <summary>The last sync problem for a reward name, shown next to the rule.</summary>
    public static string? SyncErrorFor(string title) =>
        LastSyncErrors.TryGetValue(title.Trim(), out var error) ? error : null;

    private static async Task<string?> Adopt(RewardChange change)
    {
        change.Existing!.OwnedByRules = true;
        return await UpdateReward(change.Existing, change.Cost, change.Enabled);
    }

    #endregion

    #region Helpers

    private static async Task<HttpResponseMessage> Helix(HttpMethod method, string path, string? body, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            var accessToken = Unprotect(Settings.AccessToken) ?? "";
            var request = new HttpRequestMessage(method, "https://api.twitch.tv/helix/" + path);
            request.Headers.TryAddWithoutValidation("Client-Id", ClientId);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await Http.SendAsync(request, token);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 0) return response;

            response.Dispose();
            if (await RefreshToken(token) == null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }
    }

    private sealed class FormResult : IDisposable
    {
        public bool Ok { get; init; }
        public JsonDocument? Json { get; init; }
        public string Error { get; init; } = "";
        public void Dispose() => Json?.Dispose();
    }

    private static async Task<FormResult> PostForm(string url, Dictionary<string, string> form, CancellationToken token)
    {
        using var response = await Http.PostAsync(url, new FormUrlEncodedContent(form), token);
        var text = await response.Content.ReadAsStringAsync(token);

        if (response.IsSuccessStatusCode)
        {
            return new FormResult { Ok = true, Json = text.Length > 0 ? JsonDocument.Parse(text) : null };
        }

        return new FormResult { Error = text };
    }

    private static string String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string UserOrAnonymous(JsonElement e, string property)
    {
        var name = String(e, property);
        return name.Length > 0 ? name : "Anonymous";
    }

    // "1000", "2000" or "3000". Prime subs show up as 1000.
    private static int Tier(JsonElement e) => String(e, "tier") switch
    {
        "2000" => 2,
        "3000" => 3,
        _ => 1
    };

    private static ChatterLevel ChatterLevelFrom(JsonElement e)
    {
        if (!e.TryGetProperty("badges", out var badges) || badges.ValueKind != JsonValueKind.Array) return ChatterLevel.Everyone;

        var sets = badges.EnumerateArray().Select(b => String(b, "set_id")).ToHashSet();
        if (sets.Contains("broadcaster") || sets.Contains("moderator")) return ChatterLevel.Moderators;
        if (sets.Contains("vip")) return ChatterLevel.Vips;
        if (sets.Contains("subscriber") || sets.Contains("founder")) return ChatterLevel.Subscribers;
        return ChatterLevel.Everyone;
    }

    private static string Shorten(string text) => text.Length > 120 ? text[..120] + "…" : text;

    private static void SetState(TwitchState state, string? error = null)
    {
        State = state;
        if (state is TwitchState.Connected or TwitchState.LoggedOut) LastError = null;
        if (error != null) LastError = error;
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetError(string message)
    {
        ActivityLog.Add(message, LogLevel.Error);
        SetState(TwitchState.Error, message);
    }

    // Tokens are encrypted for the current Windows user so copying settings.json doesn't hand out the login.
    private const string ProtectedPrefix = "dpapi:";

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows()) return value;

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(bytes);
    }

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!value.StartsWith(ProtectedPrefix) || !OperatingSystem.IsWindows()) return value;

        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[ProtectedPrefix.Length..]), null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
