using System.Text.RegularExpressions;

namespace NetPilot.Core.Devices;

/// <summary>
/// Normalizes MAC addresses to upper-case dash format (XX-XX-XX-XX-XX-XX) — the format
/// confirmed live for both the TP-Link read and write payloads — so identity comparisons
/// are stable regardless of the casing/separator a given provider hands back.
/// </summary>
public readonly partial record struct MacAddress
{
    private readonly string _value;

    public MacAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("MAC address cannot be empty.", nameof(raw));

        var normalized = NonHexSeparator().Replace(raw.Trim(), "-").ToUpperInvariant();
        if (!MacPattern().IsMatch(normalized))
            throw new ArgumentException($"'{raw}' is not a recognizable MAC address.", nameof(raw));

        _value = normalized;
    }

    public override string ToString() => _value;

    public static implicit operator string(MacAddress mac) => mac._value;

    [GeneratedRegex("[:._]")]
    private static partial Regex NonHexSeparator();

    [GeneratedRegex("^([0-9A-F]{2}-){5}[0-9A-F]{2}$")]
    private static partial Regex MacPattern();
}
