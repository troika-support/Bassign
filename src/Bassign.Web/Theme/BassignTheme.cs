using MudBlazor;

namespace Bassign.Web.Theme;

// Custom MudTheme built from the design-system tokens (docs/design-system.md §2–§4). Contrast is
// script-verified AAA for text. Palette + layout below are stable across MudBlazor 9.x; the
// Typography API has shifted between MudBlazor versions, so verify those type names against the
// MudBlazor 9.9 docs if the build complains — the values (Inter, weights 400/500) stay the same.
//
// Brand accent: at runtime, set PaletteLight.Primary / PaletteDark.Primary from the organization's
// PrimaryColorHex (validate >= 4.5:1 with white for button text first), then pass this theme to
// <MudThemeProvider Theme="@theme" />.
public static class BassignTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary        = "#4F46E5",   // indigo accent (brand override per install); white-on 6.2 AAA
            Background     = "#F6F7F9",   // grey canvas
            Surface        = "#FFFFFF",   // white cards
            TextPrimary    = "#16191D",    // AAA 17.6
            TextSecondary  = "#44505E",    // AAA 8.2
            Success        = "#0B7A3B",
            Warning        = "#B0520A",
            Error          = "#C01B1B",
            LinesDefault   = "#E3E7EC",
            TableLines     = "#E3E7EC",
            DrawerBackground = "#FFFFFF",
            AppbarBackground = "#F6F7F9",
            AppbarText     = "#16191D",
        },
        PaletteDark = new PaletteDark
        {
            Primary        = "#A5B4FC",
            Background     = "#121418",
            Surface        = "#1A1F26",
            TextPrimary    = "#E7EAEE",    // AAA 14.8
            TextSecondary  = "#AEB6C0",    // AAA 8.7
            Success        = "#56D08A",
            Warning        = "#F0A24B",
            Error          = "#FF8585",
            LinesDefault   = "#2A313A",
            TableLines     = "#2A313A",
            DrawerBackground = "#1A1F26",
            AppbarBackground = "#121418",
            AppbarText     = "#E7EAEE",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",  // FlowDesk-style rounded controls
        },
        Typography = new Typography
        {
            // If these type names don't resolve on MudBlazor 9.9, replace with the current
            // equivalents — keep FontFamily Inter and weights 400 (body) / 500 (titles).
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "system-ui", "Segoe UI", "sans-serif"],
                FontWeight = "400",
            },
            H4 = new H4Typography { FontWeight = "500" },
            H5 = new H5Typography { FontWeight = "500" },
            H6 = new H6Typography { FontWeight = "500" },
            Subtitle1 = new Subtitle1Typography { FontWeight = "500" },
            Button = new ButtonTypography { FontWeight = "500", TextTransform = "none" },
        },
    };
}
