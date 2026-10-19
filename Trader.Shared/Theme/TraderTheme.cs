using MudBlazor;
using Trader.Shared.Services;

namespace Trader.Shared.Theme;

public static class TraderTheme
{
    public static MudTheme Create(AccentOption accent) => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = accent.Light,
            Secondary = "#7C3AED",
            Success = "#16A34A",
            Error = "#DC2626",
            Warning = "#D97706",
            Background = "#F4F6FA",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#0F172A",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#334155",
            TextPrimary = "#0F172A",
            TextSecondary = "#64748B",
            LinesDefault = "#E2E8F0",
            TableLines = "#E2E8F0"
        },
        PaletteDark = new PaletteDark
        {
            Primary = accent.Dark,
            Secondary = "#9F7AEA",
            Success = "#22C55E",
            Error = "#EF4444",
            Warning = "#F59E0B",
            Background = "#0B0E14",
            Surface = "#141923",
            AppbarBackground = "#0F131B",
            AppbarText = "#E6EAF2",
            DrawerBackground = "#0F131B",
            DrawerText = "#AAB3C5",
            TextPrimary = "#E6EAF2",
            TextSecondary = "#8A94A8",
            LinesDefault = "#222A38",
            TableLines = "#222A38",
            ActionDefault = "#AAB3C5"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "248px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = new[] { "Inter", "Segoe UI", "Roboto", "sans-serif" }
            }
        }
    };
}
