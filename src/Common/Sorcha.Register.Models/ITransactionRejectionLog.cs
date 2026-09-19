// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// Why the validator refused a transaction, recorded so whoever submitted it can find out (#1669).
/// </summary>
/// <param name="TransactionId">The transaction the submitter was handed in the 202.</param>
/// <param name="RegisterId">The register it was submitted to.</param>
/// <param name="Code">The validation code, e.g. <c>VAL_CHAIN_FORK</c>.</param>
/// <param name="Message">The validator's own explanation.</param>
/// <param name="RejectedAt">When the rejection happened.</param>
public sealed record TransactionRejection(
    string TransactionId,
    string RegisterId,
    string Code,
    string Message,
    DateTimeOffset RejectedAt);

/// <summary>
/// Records and serves validator rejections, so a transaction that was accepted for validation and
/// then refused is discoverable by the caller rather than only by whoever reads the container logs.
/// </summary>
/// <remarks>
/// <para>
/// Submission is asynchronous by design (Feature 145): the API answers 202 and validation happens
/// afterwards. Before this existed, a rejection produced no error, no observable status change and
/// no queryable record — the operation simply never happened. Cold-start run #5 lost a participant
/// revocation to <c>VAL_CHAIN_FORK</c> that way, and the 202 body said
/// <c>"status":"submitted"</c> throughout.
/// </para>
/// <para>
/// The register's transaction-status endpoint reads this when it cannot find the transaction on the
/// chain, which is precisely the state a rejected transaction leaves behind.
/// </para>
/// <para>
/// This is a <em>notification</em> surface, not a ledger. Entries expire: they exist so a caller
/// holding a transaction id can learn what happened to it, not to provide a permanent audit trail.
/// An expired or missing entry therefore means "not known", never "accepted".
/// </para>
/// </remarks>
public interface ITransactionRejectionLog
{
    /// <summary>Records that the validator refused a transaction.</summary>
    Task RecordAsync(TransactionRejection rejection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the recorded rejection for a transaction, or null when none is known — which may
    /// mean it was not rejected, or that the record has expired. Never infer acceptance from null.
    /// </summary>
    Task<TransactionRejection?> FindAsync(
        string registerId, string transactionId, CancellationToken cancellationToken = default);
}
