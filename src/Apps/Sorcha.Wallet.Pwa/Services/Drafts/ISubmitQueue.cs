// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Services.Drafts.Models;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 — outcome of attempting to submit a queued item, as classified from the server
/// response (see <c>SubmitConflictClassifier</c>, US4).
/// </summary>
public enum SubmitOutcome
{
    /// <summary>Accepted (including an idempotent same-key replay).</summary>
    Submitted,
    /// <summary>Transient failure (network / 5xx) — retry later.</summary>
    Retry,
    /// <summary>Already submitted (here or elsewhere) — hold for the citizen.</summary>
    AlreadySubmitted,
    /// <summary>The workflow has moved on — hold for the citizen.</summary>
    StepMovedOn,
    /// <summary>The instance is closed — hold for the citizen.</summary>
    InstanceClosed,
}

/// <summary>
/// Feature 152 (US3) — device-local, encrypted outbox of completed action submissions made offline.
/// Items are queued when there is no connectivity and drained (submitted) on reconnect. Retries are
/// safe because each item reuses the server idempotency key. Stale items are held (US4), never
/// silently dropped.
/// </summary>
public interface ISubmitQueue
{
    /// <summary>Enqueues a completed submission, assigning an id; returns the stored item.</summary>
    Task<QueuedSubmission> EnqueueAsync(QueuedSubmission item, CancellationToken ct = default);

    /// <summary>Lists all queue items (for inbox status).</summary>
    Task<IReadOnlyList<QueuedSubmission>> ListAsync(CancellationToken ct = default);

    /// <summary>Removes a queue item by id. Idempotent.</summary>
    Task RemoveAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Attempts to submit every queued item (oldest first) via <paramref name="submit"/>. Per item:
    /// <c>Submitted</c> → removed; <c>Retry</c> → left queued with attempts incremented; a stale
    /// outcome → marked <see cref="QueuedSubmissionState.NeedsAttention"/> with the reason (never
    /// dropped). One item's failure never blocks the rest.
    /// </summary>
    Task DrainAsync(Func<QueuedSubmission, CancellationToken, Task<SubmitOutcome>> submit, CancellationToken ct = default);
}

/// <summary>Default <see cref="ISubmitQueue"/> over the encrypted <c>submitQueue</c> store.</summary>
public sealed class SubmitQueue : ISubmitQueue
{
    private const string StoreName = "submitQueue";

    private readonly IEncryptedObjectStore _store;

    /// <summary>Initialises a new instance.</summary>
    public SubmitQueue(IEncryptedObjectStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<QueuedSubmission> EnqueueAsync(QueuedSubmission item, CancellationToken ct = default)
    {
        var key = string.IsNullOrEmpty(item.QueuedKey) ? Guid.NewGuid().ToString("N") : item.QueuedKey;
        var stored = item with { QueuedKey = key, State = QueuedSubmissionState.Queued };
        await _store.PutAsync(StoreName, key, stored, ct).ConfigureAwait(false);
        return stored;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<QueuedSubmission>> ListAsync(CancellationToken ct = default) =>
        _store.ListAsync<QueuedSubmission>(StoreName, ct);

    /// <inheritdoc />
    public Task RemoveAsync(string id, CancellationToken ct = default) =>
        _store.DeleteAsync(StoreName, id, ct);

    /// <inheritdoc />
    public async Task DrainAsync(
        Func<QueuedSubmission, CancellationToken, Task<SubmitOutcome>> submit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submit);
        var items = await ListAsync(ct).ConfigureAwait(false);
        foreach (var item in items.Where(i => i.State is QueuedSubmissionState.Queued).OrderBy(i => i.QueuedKey))
        {
            ct.ThrowIfCancellationRequested();
            var key = item.QueuedKey;
            await _store.PutAsync(StoreName, key, item with { State = QueuedSubmissionState.Submitting }, ct).ConfigureAwait(false);

            SubmitOutcome outcome;
            try
            {
                outcome = await submit(item, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                outcome = SubmitOutcome.Retry;
            }

            switch (outcome)
            {
                case SubmitOutcome.Submitted:
                    await RemoveAsync(key, ct).ConfigureAwait(false);
                    break;
                case SubmitOutcome.Retry:
                    await _store.PutAsync(StoreName, key,
                        item with { State = QueuedSubmissionState.Queued, Attempts = item.Attempts + 1 }, ct)
                        .ConfigureAwait(false);
                    break;
                default:
                    await _store.PutAsync(StoreName, key,
                        item with
                        {
                            State = QueuedSubmissionState.NeedsAttention,
                            ConflictReason = ToReason(outcome),
                        }, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static ConflictReason ToReason(SubmitOutcome o) => o switch
    {
        SubmitOutcome.AlreadySubmitted => ConflictReason.AlreadySubmitted,
        SubmitOutcome.StepMovedOn => ConflictReason.StepMovedOn,
        SubmitOutcome.InstanceClosed => ConflictReason.InstanceClosed,
        _ => ConflictReason.None,
    };
}
