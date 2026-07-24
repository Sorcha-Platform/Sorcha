// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Server-sent 2FA channel endpoints (Feature 150 US2 Email, US3 SMS). Cross-tier <c>/me/*</c>
/// (F136 <c>.RequireAuthorization()</c>) so one surface serves consumer and platform tokens. Enabling
/// a channel proves delivery with a one-time code; disabling always-notifies. SMS endpoints return
/// 404 when no provider is configured (US3).
/// </summary>
public static class TwoFactorChannelEndpoints
{
    /// <summary>Request carrying a submitted one-time code.</summary>
    /// <param name="Code">The 6-digit code the user received.</param>
    public sealed record CodeRequest(string Code);

    /// <summary>Maps the 2FA-channel endpoints.</summary>
    public static IEndpointRouteBuilder MapTwoFactorChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var email = app.MapGroup("/api/me/2fa/email").WithTags("Two-Factor Channels");

        email.MapPost("/enable", EnableEmail)
            .WithName("EnableEmailOtp")
            .WithSummary("Begin enabling email one-time codes")
            .WithDescription("Sends a Sorcha-branded confirmation code to the account email. The user "
                + "confirms delivery via POST /verify. Rate-limited per user; returns 429 within the "
                + "send cooldown.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        email.MapPost("/verify", VerifyEmail)
            .WithName("VerifyEmailOtp")
            .WithSummary("Confirm the emailed code and enable email 2FA")
            .WithDescription("Verifies the code from POST /enable (single-use). On success, enables "
                + "email one-time codes as a Basic second factor and always-notifies. 400 invalid, "
                + "410 expired/used, 429 attempt cap reached.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized);

        email.MapDelete("", DisableEmail)
            .WithName("DisableEmailOtp")
            .WithSummary("Disable email one-time codes")
            .WithDescription("Turns off email one-time codes as a second factor and always-notifies "
                + "(inbox + email). Idempotent.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        // ---- SMS (US3, config-gated) — all return 404 when no provider is configured ----
        var sms = app.MapGroup("/api/me/2fa/sms").WithTags("Two-Factor Channels");

        sms.MapPost("/phone", CapturePhone)
            .WithName("CaptureSmsPhone")
            .WithSummary("Capture a mobile number and send a verification code")
            .WithDescription("Stores the number (clearing any prior verification) and texts a code. "
                + "404 when SMS is not configured on this installation.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status202Accepted).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized);

        sms.MapPost("/phone/verify", VerifyPhone)
            .WithName("VerifySmsPhone")
            .WithSummary("Verify the texted code to confirm the number")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        sms.MapPost("/enable", EnableSms)
            .WithName("EnableSmsOtp")
            .WithSummary("Enable SMS one-time codes (requires a verified number)")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status401Unauthorized);

        sms.MapDelete("", DisableSms)
            .WithName("DisableSmsOtp")
            .WithSummary("Disable SMS one-time codes")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>Request capturing a candidate mobile number.</summary>
    /// <param name="Phone">The E.164 mobile number.</param>
    public sealed record PhoneRequest(string Phone);

    private static async Task<IResult> CapturePhone(
        PhoneRequest request, HttpContext httpContext, ISecurityChangeNotifier notifier, CancellationToken ct)
    {
        var (userId, svc) = ResolveSms(httpContext);
        if (svc is null) return Results.NotFound();
        if (userId == Guid.Empty) return Results.Unauthorized();

        var outcome = await svc.CapturePhoneAsync(userId, request?.Phone ?? string.Empty, ct);
        if (outcome == SmsFlowOutcome.Ok) await notifier.NotifyAsync(userId, SecurityChangeKind.PhoneChanged, ct);
        return outcome switch
        {
            SmsFlowOutcome.Ok => Results.Accepted(),
            SmsFlowOutcome.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => Results.BadRequest("Enter a valid mobile number in international format (e.g. +14155550123)."),
        };
    }

    private static async Task<IResult> VerifyPhone(
        CodeRequest request, HttpContext httpContext, CancellationToken ct)
    {
        var (userId, svc) = ResolveSms(httpContext);
        if (svc is null) return Results.NotFound();
        if (userId == Guid.Empty) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.Code)) return Results.BadRequest("Code is required.");

        var outcome = await svc.VerifyPhoneAsync(userId, request.Code, ct);
        return outcome switch
        {
            SmsFlowOutcome.Ok => Results.NoContent(),
            SmsFlowOutcome.InvalidCode => Results.BadRequest("That code didn't match."),
            SmsFlowOutcome.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => Results.StatusCode(StatusCodes.Status410Gone),
        };
    }

    private static async Task<IResult> EnableSms(
        HttpContext httpContext, ISecurityChangeNotifier notifier, CancellationToken ct)
    {
        var (userId, svc) = ResolveSms(httpContext);
        if (svc is null) return Results.NotFound();
        if (userId == Guid.Empty) return Results.Unauthorized();

        var outcome = await svc.EnableAsync(userId, ct);
        if (outcome == SmsFlowOutcome.Ok)
        {
            await notifier.NotifyAsync(userId, SecurityChangeKind.SmsOtpEnabled, ct);
            return Results.NoContent();
        }
        return Results.Conflict("Verify a mobile number before enabling SMS codes.");
    }

    private static async Task<IResult> DisableSms(
        HttpContext httpContext, ISecurityChangeNotifier notifier, CancellationToken ct)
    {
        var (userId, svc) = ResolveSms(httpContext);
        if (svc is null) return Results.NotFound();
        if (userId == Guid.Empty) return Results.Unauthorized();

        await svc.DisableAsync(userId, ct);
        await notifier.NotifyAsync(userId, SecurityChangeKind.SmsOtpDisabled, ct);
        return Results.NoContent();
    }

    // SMS service is registered only when configured — its absence is the 404 gate.
    private static (Guid userId, ISmsPhoneVerificationService? svc) ResolveSms(HttpContext ctx)
        => (GetUserId(ctx), ctx.RequestServices.GetService(typeof(ISmsPhoneVerificationService)) as ISmsPhoneVerificationService);

    private static async Task<Results<Accepted, UnauthorizedHttpResult, StatusCodeHttpResult, BadRequest<string>>> EnableEmail(
        HttpContext httpContext,
        IVerificationChannelRegistry registry,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var channel = registry.Resolve(ChallengeMethod.EmailOtp);
        if (channel is null) return TypedResults.BadRequest("Email channel is not available.");

        var outcome = await channel.SendAsync(userId, OtpPurpose.ChannelEnable, ct);
        return outcome switch
        {
            ChannelSendOutcome.Sent => TypedResults.Accepted((string?)null),
            ChannelSendOutcome.RateLimited => TypedResults.StatusCode(StatusCodes.Status429TooManyRequests),
            _ => TypedResults.BadRequest("Email channel is not available for this account."),
        };
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult, BadRequest<string>, StatusCodeHttpResult>> VerifyEmail(
        CodeRequest request,
        HttpContext httpContext,
        IVerificationChannelRegistry registry,
        TenantDbContext db,
        ISecurityChangeNotifier notifier,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();
        if (string.IsNullOrWhiteSpace(request?.Code)) return TypedResults.BadRequest("Code is required.");

        var channel = registry.Resolve(ChallengeMethod.EmailOtp);
        if (channel is null) return TypedResults.BadRequest("Email channel is not available.");

        var outcome = await channel.VerifyAsync(userId, OtpPurpose.ChannelEnable, request.Code, ct);
        switch (outcome)
        {
            case OtpVerifyOutcome.Verified:
                await SetEmailEnabledAsync(db, userId, true, ct);
                await notifier.NotifyAsync(userId, SecurityChangeKind.EmailOtpEnabled, ct);
                return TypedResults.NoContent();
            case OtpVerifyOutcome.Invalid:
                return TypedResults.BadRequest("That code didn't match.");
            case OtpVerifyOutcome.TooManyAttempts:
                return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
            default: // Expired
                return TypedResults.StatusCode(StatusCodes.Status410Gone);
        }
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> DisableEmail(
        HttpContext httpContext,
        TenantDbContext db,
        ISecurityChangeNotifier notifier,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        await SetEmailEnabledAsync(db, userId, false, ct);
        await notifier.NotifyAsync(userId, SecurityChangeKind.EmailOtpDisabled, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Upserts the user's 1:1 two-factor row and flips the email flag.</summary>
    private static async Task SetEmailEnabledAsync(TenantDbContext db, Guid platformUserId, bool enabled, CancellationToken ct)
    {
        var row = await db.PlatformUserTwoFactors.FirstOrDefaultAsync(t => t.PlatformUserId == platformUserId, ct);
        if (row is null)
        {
            row = new PlatformUserTwoFactor { PlatformUserId = platformUserId };
            db.PlatformUserTwoFactors.Add(row);
        }
        row.EmailOtpEnabled = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static Guid GetUserId(HttpContext context)
    {
        var raw = context.User.FindFirst("platform_user_id")?.Value
                  ?? context.User.FindFirst("pid")?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
