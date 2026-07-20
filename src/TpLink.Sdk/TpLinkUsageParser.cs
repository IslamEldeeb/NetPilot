using System.Globalization;
using System.Text.RegularExpressions;

namespace TpLink.Sdk;

/// <summary>
/// Parses the raw trafficUsage string into a byte count. The unit is NOT yet confirmed
/// live (see docs/phase2-usage-tracking-plan.md, open item #1) — this defaults to
/// "plain integer = bytes", matching every other numeric field on this endpoint, with a
/// formatted-string fallback in case that assumption is wrong. This is the single place
/// to fix if a live capture shows a different unit (e.g. multiply by 1024 for KB).
/// </summary>
public static partial class TpLinkUsageParser
{
    public static bool TryParseBytes(string? raw, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
        {
            bytes = plain;
            return true;
        }

        var match = FormattedSizeRegex().Match(trimmed);
        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        var multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "B" => 1L,
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            _ => 0L
        };
        if (multiplier == 0L)
            return false;

        bytes = (long)(value * multiplier);
        return true;
    }

    [GeneratedRegex(@"^([\d.]+)\s*(B|KB|MB|GB|TB)$", RegexOptions.IgnoreCase)]
    private static partial Regex FormattedSizeRegex();
}
