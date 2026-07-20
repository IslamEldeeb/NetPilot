namespace TpLink.Sdk.Tests;

public class TpLinkUsageParserTests
{
    [Theory]
    [InlineData("12345", 12345L)]
    [InlineData("0", 0L)]
    [InlineData("1.2 GB", 1288490188L)]
    [InlineData("512 KB", 524288L)]
    [InlineData("3 B", 3L)]
    [InlineData("2MB", 2097152L)]
    public void TryParseBytes_ValidInput_ReturnsExpectedBytes(string raw, long expected)
    {
        Assert.True(TpLinkUsageParser.TryParseBytes(raw, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    [InlineData("unknown")]
    public void TryParseBytes_InvalidInput_ReturnsFalse_DoesNotThrow(string? raw)
    {
        Assert.False(TpLinkUsageParser.TryParseBytes(raw, out var bytes));
        Assert.Equal(0L, bytes);
    }
}
