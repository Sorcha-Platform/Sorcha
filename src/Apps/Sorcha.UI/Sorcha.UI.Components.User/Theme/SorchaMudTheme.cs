// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MudBlazor;

namespace Sorcha.UI.Core.Theme;

/// <summary>
/// Canonical Sorcha <see cref="MudTheme"/> — single source of truth for
/// palette, surfaces, and (eventually) typography shared by every host
/// that mounts MudBlazor. Web (<c>Sorcha.UI.Web.Client</c>) and PWA
/// (<c>Sorcha.Wallet.Pwa</c>) both bind their <c>MudThemeProvider</c> to
/// <see cref="Default"/> so the citizen sees a consistent identity moving
/// between the multi-tenant app and the wallet.
/// </summary>
/// <remarks>
/// The instance is built once at type-init and exposed read-only. Do not
/// mutate the returned <see cref="MudTheme"/> — MudBlazor reads palette
/// properties on every render and a mutation would propagate to every
/// host sharing the singleton.
/// </remarks>
public static class SorchaMudTheme
{
    /// <summary>
    /// The default theme. Lifted verbatim from the original
    /// <c>Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor</c>
    /// private field so existing Web appearance is byte-identical.
    /// </summary>
    public static MudTheme Default { get; } = Build();

    private static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#667eea",
            Secondary = "#764ba2",
            AppbarBackground = "#667eea",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#667eea",
            Secondary = "#764ba2",
            AppbarBackground = "#1e1e1e",
            Surface = "#1e1e1e",
            Background = "#121212",
            DrawerBackground = "#1e1e1e",
            TextPrimary = "#e0e0e0",
            TextSecondary = "#b0b0b0",
            LinesDefault = "#333333",
        }
    };
}
