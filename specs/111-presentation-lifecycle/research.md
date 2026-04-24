# Phase 0 Research: Timebound Presentation Lifecycle

**Feature**: 111-presentation-lifecycle
**Date**: 2026-04-23

## R1 — Current HAIP presentation flow in ActionExecutionService

**Decision**: Intercept at `ActionExecutionService.ExecuteAsync` step 4c (around `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:226-277`). When a `CredentialRequirement` has `PresentationSource == HaipExternalWallet` and no presentations were submitted inline, today the code creates a presentation request via `HaipServiceClient.CreatePresentationRequestAsync` and **continues through to write the action transaction**. This is the root SEC-014 bug: the action is sealed before the citizen has scanned anything.

**Rationale**: The new lifecycle short-circuits step 4c — instead of continuing to action-transaction build, it writes a `presentation-initiated` transaction, stashes the pending action context (instance, action, draft payload, HAIP requirement, delegation token) in Redis keyed by the presentation request id, and returns the QR to the caller with a status indicating "awaiting presentation". The action is NOT complete. On verifier callback, `PresentationLifecycleService` resumes the original action with the verified claims and writes a `presentation-outcome=success` transaction that carries both the action's payload and the verified claims. The outcome-success transaction IS the action's completion event.

**Alternatives considered**:
- *Two transactions at success — an outcome and a separate action tx.* Rejected: unnecessary duplication, makes the workflow engine's "is this action complete?" check ambiguous (which tx counts?).
- *Keep the eager action tx and add a follow-on outcome.* Rejected: restates the SEC-014 bug — a false "completed" entry is still on the register during the citizen's scan window.

## R2 — Transaction-type vocabulary extension

**Decision**: Add three new values to the existing `Sorcha.Register.Models.Enums.TransactionType` enum: `PresentationInitiated`, `PresentationOutcome`, `PresentationAbandoned`. The Register Service's `TransactionMetaData` already has a `TransactionType` field; queries that want to find lifecycle transactions for an instance filter on these values.

**Rationale**: Minimal disturbance to existing code paths. Validator chain validation, docket sealing, state reconstruction, and replication all treat transactions by type; adding three enum values is idiomatic. State reconstruction (fix from PR #377) already walks the full chain; the three new types just participate naturally in the walk. `PresentationOutcome` with `Kind=Success` is the completion signal for the action; `Kind=Decline` is treated by the routing layer like a rejection (re-route or terminate per blueprint config).

**Alternatives considered**:
- *Repurpose the existing `Action` type with metadata flags.* Rejected: loses first-class queryability; auditors couldn't SELECT WHERE type = 'presentation-initiated' cleanly.
- *Use `Rejection` for decline outcomes.* Rejected: declines have semantics distinct from user-initiated rejection (e.g. no `targetActionId` routing back to a prior step; reason codes are a closed enum).

## R3 — Abandonment sweeper design

**Decision**: A new `AbandonmentSweeper : BackgroundService` in Blueprint Service that wakes every 30 seconds, scans Redis for keys matching the pending-presentation pattern with TTL < 30s (about to expire), and for each (a) checks whether an outcome has already been written for that requestId (via an idempotency sentinel also in Redis), (b) reads the blueprint's `recordAbandonment` flag, (c) if opted in, writes a `PresentationAbandoned` transaction via the normal transaction-submission path.

**Rationale**: 30-second cadence + pre-TTL scan gives ≤60-second abandonment latency (SC-006) with a single in-process worker per Blueprint Service instance. If multiple instances run (HA), Redis-backed leader election via SET NX on a sweeper-lock key ensures only one instance sweeps at a time. The outcome-exists check is a cheap Redis lookup — no race with a concurrent callback because the callback writes the idempotency sentinel before writing the outcome tx.

**Alternatives considered**:
- *Redis keyspace notifications on TTL expiry.* Rejected: requires `notify-keyspace-events` config change on every Redis deployment, fragile across deployment envs, non-deterministic order of delivery.
- *Quartz.NET scheduled job.* Rejected: adds a dependency for a trivial loop; introduces a cluster coordination story.
- *Check-on-read lazy abandonment.* Rejected: no "read" happens if nobody queries the action, so abandonment would silently never occur for inactive flows.

## R4 — Pending-presentation state schema in Redis

**Decision**: Redis hash at key `sorcha:presentation:pending:{presentationRequestId}` with fields:
- `instanceId` (Guid)
- `actionId` (int)
- `registerId` (string)
- `submitterWallet` (string)
- `blueprintId` (string)
- `draftPayload` (JSON string — the action's non-credential fields)
- `credentialRequirementDigest` (sha256)
- `delegationToken` (JWT, scoped)
- `recordAbandonment` (bool)
- `outcomeDetailLevel` ("minimal" | "verbose")
- `validityWindowSeconds` (int)
- `createdAt` (ISO-8601 UTC)

TTL set on the hash = validityWindowSeconds (default 600).

A sibling key `sorcha:presentation:outcome-sentinel:{presentationRequestId}` is created by the first callback to arrive (SET NX, TTL = validityWindowSeconds + 3600 so it outlives any late abandonment race). Sweeper and callback both check this sentinel before writing their respective lifecycle transactions — it's the idempotency guard.

**Rationale**: Redis is already used for comparable state (PreAuthCodeStore, NonceStore, AccessTokenStore in HAIP Service). Hash structure keeps the pending state retrievable in a single round-trip. Sentinel pattern gives atomic first-write-wins for the outcome without a full Redis transaction.

**Alternatives considered**:
- *Postgres `pending_presentations` table in Blueprint Service DB.* Rejected: Redis is cheaper, matches existing patterns, and data is genuinely ephemeral — there's no audit value in post-mortem inspection (the legal record is on the register).
- *In-memory dictionary.* Rejected: breaks under the microservices-first principle; an HTTP load-balanced Blueprint Service deployment would see callbacks on a different instance than the initiation.

## R5 — Consumer contract for non-HAIP timebound flows

**Decision**: New project `src/Common/Sorcha.PresentationLifecycle.Abstractions` containing:

```csharp
public interface IPresentationConsumer
{
    string ConsumerName { get; }   // e.g. "haip", "file-upload-deadline"
    
    // Called by the lifecycle service when a callback arrives from the consumer
    Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken ct);
}

public record PresentationInitiationContext(
    Guid PresentationRequestId,
    Guid InstanceId, int ActionId, string RegisterId,
    string SubmitterWallet, byte[] RequirementsDigest);

public record PresentationOutcome(
    PresentationOutcomeKind Kind,     // Success | Decline
    Dictionary<string, object>? VerifiedClaims,   // null when Decline
    PresentationDeclineReason? Reason,            // null when Success
    object? VerifierDiagnostics);                 // for verbose outcomeDetailLevel

public enum PresentationOutcomeKind { Success, Decline }
public enum PresentationDeclineReason { ExpiredCredential, WrongIssuer, Revoked, SchemaMismatch, SignatureInvalid, ActionNoLongerAvailable, VerifierError }
```

`PresentationLifecycleService` in Blueprint Service is consumer-agnostic and invokes `IPresentationConsumer.VerifyAsync` via DI registration. HAIP Service registers `HaipPresentationConsumer` as the first implementation. Future consumers (e.g. `FileUploadDeadlineConsumer`) register their own implementation under a distinct `ConsumerName`.

**Rationale**: Cleanly separates orchestration (Blueprint Service) from verifier-specific logic (HAIP or otherwise). The consumer name is carried in the `PresentationInitiated` transaction metadata so auditors can see which consumer handled a given attempt. Keeps Blueprint Service free of OpenID4VP-specific types.

**Alternatives considered**:
- *Webhook / callback URL per consumer.* Rejected: adds HTTP hop latency inside the same trust boundary, complicates testing.
- *Event-bus (MessagePump / RabbitMQ).* Rejected: introduces a new infrastructure dependency for state that already flows through DI in the same process.

## R6 — Idempotency and late-callback-after-abandonment resolution

**Decision**: The outcome sentinel (R4) is written via `SET NX` before any register write. On callback:
1. `SET NX sorcha:presentation:outcome-sentinel:{id} = "outcome-pending-write"` — if this fails, a prior callback got there first; current callback returns 200 with body "already processed" and no register write.
2. If SET succeeds, proceed to write the `PresentationOutcome` transaction. On success, update sentinel value to final outcome kind.
3. The abandonment sweeper also attempts `SET NX sorcha:presentation:outcome-sentinel:{id} = "abandoned"` before writing `PresentationAbandoned`. If SET fails, a callback got there first; sweeper skips.

If a callback arrives after abandonment has been written (the user's edge case from spec), the `SET NX` by the callback fails. Per FR-009, the late outcome MUST still be written. Solution: the callback path performs a second check — if the sentinel value is exactly `"abandoned"`, the callback bypasses the NX guard and writes the outcome anyway; the register shows both abandonment and outcome, timestamps resolve ordering. Update the sentinel to `"abandoned+outcome"` so subsequent retries of the same callback are still deduped.

**Rationale**: Two-level guard — NX for the common idempotency case, explicit "can-override-abandonment" for the late-outcome edge — maps cleanly to the spec's first-write-wins model + FR-009's explicit exception.

**Alternatives considered**:
- *Pure first-write-wins, reject late outcomes.* Rejected: violates FR-009 — spec explicitly preserves full event stream.
- *Optimistic locking via version field.* Rejected: heavier than needed for two-state transitions; NX + explicit escape is simpler.

## R7 — Rate-limiting implementation

**Decision**: Per-wallet-per-register quota enforced via Redis counter keyed `sorcha:presentation:ratelimit:{walletAddress}:{registerId}` with sliding window via Redis `INCR` + TTL on first increment. Default threshold (configurable in `PresentationLifecycleOptions`): 10 attempts per 10-minute window. Exceeded = endpoint returns HTTP 429, no attempt transaction written.

**Rationale**: Redis INCR-with-TTL is the standard sliding-window quota primitive; already used by the existing rate-limiter in `ServiceDefaults`. Per-wallet-per-register scope matches the spec Q1 answer. Threshold starts permissive (10/window) with ops visibility on rate-limit rejections so operators can tighten after observing real traffic.

**Alternatives considered**:
- *Token bucket via the ASP.NET Core rate-limiter middleware.* Rejected: middleware rate limits are HTTP-path-scoped; can't reach into the "per-wallet" dimension without extra wiring.
- *Database-backed counter.* Rejected: unnecessary persistence for a counter that resets every window.

## R8 — Outcome-detail-level register-visibility default

**Decision**: The platform default for `outcomeDetailLevel` is chosen per-register based on its visibility: registers with `advertise = true` (public subscriptions allowed) default to `minimal`; registers with `advertise = false` (invitation-only) default to `verbose`. Per-blueprint override via `BlueprintPresentationConfig.OutcomeDetailLevel` takes precedence over the register default.

**Rationale**: Matches spec Q2 answer. Publicly-advertised registers carry the highest privacy constraint (any subscribing org sees all transaction metadata); minimal decline reasons prevent leaking detailed diagnostic information (e.g. a specific token signature error that might identify the citizen's wallet vendor). Private registers are already access-controlled, so the debugging value of verbose diagnostics outweighs the smaller residual disclosure risk.

**Alternatives considered**:
- *Unconditional minimal default.* Rejected: loses debuggability for authorities running private registers where full decline context is operationally useful.
- *Unconditional verbose default.* Rejected: violates privacy-first principle on public registers.

## R9 — Observability plan

**Decision**: Three OTel spans keyed on `presentationRequestId`:
- `presentation.initiated` — parent = action-submission span; attributes: consumerName, blueprintId, actionId, instanceId, registerId, requirementsDigest, validityWindowSeconds.
- `presentation.outcome` — parent = verifier-callback span; attributes: consumerName, requestId, outcome.kind, outcome.reason (if decline), outcome.durationMs (time from initiated to outcome).
- `presentation.abandoned` — parent = sweeper loop span; attributes: requestId, blueprintId, reason = "ttl-expired".

Structured log events via `ILogger`: `PresentationInitiated`, `PresentationOutcomeWritten`, `PresentationAbandoned`, `PresentationCallbackRejected` (duplicate / unknown id / signature invalid).

Metrics (Prometheus via OTel exporter):
- `sorcha_presentation_initiated_total{consumer, blueprint}` (counter)
- `sorcha_presentation_outcome_total{consumer, kind, reason}` (counter)
- `sorcha_presentation_abandoned_total{consumer, blueprint}` (counter)
- `sorcha_presentation_duration_seconds{consumer, kind}` (histogram)
- `sorcha_presentation_ratelimit_rejected_total{wallet, register}` (counter)

**Rationale**: Matches the Constitution's Observability-by-Default principle. Metrics span both business (attempts per outcome) and operational (rate-limit pressure) concerns. OTel span parentage gives distributed-trace coherence across Blueprint Service and HAIP Service.

**Alternatives considered**: Nothing substantive — this is standard Sorcha telemetry wiring.

## Summary

All spec-level clarifications (Q1, Q2, Q3) were resolved during `/speckit.specify` review and are reflected in the implementation decisions above. No further `NEEDS CLARIFICATION` markers remain. Phase 0 research complete — ready for Phase 1 design.
