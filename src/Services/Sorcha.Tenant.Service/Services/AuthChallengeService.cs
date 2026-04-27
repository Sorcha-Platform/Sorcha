// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IAuthChallengeService"/>. Implements the Feature 116
/// challenge ladder and persists a SHA-256-hashed token on each successful
/// verification. TOTP and Password verification are fully implemented;
/// Passkey step-up and re-OAuth fall through to <see cref="ChallengeVerificationOutcome.MethodNotAvailable"/>
/// pending the multi-step round-trip wiring in their respective user-story phases.
/// </summary>
public sealed class AuthChallengeService : IAuthChallengeService
{
    /// <summary>Token lifetime — short enough to prevent replay windows.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly TenantDbContext _db;
    private readonly IAuthChallengeRepository _repository;
    private readonly ITotpService _totp;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<AuthChallengeService> _logger;

    /// <summary>Creates a new <see cref="AuthChallengeService"/>.</summary>
    public AuthChallengeService(
        TenantDbContext db,
        IAuthChallengeRepository repository,
        ITotpService totp,
        AuthMetrics metrics,
        ILogger<AuthChallengeService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _totp = totp ?? throw new ArgumentNullException(nameof(totp));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ChallengePreparation> InitiateAsync(
        ChallengeContext context,
        ScopedOperation scopedOperation,
        ChallengeMethod? preferredMethod,
        CancellationToken cancellationToken = default)
    {
        var enrolment = await GetEnrolmentAsync(context, cancellationToken);

        // Honour the caller's preference iff that method is actually enrolled.
        // Falls back to the ladder otherwise — never downgrades past TOTP.
        var method = preferredMethod is { } pref && enrolment.IsEnrolled(pref)
            ? pref
            : enrolment.PickStrongest();

        if (method is null)
        {
            _logger.LogWarning(
                "No challenge method available for {PlatformUserId} {ScopedOperation}",
                context.PlatformUserId, scopedOperation);
            return ChallengePreparation.NoMethodAvailable;
        }

        // For TOTP/Password the dialog needs no payload — the user just types
        // their code/password. Passkey/ReOAuth would carry assertion options
        // / OAuth state in the payload; left null pending US1/US2 wiring.
        return new ChallengePreparation(method.Value, Payload: null);
    }

    /// <inheritdoc />
    public async Task<ChallengeVerification> VerifyAsync(
        ChallengeContext context,
        ChallengeMethod method,
        ScopedOperation scopedOperation,
        JsonElement proof,
        CancellationToken cancellationToken = default)
    {
        var enrolment = await GetEnrolmentAsync(context, cancellationToken);
        if (!enrolment.IsEnrolled(method))
        {
            _metrics.RecordChallengeIssued(method, scopedOperation, success: false);
            _logger.LogWarning(
                "Verification attempted with non-enrolled method {Method} for {PlatformUserId}",
                method, context.PlatformUserId);
            return new ChallengeVerification(ChallengeVerificationOutcome.MethodNotAvailable, null, null);
        }

        var proofAccepted = method switch
        {
            ChallengeMethod.Totp => await VerifyTotpAsync(context, proof, cancellationToken),
            ChallengeMethod.Password => await VerifyPasswordAsync(context, proof, cancellationToken),
            // Passkey + ReOAuth need the multi-step ceremony wiring built in
            // US1/US2; treat as not-available until then.
            ChallengeMethod.Passkey => false,
            ChallengeMethod.ReOAuth => false,
            _ => false
        };

        if (proofAccepted is null)
        {
            _metrics.RecordChallengeIssued(method, scopedOperation, success: false);
            return new ChallengeVerification(ChallengeVerificationOutcome.InvalidProofShape, null, null);
        }
        if (!proofAccepted.Value)
        {
            _metrics.RecordChallengeIssued(method, scopedOperation, success: false);
            return new ChallengeVerification(ChallengeVerificationOutcome.ProofRejected, null, null);
        }

        // Issue the token: 32 random bytes, base64url-encoded; persist the
        // SHA-256 hash, return the raw string to the caller.
        var rawToken = GenerateRawToken();
        var tokenHash = ComputeSha256Hex(rawToken);
        var now = DateTimeOffset.UtcNow;

        var token = new AuthChallengeToken
        {
            PlatformUserId = context.PlatformUserId,
            TokenHash = tokenHash,
            Method = method,
            ScopedOperation = scopedOperation,
            IssuedAt = now,
            ExpiresAt = now.Add(TokenLifetime)
        };

        await _repository.InsertAsync(token, cancellationToken);
        _metrics.RecordChallengeIssued(method, scopedOperation, success: true);

        _logger.LogInformation(
            "Issued auth challenge token for {PlatformUserId} method={Method} scope={ScopedOperation}",
            context.PlatformUserId, method, scopedOperation);

        return new ChallengeVerification(ChallengeVerificationOutcome.Success, rawToken, token.ExpiresAt);
    }

    private async Task<bool?> VerifyTotpAsync(
        ChallengeContext context,
        JsonElement proof,
        CancellationToken cancellationToken)
    {
        // Expected proof shape: { "code": "123456" }.
        if (!proof.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != JsonValueKind.String)
            return null;

        var code = codeProp.GetString();
        if (string.IsNullOrWhiteSpace(code))
            return null;

        return await _totp.ValidateCodeAsync(context.UserIdentityId, code, cancellationToken);
    }

    private async Task<bool?> VerifyPasswordAsync(
        ChallengeContext context,
        JsonElement proof,
        CancellationToken cancellationToken)
    {
        // Expected proof shape: { "password": "..." }.
        if (!proof.TryGetProperty("password", out var passwordProp) || passwordProp.ValueKind != JsonValueKind.String)
            return null;

        var password = passwordProp.GetString();
        if (string.IsNullOrEmpty(password))
            return null;

        var hash = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == context.PlatformUserId)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(hash))
            return false;

        return BCrypt.Net.BCrypt.Verify(password, hash);
    }

    private async Task<UserEnrolment> GetEnrolmentAsync(
        ChallengeContext context,
        CancellationToken cancellationToken)
    {
        var hasPassword = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == context.PlatformUserId)
            .Select(u => u.PasswordHash != null)
            .FirstOrDefaultAsync(cancellationToken);

        // TOTP is keyed by UserIdentity, not PlatformUser — TotpConfiguration
        // belongs to a per-org session. The bearer that drove the request
        // identifies the active UserIdentity.
        var totpEnabled = await _db.TotpConfigurations
            .AsNoTracking()
            .AnyAsync(t => t.UserId == context.UserIdentityId && t.IsEnabled, cancellationToken);

        var hasActivePasskey = await _db.PasskeyCredentials
            .AsNoTracking()
            .AnyAsync(p => p.PlatformUserId == context.PlatformUserId
                        && p.Status == CredentialStatus.Active, cancellationToken);

        var hasSocial = await _db.PlatformSocialLogins
            .AsNoTracking()
            .AnyAsync(s => s.PlatformUserId == context.PlatformUserId, cancellationToken);

        return new UserEnrolment(totpEnabled, hasPassword, hasActivePasskey, hasSocial);
    }

    private static string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "ch_" + Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string ComputeSha256Hex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private readonly record struct UserEnrolment(bool TotpEnabled, bool HasPassword, bool HasActivePasskey, bool HasSocial)
    {
        public bool IsEnrolled(ChallengeMethod method) => method switch
        {
            ChallengeMethod.Totp => TotpEnabled,
            ChallengeMethod.Password => HasPassword,
            ChallengeMethod.Passkey => HasActivePasskey,
            ChallengeMethod.ReOAuth => HasSocial,
            _ => false
        };

        public ChallengeMethod? PickStrongest()
        {
            if (TotpEnabled) return ChallengeMethod.Totp;
            if (HasPassword) return ChallengeMethod.Password;
            if (HasActivePasskey) return ChallengeMethod.Passkey;
            if (HasSocial) return ChallengeMethod.ReOAuth;
            return null;
        }
    }
}
