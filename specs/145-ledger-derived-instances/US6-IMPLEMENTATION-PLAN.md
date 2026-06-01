# US6 — Presentation lifecycle onto the projection (implementation plan)

**Status:** Increment 1 DONE + unit-tested (branch `feature/145-us6-presentation-projection`).
Increments 2–3 designed below; they are **atomic + live-gated** (need the F111/F127 walkthroughs).

This plan was written from a CURRENT-code recon (2026-06-01), correcting the stale-worktree recon
the resume prompt warned about. Key correction below.

## Critical finding (corrects the memory/skill)

The skill said "presentation txs aren't projector-folded, so no double-write." **That is false on
current master.** `BuildPresentationInitiatedAsync` / `BuildPresentationOutcomeAsync` /
`BuildPresentationAbandonedAsync` (`ITransactionBuilderService.cs`) all stamp
`blueprintId`+`actionId`+`instanceId` into metadata, and `DocketBuildTriggerService.cs:642` sets a
numeric `TransactionMetaData.ActionId` (`uint.TryParse`). So `InstanceProjectionResolver.ResolveAsync`
returns non-null for them and the `InstanceProjector` **folds them** — with no `RoutingDecision` they
fold as an empty-terminal and retire the still-current presentation action.

For a **non-terminal** presentation-gated action this is a latent bug: the projector prematurely
completes the instance, then the imperative `CompleteAfterPresentationAsync` early-exits ("action no
longer current") and the workflow never reaches the real successor. Masked today only because the
tested presentation flows have the presentation as the **terminal** action.

## Increment 1 (DONE)

`InstanceProjectionResolver.ResolveAsync` skips a presentation-lifecycle tx
(`IsPresentationLifecycle()` = Initiated/Outcome/Abandoned) that carries **no** `RoutingDecision`, so
the gated action — current via the previous action's routing fold — stays current until a successful
outcome routes onward. New `TransactionType.IsPresentationLifecycle()` /
`IsIntraActionLifecycleTerminal()` predicates in `Sorcha.Register.Models`. 5 resolver tests; suite
851/0. This restores the intended invariant AND fixes the non-terminal bug, independent of 2–3.

## Increment 2 + 3 (atomic — must land together; live-gated)

**Why atomic:** once a SUCCESS outcome carries a `RoutingDecision`, the projector folds it and
advances. If the imperative `CompleteAfterPresentationAsync` advance is still wired, you get a
double-advance race. So "attach decision" (2) and "retire imperative advance" (3) are one change.

### 2 — Produce + sign a RoutingDecision for a successful outcome
- Add `IActionExecutionService.BuildPresentationRoutingDecisionAsync(instanceId, completedActionId,
  draftPayload, submitterWallet, ct)` → loads the real blueprint+instance+actionDef, runs the SAME
  `EvaluateCalculationsAsync` + `BuildOutputMappingSource` + `EvaluateRoutingAsync` that
  `CompleteAfterPresentationAsync` runs today, then builds + sender-signs a `RoutingDecision`
  (mirror `ActionExecutionService` step 10d ~L1007–1029) and RETURNS it (does NOT advance). Returns
  null if the action is no longer current (idempotency) so a replay attaches nothing.
- `PresentationLifecycleService.HandleOutcomeAsync` (success branch, both the inline path ~L568+ and
  the F119-deferred path ~L526+): call it and attach canonical JSON to `built.Metadata["routingDecision"]`
  BEFORE `ToTransactionSubmission`. `ToTransactionSubmission` already whitelists `"routingDecision"`
  (`ITransactionBuilderService.cs:669`), so it rides to the validator + seal. Attaching metadata
  post-build does NOT change txId/signature (those are over `TransactionData`/`SigningData`, not
  `Metadata`). **The `HandleOutcomeAsync` shims (blueprint/instance) stay for the BUILD** — the real
  load happens inside `BuildPresentationRoutingDecisionAsync`.
- Validator interaction: a success outcome now carries a decision, so `VAL_ROUTING_001/002` (which do
  NOT skip intra-action-lifecycle terminals) will validate it — `nextActions` must be structural
  successors of the presentation action. The `VAL_BP_003` carve-out
  (`IsIntraActionLifecycleTerminal`) stays untouched. **This is the dormant-routing trap surface —
  must verify in Mongo that the sealed success outcome shows `RoutingDecision=PRESENT` and the loop
  advances.**

### 3 — Retire the imperative advance (after 2 proves out live)
- Remove the `EnqueueAdvancementAsync` calls (deferred ~L539 + inline ~L679) and the legacy
  `Task.Run → CompleteAfterPresentationAsync` fallback (~L690–721) in `PresentationLifecycleService`.
  Advancement now happens only on the outcome seal via the projector; the `ReactionDispatcher`
  (post-fold) fires the action-available / workflow-completed notifications.
- Keep `EnqueueSubmissionAsync` (the F119 outcome-tx **submission** deferral for chain ordering) — it
  is still required so the outcome tx doesn't race its `PresentationInitiated` predecessor seal.
- Delete the now-dead imperative methods: `CompleteAfterPresentationAsync`,
  `UpdateInstanceAfterExecutionAsync`, `ApplyInstanceStateChanges`, `NotifyParticipantsAsync`
  (`ActionExecutionService`), the `IActionExecutionService.CompleteAfterPresentationAsync` interface
  member, and the `SealAwaitingAdvancement` / advancement branch of `DrainOnSealAsync` in
  `RedisPresentationSealCoordinator` + `IPresentationSealCoordinator`. Keep the F119 idempotency
  sentinels (`SetOutcomeSentinelAsync`) and the recovery sweep.
- **Removal-follows-replacement:** delete only after the live walkthrough confirms the projector
  advances on the carried decision. A clean rollback is "re-add the imperative advance call."

### Tests to keep green
`PresentationSealCoordinatorTests`, `PresentationLifecycle*`, `PresentationExecute*`,
`SorchaWalletPresentationConsumerTests`. Many assert the imperative advance / `EnqueueAdvancementAsync`
— they will need rework to assert "success outcome carries a signed RoutingDecision" +
"projector folds it" instead.

### Live validation (the gate — Stuart)
Re-run the F111 + F127 presentation walkthroughs. Confirm: presentation success advances the
instance on every node, no ordering race, and (Mongo) the sealed success outcome shows
`RoutingDecision=PRESENT`. Then T040 can enforce the `ApplyInstanceStateChanges` clean-break pattern.
