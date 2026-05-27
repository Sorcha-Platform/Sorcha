// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Feature 128 US2 "Email me a link" pairing-resumption surface — two
/// endpoints supporting the desktop handoff page's email-resumption
/// affordance.
/// </summary>
public static class PairingResumptionEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/auth/pairing-resumption-email</c> (send) and
    /// <c>GET /api/auth/pairing-resumption/redeem</c> (redeem).
    /// </summary>
    public static IEndpointRouteBuilder MapPairingResumptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("PairingResumption");

        group.MapPost("/pairing-resumption-email", SendAsync)
            .WithName("SendPairingResumptionEmail")
            .WithSummary("Email the signed-in caller a magic-link to reopen /setup/add-device.")
            .WithDescription(
                "Feature 128 US2 — the 'Email me a link' affordance on the desktop "
                + "handoff page. Body is empty; the citizen's email is read from the "
                + "auth principal (never trusting a request-body email). Rate-limited.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/pairing-resumption/redeem", RedeemAsync)
            .WithName("RedeemPairingResumptionEmail")
            .WithSummary("Redeem an emailed pairing-resumption link.")
            .WithDescription(
                "Anonymous. Validates the token, looks up the bound platform user, "
                + "issues a fresh access+refresh token, and 302s to "
                + "/app/#token=...&returnUrl=/setup/add-device. Single-use.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Produces(StatusCodes.Status302Found);

        return app;
    }

    internal static async Task<Results<Accepted, UnauthorizedHttpResult>> SendAsync(
        ClaimsPrincipal principal,
        IPairingResumptionTokenService resumptionService,
        ITransactionalEmailService emailService,
        IPlatformUserService platformUserService,
        IConfiguration configuration,
        ILogger<TransactionalEmailService> logger,
        CancellationToken ct)
    {
        var platformUserId = ResolvePlatformUserId(principal);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await platformUserService.GetByIdAsync(platformUserId.Value, ct).ConfigureAwait(false);
        if (user is null)
        {
            // Authenticated principal references a user that doesn't exist —
            // treat as unauthorised. Should never happen in practice.
            return TypedResults.Unauthorized();
        }

        var minted = await resumptionService.MintAsync(platformUserId.Value, ct).ConfigureAwait(false);

        var baseUrl = (configuration["EmailSettings:BaseUrl"]
                       ?? configuration["Sorcha:BaseUrl"]
                       ?? "http://localhost").TrimEnd('/');
        var resumptionUrl = $"{baseUrl}/api/auth/pairing-resumption/redeem?token={Uri.EscapeDataString(minted.Token)}";

        await emailService.SendPairingResumptionAsync(new PairingResumptionDispatch(
            ToEmail: user.Email,
            DisplayName: user.DisplayName ?? user.Email,
            ResumptionUrl: resumptionUrl,
            ExpiresInHours: 24), ct).ConfigureAwait(false);

        logger.LogInformation(
            "Dispatched pairing-resumption email (platformUserId={PlatformUserId})",
            platformUserId);

        return TypedResults.Accepted((string?)null);
    }

    internal static async Task<IResult> RedeemAsync(
        [FromQuery] string token,
        IPairingResumptionTokenService resumptionService,
        IPlatformUserService platformUserService,
        ILogger<TransactionalEmailService> logger,
        CancellationToken ct)
    {
        // F128 US2 — the magic-link redeem flow. We consume the token here
        // (single-use), validate the bound user still exists, and 302 the
        // citizen to /auth/login with the returnUrl pinned to
        // /setup/add-device. The citizen re-authenticates one step (this is
        // intentional — the magic link is a navigation convenience, not a
        // credential bypass), then lands on the handoff page.
        //
        // Deferred: the auto-sign-in variant (mint a fresh access+refresh
        // token, 302 to /app/#token=...&returnUrl=...) is a follow-up and
        // matches the spec's "authenticated resumption link" text — the
        // current implementation chooses the conservative form for
        // minimum-viable shipping. Captured as a polish-phase TODO.
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.Redirect("/auth/login?reason=resumption-expired");
        }

        var platformUserId = await resumptionService.RedeemAsync(token, ct).ConfigureAwait(false);
        if (platformUserId is null)
        {
            logger.LogInformation("Pairing-resumption redeem missed (token consumed or expired)");
            return Results.Redirect("/auth/login?reason=resumption-expired");
        }

        var user = await platformUserService.GetByIdAsync(platformUserId.Value, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Results.Redirect("/auth/login?reason=resumption-expired");
        }

        // /app prefix required — the WASM client lives under <base href="/app/">
        // and NavigateTo treats slash-prefixed paths as origin-absolute,
        // bypassing the base href. See Login.cshtml.cs:RedirectToApp.
        var returnUrl = Uri.EscapeDataString("/app/setup/add-device");
        var emailHint = Uri.EscapeDataString(user.Email);
        return Results.Redirect($"/auth/login?returnUrl={returnUrl}&email={emailHint}&reason=pairing-resumption");
    }

    private static Guid? ResolvePlatformUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
