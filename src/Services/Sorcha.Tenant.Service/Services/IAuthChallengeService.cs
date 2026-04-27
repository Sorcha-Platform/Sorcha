// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Issues and verifies short-lived re-authentication challenges that gate
/// every sensitive auth-method mutation in the Tenant Service. Picks the
/// strongest available proof factor per the Feature 116 ladder:
/// TOTP → Password → Passkey step-up → re-OAuth.
/// </summary>
public interface IAuthChallengeService
{
    /// <summary>
    /// Begin a challenge for the given operation. Selects a method per the
    /// ladder (or honours <paramref name="preferredMethod"/> if it is enrolled).
    /// Returns the prepared challenge — the caller is expected to render the
    /// appropriate proof UI (TOTP code input, password input, WebAuthn
    /// assertion options, OAuth redirect) and call <see cref="VerifyAsync"/>
    /// once the user has provided proof.
    /// </summary>
    /// <returns>
    /// <see cref="ChallengePreparation.NoMethodAvailable"/> when the user has
    /// no enrolled method (only reachable in the bootstrap edge case);
    /// otherwise a populated <see cref="ChallengePreparation"/>.
    /// </returns>
    Task<ChallengePreparation> InitiateAsync(
        ChallengeContext context,
        ScopedOperation scopedOperation,
        ChallengeMethod? preferredMethod,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify the user's proof for an in-flight challenge. On success,
    /// persists a single-use <see cref="AuthChallengeToken"/> and returns
    /// the raw token string. The raw token is never available again — only
    /// the SHA-256 hash is stored.
    /// </summary>
    Task<ChallengeVerification> VerifyAsync(
        ChallengeContext context,
        ChallengeMethod method,
        ScopedOperation scopedOperation,
        JsonElement proof,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the user across the two id systems involved in re-authentication.
/// <see cref="PlatformUserId"/> scopes the challenge token (account-wide).
/// <see cref="UserIdentityId"/> is what TOTP, passkey, and other per-identity
/// services key their state by — derived from the active session's bearer claim.
/// </summary>
public readonly record struct ChallengeContext(Guid PlatformUserId, Guid UserIdentityId);

/// <summary>
/// Result of <see cref="IAuthChallengeService.InitiateAsync"/>. The caller
/// uses <see cref="Method"/> to render the matching proof UI; <see cref="Payload"/>
/// (when non-null) carries method-specific data (e.g. WebAuthn assertion options).
/// </summary>
public sealed record ChallengePreparation(
    ChallengeMethod Method,
    JsonElement? Payload)
{
    /// <summary>
    /// Sentinel returned when the user has no enrolled method capable of
    /// producing a proof. Only reachable in the bootstrap edge case.
    /// </summary>
    public static ChallengePreparation NoMethodAvailable { get; } =
        new((ChallengeMethod)(-1), null);

    /// <summary>True iff this preparation is the no-method-available sentinel.</summary>
    public bool IsAvailable => (int)Method >= 0;
}

/// <summary>
/// Result of <see cref="IAuthChallengeService.VerifyAsync"/>.
/// </summary>
/// <param name="Outcome">Whether the proof was accepted.</param>
/// <param name="Token">
/// Raw token string (returned only on success; never persisted). Caller
/// returns this to the user in the response body — the user presents it in
/// the <c>X-Auth-Challenge</c> header on the subsequent mutation call.
/// </param>
/// <param name="ExpiresAt">When the issued token expires (UTC).</param>
public sealed record ChallengeVerification(
    ChallengeVerificationOutcome Outcome,
    string? Token,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>True iff the proof was accepted and a token was issued.</summary>
    public bool Succeeded => Outcome == ChallengeVerificationOutcome.Success;
}

/// <summary>
/// Why a verification did or did not succeed.
/// </summary>
public enum ChallengeVerificationOutcome
{
    /// <summary>Proof accepted, token issued.</summary>
    Success = 0,

    /// <summary>Proof rejected (wrong code / wrong password / etc.).</summary>
    ProofRejected = 1,

    /// <summary>The requested method is not enrolled for this user.</summary>
    MethodNotAvailable = 2,

    /// <summary>The proof shape was invalid (missing field, wrong type).</summary>
    InvalidProofShape = 3
}
