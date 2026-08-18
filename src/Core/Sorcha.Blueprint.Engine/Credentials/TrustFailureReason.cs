// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Why a trust decision rejected a credential (feature 135). Fail-closed: any value
/// other than a successful decision means the credential is not trusted.
/// </summary>
public enum TrustFailureReason
{
    /// <summary>No configured trust source vouched for the issuer.</summary>
    UntrustedIssuer,

    /// <summary>The issuer signature did not verify.</summary>
    SignatureInvalid,

    /// <summary>The credential is revoked per its status list. Terminal — it cannot come back.</summary>
    Revoked,

    /// <summary>
    /// The credential is suspended per its status list. Refused exactly like a revocation, but
    /// REVERSIBLE — the issuer may reinstate it. Kept distinct from <see cref="Revoked"/> so a
    /// verifier can respond proportionately and a holder is not told a temporary pause is final.
    /// </summary>
    Suspended,

    /// <summary>Revocation status could not be resolved and policy is fail-closed.</summary>
    RevocationUnavailable,

    /// <summary>A required trust source was unreachable.</summary>
    SourceUnavailable,

    /// <summary>The established assurance level is below the policy minimum.</summary>
    InsufficientAssurance,

    /// <summary>An X.509 certificate chain failed to validate to a trusted root.</summary>
    ChainInvalid,

    /// <summary>The holder binding (key-binding / device authentication) did not verify.</summary>
    HolderBindingInvalid,

    /// <summary>Disclosed values did not match the credential's integrity digests.</summary>
    IntegrityFailure,

    /// <summary>The presented credential format is not the one the requirement accepts.</summary>
    FormatUnsupported
}
