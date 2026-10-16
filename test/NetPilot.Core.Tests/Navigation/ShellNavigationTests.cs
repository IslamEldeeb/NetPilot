using NetPilot.Core.Navigation;

namespace NetPilot.Core.Tests.Navigation;

public class ShellNavigationTests
{
    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/?period=day#summary", "Home")]
    [InlineData("/devices", "Devices")]
    [InlineData("/devices/AA-BB-CC-DD-EE-FF/history?day=1", "Devices")]
    [InlineData("/DEVICES/", "Devices")]
    [InlineData("/usage/devices/AA-BB-CC-DD-EE-FF", "Usage")]
    [InlineData("/automations/rules/new?step=trigger", "Automations")]
    [InlineData("/activity/inbox#latest", "Activity")]
    [InlineData("/settings/safety", "Settings")]
    [InlineData("/legacy", null)]
    [InlineData("/devices-other", null)]
    [InlineData("/unknown", null)]
    public void NestedRoute_SelectsOnlyItsOwningDestination(string path, string? label) =>
        Assert.Equal(label, ShellNavigation.Owner(path)?.Label);
}
