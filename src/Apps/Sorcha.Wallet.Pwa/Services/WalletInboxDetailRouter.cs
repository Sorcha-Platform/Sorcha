// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Services.Shared;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Wallet-PWA inbox detail routing (issue #1266). Maps the API instance reference an inbox entry
/// carries onto the PWA's own application page, and inherits the base router's refusal of everything
/// else it cannot render in-app.
/// </summary>
/// <remarks>
/// The PWA has <c>/applications/{instanceId:guid}</c> (<c>Pages/ApplicationInstance.razor</c>); the
/// web host now has <c>my-applications/{instanceId}</c> (Feature 186 / #1163). Both resolve, to
/// different routes — which is why the router stays a per-host seam rather than one shared rewrite.
/// </remarks>
public sealed class WalletInboxDetailRouter : DefaultInboxDetailRouter
{
    /// <inheritdoc />
    public override string? Resolve(string? detailHref)
    {
        if (string.IsNullOrWhiteSpace(detailHref)) return null;

        // An /api/instances/{id} reference becomes this app's application view. Base-relative (no
        // leading slash) because the PWA is mounted under /wallet/ by the gateway — a rooted path
        // would escape the app's base and 404.
        var instanceId = TryReadInstanceId(detailHref.Trim());
        if (instanceId is not null)
        {
            return $"applications/{instanceId}";
        }

        return base.Resolve(detailHref);
    }
}
