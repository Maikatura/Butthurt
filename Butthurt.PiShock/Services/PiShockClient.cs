using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Butthurt.Contracts.Services;
using Butthurt.PiShock.Models;

namespace Butthurt.PiShock.Services;

/// <summary>A shocker on the user's own PiShock account.</summary>
public record PiShockDevice(int HubId, int ShockerId, string Name, bool IsV3);

/// <summary>
/// PiShock's API (https://pishock.helpscoutdocs.com/article/32-api). Every call sends the API key in the
/// <c>X-PiShock-Api-Key</c> header, no username needed:
///
///   GET  https://api.pishock.com/Account                       → {"UserId","Username",…}
///   GET  https://api.pishock.com/Shockers                      → [{"HubId","ShockerId","Name","IsV3"}]
///   GET  https://api.pishock.com/Share/GetShared               → shockers shared with you: {"Id","ClientId","ShareCode","MaxIntensity",…}
///   POST https://api.pishock.com/Shockers/OperateById/{id}     {"AgentName","Operation":0|1|2,"Duration":16-15000 ms,"Intensity":1-100,
///                                                               "IntensityAsPercentage":false} → 204 when sent
///   POST https://api.pishock.com/Shockers/OperateByShare/{code} same body, for a share code (its limits apply)
///   (schema: https://api.pishock.com/swagger/v1/swagger.json, OperateShockerRequest)
///
/// Operation 0 shocks, 1 vibrates, 2 beeps. Errors are status codes only (401 key/access, 404 no such device,
/// 405 not allowed on the share, 406 not V3 firmware, 410 share locked, 412 intensity, 416 duration, 503 paused).
/// </summary>
public static class PiShockClient
{
    private const string BaseUrl = "https://api.pishock.com/";
    private const string KeyHeader = "X-PiShock-Api-Key";

    private static readonly HttpClient Http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };

    private static PiShockSettings Settings => PiShockPlugin.Settings;

    /// <summary>Forget what was looked up with the old key.</summary>
    public static void ResetLogin() => Settings.UserId = 0;

    #region Account

    /// <summary>Checks the API key and remembers whose it is. Returns an error message, or null when it worked.</summary>
    public static async Task<string?> CheckLogin(string apiKey)
    {
        var (json, error) = await Get("Account", apiKey, "checking your API key");
        if (error != null) return error;

        using (json)
        {
            Settings.UserId = Int(json!.RootElement, "UserId");
            var name = Str(json.RootElement, "Username");
            if (name.Length > 0) Settings.Username = name;
        }

        SettingsService.Save();
        return null;
    }

    /// <summary>Every shocker on the user's own account.</summary>
    public static async Task<(List<PiShockDevice> Devices, string? Error)> GetMyShockers(string apiKey)
    {
        var (json, error) = await Get("Shockers", apiKey, "listing your shockers");
        if (error != null) return (new(), error);

        var devices = new List<PiShockDevice>();
        using (json)
        {
            if (json!.RootElement.ValueKind != JsonValueKind.Array) return (devices, null);

            foreach (var shocker in json.RootElement.EnumerateArray())
            {
                devices.Add(new PiShockDevice(Int(shocker, "HubId"), Int(shocker, "ShockerId"), Str(shocker, "Name"),
                    TryGet(shocker, "IsV3", out var v3) && v3.ValueKind == JsonValueKind.True));
            }
        }

        // Also learn whose key it is, for the account line on the page.
        if (Settings.UserId <= 0) await CheckLogin(apiKey);
        return (devices, null);
    }

    /// <summary>
    /// Finds the shocker behind a share code. Works for codes you made and codes others shared with you, as long as
    /// the code is on your PiShock account. Returns an error message, or null when the ids were filled in.
    /// </summary>
    public static async Task<string?> ResolveShareCode(PiShockShocker shocker, string apiKey)
    {
        var code = shocker.ShareCode.Trim();
        var (json, error) = await Get("Share/GetShared", apiKey, "looking up the share code");
        if (error != null) return error;

        using (json)
        {
            if (json!.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var share in json.RootElement.EnumerateArray())
                {
                    if (!string.Equals(Str(share, "ShareCode"), code, StringComparison.OrdinalIgnoreCase)) continue;

                    shocker.HubId = Int(share, "ClientId");
                    shocker.ShockerId = Int(share, "Id");
                    SettingsService.Save();
                    return shocker.ShockerId > 0 ? null : "PiShock didn't say which shocker this share code is for";
                }
            }
        }

        return "this share code isn't on your PiShock account. Add it on pishock.com first, or use Find my shockers for your own";
    }

    #endregion

    #region Commands

    /// <summary>Sends one command. Returns an error message, or null when PiShock sent it to the shocker.</summary>
    public static async Task<string?> Operate(PiShockShocker shocker, PiShockOp op, int intensity, int durationSeconds, string agentName)
    {
        var apiKey = Secrets.Unprotect(Settings.ApiKey) ?? "";
        if (apiKey.Length == 0) return "add your PiShock API key first";

        // A share code is used as is (PiShock applies its limits); otherwise the shocker's id from Find my shockers.
        var code = shocker.ShareCode.Trim();
        string path;
        if (code.Length > 0) path = $"Shockers/OperateByShare/{Uri.EscapeDataString(code)}";
        else if (shocker.ShockerId > 0) path = $"Shockers/OperateById/{shocker.ShockerId}";
        else return "this shocker isn't linked to PiShock. Use Find my shockers, or add a share code";

        var body = JsonSerializer.Serialize(new
        {
            AgentName = agentName,
            Operation = (int)op,
            Duration = Math.Clamp(durationSeconds * 1000, 16, 15000),
            Intensity = Math.Clamp(intensity, 1, 100),
            IntensityAsPercentage = false
        });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add(KeyHeader, apiKey);

            using var response = await Http.SendAsync(request);
            if (response.IsSuccessStatusCode) return null;

            var answer = await response.Content.ReadAsStringAsync();

            // What was sent and what came back, without the key, to compare with PiShock's docs when something's off.
            ActivityLog.Add($"PiShock: POST {path} {body} → {(int)response.StatusCode} {Shorten(answer)}", LogLevel.Info);
            return Explain((int)response.StatusCode, answer, code.Length > 0);
        }
        catch (Exception e)
        {
            return $"couldn't reach PiShock: {e.Message}";
        }
    }

    /// <summary>Nothing to close any more (plain HTTP); kept so callers stopping everything don't need to know.</summary>
    public static void CloseBroker()
    {
    }

    #endregion

    #region Helpers

    private static async Task<(JsonDocument? Json, string? Error)> Get(string path, string apiKey, string what)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return (null, "add your PiShock API key first");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(KeyHeader, apiKey);

            using var response = await Http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return (null, Explain((int)response.StatusCode, text, false, what));

            return (JsonDocument.Parse(text.Length > 0 ? text : "null"), null);
        }
        catch (Exception e)
        {
            return (null, $"couldn't reach PiShock while {what}: {e.Message}");
        }
    }

    /// <summary>PiShock's status codes (from its API docs) with a hint on what to do about them.</summary>
    private static string Explain(int status, string body, bool viaShare, string? what = null)
    {
        var message = status switch
        {
            401 => (what == null ? "PiShock didn't accept the API key, or it has no access to this shocker. " : "PiShock didn't accept the API key. ") +
                   "Make a new key on login.pishock.com/Account → API Keys → Add New, and paste it again",
            404 => viaShare ? "PiShock couldn't find this shocker or share code" : "PiShock couldn't find this shocker. Press Find my shockers again",
            405 => "the share code doesn't allow this (e.g. shocks are turned off on the share)",
            406 => "this shocker needs the V3 firmware. Update it from the PiShock website or ask PiShock support",
            410 => "the share code is locked",
            412 => "the intensity is out of bounds for this shocker or share",
            416 => "the duration is over the share's limit",
            503 => "the shocker or share is paused on pishock.com",
            // Not one of PiShock's documented answers: something broke on their side, usually the hub.
            >= 500 => $"PiShock had an error on its side ({status}). Usually the hub is offline or the shocker isn't paired to it. " +
                      "Check that pishock.com shows it online and that its own Beep button works",
            _ => $"PiShock answered {status}"
        };

        if (what != null && status is not 401) message = $"{message} (while {what})";

        // Any text PiShock sent along can help, keep it short.
        body = body.Trim();
        if (body.Length > 0 && status is not (401 or 404) && !body.StartsWith('<'))
        {
            message += $": {(body.Length > 120 ? body[..120] + "…" : body)}";
        }

        return message;
    }

    /// <summary>Property lookup that ignores case.</summary>
    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }

        return false;
    }

    private static int Int(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => 0
        };
    }

    private static string Shorten(string text)
    {
        text = text.Trim();
        return text.Length > 200 ? text[..200] + "…" : text;
    }

    private static string Str(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    #endregion
}
