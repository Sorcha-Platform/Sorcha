// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Typed HTTP client for the HAIP verifier endpoints consumed by <see cref="HaipVerificationTransport"/>
/// (Feature 164, B3). WASM-safe — depends only on <see cref="HttpClient"/>.
/// </summary>
public interface IHaipVerifierClient
{
    /// <summary>Creates an OID4VP presentation request and returns the session id and QR deep link.</summary>
    Task<HaipCreateResult> CreateRequestAsync(
        string clientId,
        string credentialType,
        IReadOnlyList<string> requiredClaims,
        CancellationToken ct = default);

    /// <summary>
    /// Polls for the holder's submission; returns the current state and, when complete, the raw vp_token.
    /// </summary>
    Task<HaipPollResult> PollResultAsync(string requestId, CancellationToken ct = default);
}

/// <summary>Result from creating a HAIP presentation request.</summary>
/// <param name="RequestId">The opaque request identifier used to poll.</param>
/// <param name="AuthorizationRequestUri">The <c>openid4vp://</c> deep link for QR rendering.</param>
public sealed record HaipCreateResult(string RequestId, string AuthorizationRequestUri);

/// <summary>Result of polling a HAIP verification request.</summary>
/// <param name="State">Server-side state string (Pending / Submitted / Verified / Denied / Expired / Cancelled).</param>
/// <param name="VpToken">The raw vp_token when submitted; null otherwise.</param>
/// <param name="PresentationSubmission">The OID4VP presentation_submission, when present.</param>
/// <param name="IsValid">HAIP's authoritative validity, when a result object is present.</param>
/// <param name="VerifiedClaims">Disclosed claim name → value, from HAIP's verified result.</param>
/// <param name="Errors">HAIP's rejection reasons, when present.</param>
/// <param name="HolderKeyVerified">Whether HAIP verified the holder key binding.</param>
public sealed record HaipPollResult(
    string State,
    string? VpToken,
    string? PresentationSubmission,
    bool? IsValid = null,
    IReadOnlyDictionary<string, object?>? VerifiedClaims = null,
    IReadOnlyList<string>? Errors = null,
    bool HolderKeyVerified = false);
