using MudBlazor;

namespace NetPilot.Web.Components.DesignSystem;

public static class NetPilotTheme
{
    private static readonly string[] SystemFonts = ["system-ui", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif"];

    // CSS and viewport observers agree: 600 is phone, 601 starts the rail, 1200 starts the drawer.
    public static Dictionary<Breakpoint, int> ViewportBreakpoints => new()
    {
        [Breakpoint.Xs] = 0, [Breakpoint.Sm] = 601, [Breakpoint.Md] = 960,
        [Breakpoint.Lg] = 1200, [Breakpoint.Xl] = 1920, [Breakpoint.Xxl] = 2560
    };

    public static MudTheme Theme { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#006a60", PrimaryContrastText = "#ffffff",
            Secondary = "#4a635e", SecondaryContrastText = "#ffffff",
            Tertiary = "#66558f", TertiaryContrastText = "#ffffff",
            Background = "#f6faf7", BackgroundGray = "#eaf0ec", Surface = "#ffffff",
            TextPrimary = "#171d1b", TextSecondary = "#3f4945", ActionDefault = "#3f4945",
            DrawerBackground = "#f0f5f1", DrawerText = "#171d1b", DrawerIcon = "#3f4945",
            AppbarBackground = "#f0f5f1", AppbarText = "#171d1b",
            LinesDefault = "#bec9c2", LinesInputs = "#707974", Divider = "#bec9c2",
            Error = "#ba1a1a", ErrorContrastText = "#ffffff",
            Warning = "#704600", WarningContrastText = "#ffffff",
            Success = "#155d36", SuccessContrastText = "#ffffff",
            Info = "#4a635e", InfoContrastText = "#ffffff"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#80d5c5", PrimaryContrastText = "#00382f",
            Secondary = "#b1ccc5", SecondaryContrastText = "#1c3530",
            Tertiary = "#d0bcff", TertiaryContrastText = "#38265c",
            Background = "#0e1512", BackgroundGray = "#1b211e", Surface = "#171d1a",
            TextPrimary = "#dee4e0", TextSecondary = "#bec9c2", ActionDefault = "#bec9c2",
            DrawerBackground = "#171d1a", DrawerText = "#dee4e0", DrawerIcon = "#bec9c2",
            AppbarBackground = "#171d1a", AppbarText = "#dee4e0",
            LinesDefault = "#3f4945", LinesInputs = "#89938c", Divider = "#3f4945",
            Error = "#ffb4ab", ErrorContrastText = "#690005",
            Warning = "#ffdfa0", WarningContrastText = "#513300",
            Success = "#a6ebbc", SuccessContrastText = "#103d26",
            Info = "#b1ccc5", InfoContrastText = "#1c3530"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px", DrawerWidthLeft = "248px", DrawerMiniWidthLeft = "88px", AppbarHeight = "64px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = SystemFonts },
            H1 = new H1Typography { FontFamily = SystemFonts, FontSize = "2rem", LineHeight = "1.25" },
            H2 = new H2Typography { FontFamily = SystemFonts, FontSize = "1.5rem", LineHeight = "1.3" },
            H3 = new H3Typography { FontFamily = SystemFonts, FontSize = "1.375rem" },
            H4 = new H4Typography { FontFamily = SystemFonts },
            H5 = new H5Typography { FontFamily = SystemFonts },
            H6 = new H6Typography { FontFamily = SystemFonts },
            Subtitle1 = new Subtitle1Typography { FontFamily = SystemFonts },
            Subtitle2 = new Subtitle2Typography { FontFamily = SystemFonts },
            Body1 = new Body1Typography { FontFamily = SystemFonts },
            Body2 = new Body2Typography { FontFamily = SystemFonts },
            Button = new ButtonTypography { FontFamily = SystemFonts, TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = SystemFonts },
            Overline = new OverlineTypography { FontFamily = SystemFonts }
        }
    };
}
