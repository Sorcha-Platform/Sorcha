// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Closed-set reason codes for a declined presentation. Consumers MUST map
/// verifier-specific errors onto this enum; <see cref="VerifierError"/> is the
/// catch-all for infrastructure failures that don't fit any other category.
/// </summary>
public enum PresentationDeclineReason
{
    /// <summary>
    /// Credential was valid when issued but has since expired.
    /// </summary>
    ExpiredCredential,

    /// <summary>
    /// Credential was issued by an issuer not in the blueprint's accepted list.
    /// </summary>
    WrongIssuer,

    /// <summary>
    /// Credential has been revoked by the issuer (status-list check failed).
    /// </summary>
    Revoked,

    /// <summary>
    /// Presentation payload did not match the required claim schema.
    /// </summary>
    SchemaMismatch,

    /// <summary>
    /// Cryptographic signature on the credential or presentation failed verification.
    /// </summary>
    SignatureInvalid,

    /// <summary>
    /// An open-participant action was bound to a different submitter while this
    /// presentation was in flight; the action is no longer available for this
    /// citizen.
    /// </summary>
    ActionNoLongerAvailable,

    /// <summary>
    /// Catch-all for infrastructure failures in the verifier (timeouts, network
    /// errors, unexpected backend state). Use only when no other reason fits.
    /// </summary>
    VerifierError
}
