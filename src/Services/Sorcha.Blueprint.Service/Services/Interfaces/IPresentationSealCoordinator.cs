// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Validator;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Feature 119 — coordinates seal-aware ordering of chain-pointer-bearing
/// presentation lifecycle transactions and their dependent workflow advancements.
/// </summary>
/// <remarks>
/// Holds queued submissions and workflow advancements until the predecessor they
/// reference has been observed sealed via the
/// <c>transaction:confirmed</c> Redis Streams channel. Drained by
/// <c>PresentationSealSubscriber</c> on event arrival, with a periodic recovery
/// sweep covering missed events and never-seals timeouts.
/// </remarks>
public interface IPresentationSealCoordinator
{
    /// <summary>
    /// Enqueue a built-and-signed transaction submission whose chain pointer
    /// references a predecessor not yet sealed in the register. The submission
    /// will be drained and submitted to the validator when the predecessor's
    /// <c>transaction:confirmed</c> event arrives, or failed with a structured
    /// timeout when the recovery sweep determines the predecessor will never seal.
    /// </summary>
    /// <param name="submission">The fully built submission envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueSubmissionAsync(
        SealAwaitingSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a workflow advancement (CompleteAfterPresentationAsync invocation)
    /// whose trigger references an outcome transaction not yet sealed. The
    /// advancement will be invoked when the outcome's <c>transaction:confirmed</c>
    /// event arrives.
    /// </summary>
    /// <param name="advancement">The advancement envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueAdvancementAsync(
        SealAwaitingAdvancement advancement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drain any queued submissions or advancements waiting on the given txId.
    /// Called by the seal subscriber on <c>transaction:confirmed</c> events and
    /// by the recovery sweeper for entries past the missed-event threshold.
    /// Idempotent — returns 0 if no entries were present (e.g. duplicate event).
    /// </summary>
    /// <param name="sealedTxId">Transaction id observed sealed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries drained (0, 1, or 2 — at most one submission and one advancement per txId).</returns>
    Task<int> DrainOnSealAsync(
        string sealedTxId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run the recovery sweep — fail any queue entries past their TTL and poll
    /// the register for entries past the missed-event threshold (research R3, R6).
    /// Called every <c>SealRecoverySweepIntervalSeconds</c> by the subscriber.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of entries recovered via poll and failed at TTL.</returns>
    Task<SweepResult> RunRecoverySweepAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Envelope for a built-and-signed transaction submission whose chain pointer
/// references a predecessor not yet sealed.
/// </summary>
/// <param name="PresentationRequestId">The presentation this submission belongs to.</param>
/// <param name="PredecessorTxId">The predecessor txId being awaited.</param>
/// <param name="Site">Originating call site (outcome | abandonment).</param>
/// <param name="Submission">The fully built and signed validator-ready submission DTO.</param>
/// <param name="TargetSentinelOnSuccess">Final sentinel value to set after a successful drain (e.g. <c>success</c>, <c>decline</c>, <c>abandoned</c>).</param>
/// <param name="ValidityWindowSeconds">Validity window driving sentinel TTL on transition.</param>
/// <param name="EnqueuedAt">When the entry was enqueued (for wait-duration metric and TTL fail).</param>
/// <param name="TraceContext">W3C traceparent for OTel span continuity across enqueue → drain.</param>
public sealed record SealAwaitingSubmission(
    Guid PresentationRequestId,
    string PredecessorTxId,
    SealAwaitingSubmissionSite Site,
    TransactionSubmission Submission,
    string TargetSentinelOnSuccess,
    int ValidityWindowSeconds,
    DateTimeOffset EnqueuedAt,
    string TraceContext);

/// <summary>
/// Envelope for a workflow advancement whose trigger references an outcome
/// transaction not yet sealed. Drained by invoking
/// <c>IActionExecutionService.CompleteAfterPresentationAsync</c> in a fresh DI
/// scope (mirrors PR #583 lifetime contract).
/// </summary>
/// <param name="PresentationRequestId">The presentation whose advancement is queued.</param>
/// <param name="OutcomeTxId">The outcome txId being awaited (also the queue key).</param>
/// <param name="InstanceId">Workflow instance to advance.</param>
/// <param name="CompletedActionId">Action being completed by this advancement.</param>
/// <param name="RegisterId">Register id (tracing only, not used for authorisation).</param>
/// <param name="DraftPayload">Pending presentation's draftPayload (carried through).</param>
/// <param name="EnqueuedAt">When the entry was enqueued.</param>
/// <param name="TraceContext">W3C traceparent.</param>
public sealed record SealAwaitingAdvancement(
    Guid PresentationRequestId,
    string OutcomeTxId,
    Guid InstanceId,
    int CompletedActionId,
    string RegisterId,
    IReadOnlyDictionary<string, object>? DraftPayload,
    DateTimeOffset EnqueuedAt,
    string TraceContext);

/// <summary>
/// Originating call site for a queued submission. Drives the metric
/// <c>site</c> label and the sentinel state-machine transitions.
/// </summary>
public enum SealAwaitingSubmissionSite
{
    /// <summary>Outcome submission (Race 2 — VAL_CHAIN_001).</summary>
    Outcome,

    /// <summary>Abandonment submission (latent variant of Race 2).</summary>
    Abandonment
}

/// <summary>
/// Result of a single recovery sweep tick.
/// </summary>
/// <param name="RecoveredViaPoll">Entries drained via direct register poll (missed-event recovery).</param>
/// <param name="FailedAtTtl">Entries failed because predecessor never sealed within validity window.</param>
public sealed record SweepResult(
    int RecoveredViaPoll,
    int FailedAtTtl);
