// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Models.Verification;

/// <summary>
/// Live result of a verification flow run (Feature 125, T036). This is the
/// in-memory shape passed from <c>IVerifierEngine.VerifyAsync</c> through
/// <c>VerifyFlow</c> to the trust panel UI. Distinct from the persisted
/// <c>Sorcha.Wallet.Pwa.Services.VerificationRecord</c> — that's the
/// historical artefact stored in IndexedDB for replay; this is the
/// transient artefact used to render the trust outcome and to feed
/// <see cref="TrustPanelJson"/> into the record at the end of the flow.
/// </summary>
/// <param name="Outcome">Pass / Warn / Fail — drives the trust panel colour and tone.</param>
/// <param name="HolderDisplayName">Display name extracted from the verified credential's holder.</param>
/// <param name="IssuerOrgName">Display name of the issuing organisation.</param>
/// <param name="CredentialType">Credential VCT (e.g. <c>WaterEngineerCredential/v1</c>).</param>
/// <param name="DisclosedClaims">Claim name → value map disclosed by the holder in this presentation.</param>
/// <param name="Messages">Human-readable diagnostics — empty on a clean pass; warnings or rejection reasons otherwise.</param>
/// <param name="VerifiedAt">UTC time the engine completed validation.</param>
/// <param name="TrustPanelJson">Serialised state the trust panel uses; persisted alongside the historical record so re-display is exact.</param>
public sealed record VerificationResult(
    VerifyOutcome Outcome,
    string HolderDisplayName,
    string IssuerOrgName,
    string CredentialType,
    IReadOnlyDictionary<string, object?> DisclosedClaims,
    IReadOnlyList<string> Messages,
    DateTimeOffset VerifiedAt,
    string TrustPanelJson);

/// <summary>
/// Trust verdict of a verification flow. Mirrors the storage-side
/// <c>Sorcha.Wallet.Pwa.Services.VerifyOutcome</c> — kept as separate types
/// so the library doesn't depend on the PWA project. Convert between them
/// via name (both enums share the three values).
/// </summary>
public enum VerifyOutcome
{
    /// <summary>All checks passed.</summary>
    Pass,
    /// <summary>At least one check produced a warning (e.g. status list unreachable).</summary>
    Warn,
    /// <summary>At least one check failed (e.g. revoked credential, invalid signature).</summary>
    Fail
}
