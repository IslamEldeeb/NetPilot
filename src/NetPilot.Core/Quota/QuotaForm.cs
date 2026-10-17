using System.Globalization;
using NetPilot.Core.Rules.Presentation;

namespace NetPilot.Core.Quota;

public sealed class QuotaForm
{
    public string DailyGb { get; set; } = "";
    public string MonthlyGb { get; set; } = "";
    public string ThrottleKbps { get; set; } = "";
    public string Warnings { get; set; } = "80";
    public QuotaAction Action { get; set; }
    public bool Exempt { get; set; }
    public bool DryRun { get; set; } = true;

    public static QuotaForm From(QuotaPolicy policy) => new()
    {
        DailyGb = policy.DailyBytes is { } day ? RuleForm.FormatGb(day) : "",
        MonthlyGb = policy.MonthlyBytes is { } month ? RuleForm.FormatGb(month) : "",
        ThrottleKbps = policy.ThrottleKbps?.ToString(CultureInfo.InvariantCulture) ?? "",
        Warnings = string.Join(',', policy.WarnPercents), Action = policy.Action, Exempt = policy.Exempt, DryRun = policy.DryRun
    };

    public QuotaPolicy Build(QuotaPolicy original)
    {
        var policy = original with
        {
            DailyBytes = Cap(DailyGb), MonthlyBytes = Cap(MonthlyGb), Action = Action,
            ThrottleKbps = Action == QuotaAction.Throttle ? Integer(ThrottleKbps) : null,
            WarnPercents = string.IsNullOrWhiteSpace(Warnings) ? [] : Warnings.Split(',').Select(Integer).ToArray(),
            Exempt = Exempt, DryRun = DryRun
        };
        if (policy.Validate() is { Count: > 0 } errors) throw new ArgumentException(string.Join(' ', errors));
        return policy;
    }

    private static long? Cap(string text) => string.IsNullOrWhiteSpace(text) ? null
        : RuleForm.TryBytes(text, out var bytes) ? bytes : throw new ArgumentException("Enter positive decimal GB, at most nine decimal places.");
    private static int Integer(string text) => int.TryParse(text, out var number) ? number
        : throw new ArgumentException("Throttle and warnings require whole numbers.");
}
