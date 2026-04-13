# T014a — Investigation: Instance Persistence Path

**Status**: Completed 2026-04-13
**Task**: T014a [US1] Investigate whether `IInstanceStore.UpdateAsync` is actually called and persists in the live action-submission code path
**Time-box**: 2h (actual: ~30 min)
**Outcome**: **Path is intact in the orchestrated code path used by walkthroughs.** T014b reduces to a single code comment confirming the contract. See T014a.finding-2 below for a separate legacy-endpoint concern that is out of scope for this feature.

## Executive summary

The late-bind block at `ActionExecutionService.cs:309-332` correctly persists the participant binding via `_instanceStore.UpdateAsync(instance, cancellationToken)` on line 327. This method is reached via the orchestrated action execution endpoint at `Program.cs:1750-1833`, which is the path the walkthroughs use today (verified via `SorchaWalkthrough.psm1:1339`).

T014b's scope is therefore small: add one code comment on line 327 cross-referencing `contracts/instance-binding-cache.md` so the persistence contract cannot be re-flagged later.

## Trace

### Path 1 — orchestrated action execution (used by walkthroughs)

```text
walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1:1339
    Invoke-SorchaApi POST /instances/{instanceId}/actions/{actionId}/execute
        ↓
src/Services/Sorcha.Blueprint.Service/Program.cs:1750 (instancesGroup.MapPost execute)
    ↓ line 1804
actionExecutionService.ExecuteAsync(instanceId, actionId, request, delegationToken, context.User)
    ↓
src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:ExecuteAsync
    ↓ line 184  (IsStartingAction exempt from CurrentActionIds check)
    ↓ line 196-216 (strict wallet equality check — fires ONLY when participant.WalletAddress non-null)
    ↓ line 218-269 (credential requirement gate — HAIP or internal verifier)
    ↓ line 297    (starting action chain anchor from blueprint publish tx)
    ↓ line 309-332 (LATE BINDING BLOCK)
           │
           │  if (actionDef.IsStartingAction && !string.IsNullOrWhiteSpace(actionDef.Sender))
           │  {
           │      ...
           │      instance.ParticipantWallets[senderParticipantId] = request.SenderWallet;
           │      await _instanceStore.UpdateAsync(instance, cancellationToken);   // ← LINE 327 — PERSISTS
           │      ...
           │  }
```

**Verified**: the persistence call is present, reached, and awaited. The orchestrated endpoint is the path walkthroughs use. `IInstanceStore.UpdateAsync` writes back to the instance store; the next action's `ExecuteAsync` call reads via `_instanceStore.GetAsync` and sees the updated `ParticipantWallets` dictionary.

### Path 2 — legacy direct submission (NOT used by walkthroughs, but exists)

```text
src/Services/Sorcha.Blueprint.Service/Program.cs:883 (actionsGroup.MapPost "/")
    POST /actions/
    │
    │  Bypasses ActionExecutionService entirely
    │  Calls IActionResolverService, IPayloadResolverService, ITransactionBuilderService
    │  directly. No instance store write. No late binding. No credential gate.
    │
    │  Used by: legacy / Demo app / early clients
    │  Used by walkthroughs: NO (confirmed by grep of SorchaWalkthrough.psm1)
```

## Findings

### Finding 1 — Orchestrated path is intact

The orchestrated endpoint wires `IActionExecutionService` via DI at `Program.cs:154-155` and invokes `ExecuteAsync` at `Program.cs:1804`. `ExecuteAsync` calls `_instanceStore.UpdateAsync` at `ActionExecutionService.cs:327` after setting `instance.ParticipantWallets[...]`. The call is awaited and exceptions propagate up to the endpoint's catch handlers, so a persistence failure would surface as an HTTP error rather than a silent loss.

No fix required. The Explore agent's earlier flag ("half-built persistence") was based on a misread: the Explore agent saw the legacy endpoint at line 883 skipping instance creation and assumed this was the only submission path. It isn't — the walkthroughs exclusively use the orchestrated endpoint at line 1750.

### Finding 2 — Legacy direct-submission endpoint (out of scope)

The endpoint at `Program.cs:883` (`POST /actions/`) is a parallel action submission path that:

- Does NOT call `ActionExecutionService`
- Does NOT persist to `IInstanceStore`
- Does NOT enforce the late-binding contract
- Does NOT enforce credential requirements
- Auto-generates `instanceId = Guid.NewGuid()` at line 1136 if the request omits it

This endpoint is reachable from any caller that constructs an `ActionSubmissionRequest` and hits `/actions/` directly. It is **not** reached by walkthroughs (confirmed grep of `SorchaWalkthrough.psm1`), but it IS still mapped and would process a request if one arrived.

**Recommendation** (out of scope for Feature 103): deprecate this endpoint or route it through `ActionExecutionService` so there is only one submission code path with one contract. Open a follow-up ticket to audit callers and either remove or migrate the endpoint. Do not take action in this feature.

### Finding 3 — Instance store read side before late-bind

The late-bind block at line 310 reads `instance.ParticipantWallets.TryGetValue(senderParticipantId, out var boundWallet)`. For this to work, the `instance` object must be hydrated from the store with its existing `ParticipantWallets` dictionary intact. Verified: `ExecuteAsync` loads the instance via `_instanceStore.GetAsync(instanceId)` at the top of the method (before the late-bind block), and the existing `ParticipantWallets` round-trips through the store.

No fix required.

## T014b scope after this investigation

T014b reduces to:

1. **Add a single XML-style code comment above `ActionExecutionService.cs:327`** cross-referencing `contracts/instance-binding-cache.md` and noting that this persistence call is the authoritative record, with the Redis cache layer (`InstanceBindingCache`) being a performance optimisation only.

2. **Add a line to the `ActionExecutionService.cs` class-level XML summary** noting that the orchestrated action submission path is the *only* path that enforces the late-binding contract, and that the legacy endpoint at `Program.cs:883` is out of scope for the open-participant contract (with a one-line pointer to Finding 2 above).

3. **Do NOT add an integration regression test** for the persistence path itself — it is already covered by the existing end-to-end walkthrough runs (which hit the orchestrated endpoint) plus the new `LateBindingIntegrationTest` (T007). Adding another test would duplicate coverage.

T014b should size as a ≤15-minute task. Execution can happen as part of the US1 implementation batch alongside T017-T019.

## Artefacts

- `specs/103-verified-citizen-v2/investigation-t014a.md` — this file
- PR description: the commit referencing this file is the audit trail

## Follow-up items (out of scope for Feature 103)

1. **Legacy endpoint audit**: identify callers of `POST /actions/` (non-orchestrated path) and either route them through `ActionExecutionService` or deprecate. Ticket title suggestion: *"Audit and consolidate Blueprint Service action submission endpoints onto a single orchestrated path"*.
2. **Endpoint route naming clarity**: consider moving `POST /instances/{instanceId}/actions/{actionId}/execute` to `POST /instances/{instanceId}/actions/{actionId}/submit` — "execute" reads like server-side execution engine semantics, but this endpoint is primarily the submission boundary. Purely a renaming ticket. Low priority.
