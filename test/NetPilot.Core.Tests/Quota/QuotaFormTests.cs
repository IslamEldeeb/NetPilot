using NetPilot.Abstractions;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Tests.Quota;

public class QuotaFormTests
{
    [Fact]
    public void BlankCaps_AreExplicitNoCap_AndEditingRetainsLiveMode()
    {
        var saved = new QuotaPolicy { Id = "default", DailyBytes = 1_000_000_000, DryRun = false };
        var form = QuotaForm.From(saved); form.DailyGb = ""; form.Warnings = "";
        var draft = form.Build(saved);
        Assert.Null(draft.DailyBytes); Assert.Empty(draft.WarnPercents); Assert.False(draft.DryRun);
    }

    [Theory]
    [InlineData("0.0000000001", "80", "512")]
    [InlineData("0", "80", "512")]
    [InlineData("1", "90,80", "512")]
    [InlineData("1", "80,80", "512")]
    [InlineData("1", "100", "512")]
    [InlineData("1", "80", "0")]
    public void InvalidCapsWarningsAndThrottle_KeepValidation(string cap, string warnings, string throttle) =>
        Assert.Throws<ArgumentException>(() => new QuotaForm { DailyGb = cap, Warnings = warnings, Action = QuotaAction.Throttle, ThrottleKbps = throttle }.Build(new() { Id = "default" }));

    [Fact]
    public void SpeedPolicy_SavePreservesNoOpVersion_AndForceReapplyAlwaysBumps()
    {
        var policy = new DevicePolicy("new_category", new(true, 512, 128), 5, false);
        var draft = SpeedPolicyDraft.From(policy);
        Assert.Equal(5, draft.SavedPolicy().DefinitionVersion); Assert.True(draft.SavedPolicy().IsUserConfigured);
        draft.DownloadMbps = 1.5m; draft.UploadMbps = null;
        Assert.Equal(new SpeedLimit(true, 1500, null), draft.SavedPolicy().Limit);
        Assert.Equal(6, draft.SavedPolicy().DefinitionVersion);
        Assert.Equal(6, SpeedPolicyDraft.From(policy).ReappliedPolicy().DefinitionVersion);
    }
}
