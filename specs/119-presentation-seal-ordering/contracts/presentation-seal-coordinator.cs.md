# Internal Contract: `IPresentationSealCoordinator`

**Feature**: 119-presentation-seal-ordering
**Visibility**: Internal to `Sorcha.Blueprint.Service` (not a public REST/gRPC contract).
**File**: `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationSealCoordinator.cs`
**Implementation**: `RedisPresentationSealCoordinator` in `Services/Implementation/`

> This feature has no new REST or gRPC endpoints. The "contract" surface is the internal C# interface that `PresentationLifecycleService` and `PresentationSealSubscriber` collaborate through. It's documented here so reviewers can scrutinise the coordination boundary before implementation.

---

## Interface

```csharp
namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Coordinates seal-aware ordering of chain-pointer-bearing presentation lifecycle
/// transactions. Holds queued submissions and workflow advancements until the
/// predecessor they reference has been observed sealed via the
/// transaction:confirmed Redis Streams channel.
/// </summary>
public interface IPresentationSealCoordinator
{
    /// <summary>
    /// Enqueue a built-and-signed transaction submission whose chain pointer
    /// references a predecessor not yet sealed in the register. The submission
    /// will be drained and submitted to the validator when the predecessor's
    /// transaction:confirmed event arrives, or failed with a structured timeout
    /// when the recovery sweep determines the predecessor will never seal.
    /// </summary>
    Task EnqueueSubmissionAsync(
        SealAwaitingSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a workflow advancement (CompleteAfterPresentationAsync invocation)
    /// whose trigger references an outcome transaction not yet sealed. The
    /// advancement will be invoked when the outcome's transaction:confirmed
    /// event arrives.
    /// </summary>
    Task EnqueueAdvancementAsync(
        SealAwaitingAdvancement advancement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drain any queued submissions or advancements waiting on the given txId.
    /// Called by the seal subscriber on transaction:confirmed events and by the
    /// recovery sweeper for entries past the missed-event threshold.
    /// Idempotent: returns immediately if no entries are present.
    /// </summary>
    /// <returns>
    /// The number of entries drained. Useful for the sweeper-recovery counter.
    /// </returns>
    Task<int> DrainOnSealAsync(
        string sealedTxId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run the recovery sweep — fail any queue entries past their TTL and
    /// poll the register for entries past the missed-event threshold.
    /// Called every SealRecoverySweepIntervalSeconds by the subscriber.
    /// </summary>
    Task<SweepResult> RunRecoverySweepAsync(
        CancellationToken cancellationToken = default);
}

public sealed record SealAwaitingSubmission(
    Guid PresentationRequestId,
    string PredecessorTxId,
    SealAwaitingSubmissionSite Site,
    Sorcha.ServiceClients.Validator.TransactionSubmission Submission,
    string TargetSentinelOnSuccess,
    DateTimeOffset EnqueuedAt,
    string TraceContext);

public sealed record SealAwaitingAdvancement(
    Guid PresentationRequestId,
    string OutcomeTxId,
    Guid InstanceId,
    int CompletedActionId,
    string RegisterId,
    IReadOnlyDictionary<string, object>? DraftPayload,
    DateTimeOffset EnqueuedAt,
    string TraceContext);

public enum SealAwaitingSubmissionSite
{
    Outcome,
    Abandonment
}

public sealed record SweepResult(
    int RecoveredViaPoll,
    int FailedAtTtl);
```

---

## Behavioural contract (acceptance criteria)

| Invariant | Notes |
|---|---|
| `EnqueueSubmissionAsync` MUST persist atomically to Redis before returning. | Caller relies on this for restart-safety. Use a single `HSET` plus `EXPIRE`. |
| `EnqueueAdvancementAsync` MUST persist atomically before returning. | Same. |
| `DrainOnSealAsync` MUST be idempotent on duplicate seal events. | Use `HDEL` + check return value (1 = drained, 0 = already drained). |
| `DrainOnSealAsync` MUST submit the queued tx via `IValidatorServiceClient.SubmitTransactionAsync`. | On `Success=true`: update sentinel to `targetSentinelOnSuccess`, log + metric. |
| `DrainOnSealAsync` MUST treat `VAL_CHAIN_FORK` errors as "already sealed via another path" — dedupe, no error propagation. | Edge case from design doc §Edge Cases. |
| `DrainOnSealAsync` MUST treat other validator errors as `failed-validator-reject`. | LogError, sentinel transition, metric. |
| `DrainOnSealAsync` MUST invoke `IActionExecutionService.CompleteAfterPresentationAsync` in a fresh `IServiceScope` with `CancellationToken.None`. | Same pattern as PR #583. |
| `RunRecoverySweepAsync` MUST poll the register via `IRegisterServiceClient.GetTransactionAsync` for entries older than 30 s. | If sealed, drain via `DrainOnSealAsync`. |
| `RunRecoverySweepAsync` MUST fail entries older than `pending.ValidityWindowSeconds`. | Sentinel transitions to `failed-predecessor-not-sealed`. |

---

## Observability contract

The coordinator MUST emit on the `Sorcha.Blueprint.PresentationLifecycle` meter:

| Instrument | Type | Labels | Trigger |
|---|---|---|---|
| `sorcha_presentation_seal_wait_seconds` | histogram | `site` ∈ {`outcome`, `abandonment`, `advance`} | At `DrainOnSealAsync` success — duration since `enqueuedAt`. |
| `sorcha_presentation_seal_queue_depth` | observable gauge | `site` | Sampled every 10 s by the subscriber. |
| `sorcha_presentation_seal_timeout_total` | counter | `site` | At sweeper TTL fail. |
| `sorcha_presentation_seal_recovered_via_sweeper_total` | counter | `site` | At sweeper-poll success. |

OTel span `presentation.seal-wait` MUST be parented to the `presentation.outcome` / `presentation.abandoned` span (via `traceContext`) and span the enqueue → drain lifetime.

---

## Test contract

Each public method has the following test obligations (covered in `PresentationSealCoordinatorTests.cs`):

1. `EnqueueSubmissionAsync` round-trip: enqueue → `DrainOnSealAsync(predecessorTxId)` returns 1, validator client invoked with the right submission.
2. `EnqueueAdvancementAsync` round-trip: enqueue → `DrainOnSealAsync(outcomeTxId)` returns 1, `CompleteAfterPresentationAsync` invoked with the right tuple.
3. `DrainOnSealAsync` idempotence: drain twice, second call returns 0, validator/advancement not invoked twice.
4. `DrainOnSealAsync` validator-reject path: returns failure status, sentinel transitions to `failed-validator-reject`, log + metric.
5. `DrainOnSealAsync` `VAL_CHAIN_FORK` path: dedupes silently, no error.
6. `RunRecoverySweepAsync` missed-event recovery: entry >30s old whose predecessor is sealed gets drained.
7. `RunRecoverySweepAsync` TTL-fail: entry >`ValidityWindowSeconds` old gets failed with sentinel transition.
8. Restart safety: enqueue → simulate process restart (new coordinator instance pointing at same Redis) → `DrainOnSealAsync` still finds the entry.
