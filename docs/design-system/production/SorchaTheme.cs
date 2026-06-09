// =============================================================================
// SorchaTheme.cs — MudBlazor 9.2.0 theme for the Sorcha Refined Visual System
//
// Place at:
//   src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Theme/SorchaTheme.cs
//
// Then in MainLayout.razor, REPLACE the inline `private readonly MudTheme _theme = new() {...}`
// with:
//     private readonly MudTheme _theme = SorchaTheme.Build();
// and keep:
//     <MudThemeProvider Theme="_theme" IsDarkMode="@_isDarkMode" />
//
// Reconciliation vs. the old inline theme:
//   • Primary is now #4F46E5 (the deep "action" indigo) so white text/AppBar pass WCAG AA.
//     The brand glow #6366F1 moves to Secondary (large/icon/border use only).
//   • Tertiary = the verification step (#A5B4FC dark / #4338CA light).
//   • The lighter #667eea/#764ba2 drift and the green #48bb78 accent are gone.
//     Green survives only as Success.
//   • Adds a Typography scale (Inter body / IBM Plex Mono mono) and a 12px shape scale
//     — neither existed before.
// =============================================================================

using MudBlazor;

namespace Sorcha.UI.Web.Client.Theme;

public static class SorchaTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary           = "#4F46E5",   // action indigo (white text = 6.3:1)
            PrimaryDarken     = "#4338CA",
            PrimaryLighten    = "#6366F1",
            Secondary         = "#6366F1",   // brand glow — large/icon/border only
            Tertiary          = "#4338CA",   // verification / proof (text-safe on light)
            TertiaryDarken    = "#3730A3",
            Background        = "#FCFCFE",
            BackgroundGray    = "#F3F4FB",   // section banding
            Surface           = "#FFFFFF",
            DrawerBackground  = "#FFFFFF",
            DrawerText        = "#14162B",
            AppbarBackground  = "#FFFFFF",
            AppbarText        = "#14162B",
            TextPrimary       = "#14162B",   // 17.4:1 on bg
            TextSecondary     = "#585E7C",   // 6.2:1
            TextDisabled      = "rgba(20,22,43,0.38)",
            ActionDefault     = "#585E7C",
            ActionDisabled    = "rgba(20,22,43,0.26)",
            Divider           = "#E4E5F1",
            DividerLight      = "#EEEFF6",
            LinesDefault      = "#E4E5F1",
            LinesInputs       = "#CDD0E4",
            TableLines        = "#E4E5F1",
            Success           = "#047857",   // ~5.4:1
            Warning           = "#B45309",   // ~5.0:1
            Error             = "#C81E1E",   // ~5.7:1
            Info              = "#4F46E5",
        },

        PaletteDark = new PaletteDark
        {
            Primary           = "#4F46E5",   // action indigo (white text = 6.3:1)
            PrimaryDarken     = "#4338CA",
            PrimaryLighten    = "#6366F1",
            Secondary         = "#6366F1",   // brand glow — large/icon/border only
            Tertiary          = "#A5B4FC",   // verification / proof (text-safe on dark, 9.8:1)
            TertiaryDarken    = "#818CF8",
            Background        = "#0A0B14",   // the brand ground
            BackgroundGray    = "#0E1020",   // section banding
            Surface           = "#14162B",
            DrawerBackground  = "#0E1020",
            DrawerText        = "#EDEEF8",
            AppbarBackground  = "#0E1020",
            AppbarText        = "#EDEEF8",
            TextPrimary       = "#EDEEF8",   // 16.98:1 on bg
            TextSecondary     = "#9A9FBC",   // 7.5:1
            TextDisabled      = "rgba(237,238,248,0.38)",
            ActionDefault     = "#9A9FBC",
            ActionDisabled    = "rgba(237,238,248,0.26)",
            Divider           = "#282C46",
            DividerLight      = "#1E2138",
            LinesDefault      = "#282C46",
            LinesInputs       = "#3A3F63",
            TableLines        = "#282C46",
            Success           = "#34D399",   // ~10.2:1
            Warning           = "#FBBF24",   // ~11.8:1
            Error             = "#F87171",   // ~7.1:1
            Info              = "#A5B4FC",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "-apple-system", "BlinkMacSystemFont", "Segoe UI", "sans-serif"],
                FontSize = "16px", FontWeight = "400", LineHeight = "1.6", LetterSpacing = "normal"
            },
            H1 = new H1Typography { FontSize = "56px", FontWeight = "700", LineHeight = "1.05", LetterSpacing = "-0.022em" },
            H2 = new H2Typography { FontSize = "40px", FontWeight = "700", LineHeight = "1.12", LetterSpacing = "-0.018em" },
            H3 = new H3Typography { FontSize = "28px", FontWeight = "600", LineHeight = "1.2",  LetterSpacing = "-0.012em" },
            H4 = new H4Typography { FontSize = "22px", FontWeight = "600", LineHeight = "1.3" },
            H5 = new H5Typography { FontSize = "18px", FontWeight = "600", LineHeight = "1.4" },
            H6 = new H6Typography { FontSize = "16px", FontWeight = "600", LineHeight = "1.5" },
            Subtitle1 = new Subtitle1Typography { FontSize = "19px", FontWeight = "400", LineHeight = "1.6" },
            Subtitle2 = new Subtitle2Typography { FontSize = "15px", FontWeight = "500", LineHeight = "1.5" },
            Body1 = new Body1Typography { FontSize = "16px", FontWeight = "400", LineHeight = "1.6" },
            Body2 = new Body2Typography { FontSize = "14px", FontWeight = "400", LineHeight = "1.5" },
            Button = new ButtonTypography { FontSize = "15px", FontWeight = "600", LineHeight = "1", LetterSpacing = "normal", TextTransform = "none" },
            Caption = new CaptionTypography { FontSize = "13px", FontWeight = "500", LineHeight = "1.45" },
            Overline = new OverlineTypography
            {
                FontFamily = ["IBM Plex Mono", "JetBrains Mono", "ui-monospace", "monospace"],
                FontSize = "12px", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "0.14em", TextTransform = "uppercase"
            },
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "260px",
            AppbarHeight = "64px",
        },

        // Flatter, calmer elevation than MudBlazor's default drop shadows.
        Shadows = new Shadow(),
    };
}
