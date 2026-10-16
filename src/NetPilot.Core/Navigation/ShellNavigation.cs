namespace NetPilot.Core.Navigation;

public sealed record ShellDestination(string Label, string Path, string Icon);

/// <summary>Route ownership shared by navigation and search, independent of the UI framework.</summary>
public static class ShellNavigation
{
    public static IReadOnlyList<ShellDestination> Primary { get; } = Array.AsReadOnly<ShellDestination>(
    [
        new("Home", "/", "home"), new("Devices", "/devices", "devices"),
        new("Usage", "/usage", "usage"), new("Automations", "/automations", "automations"),
        new("Activity", "/activity", "activity")
    ]);
    public static ShellDestination Settings { get; } = new("Settings", "/settings", "settings");

    public static ShellDestination? Owner(string path)
    {
        var cleanPath = path.Split('?', '#')[0].TrimEnd('/');
        if (cleanPath.Length == 0) return Primary[0];
        return Primary.Skip(1).Append(Settings).FirstOrDefault(destination =>
            cleanPath.Equals(destination.Path, StringComparison.OrdinalIgnoreCase)
            || cleanPath.StartsWith(destination.Path + "/", StringComparison.OrdinalIgnoreCase));
    }
}
