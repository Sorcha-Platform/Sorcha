// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Decides whether a duplicate-key collision on a docket or transaction write is a benign
/// idempotent retry or a genuine integrity divergence.
/// </summary>
/// <remarks>
/// The docket-write endpoint receives sealed dockets from the Validator Service. A docket's
/// number is its Mongo <c>_id</c>, so a re-submitted docket number throws a duplicate-key error.
/// That is legitimate when the validator simply retries after a lost response (the persisted
/// record is byte-for-byte the same). It is NOT legitimate when a <em>different</em> docket
/// (different hash) — or a transaction with the same id but a different signature — already
/// occupies that slot: that means a divergent record was built on a stale chain head. Masking
/// such a collision as success silently drops the incoming docket's transactions (issue #814),
/// so the endpoint must verify the persisted record matches before treating the duplicate as a
/// no-op, and reject loudly otherwise.
/// </remarks>
internal static class DocketWriteReconciliation
{
    /// <summary>The outcome of reconciling a duplicate-key collision against the persisted record.</summary>
    internal enum Verdict
    {
        /// <summary>The persisted record is the same one being written — a safe idempotent retry.</summary>
        IdempotentMatch,

        /// <summary>A different record already occupies the slot — a genuine integrity divergence.</summary>
        Divergence,
    }

    /// <summary>
    /// Reconciles a duplicate docket-number collision. A match requires the persisted docket to
    /// exist and carry the same hash as the incoming docket.
    /// </summary>
    internal static Verdict ReconcileDocket(DocketHeader? existing, DocketHeader incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        return existing is not null
            && !string.IsNullOrEmpty(existing.Hash)
            && string.Equals(existing.Hash, incoming.Hash, StringComparison.Ordinal)
                ? Verdict.IdempotentMatch
                : Verdict.Divergence;
    }

    /// <summary>
    /// Reconciles a duplicate transaction-id collision. A match requires the persisted transaction
    /// to exist and carry the same signature as the incoming transaction.
    /// </summary>
    internal static Verdict ReconcileTransaction(TransactionModel? existing, TransactionModel incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        return existing is not null
            && !string.IsNullOrEmpty(existing.Signature)
            && string.Equals(existing.Signature, incoming.Signature, StringComparison.Ordinal)
                ? Verdict.IdempotentMatch
                : Verdict.Divergence;
    }
}
