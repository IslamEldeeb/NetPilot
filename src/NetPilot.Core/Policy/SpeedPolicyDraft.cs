using NetPilot.Abstractions;

namespace NetPilot.Core.Policy;

public sealed class SpeedPolicyDraft
{
    public required DevicePolicy Original { get; set; }
    public bool Enabled { get; set; }
    public decimal? DownloadMbps { get; set; }
    public decimal? UploadMbps { get; set; }

    public static SpeedPolicyDraft From(DevicePolicy policy) => new()
    {
        Original = policy, Enabled = policy.Limit.Enabled,
        DownloadMbps = policy.Limit.DownloadKbps / 1000m, UploadMbps = policy.Limit.UploadKbps / 1000m
    };

    public DevicePolicy SavedPolicy() => Original.WithLimit(Limit()) with { IsUserConfigured = true };
    public DevicePolicy ReappliedPolicy() => Original with
    {
        Limit = Limit(), DefinitionVersion = checked(Original.DefinitionVersion + 1), IsUserConfigured = true
    };
    private SpeedLimit Limit() => new(Enabled, Kbps(DownloadMbps), Kbps(UploadMbps));
    private static int? Kbps(decimal? mbps) => mbps is null ? null : mbps is < 0 or > (int.MaxValue / 1000m)
        ? throw new ArgumentException("Mbps must be nonnegative and within the supported Kbps range.") : (int)Math.Round(mbps.Value * 1000);
}
