// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Register.Service.Verification;

/// <summary>
/// The one rule for rebuilding a docket's Merkle leaves, and the one comparison against the root the
/// proposing validator actually sealed (issue #1372, Feature 187 US3).
/// </summary>
/// <remarks>
/// <para>
/// <b>A docket does not commit to its transaction ids.</b> <c>DocketBuilder</c> builds the tree from
/// per-transaction composite hashes of <c>(TransactionId, PayloadHash, Timestamp)</c> via
/// <see cref="DocketHasher.ComputeTransactionHash"/>. Recomputing over raw ids does not merely give a
/// different answer occasionally — it mismatches on <i>every</i> docket of <i>every</i> register, so a
/// cross-check built that way reports tampering on a healthy ledger. That is the most damaging output
/// this work can produce, and it is how the same mistake was caught in Feature 188: by running the
/// check against real n1 dockets and getting <c>seal: failed</c> on a sound one.
/// </para>
/// <para>
/// <b>Leaves follow the docket's own <see cref="DocketHeader.TransactionIds"/>, in stored order</b> —
/// never the order the store happens to return rows in. That is what makes tampering with the id list
/// itself detectable: alter, remove or reorder an id and the leaf sequence changes, so the recomputed
/// root changes. Building from whatever the repository returned would leave the id list uncommitted
/// and the check vacuous.
/// </para>
/// <para>
/// This type is pure — no repository, no clock, no logger — so the properties above can be executed
/// rather than asserted about. The three sites that need it (inclusion-proof generation, receipt
/// verification, and the ZK proof endpoints) each supply their own transactions.
/// </para>
/// </remarks>
internal static class DocketMerkleCommitment
{
    /// <summary>
    /// Rebuilds the leaf hashes this docket's commitment was computed over.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the docket lists a transaction that is not among
    /// <paramref name="heldTransactions"/> — this node cannot recompute, which is
    /// <see cref="VerificationStatus.Unverified"/>, never a tamper report.
    /// </returns>
    internal static LeafSet? BuildLeaves(
        DocketHeader docket,
        IReadOnlyList<TransactionModel> heldTransactions,
        DocketHasher docketHasher)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentNullException.ThrowIfNull(heldTransactions);
        ArgumentNullException.ThrowIfNull(docketHasher);

        var ids = docket.TransactionIds;
        if (ids is null || ids.Count == 0)
        {
            return new LeafSet([], []);
        }

        var byId = new Dictionary<string, TransactionModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var tx in heldTransactions)
        {
            var key = tx.TxId ?? tx.Id;
            if (!string.IsNullOrWhiteSpace(key))
            {
                byId[key] = tx;
            }
        }

        var leaves = new List<string>(ids.Count);
        var ordered = new List<TransactionModel>(ids.Count);

        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var tx))
            {
                return null;
            }

            ordered.Add(tx);
            leaves.Add(docketHasher.ComputeTransactionHash(
                tx.TxId ?? tx.Id ?? string.Empty,
                tx.Payloads?.FirstOrDefault()?.Hash ?? string.Empty,
                new DateTimeOffset(tx.TimeStamp, TimeSpan.Zero)));
        }

        return new LeafSet(leaves, ordered);
    }

    /// <summary>
    /// Compares a root recomputed from <paramref name="leaves"/> against the root the docket records
    /// as sealed.
    /// </summary>
    /// <remarks>
    /// Tri-state, and the third state is load-bearing. A docket sealed before Feature 187 kept the
    /// commitment has nothing to compare against, and <see cref="DocketHeader.MerkleRoot"/> is a
    /// non-nullable string defaulting to empty — so blank is exactly the form such a docket arrives
    /// in. Reporting that as agreement would manufacture confidence; reporting it as tampering would
    /// be a false alarm on every legacy docket. It is <see cref="VerificationStatus.Unverified"/>.
    /// </remarks>
    internal static SealComparison Compare(DocketHeader docket, LeafSet? leaves, MerkleTree merkleTree)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentNullException.ThrowIfNull(merkleTree);

        var sealedRoot = string.IsNullOrWhiteSpace(docket.MerkleRoot) ? null : docket.MerkleRoot;

        if (leaves is null)
        {
            return new SealComparison(
                VerificationStatus.Unverified, sealedRoot, RecomputedRoot: null,
                "this node does not hold every transaction the docket lists, so the commitment cannot be recomputed");
        }

        var recomputed = merkleTree.ComputeMerkleRoot(leaves.Hashes);

        if (sealedRoot is null)
        {
            return new SealComparison(
                VerificationStatus.Unverified, SealedRoot: null, recomputed,
                "this docket was sealed before the platform kept the sealed Merkle root, so there is no commitment to compare against");
        }

        return string.Equals(recomputed, sealedRoot, StringComparison.OrdinalIgnoreCase)
            ? new SealComparison(VerificationStatus.Verified, sealedRoot, recomputed, Reason: null)
            : new SealComparison(
                VerificationStatus.Failed, sealedRoot, recomputed,
                "the docket's stored contents do not reproduce the root its proposing validator sealed");
    }

    /// <summary>The docket's leaves, plus its transactions in the same (committed) order.</summary>
    internal sealed record LeafSet(IReadOnlyList<string> Hashes, IReadOnlyList<TransactionModel> OrderedTransactions);

    /// <summary>Outcome of comparing a recomputation against the sealed commitment.</summary>
    /// <param name="Status">Verified, Failed, or Unverified — see <see cref="Compare"/>.</param>
    /// <param name="SealedRoot">What the docket records, or null when it records nothing.</param>
    /// <param name="RecomputedRoot">What the stored contents produce, or null when they could not be assembled.</param>
    /// <param name="Reason">Why the check could not run, or why it failed. Null when Verified.</param>
    internal sealed record SealComparison(
        VerificationStatus Status,
        string? SealedRoot,
        string? RecomputedRoot,
        string? Reason)
    {
        /// <summary>
        /// True only when the recomputation reproduced the sealed root. Deliberately NOT
        /// "not Failed": an unverifiable docket must never read as a verified one.
        /// </summary>
        internal bool IsAnchored => Status == VerificationStatus.Verified;

        /// <summary>The wire form: <c>"verified"</c> / <c>"failed"</c> / <c>"unverified"</c>.</summary>
        internal string Wire => Status.ToString().ToLowerInvariant();
    }
}
