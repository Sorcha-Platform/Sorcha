// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Context about the credential issuer presented to a trust source for evaluation
/// (feature 135). Format-agnostic — populated by the format handler before the trust
/// evaluator runs.
/// </summary>
public class IssuerContext
{
    /// <summary>The issuer identifier as carried in the credential (DID URI or certificate subject).</summary>
    public string IssuerId { get; set; } = string.Empty;

    /// <summary>The credential format being evaluated.</summary>
    public CredentialFormat Format { get; set; }

    /// <summary>The leaf-first X.509 certificate chain (DER), when the credential carries one.</summary>
    public IReadOnlyList<byte[]>? X5cChain { get; set; }

    /// <summary>An explicit assurance-level claim carried by the credential, if any (e.g. eIDAS LoA).</summary>
    public AssuranceLevel? ClaimedAssurance { get; set; }

    /// <summary>Whether the issuer signature has already been verified by the format handler.</summary>
    public bool SignatureVerified { get; set; }
}

/// <summary>
/// A single trust source's answer about an issuer (feature 135).
/// </summary>
public class TrustSourceVouch
{
    /// <summary>Whether this source vouches for the issuer.</summary>
    public bool Vouched { get; set; }

    /// <summary>The assurance level this source confers (when it vouches).</summary>
    public AssuranceLevel Assurance { get; set; } = AssuranceLevel.Low;

    /// <summary>Why the source declined, when <see cref="Vouched"/> is false.</summary>
    public TrustFailureReason? Reason { get; set; }

    /// <summary>Evidence fragment merged into the final <see cref="TrustEvidence"/>.</summary>
    public Action<TrustEvidence>? ApplyEvidence { get; set; }

    /// <summary>Convenience factory for a declining vouch.</summary>
    public static TrustSourceVouch Decline(TrustFailureReason reason) =>
        new() { Vouched = false, Reason = reason };
}

/// <summary>
/// Resolves whether one kind of trust source vouches for a credential issuer (feature 135).
/// One implementation per <see cref="TrustSourceKind"/>. Network-bound implementations are
/// injected from the service layer; the engine ships WASM-safe in-memory variants for
/// offline verification.
/// </summary>
public interface ITrustSourceResolver
{
    /// <summary>The trust source kind this resolver handles.</summary>
    TrustSourceKind Kind { get; }

    /// <summary>Asks this source whether it vouches for the issuer described by <paramref name="issuer"/>.</summary>
    Task<TrustSourceVouch> VouchAsync(
        IssuerContext issuer,
        TrustSourceRef source,
        CancellationToken cancellationToken = default);
}
