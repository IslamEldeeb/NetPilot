using MudBlazor;

namespace NetPilot.Web.Components.DesignSystem;

public static class MaterialIcon
{
    public static string For(string name) => name.ToLowerInvariant() switch
    {
        "home" => Icons.Material.Outlined.Home,
        "devices" => Icons.Material.Outlined.Devices,
        "usage" => Icons.Material.Outlined.BarChart,
        "automations" => Icons.Material.Outlined.Bolt,
        "activity" => Icons.Material.Outlined.ListAlt,
        "settings" => Icons.Material.Outlined.Settings,
        "phone" or "mobile" => Icons.Material.Outlined.PhoneAndroid,
        "tv" or "television" => Icons.Material.Outlined.Tv,
        "laptop" => Icons.Material.Outlined.Laptop,
        "camera" or "ip camera" or "ip_camera" => Icons.Material.Outlined.Videocam,
        "console" or "game" or "game console" or "game_console" => Icons.Material.Outlined.SportsEsports,
        "server" => Icons.Material.Outlined.Storage,
        "speaker" => Icons.Material.Outlined.Speaker,
        "printer" => Icons.Material.Outlined.Print,
        "router" => Icons.Material.Outlined.Router,
        _ => Icons.Material.Outlined.DevicesOther
    };
}
