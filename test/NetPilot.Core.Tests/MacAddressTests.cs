using NetPilot.Core.Devices;

namespace NetPilot.Core.Tests;

public class MacAddressTests
{
    [Theory]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA-BB-CC-DD-EE-FF")]
    [InlineData("AA-BB-CC-DD-EE-FF", "AA-BB-CC-DD-EE-FF")]
    [InlineData("aa.bb.cc.dd.ee.ff", "AA-BB-CC-DD-EE-FF")]
    public void Normalizes_ToUppercaseDashFormat(string input, string expected)
    {
        Assert.Equal(expected, (string)new MacAddress(input));
    }

    [Fact]
    public void Rejects_Malformed_Input()
    {
        Assert.Throws<ArgumentException>(() => new MacAddress("not-a-mac"));
    }

    [Fact]
    public void EqualityIsCaseAndSeparatorInsensitive()
    {
        Assert.Equal(new MacAddress("aa:bb:cc:dd:ee:ff"), new MacAddress("AA-BB-CC-DD-EE-FF"));
    }
}
