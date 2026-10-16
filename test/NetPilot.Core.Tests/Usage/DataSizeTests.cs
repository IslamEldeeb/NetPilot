using System.Globalization;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class DataSizeTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(1000L, "1 KB")]
    [InlineData(999_950L, "1 MB")]
    [InlineData(1_000_000L, "1 MB")]
    [InlineData(12_300_000L, "12.3 MB")]
    [InlineData(999_950_000L, "1.00 GB")]
    [InlineData(1_000_000_000L, "1.00 GB")]
    [InlineData(1_500_000_000L, "1.50 GB")]
    [InlineData(1_005_000_000L, "1.01 GB")]
    [InlineData(1_000_000_000_000L, "1.00 TB")]
    [InlineData(-1L, "-1 B")]
    [InlineData(-1_500_000_000L, "-1.50 GB")]
    [InlineData(long.MaxValue, "9.22 EB")]
    [InlineData(long.MinValue, "-9.22 EB")]
    public void ByteCounts_UseDecimalUnitsAndPredictableRounding(long bytes, string expected) =>
        Assert.Equal(expected, DataSize.Format(bytes));

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(1_500_000_000L)]
    [InlineData(long.MaxValue)]
    public void GbConversion_RoundTripsEveryWholeByte(long bytes) =>
        Assert.Equal(bytes, DataSize.FromGb(DataSize.ToGb(bytes)));

    [Fact]
    public void FractionalByte_IsRejectedRatherThanTruncated() =>
        Assert.Throws<ArgumentException>(() => DataSize.FromGb(0.0000000001m));

    [Theory]
    [InlineData("9223372036.854775808")]
    [InlineData("-9223372036.854775809")]
    public void AmountBeyondInt64_IsRejected(string gb) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DataSize.FromGb(decimal.Parse(gb, CultureInfo.InvariantCulture)));

    [Fact]
    public void Display_UsesDecimalPointRegardlessOfUiCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1.50 GB", DataSize.Format(1_500_000_000));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
