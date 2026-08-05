// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Provenance.Engine.Seams;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Supplies the engine's Merkle seam by delegating to the platform's one Merkle implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class must stay a delegation.</b> <see cref="MerkleTree.ComputeMerkleRoot"/> is the
/// algorithm that <c>DocketBuilder</c>, <c>DocketConfirmer</c>, <c>GenesisManager</c> and this
/// service's own inclusion-proof generation all use to produce and verify roots. The Seal check
/// compares a recomputation against what a validator actually sealed, so if this recomputed by any
/// other route the two would agree right up until they didn't — and the disagreement would surface
/// as a <i>false</i> tamper report, which is the most damaging output this feature can produce.
/// </para>
/// <para>
/// A second Merkle implementation is the duplicate-projection defect Feature 187 existed to fix
/// (research R-003, plan D3/D4). If you find yourself editing the body below to do arithmetic, the
/// change belongs in <c>MerkleTree</c> instead — where every other caller will get it too.
/// </para>
/// <para>
/// Per plan D4, issue #1372's remaining scope is the proof-generation and chain-integrity endpoints
/// calling this same seam rather than growing their own comparison.
/// </para>
/// </remarks>
public sealed class MerkleRootCalculator : IMerkleRootCalculator
{
    private readonly MerkleTree _merkleTree;

    /// <summary>Creates a calculator over the platform's hash provider.</summary>
    public MerkleRootCalculator(IHashProvider hashProvider)
    {
        _merkleTree = new MerkleTree(hashProvider);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Ids are passed through in stored order. Order is part of the commitment — sorting here would
    /// let a reordering pass the Seal check.
    /// </remarks>
    public string ComputeRoot(IReadOnlyList<string> transactionIds) =>
        _merkleTree.ComputeMerkleRoot(transactionIds);
}
