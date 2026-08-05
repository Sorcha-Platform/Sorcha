// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine.Evidence;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Everything the engine needs about one docket, gathered from storage.
/// </summary>
/// <param name="Docket">The docket's stored evidence.</param>
/// <param name="Anchor">What this node can say about the register's origin and its own anchor.</param>
public sealed record DocketEvidenceBundle(DocketEvidence Docket, AnchorEvidence Anchor);

/// <summary>
/// Gathers a docket's stored evidence for the engine, which cannot reach storage itself (plan D1).
/// </summary>
public interface IDocketEvidenceAssembler
{
    /// <summary>
    /// Assembles evidence for one docket, or returns null when this node holds no such docket.
    /// </summary>
    /// <remarks>
    /// Null means "no such docket here" and becomes a 404. Everything else — a missing predecessor,
    /// an absent sealed root, no recorded proposer, an unknown anchor — is assembled as a null field
    /// that the engine renders <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>
    /// with a reason. Implementations must not throw for missing evidence (FR-020).
    /// </remarks>
    Task<DocketEvidenceBundle?> AssembleAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default);
}
