// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// Outcome of <c>POST /api/auth/challenge/verify</c> as the dialog sees it. On
/// <see cref="Succeeded"/> the caller receives the opaque <see cref="Token"/> for the subsequent
/// mutation call (presented via the <c>X-Auth-Challenge</c> header).
/// </summary>
/// <remarks>
/// <b>This is a client-side outcome wrapper, not a wire contract.</b> It folds the server's
/// <c>ChallengeVerifyResponse</c> body together with the HTTP status into one value the dialog can
/// switch on, so it deliberately does not mirror any server type. The wire DTOs themselves —
/// <c>ChallengeInitiateRequest</c>/<c>Response</c>, <c>ChallengeVerifyRequest</c>/<c>Response</c>
/// and the <c>ChallengeMethod</c>/<c>ScopedOperation</c>/<c>AuthMethodKind</c> enums — live in
/// <c>Sorcha.Tenant.Models.Auth</c> and are shared with the Tenant Service.
/// </remarks>
public sealed record ChallengeVerifyResult(bool Succeeded, string? Token, ChallengeVerifyError Error);

/// <summary>
/// Failure reasons surfaced to the dialog. Derived from the HTTP status by the client — not a
/// server-sent value.
/// </summary>
public enum ChallengeVerifyError
{
    /// <summary>No failure — the call succeeded.</summary>
    None = 0,

    /// <summary>The proof was rejected (wrong TOTP code, wrong password, etc.).</summary>
    ProofRejected = 1,

    /// <summary>The challenge has already been verified or expired (5-minute lifetime).</summary>
    Expired = 2,

    /// <summary>Transport or unexpected status — caller should offer retry.</summary>
    Failed = 3,

    /// <summary>
    /// The proof was valid but its assurance tier is below the floor required for the operation
    /// (server returned <c>403 proof_tier_insufficient</c>, Feature 150). The dialog should guide
    /// the user to a stronger proof method.
    /// </summary>
    ProofTierInsufficient = 4,
}
