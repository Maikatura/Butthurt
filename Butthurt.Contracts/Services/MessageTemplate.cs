using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Butthurt.Contracts.Services;

/// <summary>
/// User-written messages with {placeholders}, e.g. "{user} cheered {bits} bits: ×{multiplier} for {duration}s".
/// Names ignore case. Unknown placeholders are left as they are, so a typo is easy to spot in the preview.
/// </summary>
public static partial class MessageTemplate
{
    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex Placeholder();

    public static string Format(string template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return "";

        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        return Placeholder().Replace(template, match =>
            lookup.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
