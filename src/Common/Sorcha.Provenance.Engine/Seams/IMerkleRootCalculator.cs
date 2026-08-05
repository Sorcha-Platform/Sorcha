// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Seams;

/// <summary>
/// Recomputes a docket's Merkle root from its transaction ids, so the Seal check can compare the
/// recomputation against the root the proposing validator actually sealed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a seam rather than a method (research R-003, plan D3).</b> The algorithm already
/// exists exactly once, as <c>MerkleTree.ComputeMerkleRoot</c> in
/// <c>src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs</c>, and is what
/// <c>DocketBuilder</c>, <c>DocketConfirmer</c>, <c>GenesisManager</c> and the Register Service's
/// proof generation all use. The engine cannot reference that assembly: it P/Invokes libsodium and
/// cannot load under browser-wasm — the documented reason <c>Sorcha.Mdoc</c> was extracted from it
/// during Feature 185 — and the engine exists precisely so it can run where the service cannot.
/// </para>
/// <para>
/// <b>Implement it by delegating, never by reimplementing.</b> A second Merkle implementation is the
/// duplicate-projection defect Feature 187 existed to fix: two implementations agree right up until
/// they don't, and the disagreement surfaces here as a <i>false</i> tamper report — the single most
/// damaging output this feature can produce. The Register Service's implementation is a one-line
/// delegation for exactly this reason.
/// </para>
/// <para>
/// Mirrors the established seam pattern (<c>IRevocationChecker</c>, <c>IIssuerKeyResolver</c>,
/// <c>ITenantTrustAnchorProvider</c> in Feature 135): the engine declares the contract, the service
/// supplies the implementation across the boundary.
/// </para>
/// </remarks>
public interface IMerkleRootCalculator
{
    /// <summary>
    /// Computes the Merkle root over <paramref name="transactionIds"/> in the given order, using the
    /// same algorithm that sealed the docket.
    /// </summary>
    /// <param name="transactionIds">
    /// The docket's transaction ids, in stored order. Order is part of the commitment — do not sort.
    /// </param>
    /// <returns>
    /// The recomputed root, in the same encoding the register stores (lowercase hex). Never null;
    /// an empty transaction list has a well-defined root.
    /// </returns>
    string ComputeRoot(IReadOnlyList<string> transactionIds);
}
