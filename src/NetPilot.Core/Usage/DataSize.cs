using System.Globalization;

namespace NetPilot.Core.Usage;

/// <summary>Decimal SI display units. Stored byte counts are never rescaled.</summary>
public static class DataSize
{
    public const decimal BytesPerGb = 1_000_000_000m;
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    public static decimal ToGb(long bytes) => ToGb((decimal)bytes);
    public static decimal ToGb(decimal bytes) => bytes / BytesPerGb;

    /// <summary>Converts exactly; fractional bytes and values outside Int64 are rejected.</summary>
    public static long FromGb(decimal gigabytes)
    {
        if (gigabytes < long.MinValue / BytesPerGb || gigabytes > long.MaxValue / BytesPerGb)
            throw new ArgumentOutOfRangeException(nameof(gigabytes), "The byte count is outside the supported range.");
        var bytes = gigabytes * BytesPerGb;
        if (bytes != decimal.Truncate(bytes))
            throw new ArgumentException("The amount must represent whole bytes.", nameof(gigabytes));
        return (long)bytes;
    }

    public static string Format(long bytes)
    {
        var amount = Math.Abs((decimal)bytes);
        var unit = 0;
        while (amount >= 1000 && unit < Units.Length - 1) { amount /= 1000; unit++; }
        var digits = unit >= 3 ? 2 : unit == 0 ? 0 : 1;
        amount = Math.Round(amount, digits, MidpointRounding.AwayFromZero);
        if (amount >= 1000 && unit < Units.Length - 1) { amount /= 1000; unit++; }
        var format = unit >= 3 ? "0.00" : unit == 0 ? "0" : "0.#";
        return $"{(bytes < 0 ? "-" : "")}{amount.ToString(format, CultureInfo.InvariantCulture)} {Units[unit]}";
    }
}
