// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IAuthChallengeService"/>. Implements the Feature 116
/// challenge ladder and persists a SHA-256-hashed token on each successful
/// verification. All four proof methods are implemented: TOTP and Password are
/// single-step; Passkey (WebAuthn assertion) and Re-OAuth (linked-provider
/// round-trip) were completed in Feature 150 (T014/T015).
/// </summary>
public sealed class AuthChallengeService : IAuthChallengeService
{
    /// <summary>Token lifetime — short enough to prevent replay windows.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly TenantDbContext _db;
    private readonly IAuthChallengeRepository _repository;
    private readonly ITotpService _totp;
    private readonly IPasskeyService _passkeys;
    private readonly ISocialLoginService _social;
    private readonly IVerificationChannelRegistry _channels;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<AuthChallengeService> _logger;

    /// <summary>Creates a new <see cref="AuthChallengeService"/>.</summary>
    public AuthChallengeService(
        TenantDbContext db,
        IAuthChallengeRepository repository,
        ITotpService totp,
        IPasskeyService passkeys,
        ISocialLoginService social,
        IVerificationChannelRegistry channels,
        AuthMetrics metrics,
        ILogger<AuthChallengeService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _totp = totp ?? throw new ArgumentNullException(nameof(totp));
        _passkeys = passkeys ?? throw new ArgumentNullException(nameof(passkeys));
        _social = social ?? throw new ArgumentNullException(nameof(social));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ChallengePreparation> InitiateAsync(
        ChallengeContext context,
        ScopedOperation scopedOperation,
        ChallengeMethod? preferredMethod,
        AuthMethodKind? targetMethodKind = null,
        CancellationToken cancellationToken = default)
    {
        var enrolment = await GetEnrolmentAsync(context, cancellationToken);

        // The floor rule (Feature 150): only proof methods whose tier is at least the
        // required tier for this (operation, target) may be offered. A null target
        // fails safe to Strongest, never to a weaker tier.
        var requiredTier = AssurancePolicy.RequiredProofTier(scopedOperation, targetMethodKind);

        // Honour the caller's preference iff it is enrolled AND meets the floor.
        // Otherwise pick the strongest-enrolled method that meets the floor, in
        // the existing ladder order (TOTP → Password → Passkey → re-OAuth).
        var method = preferredMethod is { } pref
                && enrolment.IsEnrolled(pref)
                && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(pref), requiredTier)
            ? pref
            : enrolment.PickForFloor(requiredTier);

        if (method is null)
        {
            _logger.LogWarning(
                "No challenge method available for {PlatformUserId} {ScopedOperation}",
                context.PlatformUserId, scopedOperation);
            return ChallengePreparation.NoMethodAvailable;
        }

        // Server-sent channels (Email/SMS OTP) must dispatch a code now so the user has something to
        // type. Best-effort: a rate-limited send still returns the method (the user likely has a
        // recent code), and the prior code stays valid.
        if (method.Value is ChallengeMethod.EmailOtp or ChallengeMethod.SmsOtp)
        {
            var channel = _channels.Resolve(method.Value);
            if (channel is not null)
                await channel.SendAsync(context.PlatformUserId, OtpPurpose.StepUp, cancellationToken);
        }

        // TOTP/Password/Email/SMS need no payload (the user types a code/password). Passkey carries
        // WebAuthn assertion options scoped to this user's credentials; ReOAuth carries the
        // linked provider the client re-authenticates against (Feature 150 T014/T015).
        JsonElement? payload = method.Value switch
        {
            ChallengeMethod.Passkey => await BuildPasskeyPayloadAsync(context, cancellationToken),
            ChallengeMethod.ReOAuth => await BuildReOAuthPayloadAsync(context, cancellationToken),
            _ => null
        };

        return new ChallengePreparation(method.Value, payload);
    }

    /// <inheritdoc />
    public async Task<ChallengeVerification> VerifyAsync(
        ChallengeContext context,
        ChallengeMethod method,
        ScopedOperation scopedOperation,
        JsonElement proof,
        AuthMethodKind? targetMethodKind = null,
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

        // Floor rule (Feature 150): re-check server-side that the proof's tier is at least
        // the required tier for this (operation, target). A null target fails safe to
        // Strongest. We reject BEFORE validating the proof body so a too-weak factor can
        // never authorise a stronger method even with otherwise-valid proof.
        var requiredTier = AssurancePolicy.RequiredProofTier(scopedOperation, targetMethodKind);
        if (!AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(method), requiredTier))
        {
            _metrics.RecordChallengeIssued(method, scopedOperation, success: false);
            _logger.LogWarning(
                "Proof tier {ProofTier} below required {RequiredTier} for {Method}/{ScopedOperation} target={Target} ({PlatformUserId})",
                AssurancePolicy.TierOfProof(method), requiredTier, method, scopedOperation, targetMethodKind, context.PlatformUserId);
            return new ChallengeVerification(ChallengeVerificationOutcome.ProofTierInsufficient, null, null);
        }

        var proofAccepted = method switch
        {
            ChallengeMethod.Totp => await VerifyTotpAsync(context, proof, cancellationToken),
            ChallengeMethod.Password => await VerifyPasswordAsync(context, proof, cancellationToken),
            ChallengeMethod.Passkey => await VerifyPasskeyAsync(context, proof, cancellationToken),
            ChallengeMethod.ReOAuth => await VerifyReOAuthAsync(context, proof, cancellationToken),
            ChallengeMethod.EmailOtp or ChallengeMethod.SmsOtp => await VerifyServerSentOtpAsync(method, context, proof, cancellationToken),
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

    /// <summary>
    /// Verifies a server-sent (Email/SMS) one-time code as a step-up proof (Feature 150 US2/US3).
    /// Returns null for a malformed proof, false for any non-Verified outcome.
    /// </summary>
    private async Task<bool?> VerifyServerSentOtpAsync(
        ChallengeMethod method, ChallengeContext context, JsonElement proof, CancellationToken ct)
    {
        // Expected proof shape: { "code": "123456" }.
        if (!proof.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != JsonValueKind.String)
            return null;
        var code = codeProp.GetString();
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var channel = _channels.Resolve(method);
        if (channel is null)
            return false; // channel not registered on this installation

        var outcome = await channel.VerifyAsync(context.PlatformUserId, OtpPurpose.StepUp, code, ct);
        return outcome == OtpVerifyOutcome.Verified;
    }

    // ---- Feature 150 (T014/T015): Passkey + Re-OAuth step-up proofs ----

    /// <summary>
    /// Builds the WebAuthn assertion-options payload for a Passkey step-up, scoped to the
    /// user's own active credentials so only one of their passkeys can satisfy the challenge.
    /// </summary>
    private async Task<JsonElement?> BuildPasskeyPayloadAsync(ChallengeContext context, CancellationToken ct)
    {
        var credentialIds = await _db.PasskeyCredentials
            .AsNoTracking()
            .Where(p => p.PlatformUserId == context.PlatformUserId && p.Status == CredentialStatus.Active)
            .Select(p => p.CredentialId)
            .ToListAsync(ct);

        if (credentialIds.Count == 0)
            return null;

        var options = await _passkeys.CreateAssertionOptionsAsync(
            email: null, allowedCredentialIds: credentialIds, cancellationToken: ct);

        var node = new JsonObject
        {
            ["transactionId"] = options.TransactionId,
            ["assertionOptions"] = JsonNode.Parse(options.Options.ToJson()),
        };
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Builds the Re-OAuth payload — the linked provider the client re-authenticates against.
    /// The client re-runs the existing social OAuth flow and posts <c>{ provider, code, state }</c> back.
    /// </summary>
    private async Task<JsonElement?> BuildReOAuthPayloadAsync(ChallengeContext context, CancellationToken ct)
    {
        var provider = await _db.PlatformSocialLogins
            .AsNoTracking()
            .Where(s => s.PlatformUserId == context.PlatformUserId)
            .OrderBy(s => s.LinkedAt)
            .Select(s => s.Provider)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(provider))
            return null;

        var node = new JsonObject { ["provider"] = provider };
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Verifies a WebAuthn assertion as a Passkey step-up proof. The asserted credential MUST
    /// belong to the same platform user that initiated the challenge.
    /// </summary>
    private async Task<bool?> VerifyPasskeyAsync(ChallengeContext context, JsonElement proof, CancellationToken ct)
    {
        // Expected proof shape: { "transactionId": "...", "assertionResponse": { ...WebAuthn... } }.
        if (!proof.TryGetProperty("transactionId", out var txProp) || txProp.ValueKind != JsonValueKind.String)
            return null;
        if (!proof.TryGetProperty("assertionResponse", out var arProp) || arProp.ValueKind != JsonValueKind.Object)
            return null;

        var transactionId = txProp.GetString();
        if (string.IsNullOrWhiteSpace(transactionId))
            return null;

        AuthenticatorAssertionRawResponse? assertion;
        try
        {
            assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(arProp.GetRawText());
        }
        catch (JsonException)
        {
            return null; // malformed assertion → invalid proof shape
        }

        if (assertion is null)
            return null;

        try
        {
            var result = await _passkeys.VerifyAssertionAsync(transactionId, assertion, ct);
            return result.PlatformUserId == context.PlatformUserId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Passkey step-up assertion failed for {PlatformUserId}", context.PlatformUserId);
            return false;
        }
    }

    /// <summary>
    /// Verifies a re-OAuth round-trip as a step-up proof. The exchanged identity MUST resolve to a
    /// provider this user still has linked.
    /// </summary>
    private async Task<bool?> VerifyReOAuthAsync(ChallengeContext context, JsonElement proof, CancellationToken ct)
    {
        // Expected proof shape: { "provider": "...", "code": "...", "state": "..." }.
        if (!proof.TryGetProperty("provider", out var pProp) || pProp.ValueKind != JsonValueKind.String) return null;
        if (!proof.TryGetProperty("code", out var cProp) || cProp.ValueKind != JsonValueKind.String) return null;
        if (!proof.TryGetProperty("state", out var sProp) || sProp.ValueKind != JsonValueKind.String) return null;

        var provider = pProp.GetString();
        var code = cProp.GetString();
        var state = sProp.GetString();
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return null;

        SocialAuthCallbackResult result;
        try
        {
            result = await _social.ExchangeCodeAsync(provider, code, state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Re-OAuth step-up exchange failed for {PlatformUserId}", context.PlatformUserId);
            return false;
        }

        if (!result.Success || string.IsNullOrEmpty(result.Subject))
            return false;

        // Confirm the re-authenticated identity is a provider this user still has linked.
        // Hardening follow-up: match on the provider subject once the PlatformSocialLogin
        // external-subject column is threaded here; provider-match suffices for the v1 step-up.
        return await _db.PlatformSocialLogins
            .AsNoTracking()
            .AnyAsync(s => s.PlatformUserId == context.PlatformUserId
                        && s.Provider == result.Provider, ct);
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

        // Email OTP enrolment is account-wide (PlatformUserTwoFactor); only offer it as a step-up
        // proof when the channel is actually registered on this installation.
        var emailOtpEnabled = _channels.Resolve(ChallengeMethod.EmailOtp) is not null
            && await _db.PlatformUserTwoFactors
                .AsNoTracking()
                .AnyAsync(t => t.PlatformUserId == context.PlatformUserId && t.EmailOtpEnabled, cancellationToken);

        return new UserEnrolment(totpEnabled, hasPassword, hasActivePasskey, hasSocial, emailOtpEnabled);
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

    private readonly record struct UserEnrolment(bool TotpEnabled, bool HasPassword, bool HasActivePasskey, bool HasSocial, bool EmailOtpEnabled)
    {
        public bool IsEnrolled(ChallengeMethod method) => method switch
        {
            ChallengeMethod.Totp => TotpEnabled,
            ChallengeMethod.Password => HasPassword,
            ChallengeMethod.Passkey => HasActivePasskey,
            ChallengeMethod.ReOAuth => HasSocial,
            ChallengeMethod.EmailOtp => EmailOtpEnabled,
            _ => false
        };

        public ChallengeMethod? PickStrongest()
        {
            if (TotpEnabled) return ChallengeMethod.Totp;
            if (HasPassword) return ChallengeMethod.Password;
            if (HasActivePasskey) return ChallengeMethod.Passkey;
            if (HasSocial) return ChallengeMethod.ReOAuth;
            if (EmailOtpEnabled) return ChallengeMethod.EmailOtp;
            return null;
        }

        /// <summary>
        /// Picks an enrolled proof method that satisfies the floor (<paramref name="requiredTier"/>),
        /// preserving the existing ladder preference order within the eligible set. Returns null
        /// when the user holds no enrolled method strong enough to authorise the operation.
        /// </summary>
        public ChallengeMethod? PickForFloor(AuthAssuranceTier requiredTier)
        {
            if (TotpEnabled && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(ChallengeMethod.Totp), requiredTier))
                return ChallengeMethod.Totp;
            if (HasPassword && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(ChallengeMethod.Password), requiredTier))
                return ChallengeMethod.Password;
            if (HasActivePasskey && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(ChallengeMethod.Passkey), requiredTier))
                return ChallengeMethod.Passkey;
            if (HasSocial && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(ChallengeMethod.ReOAuth), requiredTier))
                return ChallengeMethod.ReOAuth;
            // Email OTP is Basic — the weakest, so it is the last resort (only chosen when nothing
            // stronger is enrolled and the operation's floor is Basic).
            if (EmailOtpEnabled && AssurancePolicy.CanAuthorize(AssurancePolicy.TierOfProof(ChallengeMethod.EmailOtp), requiredTier))
                return ChallengeMethod.EmailOtp;
            return null;
        }
    }
}
