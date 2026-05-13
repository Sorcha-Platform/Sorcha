// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Filters;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// TOTP two-factor authentication API endpoints.
/// Manages setup, verification, validation, and status of TOTP 2FA.
/// </summary>
public static class TotpEndpoints
{
    /// <summary>
    /// Rate limiter policy name for TOTP validation endpoints.
    /// Configured centrally via <see cref="RateLimitPolicies.TotpValidation"/> in ServiceDefaults.
    /// </summary>
    internal const string TotpRateLimitPolicy = RateLimitPolicies.TotpValidation;

    /// <summary>
    /// Maps TOTP 2FA endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapTotpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/totp")
            .WithTags("TOTP Two-Factor Authentication");

        // Setup — requires authenticated user
        group.MapPost("/setup", Setup)
            .WithName("TotpSetup")
            .WithSummary("Initiate TOTP 2FA setup")
            .WithDescription("Generates a new TOTP secret, QR code URI, and backup codes. "
                + "Call verify endpoint with a code from your authenticator app to complete setup.")
            .RequireAuthorization()
            .Produces<TotpSetupResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // Verify — complete initial setup (requires authenticated user)
        group.MapPost("/verify", Verify)
            .WithName("TotpVerify")
            .WithSummary("Verify and enable TOTP 2FA")
            .WithDescription("Verifies the initial TOTP code from the authenticator app to complete enrollment. "
                + "After this, the user will need to provide a TOTP code on every login.")
            .RequireAuthorization()
            .Produces<TotpVerifyResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Validate — during login (uses loginToken, no JWT auth)
        group.MapPost("/validate", Validate)
            .WithName("TotpValidate")
            .WithSummary("Validate TOTP code during login")
            .WithDescription("Validates a TOTP code during the two-factor authentication login step. "
                + "Requires the loginToken issued after successful password verification.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpRateLimitPolicy)
            .Produces<TotpValidateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Backup code validate — during login (uses loginToken, no JWT auth)
        group.MapPost("/backup-validate", BackupValidate)
            .WithName("TotpBackupValidate")
            .WithSummary("Validate backup code during login")
            .WithDescription("Validates and consumes a one-time backup code during the two-factor authentication login step. "
                + "Each backup code can only be used once.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpRateLimitPolicy)
            .Produces<TotpValidateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Disable — requires authenticated user + a fresh re-authentication challenge
        // (Feature 116 US3 / FR-024 — closes the hijacked-session unguarded-prune gap
        // that would otherwise let a stolen bearer disable 2FA without proof of
        // possession of any other factor).
        group.MapDelete("/", Disable)
            .WithName("TotpDisable")
            .WithSummary("Disable TOTP 2FA")
            .WithDescription("Removes TOTP two-factor authentication for the current user. "
                + "Requires a fresh X-Auth-Challenge token issued for Disable2Fa — the "
                + "user must prove possession of another sign-in method before the second "
                + "factor can be cleared.")
            .RequireAuthorization()
            .RequireAuthChallenge(ScopedOperation.Disable2Fa)
            .Produces<TotpStatusResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // Status — requires authenticated user
        group.MapGet("/status", GetStatus)
            .WithName("TotpStatus")
            .WithSummary("Get TOTP 2FA status")
            .WithDescription("Returns whether TOTP two-factor authentication is enabled for the current user.")
            .RequireAuthorization()
            .Produces<TotpStatusResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<TotpSetupResponse>, UnauthorizedHttpResult>> Setup(
        HttpContext context,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var result = await totpService.SetupAsync(userId, cancellationToken);

        return TypedResults.Ok(new TotpSetupResponse
        {
            Secret = result.Secret,
            QrUri = result.QrUri,
            BackupCodes = result.BackupCodes
        });
    }

    private static async Task<Results<Ok<TotpVerifyResponse>, UnauthorizedHttpResult, ValidationProblem>> Verify(
        HttpContext context,
        TotpCodeRequest request,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["TOTP code is required"]
            });
        }

        var success = await totpService.VerifyAndEnableAsync(userId, request.Code, cancellationToken);

        return TypedResults.Ok(new TotpVerifyResponse
        {
            Success = success,
            Message = success
                ? "TOTP two-factor authentication is now enabled"
                : "Invalid TOTP code. Please try again with a new code from your authenticator app."
        });
    }

    private static async Task<Results<Ok<TotpValidateResponse>, UnauthorizedHttpResult, ValidationProblem>> Validate(
        TotpValidateRequest request,
        ITotpService totpService,
        ITokenService tokenService,
        Data.Repositories.IIdentityRepository identityRepository,
        Data.Repositories.IOrganizationRepository organizationRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["loginToken"] = ["Login token is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["TOTP code is required"]
            });
        }

        // Validate the login token to get the user ID
        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        // Validate the TOTP code
        var isValid = await totpService.ValidateCodeAsync(userId.Value, request.Code, cancellationToken);
        if (!isValid)
        {
            return TypedResults.Ok(new TotpValidateResponse
            {
                Success = false,
                Message = "Invalid TOTP code"
            });
        }

        // Code is valid — issue full JWT tokens
        var tokenResponse = await GenerateTokensForUser(
            userId.Value, identityRepository, organizationRepository, tokenService, cancellationToken);

        if (tokenResponse is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new TotpValidateResponse
        {
            Success = true,
            Message = "Two-factor authentication successful",
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresIn = tokenResponse.ExpiresIn
        });
    }

    private static async Task<Results<Ok<TotpValidateResponse>, UnauthorizedHttpResult, ValidationProblem>> BackupValidate(
        TotpBackupValidateRequest request,
        ITotpService totpService,
        ITokenService tokenService,
        Data.Repositories.IIdentityRepository identityRepository,
        Data.Repositories.IOrganizationRepository organizationRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["loginToken"] = ["Login token is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.BackupCode))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["backupCode"] = ["Backup code is required"]
            });
        }

        // Validate the login token to get the user ID
        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        // Validate and consume the backup code
        var isValid = await totpService.ValidateBackupCodeAsync(userId.Value, request.BackupCode, cancellationToken);
        if (!isValid)
        {
            return TypedResults.Ok(new TotpValidateResponse
            {
                Success = false,
                Message = "Invalid backup code"
            });
        }

        // Code is valid — issue full JWT tokens
        var tokenResponse = await GenerateTokensForUser(
            userId.Value, identityRepository, organizationRepository, tokenService, cancellationToken);

        if (tokenResponse is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new TotpValidateResponse
        {
            Success = true,
            Message = "Backup code accepted. Two-factor authentication successful.",
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresIn = tokenResponse.ExpiresIn
        });
    }

    private static async Task<Results<Ok<TotpStatusResponse>, UnauthorizedHttpResult>> Disable(
        HttpContext context,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        await totpService.DisableAsync(userId, cancellationToken);

        return TypedResults.Ok(new TotpStatusResponse
        {
            IsEnabled = false,
            Message = "TOTP two-factor authentication has been disabled"
        });
    }

    private static async Task<Results<Ok<TotpStatusResponse>, UnauthorizedHttpResult>> GetStatus(
        HttpContext context,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var status = await totpService.GetStatusAsync(userId, cancellationToken);

        return TypedResults.Ok(new TotpStatusResponse
        {
            IsEnabled = status.IsEnabled,
            VerifiedAt = status.VerifiedAt
        });
    }

    // --- Helpers ---

    private static Guid GetUserId(HttpContext context)
    {
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static async Task<Models.Dtos.TokenResponse?> GenerateTokensForUser(
        Guid userId,
        Data.Repositories.IIdentityRepository identityRepository,
        Data.Repositories.IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        var user = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null || user.Status != Models.IdentityStatus.Active)
        {
            return null;
        }

        var organization = await organizationRepository.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return null;
        }

        return await tokenService.GenerateUserTokenAsync(user, organization, user.PlatformUserId, cancellationToken);
    }
}

// --- Request/Response DTOs ---

/// <summary>
/// Response from TOTP setup containing the shared secret and backup codes.
/// </summary>
public record TotpSetupResponse
{
    /// <summary>
    /// Base32-encoded shared secret for manual entry.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// otpauth:// URI for QR code scanning.
    /// </summary>
    public required string QrUri { get; init; }

    /// <summary>
    /// One-time backup codes. Store these securely — they are shown only once.
    /// </summary>
    public required string[] BackupCodes { get; init; }
}

/// <summary>
/// Response from TOTP verify (initial setup verification).
/// </summary>
public record TotpVerifyResponse
{
    /// <summary>
    /// Whether verification succeeded and TOTP is now enabled.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Response from TOTP validation during login.
/// On success, includes JWT access and refresh tokens.
/// </summary>
public record TotpValidateResponse
{
    /// <summary>
    /// Whether the TOTP/backup code was valid.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// JWT access token (present only on success).
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Refresh token (present only on success).
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Access token expiration time in seconds (present only on success).
    /// </summary>
    public int? ExpiresIn { get; init; }
}

/// <summary>
/// TOTP status response.
/// </summary>
public record TotpStatusResponse
{
    /// <summary>
    /// Whether TOTP 2FA is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// When TOTP was verified and activated. Null if not enabled.
    /// </summary>
    public DateTime? VerifiedAt { get; init; }

    /// <summary>
    /// Optional status message.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Request to verify a TOTP code (setup completion or authenticated validation).
/// </summary>
public record TotpCodeRequest
{
    /// <summary>
    /// Six-digit TOTP code from authenticator app.
    /// </summary>
    public required string Code { get; init; }
}

/// <summary>
/// Request to validate a TOTP code during login (with login token).
/// </summary>
public record TotpValidateRequest
{
    /// <summary>
    /// Short-lived login token issued after password verification.
    /// </summary>
    public required string LoginToken { get; init; }

    /// <summary>
    /// Six-digit TOTP code from authenticator app.
    /// </summary>
    public required string Code { get; init; }
}

/// <summary>
/// Request to validate a backup code during login (with login token).
/// </summary>
public record TotpBackupValidateRequest
{
    /// <summary>
    /// Short-lived login token issued after password verification.
    /// </summary>
    public required string LoginToken { get; init; }

    /// <summary>
    /// Eight-character alphanumeric backup code.
    /// </summary>
    public required string BackupCode { get; init; }
}
