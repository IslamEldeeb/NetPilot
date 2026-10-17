using System.Globalization;
using System.Text.RegularExpressions;
using NetPilot.Core.Devices;
using NetPilot.Core.Notifications;

namespace NetPilot.Core.Rules;

public static class RuleNotificationText
{
    public static NotificationMessage Create(RuleFiring firing, Notify notification, IReadOnlyList<MacAddress> protectedMacs)
    {
        var title = Template(notification.Title, firing);
        var body = Template(notification.Body, firing);
        if (firing.DryRun)
        {
            title = "[Dry-run] " + title;
            body += " Would have performed: " + DescribeActions(firing);
        }
        if (protectedMacs.Count > 0 && firing.Actions.Any(a => a is Throttle or Block))
            body += " Protected devices: throttle/block skipped for " + string.Join(", ", protectedMacs) + ".";
        return new(title, body, "/", $"rule:{firing.RuleId}:{firing.Subject}:{firing.EpisodeKey}", []) { DryRun = firing.DryRun, CorrelationId = firing.Id, Mac = firing.Subject == "household" ? null : new MacAddress(firing.Subject) };
    }

    private static string Template(string text, RuleFiring firing) => Regex.Replace(text, @"\{(rule|device|used|cap)\}", match =>
        match.Groups[1].Value switch
        {
            "rule" => firing.RuleName,
            "device" => firing.DeviceName,
            "used" => firing.UsedBytes.ToString(CultureInfo.InvariantCulture),
            "cap" => firing.CapBytes.ToString(CultureInfo.InvariantCulture),
            _ => match.Value
        });

    internal static string DescribeActions(RuleFiring firing) => string.Join(", ", firing.Actions.Select(a => a switch
    {
        Notify => "notify",
        Throttle t => $"throttle {firing.Subject} to {t.Kbps} Kbps",
        Block => $"block {firing.Subject}",
        SetWireless w => $"{(w.Enabled ? "enable" : "disable")} {w.NetworkId}",
        _ => "unknown action"
    }));
}
