# Timebound Presentation Lifecycle (Feature 111)

Three-event on-register lifecycle for timebound evidence presentations. Replaces the SEC-014 single-shot HAIP flow with a consumer-agnostic primitive suitable for any evidence-of-engagement workflow.

## Why three events

Writing a single "action complete" transaction on submission recorded actions that never actually completed — when the citizen walked away from the QR or failed verification, the register still claimed the action was done. Writing only on verifier success destroys the legally-relevant *attempt* event that timebound compliance flows need ("I engaged with the system before the deadline").

The three events preserve both:

| Event | When written | Carries | Registered always? |
|---|---|---|---|
| `PresentationInitiated` | On submission | submitter wallet, action ref, `requirementsDigest`, `presentationRequestId`, consumer name, validity window. **No credential data** (FR-002). | Yes |
| `PresentationOutcome` | On verifier callback | On success: `verifiedClaims`, `presentationSubmissionHash`, optional `actionPayload`. On decline: `reason` (enum), optional `verifierDiagnostics` (verbose level only). | Yes, on callback |
| `PresentationAbandoned` | On TTL expiry with no outcome | `validityWindowSeconds`, `abandonedAt`. | Only when `blueprint.presentationConfig.recordAbandonment = true` |

All three are first-class chain members — each carries `previousTransactionId` so state reconstruction sees a clean linear chain per presentation attempt.

## Submission flow

1. Citizen hits `POST /api/instances/{id}/actions/{n}/execute` with a draft payload.
2. `ActionExecutionService` step 4c detects a HAIP `credentialRequirement` with no submitted presentations.
3. **US3 retry gate**: if a prior `PresentationOutcome` with `kind=success` exists for this `(instanceId, actionId)`, return **`409 Conflict`**.
4. **Rate limit**: `IPresentationRateLimiter` checks per-wallet-per-register sliding-window quota. Over threshold → **`429 Too Many Requests`** with `Retry-After`.
5. Route to `IPresentationLifecycleService.InitiateAsync`:
   - Call the HAIP client to create the OpenID4VP presentation request (QR URI + requestId).
   - Compute SHA-256 of canonical `credentialRequirements` as the `requirementsDigest`.
   - Store pending state in Redis (hash at `sorcha:presentation:pending:{id}`, TTL = validity window).
   - Build + sign + submit the `PresentationInitiated` transaction (no credential data, `RecipientsWallets = [submitter]`).
   - Persist the initiated transaction id back onto the pending state so outcome/abandonment writes can reference it as `previousTxId`.
6. Return **`202 Accepted`** with `AwaitingPresentation=true`, the attempt tx hash, and the QR details.

The action does **not** complete here. The verifier callback is the only path to `PresentationOutcome(kind=success)`.

## Callback flow

1. Citizen's wallet scans the QR, presents to HAIP's `HandleDirectPost` endpoint.
2. HAIP verifies the presentation (SD-JWT VC validation, issuer trust, status check, required-claim gating).
3. HAIP's `PresentationCallbackRelay` forwards the `VerificationResult` to Blueprint Service:
   ```
   POST /api/presentations/callbacks/haip/{presentationRequestId}
   Authorization: Bearer <service-JWT>
   ```
4. `PresentationEndpoints` dispatches to the registered `IPresentationConsumer` by name (here, `HaipPresentationConsumer`).
5. Consumer converts `VerificationResult → PresentationOutcome` with a `PresentationDeclineReason` enum value for the decline path.
6. `PresentationLifecycleService.HandleOutcomeAsync`:
   - Reads the pending state for the `presentationRequestId`.
   - Runs the **two-level idempotency guard** (research R6):
     - If sentinel is `success`/`decline`/`abandoned+outcome` → idempotent replay, no tx written.
     - If sentinel is `abandoned` → take the late-outcome path (bypass NX, final sentinel `abandoned+outcome`).
     - Otherwise → `SET NX` claim of `outcome-pending-write`; loser treated as replay.
   - Writes the `PresentationOutcome` transaction with `previousTxId = initiatedTxId`.
   - Sets the final sentinel value.

## Adding a new consumer

The abstraction lives in `Sorcha.PresentationLifecycle.Abstractions`. Implement `IPresentationConsumer`:

```csharp
public sealed class FileUploadDeadlineConsumer : IPresentationConsumer
{
    public string ConsumerName => "file-upload-deadline";

    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context,
        object verifierPayload,
        CancellationToken ct)
    {
        // Deserialise verifierPayload (likely JsonElement from HTTP).
        // Verify whatever the consumer-specific proof of engagement is
        // (file hash + signed timestamp, etc.).
        // Return Success with claims, or Decline with a reason.
    }
}
```

Register as a `singleton IPresentationConsumer` in DI:

```csharp
builder.Services.AddSingleton<IPresentationConsumer, FileUploadDeadlineConsumer>();
```

The consumer's verifier posts its own callback to the Blueprint endpoint:

```
POST /api/presentations/callbacks/file-upload-deadline/{presentationRequestId}
```

`HandleOutcomeAsync` dispatches by name — no Blueprint Service changes. The initiation path (`InitiateAsync`) currently calls the HAIP client inline; extending that to dispatch through `IPresentationConsumer.CreateRequestAsync` will land when the second consumer is implemented.

## Blueprint configuration

```jsonc
{
  "id": "deadline-driven-permit-v1",
  "presentationConfig": {
    "recordAbandonment": true,      // write PresentationAbandoned on TTL expiry
    "outcomeDetailLevel": "minimal", // or "verbose" — controls verifierDiagnostics
    "presentationValidityWindowSeconds": 900
  },
  "actions": [ ... ]
}
```

Defaults when omitted: `recordAbandonment=false`, `outcomeDetailLevel=Minimal`, `presentationValidityWindowSeconds=600` (configurable via `PresentationLifecycle:DefaultValidityWindowSeconds`).

## Configuration

```json
{
  "PresentationLifecycle": {
    "DefaultValidityWindowSeconds": 600,
    "SweeperIntervalSeconds": 30,
    "SweeperLeaderLockTtlSeconds": 60,
    "RateLimit": {
      "Threshold": 10,
      "WindowSeconds": 600
    }
  }
}
```

## Abandonment sweeper (US4)

`AbandonmentSweeper` is a `BackgroundService` that:
- Ticks every 30s (configurable).
- Acquires a SET NX leader lock (`sorcha:presentation:sweeper-lock`) so only one replica sweeps.
- Scans `IPendingPresentationStore.ListPendingNearExpiryAsync` (2× tick window, cap 500/tick).
- Dispatches each to `HandleAbandonmentAsync`, which gates on `recordAbandonment` + outcome sentinel.
- Rolls back the sentinel on validator-reject so late outcomes don't misclassify.

## Related

- Spec: `specs/111-presentation-lifecycle/`
- Sorcha architecture skill: `.claude/skills/sorcha-architecture/SKILL.md` → "Timebound Presentation Lifecycle (Feature 111)"
- Research R1 (flow intercept), R4 (Redis schema), R6 (two-level idempotency): `specs/111-presentation-lifecycle/research.md`
