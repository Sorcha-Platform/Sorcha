// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Feature 128 US2 post-login routing gate — single source of truth for the
/// FR-020/FR-026 contract: when a citizen has zero paired devices and no
/// explicit ReturnUrl override, the fragment redirect MUST route them to
/// <c>/app/setup/add-device</c> so the WASM client lands on the pairing
/// handoff page.
///
/// The <c>/app/</c> prefix is load-bearing — Sorcha.UI.Web.Client is hosted
/// under <c>&lt;base href="/app/"&gt;</c> and <c>NavigationManager.NavigateTo</c>
/// treats a slash-prefixed path as origin-absolute, bypassing the base href.
/// Without the prefix the gateway has no matching route and serves 404.
/// </summary>
internal static class SetupAddDeviceRoutingGate
{
    /// <summary>
    /// The fragment <c>returnUrl</c> value to inject when the gate fires.
    /// </summary>
    public const string SetupAddDevicePath = "/app/setup/add-device";

    /// <summary>
    /// Reads <c>platform_user_id</c> from the freshly-issued access token and
    /// queries <see cref="IPlatformUserDeviceService.HasAnyAsync"/>. Returns
    /// true when the citizen has zero active paired devices — the F128
    /// auto-route trigger. Non-fatal: any failure (token parse, DB error)
    /// is logged and falls through to the existing redirect behaviour.
    /// </summary>
    public static async Task<bool> ShouldRouteAsync(
        string accessToken,
        IPlatformUserDeviceService deviceService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(accessToken);
            var raw = jwt.Claims.FirstOrDefault(c => c.Type == "platform_user_id")?.Value;
            if (!Guid.TryParse(raw, out var platformUserId))
            {
                return false;
            }

            var (hasAny, _) = await deviceService.HasAnyAsync(platformUserId, ct).ConfigureAwait(false);
            return !hasAny;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "F128 setup-add-device routing check failed — falling through to default redirect");
            return false;
        }
    }

    /// <summary>
    /// Sync wrapper around <see cref="ShouldRouteAsync"/> for callers on
    /// synchronous code paths (Login.cshtml.cs's <c>RedirectToApp</c>).
    /// </summary>
    public static bool ShouldRoute(
        string accessToken,
        IPlatformUserDeviceService deviceService,
        ILogger logger)
        => ShouldRouteAsync(accessToken, deviceService, logger, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
}
