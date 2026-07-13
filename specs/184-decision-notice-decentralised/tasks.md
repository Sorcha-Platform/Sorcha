# Tasks: Decentralised decision notice + reason codification

**Feature**: 184 | **Branch**: `fix/183-decision-notice-citizen-recipient`
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Design**: `docs/superpowers/specs/2026-07-13-aias-decision-notice-decentralised-design.md`

**TDD**: every implementation task is preceded by its test task. Write the test, watch it fail, then implement.

**Slicing note**: US1 (deliver on the citizen's node) is deliverable with the fallback message alone — it
already fixes the "applicant receives nothing" bug. US2 (per-reason codified text) layers the specific
wording on top. That is why the carrier's `routeId` is foundational but `reasonCode` lands in US2.

---

## Phase 1: Setup

- [ ] T001 Confirm a clean baseline: `dotnet build` succeeds and `tests/Sorcha.Blueprint.Service.Tests` is green on `fix/183-decision-notice-citizen-recipient` (the `dedb339c` recipient resolver must already be passing).

---

## Phase 2: Foundational (blocks both user stories)

The carrier and the route-identity plumbing. Nothing can be delivered without these.

- [ ] T002 [P] Test: `RoutingDecision` canonical round-trip carries `routeId`; a decision differing only in `routeId` produces different `ComputeSignableBytes()` output; an absent field deserializes to null — in `tests/Sorcha.Register.Models.Tests/` (add `RoutingDecisionCarrierTests.cs`; create the test project reference pattern used by the sibling Register.Models tests if the file does not exist).
- [ ] T003 Add `RouteId` (`[JsonPropertyName("routeId")]`, `string?`) to `RoutingDecision` in `src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs` **and copy it into the object `ComputeSignableBytes()` constructs** — a field omitted there rides the wire unauthenticated while appearing signed.
- [ ] T004 [P] Test: `RoutingEngine` sets `MatchedRouteId` on the top-level `RoutingResult` for a **terminal** route (empty `nextActionIds`), a conditional route, and a default route; null when no route matches — in `tests/Sorcha.Blueprint.Engine.Tests/` (`RoutingEngineMatchedRouteTests.cs`).
- [ ] T005 Add `MatchedRouteId` to `src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs` and set it on every matched-route path in `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs` — including the `nextActionIds.Count == 0` branch of `BuildRoutingResult`, which currently returns `RoutingResult.Complete()` and discards `route.Id` (that branch **is** the reject route).
- [ ] T006 Carry `MatchedRouteId` on the service-local `RoutingResult` and map it in `EvaluateRoutingAsync`, in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`.

**Checkpoint**: the taken route's identity survives routing, including terminal routes.

---

## Phase 3: User Story 1 — a rejected applicant sees why, on their own node (P1)

**Goal**: the notice is delivered by the node hosting the recipient's wallet, from the folded sealed
transaction — not by the deciding agent's node.

**Independent test**: reject an application; exactly one durable inbox entry appears for the applicant,
carrying the route's `fallbackMessage`; the deciding node writes none; a replay writes no duplicate.

- [ ] T007 [US1] Test: the producer stamps `RouteId` on the signed `RoutingDecision` at submit — in `tests/Sorcha.Blueprint.Service.Tests/Services/` (extend the existing routing-decision producer coverage, or add `RoutingDecisionProducerTests.cs`).
- [ ] T008 [US1] Producer: set `RoutingDecision.RouteId = routingResult.MatchedRouteId` where the decision is built and signed (step 10d) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`. Leave both new fields null in the presentation-outcome decision builder (`BuildPresentationRoutingDecisionAsync`).
- [ ] T009 [US1] Test: `ReactionDispatcher` decision-notice reaction — fires for a recipient whose wallet is hosted locally; **skips when `GetWalletAsync` returns null** (the cross-node case); is idempotent on replay of the same sealed tx; no-ops when the route carries no notice, when the route id is not in the blueprint, and when the recipient participant is unbound; **fires on a non-terminal route** — in `tests/Sorcha.Blueprint.Service.Tests/Services/ReactionDispatcherTests.cs`. The dispatcher must be given **no payload access** in these tests (instance + tx metadata + blueprint only).
- [ ] T010 [US1] Change `IReactionDispatcher.DispatchAsync(Instance, string sealedTxId, ct)` to `DispatchAsync(Instance, TransactionModel tx, ct)` and add the `IActionResolverService` dependency, in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ReactionDispatcher.cs`. Update DI wiring if the ctor is registered explicitly.
- [ ] T011 [US1] Implement the decision-notice reaction in `ReactionDispatcher`, running **before** the terminal/active branching: read the decision via `InstanceProjectionResolver.ResolveRoutingDecision(tx.MetaData, logger)` → `RouteId` → blueprint (`IActionResolverService.GetBlueprintAsync(instance.BlueprintId)`) → completed action → route by id → `route.DecisionNotice` → recipient wallet from `instance.ParticipantWallets` → `ShouldFireAsync(tx.TxId, "decision-notice", wallet)` → `INotificationService.NotifyDecisionAsync(...)`. Record `reaction_dispatched_total{kind="decision-notice"}`. Every miss is a quiet, logged skip.
- [ ] T012 [US1] Pass the sealed `tx` (already in hand) to `DispatchAsync` at both call sites in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjector.cs`.
- [ ] T013 [US1] Delete the inline **9-notice** block (the `DecisionNoticeDispatcher.DispatchAsync` call and its `conditionMatches`/`SafeEvaluateCondition` callback) from `ActionExecutionService.ExecuteAsync`; delete `src/Services/Sorcha.Blueprint.Service/Services/Implementation/DecisionNoticeDispatcher.cs` and `tests/Sorcha.Blueprint.Service.Tests/Services/DecisionNoticeDispatcherTests.cs`. Remove `SafeEvaluateCondition` only if it has no other caller.

**Checkpoint**: the notice fires from the fold, on the recipient's node, exactly once — with the fallback wording.

---

## Phase 4: User Story 2 — the reason survives an encrypted register (P1)

**Goal**: the specific reason travels as a signed, non-sensitive code and is rendered from the blueprint's
catalogue, with no payload decryption anywhere on the recipient's node.

**Independent test**: the sealed transaction's clear metadata carries a reason **code** and no free text;
the delivered notice text matches the blueprint's wording for that code; an unknown code falls back.

- [ ] T014 [P] [US2] Test: `RoutingDecision.ReasonCode` round-trips and is **inside** `ComputeSignableBytes()` — extend `RoutingDecisionCarrierTests.cs` (T002).
- [ ] T015 [US2] Add `ReasonCode` (`[JsonPropertyName("reasonCode")]`, `string?`) to `RoutingDecision`, copied into `ComputeSignableBytes()`'s object.
- [ ] T016 [P] [US2] Update the `DecisionNotice` model in `src/Common/Sorcha.Blueprint.Models/Route.cs`: **remove** `ReasonField`; add `ReasonCodeField` (`string?`), `Reasons` (`Dictionary<string,string>?`), `FallbackMessage` (`string?`). XML docs on each, per the contract.
- [ ] T017 [US2] Test: the producer resolves `ReasonCodeField` from the submitted payload into `RoutingDecision.ReasonCode`; a route with no notice leaves it null; an unresolvable pointer leaves it null — in the producer test file from T007.
- [ ] T018 [US2] Producer: when the taken route declares an `x-decision-notice` with a `ReasonCodeField`, resolve that JSON Pointer from the submitted payload and set `RoutingDecision.ReasonCode`, in `ActionExecutionService`. Reuse a pointer-resolution helper rather than reintroducing one (the deleted `DecisionNoticeDispatcher.ResolvePointerString` logic can move to a small private helper).
- [ ] T019 [US2] Test: `ReactionDispatcher` resolves the notice message as `reasons[reasonCode] ?? fallbackMessage`; an unknown code and an absent code both yield `fallbackMessage` — in `ReactionDispatcherTests.cs`.
- [ ] T020 [US2] Implement that message resolution in the `ReactionDispatcher` decision-notice reaction.

**Checkpoint**: the applicant reads blueprint-authored copy; no free text is in clear metadata.

---

## Phase 5: Demo artefacts + documentation

- [ ] T021 [P] Update `demos/AIAS/blueprints/aias-assured-identity.template.json`: add a `reasonCode` property (string, `enum: [postcode-not-found, profanity, email-unverified]`) to action 2's `dataSchema`; rewrite the `rejected-terminal` route's `x-decision-notice` to the codified shape (`reasonCodeField` + `reasons` + `fallbackMessage`, no `reasonField`).
- [ ] T022 [P] Update `demos/AIAS/agent/assure-id.rules.json`: add the matching `reasonCode` to each of the three reject rules' payloads. Leave `verificationNotes` in place (it stays as the on-ledger audit record).
- [ ] T023 [P] Update `.claude/skills/blueprint-builder/SKILL.md` — the `x-decision-notice` section, to the v2 codified contract (link `specs/184-decision-notice-decentralised/contracts/x-decision-notice-extension.md`).
- [ ] T024 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` — the F183 entry, to record that the notice now fires from the `ReactionDispatcher` on the recipient's node and that the reason is codified on the signed `RoutingDecision`.

---

## Phase 6: Verification

- [ ] T025 Full `dotnet build` clean (no new warnings) and the affected test projects green: `Sorcha.Blueprint.Service.Tests`, `Sorcha.Blueprint.Engine.Tests`, `Sorcha.Register.Models.Tests`.
- [ ] T026 Grep-verify the clean break: no `DecisionNoticeDispatcher` and no `ReasonField` / `reasonField` remain anywhere in `src/` or `demos/`.
- [ ] T027 Deploy to n1 (code-only). **`RoutingDecision` is in the shared `Sorcha.Register.Models`, and the validator re-derives `ComputeSignableBytes()` from the deserialized decision — an old validator image drops the new fields and REJECTS the transaction with `VAL_ROUTING_002`. So blueprint-service, validator-service and register-service must be pulled + recreated TOGETHER.** Keep `-f docker-compose.smtp.yml` in the standing `up`. Then republish the AIAS blueprint with `Publish-AiasBlueprint -Force`.
- [ ] T028 Live acceptance (Chrome-DevTools): drive a reject through the web path as the citizen; confirm a durable bell/inbox entry carrying the **blueprint's** wording for the emitted code; confirm it survives a reload and a sign-out / sign-in. Confirm an approve produces no decision notice. Confirm the sealed tx's clear metadata carries `routeId` + `reasonCode` and no free-text reason.

---

## Dependencies

```
Setup (T001)
   └── Foundational (T002–T006)          # carrier routeId + engine matched-route-id
          ├── US1 (T007–T013)            # decentralised firing; deliverable alone (fallback wording)
          │      └── US2 (T014–T020)     # codified per-reason wording
          │             └── Demo + docs (T021–T024)
          │                    └── Verification (T025–T028)
```

**Parallel opportunities**: T002 ∥ T004 (different test projects). T014 ∥ T016. T021 ∥ T022 ∥ T023 ∥ T024.

**MVP**: Foundational + US1 — a rejected applicant stops receiving nothing. US2 makes what they receive
specific and encrypted-register-safe, and is required before the AIAS demo is re-run.
