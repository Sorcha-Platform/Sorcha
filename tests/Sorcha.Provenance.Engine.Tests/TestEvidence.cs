// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Provenance.Engine.Seams;

namespace Sorcha.Provenance.Engine.Tests;

/// <summary>
/// Hand-built evidence for the engine tests.
/// </summary>
/// <remarks>
/// Adversarial by construction: the point of the engine boundary is that tampered, partial and
/// pre-Feature-187 evidence can all be built by hand, with no database, no node and no register.
/// Every helper here produces <i>healthy</i> evidence; each test then breaks exactly one thing, so a
/// failure names the thing that was broken.
/// </remarks>
internal static class TestEvidence
{
    internal const string RegisterId = "b388e51816e34d4ea7ce275ca7e8219c";
    internal const string AnchorFingerprint = "cfbac8f41d2e4a7b93c0517fa6e8d240";

    /// <summary>A validator whose key is authorised in the roster version under test.</summary>
    internal static RosterEntryAsOf Validator(string id, string key, bool heldAuthority = true) => new()
    {
        ValidatorId = id,
        PublicKey = key,
        HeldAuthority = heldAuthority,
    };

    /// <summary>A roster version resolved from a named control transaction.</summary>
    internal static RosterAsOf Roster(int version, params RosterEntryAsOf[] entries) => new()
    {
        RosterVersion = version,
        Entries = entries,
        ResolvedFrom = $"control transaction ctrl-v{version}",
    };

    /// <summary>A recorded vote claiming to be from <paramref name="validatorId"/>.</summary>
    internal static DocketVoteEvidence Vote(string validatorId, string publicKey) => new()
    {
        ValidatorId = validatorId,
        PublicKey = publicKey,
        Decision = "Approve",
    };

    /// <summary>
    /// A healthy sealed docket: linked to its predecessor, with a sealed root that matches a
    /// recomputation over its stored transaction ids, a proposer, and one recorded vote.
    /// </summary>
    internal static DocketEvidence HealthyDocket(
        ulong number = 10,
        string proposer = "validator-a",
        string proposerKey = "key-a") => new()
    {
        DocketNumber = number,
        Hash = $"hash-{number}",
        ClaimedPreviousHash = $"hash-{number - 1}",
        PredecessorHash = $"hash-{number - 1}",
        TransactionIds = ["tx-1", "tx-2", "tx-3"],
        LeafHashes = LeavesFor(["tx-1", "tx-2", "tx-3"]),
        SealedMerkleRoot = StubMerkle.RootOf(LeavesFor(["tx-1", "tx-2", "tx-3"])),
        ProposerValidatorId = proposer,
        Votes = [Vote(proposer, proposerKey)],
    };

    /// <summary>
    /// The Merkle leaves a docket actually commits to — a per-transaction composite hash, NOT the
    /// raw id. Stands in for <c>DocketHasher.ComputeTransactionHash</c>, whose real inputs are
    /// (TransactionId, PayloadHash, Timestamp).
    /// </summary>
    internal static IReadOnlyList<string> LeavesFor(IReadOnlyList<string> transactionIds) =>
        transactionIds.Select(id => $"leaf({id})").ToList();

    /// <summary>An anchor this node holds, with the register's origin tracing to it.</summary>
    internal static AnchorEvidence TraceableAnchor() => new()
    {
        IsAnchorKnown = true,
        AnchorFingerprint = AnchorFingerprint,
        OriginFingerprint = AnchorFingerprint,
        OriginDescription = "the system register's pre-signed genesis transaction",
    };
}

/// <summary>
/// A deterministic stand-in for the real Merkle algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The engine's job is to <i>compare</i> a recomputation against the sealed commitment; which
/// algorithm produced either is deliberately not its business (that is the whole point of
/// <see cref="IMerkleRootCalculator"/>). So these tests use a stub whose output is trivially
/// predictable, which keeps every Seal assertion about the comparison rather than about hashing.
/// </para>
/// <para>
/// It is order-sensitive, like the real one — order is part of the commitment, and a stub that
/// sorted would let an order-losing bug through.
/// </para>
/// </remarks>
internal sealed class StubMerkle : IMerkleRootCalculator
{
    internal static string RootOf(IReadOnlyList<string> ids) => $"root({string.Join("|", ids)})";

    public string ComputeRoot(IReadOnlyList<string> transactionIds) => RootOf(transactionIds);
}
