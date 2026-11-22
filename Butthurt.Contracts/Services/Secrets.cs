using System;
using System.Security.Cryptography;
using System.Text;

namespace Butthurt.Contracts.Services;

/// <summary>
/// Keeps logins and API keys in settings.json encrypted for the current Windows user, so copying the
/// settings file doesn't hand them out. Values saved before encryption (plain text) still read fine.
/// </summary>
public static class Secrets
{
    private const string ProtectedPrefix = "dpapi:";

    public static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows()) return value;

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? value)
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
}
