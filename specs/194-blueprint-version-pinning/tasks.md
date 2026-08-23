---
description: "Task list for Feature 194 — Blueprint Version Pinning"
---

# Tasks: Blueprint Version Pinning

**Input**: Design documents from `/specs/194-blueprint-version-pinning/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included, and non-negotiable. The spec makes mutation evidence a success criterion
(SC-008) and makes a live run the acceptance gate — this feature exists because a live run found
what ~2,500 green tests did not.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelisable — different files, no dependency on an incomplete task
- **[Story]**: the user story served (US1–US5); setup, foundational and polish phases carry none

## Path conventions

Sorcha layout: `src/Common/`, `src/Core/`, `src/Services/`, `tests/`. Paths below are repo-relative
and exact.

---

## Phase 1: Setup

**Purpose**: establish the baseline the rest of the work is measured against.

- [ ] T001 Confirm a clean baseline: `dotnet restore && dotnet build` succeeds with no new warnings, and record the result in the PR description
- [ ] T002 [P] Record the current executable-definition hash of two shipped blueprints (`walkthroughs/ForestryCertification/forestry-certification-template.json`, `demos/AIAS/blueprints/aias-assured-identity.template.json`) to a scratch note, so the T040 hasher change can be shown to alter them and the T041 presentational edit shown not to
- [ ] T003 [P] Verify `dotnet ef migrations script --idempotent --project src/Services/Sorcha.Blueprint.Service` currently names exactly one migration, establishing the pre-change state for T024

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: make the signature honest and the hash trustworthy. Nothing else may start until these
are done — pinning to a hash is meaningless while the hashed content can change under it, and adding
a field to `RoutingDecision` is unsafe until the signing guard exists.

**⚠️ CRITICAL**: no user story work begins until this phase is complete.

### The signing guard — write this before touching `RoutingDecision`

- [ ] T004 Write a reflection-driven test in `tests/Sorcha.Register.Models.Tests/Transactions/RoutingDecisionSigningCoverageTests.cs` that enumerates `typeof(RoutingDecision).GetProperties()`, and for every property except `Attestation` mutates it and asserts `ComputeSignableBytes()` output changes. It MUST NOT contain a hand-written field list — a list rots in the same direction as the bug
- [ ] T005 Mutation-prove T004: remove `ReasonCode = ReasonCode` from `ComputeSignableBytes()` in `src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs`, confirm the test fails **and names `ReasonCode`**, restore, confirm green. Record the outcome in the PR — a guard that has only ever been green proves nothing

### The hash addresses exactly the bytes that get stored

- [ ] T006 In `src/Services/Sorcha.Blueprint.Service/Program.cs` `PublishService.PublishAsync`, deep-copy the blueprint into the `PublishedBlueprint` instead of storing the live draft reference, taking the copy **after** `FlattenActionSchemas` and after the `hasCycles` metadata write (research R-004)
- [ ] T007 Add `ExecDefHash` to the `PublishedBlueprint` record in `src/Services/Sorcha.Blueprint.Service/Program.cs`, computed by `ExecutableDefinitionHasher.ComputeHash` over the T006 copy
- [ ] T008 Add `Task<PublishedBlueprint?> GetByExecDefHashAsync(string blueprintId, string execDefHash)` to `IPublishedBlueprintStore` and implement it in `InMemoryPublishedBlueprintStore`, both in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T009 [P] Test in `tests/Sorcha.Blueprint.Service.Tests/Publishing/PublishSnapshotIsolationTests.cs`: publish a blueprint, then mutate the draft object in place, and assert the published snapshot's content and `ExecDefHash` are unchanged. This is the T006 regression guard and it must be watched failing before T006 is applied
- [ ] T010 [P] Test in `tests/Sorcha.Blueprint.Service.Tests/Publishing/PublishHashOrderingTests.cs`: a blueprint whose action schemas use a Sorcha core `$ref` hashes to the value of its **flattened** form, proving the hash is taken after flattening

**Checkpoint**: the signature is guarded and a published definition is genuinely immutable and
content-addressed. User story work can begin.

---

## Phase 3: User Story 1 — An in-flight application is unaffected by a republish (P1) 🎯 MVP

**Goal**: an instance runs the definition it started on, and every submission is validated against
that definition rather than the latest.

**Independent test**: start an instance, republish with a behavioural change, advance the instance —
it completes under the original rules.

### The pin travels on the wire

- [ ] T011 [US1] Add `BlueprintExecDefHash` (`string?`, `[JsonPropertyName("blueprintExecDefHash")]`, `JsonIgnoreCondition.WhenWritingNull`) to `src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs`, **and to the `ComputeSignableBytes()` rebuild**. T004 must pass without modification
- [ ] T012 [US1] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` step 10d, stamp the pin on the `RoutingDecision`: for a starting action, the hash of the latest published definition on that register; for every subsequent action, the instance's established pin read from the instance
- [ ] T013 [US1] Apply the identical stamping in the second producer, `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs` (around its `ComputeSignableBytes` call). Both producers must agree or encrypted-register submissions diverge from plaintext ones
- [ ] T014 [P] [US1] Test in `tests/Sorcha.Register.Models.Tests/Transactions/RoutingDecisionWireCompatibilityTests.cs`: a decision with a null pin serialises byte-identically to a pre-feature decision, so an existing signature still verifies

### The pin is folded onto the instance

- [ ] T015 [US1] Add `BlueprintExecDefHash` to `ProjectedTransaction` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjection.cs`
- [ ] T016 [US1] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjectionResolver.cs`, read the pin off the sealed decision alongside `RouteId`/`ReasonCode` — typed field first, `routingDecision` tracking JSON as fallback — and carry it onto `ProjectedTransaction`
- [ ] T017 [US1] In `InstanceProjection`, set `Instance.BlueprintExecDefHash` from the starting transaction and make `Apply` **refuse** a transaction whose pin differs from the instance's established pin (FR-007). Drop the `blueprintVersion` parameter from `Project`
- [ ] T018 [US1] Remove the hardcoded `blueprintVersion: 1` from `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjector.cs:170`
- [ ] T019 [US1] Add `BlueprintExecDefHash` to `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs`

### Persistence

- [ ] T020 [US1] Add the column to `src/Services/Sorcha.Blueprint.Service/Data/Entities/InstanceEntity.cs`
- [ ] T021 [US1] Add it to the hand-written model→entity copy in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` (~line 122) and to the two entity→model projections (~lines 493, 533). A field missing here is written in memory, reported saved, and lost
- [ ] T022 [US1] Fold the column into the existing migration — `Data/Migrations/20260528205017_InitialCreate.cs`, its `.Designer.cs`, and `Data/Migrations/BlueprintDbContextModelSnapshot.cs` — all three together. Do **not** run `dotnet ef migrations add` (CLAUDE.md §19)
- [ ] T023 [P] [US1] Extend `tests/Sorcha.Blueprint.Service.Tests/Storage/EfCoreInstanceStoreUpdateRoundTripTests.cs` to cover the new field, so a missing line in the T021 hand-copy fails the build rather than losing data silently
- [ ] T024 [US1] Prove the migration: `dotnet ef migrations script --idempotent` names exactly one migration and its `CREATE TABLE` contains `BlueprintExecDefHash`; apply the script to a scratch database and inspect `information_schema.columns`

### The pin is enforced by the validator

- [ ] T025 [US1] Change `ResolveBlueprintAsync` in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:2380` to take the pin, and update **all three** call sites (lines 570, 972, 1369 — research R-007)
- [ ] T026 [US1] Refuse an unresolvable pin with `VAL_BP_VERSION_001` in `ValidationEngine`, declared alongside the service's existing internal codes (research R-010 — it stays validator-local until a second project names it). **No fallback to latest** on this path
- [ ] T027 [US1] Extend `ValidateRoutingDecisionAsync` so the pin is covered by the existing `VAL_ROUTING_002` signature verification with no new verification code — confirm by test rather than by inspection

### The cache is keyed by content

- [ ] T028 [US1] Create one shared key helper for the validator blueprint cache key format, and use it in **both** homes: `src/Services/Sorcha.Validator.Service/Services/BlueprintCache.cs` (`GetBlueprintKey`) and the hardcoded literal in `src/Services/Sorcha.Blueprint.Service/Program.cs` `PublishAsync` (research R-005). Changing one and not the other fails silently to the old behaviour
- [ ] T029 [US1] Re-key `IBlueprintCache` by `(blueprintId, execDefHash)` in `src/Services/Sorcha.Validator.Service/Services/Interfaces/IBlueprintCache.cs` and its implementation — `GetBlueprintAsync`, `GetOrFetchAsync`, `GetActionAsync`, `ExistsAsync`, `RemoveAsync`, `SetBlueprintAsync`, and the Redis invalidation channel payload. This is an interface change, not a key-string change
- [ ] T030 [US1] Resolve by pin in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionResolverService.cs:75-79` — add a pinned overload; the existing latest-resolving method stays for design-time callers

### Story tests

- [ ] T031 [P] [US1] Test in `tests/Sorcha.Blueprint.Service.Tests/Projection/InstancePinTests.cs`: an instance created from a starting action carries that action's pin, and folding a later transaction carrying a **different** pin is refused
- [ ] T032 [P] [US1] Test in `tests/Sorcha.Blueprint.Service.Tests/Projection/InstancePinDeterminismTests.cs`: folding the same transaction set in several orders yields the same pin
- [ ] T033 [P] [US1] Test in `tests/Sorcha.Validator.Service.Tests/Services/PinnedResolutionTests.cs`: the validator resolves the pinned definition when a newer one exists, and refuses an unresolvable pin rather than falling back
- [ ] T034 [P] [US1] Test in `tests/Sorcha.Validator.Service.Tests/Services/BlueprintCacheKeyTests.cs`: the cache key includes the pin, and two definitions of one blueprint occupy distinct entries

**Checkpoint**: US1 is independently demonstrable — the MVP. An in-flight instance survives a
republish.

---

## Phase 4: User Story 2 — Publishers upgrade freely, new applicants get the new rules (P2)

**Goal**: republishing is never blocked by live instances, and a new instance is governed by the new
definition.

**Independent test**: with an instance in flight, republish (must succeed unimpeded), then start a
second instance and confirm it enforces the new requirement.

- [ ] T035 [US2] In `src/Services/Sorcha.Blueprint.Service/Program.cs` (~line 2224), make instance creation record the pin of the definition it resolved, replacing the bare `PublishedBlueprintSelector.SelectLatest` result with the selected entry's `ExecDefHash`
- [ ] T036 [P] [US2] Test in `tests/Sorcha.Blueprint.Service.Tests/Publishing/RepublishWithLiveInstancesTests.cs`: publishing succeeds with instances of the blueprint in flight, and no instance's pin, state or current actions change as a result
- [ ] T037 [P] [US2] Test in `tests/Sorcha.Blueprint.Service.Tests/Publishing/ConcurrentDefinitionsTests.cs`: two instances on one register pinned to different definitions each validate against their own — and the newer instance's submission is **refused** when it omits the field only the newer definition requires, proving the new rule is enforced rather than merely recorded

**Checkpoint**: upgrade is usable as well as safe.

---

## Phase 5: User Story 3 — The pin survives a restart, a rebuild, and a second node (P2)

**Goal**: recovery restores every published definition, and the rebuild path reaches the same pin as
the online projector.

**Independent test**: run US1 to mid-flow, restart the service, advance the instance.

- [ ] T038 [US3] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintRecoveryService.cs` (~line 326), stop collapsing to newest-per-id: restore **every** published version. Keep the F138 provenance check per version — a version that fails it is still rejected
- [ ] T039 [US3] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceRebuildService.cs:104`, remove the hardcoded `blueprintVersion: 1` and take the identical resolution and fallback path as `InstanceProjector`, sharing the code rather than mirroring it
- [ ] T040 [US3] Implement the pre-feature fallback (research R-009): an un-pinned transaction resolves to the latest published definition, logged at Warning, in **one** shared code path used by both the projector and the rebuild. It must never apply to a transaction that does carry a pin
- [ ] T041 [P] [US3] Test in `tests/Sorcha.Blueprint.Service.Tests/Recovery/RecoveryRestoresAllVersionsTests.cs`: a register carrying three publications of one blueprint recovers three resolvable definitions, not one
- [ ] T042 [P] [US3] Extend the existing F145 rebuild-parity test so parity now covers the pin — the online instance and the rebuilt instance must agree on it
- [ ] T043 [P] [US3] Test in `tests/Sorcha.Blueprint.Service.Tests/Projection/PreFeatureFallbackTests.cs`: an un-pinned transaction folds via the fallback and increments the counter; a pinned one never does

**Checkpoint**: the pin is durable, not merely in memory.

---

## Phase 6: User Story 4 — Relabelling a field does not strand anyone (P3)

**Goal**: a presentational-only republish produces the same pin.

**Independent test**: republish with only presentational edits; the pin is unchanged and no instance
is disturbed.

> **Sequencing note**: T044 changes every blueprint's hash and so invalidates every recorded F142
> `RehearsalPass`. US1 does not depend on it (the ordinal does not currently change on republish),
> so this story is genuinely independent — but if both ship in one release, land T044 **before**
> deploying, so the hash churn happens once rather than twice.

- [ ] T044 [US4] Remove `["version"] = blueprint.Version` from `BuildExecutableDefinition` in `src/Core/Sorcha.Blueprint.Engine/Implementation/ExecutableDefinitionHasher.cs` (research R-003). Nothing else about the classifier or canonicalisation changes
- [ ] T045 [P] [US4] Test in `tests/Sorcha.Blueprint.Engine.Tests/ExecutableDefinitionHasherStabilityTests.cs`: a presentational-only edit (title, description, `x-introduction`, field ordering hints) yields the **same** hash; a behavioural edit (a new `required` entry, a changed `enum`, a changed route condition) yields a **different** one
- [ ] T046 [P] [US4] Test that two blueprints differing only in `Version` hash identically, pinning the T044 change
- [ ] T047 [US4] Confirm against the T002 recorded values that the shipped blueprints' hashes changed exactly once, and note in the PR that existing rehearsal passes are invalidated by design

**Checkpoint**: cosmetic edits are safe, and demonstrably so.

---

## Phase 7: User Story 5 — An operator can see which definition an instance is running (P3)

**Goal**: the pin is reportable, and the three states (pinned-and-resolvable, pinned-but-unresolvable,
unpinned) are distinguishable.

**Independent test**: read an instance after a republish; the response names its pin and says whether
it is the latest.

- [ ] T048 [US5] Add `blueprintExecDefHash` and `isPinnedToLatest` to the instance read response, and derive `blueprintVersion` from the pin rather than a stored column, per [contracts/instance-pin-read.md](./contracts/instance-pin-read.md). Return nulls — never a guess — when the pin is unresolvable
- [ ] T049 [P] [US5] Add `execDefHash` to each entry of `GET /api/blueprints/{id}/versions` in `src/Services/Sorcha.Blueprint.Service/Program.cs` (~line 1056)
- [ ] T050 [US5] In `src/Services/Sorcha.Blueprint.Service/Endpoints/MeApplicationEndpoints.cs:201`, resolve the decision-notice `reasons` catalogue from the **pinned** definition, so a refused applicant reads the wording in force when they applied. No new field on the citizen response
- [ ] T051 [P] [US5] Add the two counters (`sorcha_instance_pin_fallback_total{path}`, `sorcha_instance_pin_mismatch_total{reason}`) to the existing `Sorcha.Blueprint.Service` meter, carrying opaque identifiers only
- [ ] T052 [P] [US5] Test in `tests/Sorcha.Blueprint.Service.Tests/Endpoints/InstancePinReadTests.cs`: all three states render distinguishably, and an unresolvable pin does not substitute a version label

**Checkpoint**: the next investigation is not blind.

---

## Phase 8: Polish & cross-cutting

### Remove the dead resolver (research R-006)

- [ ] T053 Delete `src/Services/Sorcha.Validator.Service/Services/BlueprintVersionResolver.cs` and `Services/Interfaces/IBlueprintVersionResolver.cs`
- [ ] T054 Remove `AddBlueprintVersionResolver` and its call from `src/Services/Sorcha.Validator.Service/Extensions/BlueprintVersionResolverExtensions.cs`, keeping the control-blueprint resolver, which is live and unrelated
- [ ] T055 Remove the `_versionResolver` field and the `InvalidateCache(registerId)` call from `src/Services/Sorcha.Validator.Service/Services/ControlDocketProcessor.cs:271` — observably a no-op, so removing it changes no behaviour
- [ ] T056 Delete `tests/Sorcha.Validator.Service.Tests/Services/BlueprintVersionResolverTests.cs` with its subject
- [ ] T057 [P] File a follow-up issue: a register configuration change no longer even appears to invalidate the blueprint cache. It never did, and content-keyed entries largely dissolve the need — but it should be a decision on the record, not a silent side effect of this feature

### Mutation evidence (SC-008)

- [ ] T058 Mutation-prove the pin's signing coverage: remove `BlueprintExecDefHash` from `ComputeSignableBytes()`, confirm the T004 reflection test fails and names it, restore
- [ ] T059 Mutation-prove the validator refusal: make `ResolveBlueprintAsync` fall back to latest on an unresolvable pin, confirm T033 fails, restore
- [ ] T060 Mutation-prove recovery: restore newest-per-id, confirm T041 fails, restore
- [ ] T061 Mutation-prove the cache re-key: re-key the reader and not the writer, confirm a pinned-resolution test fails. **If it still passes, the test is not reading through the cache** and must be rewritten before this box is ticked
- [ ] T062 Record in the PR which named test each of T058–T061 killed. A guard whose killing test is unknown has not been proven

### Live acceptance on n1 (the gate — see [quickstart.md](./quickstart.md))

- [ ] T063 Deploy `validator-service` **first**, then `blueprint-service`. The reverse order refuses every submission (old validator computes different signable bytes)
- [ ] T064 Recreate the blueprint Postgres database on the target; confirm the new column exists before running anything
- [ ] T065 Run the six-step live test: publish v1 → start instance A mid-flow → republish v2 with a required field on a later action → advance A against v1 → start B and confirm it requires the new field → **restart `blueprint-service` and advance A again**
- [ ] T066 Verify the pin is on the wire and sealed, by reading `MetaData.RoutingDecision.blueprintExecDefHash` out of Mongo for A's actions — not by reading the instance row, which is a local projection
- [ ] T067 Verify `sorcha_instance_pin_fallback_total` reads **zero** for a register created after the deploy. This is the positive check; every failure mode of this feature degrades to the old behaviour rather than to an error, so absence of errors is not evidence

### Documentation

- [ ] T068 [P] Update `.specify/MASTER-TASKS.md` with feature 194's status
- [ ] T069 [P] Update the `blueprint-builder` skill: republishing to a register no longer affects in-flight instances, and what that means for authors
- [ ] T070 [P] Update the `sorcha-architecture` skill with the pin, its carrier, and the deployment-order constraint
- [ ] T071 [P] Update `docs/reference/API-DOCUMENTATION.md` for the additive response fields, with XML docs and OpenAPI descriptions on each
- [ ] T072 [P] Correct the design document's rollout section, which states both that a re-genesis is needed and that no genesis window is required — only the second is true, and that is why the fallback is load-bearing

---

## Dependencies

```
Phase 1 Setup
    ▼
Phase 2 Foundational  (T004–T010)   ← BLOCKS EVERYTHING
    ▼
Phase 3 US1 (P1, MVP)  (T011–T034)
    ├──► Phase 4 US2   (T035–T037)   depends on US1
    └──► Phase 5 US3   (T038–T043)   depends on US1
Phase 6 US4 (T044–T047)   independent of US1–US3; land before deploy if shipping together
Phase 7 US5 (T048–T052)   depends on US1 (needs a pin to report)
    ▼
Phase 8 Polish (T053–T072)
    T058–T062 require the guards they mutate to exist
    T063–T067 require everything merged and published
```

**Story independence**: US4 is fully independent. US2, US3 and US5 each depend only on US1, not on
each other, so they can proceed in parallel once US1 lands.

---

## Parallel execution examples

**Phase 2** — T009 and T010 are different test files and can run together once T006–T008 land.

**Phase 3** — after T011, three tracks proceed in parallel:
- persistence: T019 → T020 → T021 → T022 → T024
- validator: T025 → T026 → T027
- cache: T028 → T029

and the four story tests T031–T034 are all `[P]` against different files.

**Phase 5 vs Phase 4** — different files, no shared state; run both once US1 is merged.

**Phase 8 docs** — T068–T072 are all `[P]`.

---

## Implementation strategy

**MVP is Phase 1 + Phase 2 + Phase 3 (US1).** That alone delivers the entire user-visible value: an
in-flight application survives a republish. It is shippable and demonstrable on its own.

**Then, in order of what protects the MVP**: US3 (the pin survives a restart — the MVP is not real
until this holds), US2 (upgrade is usable), US5 (the next investigation is not blind), US4 (cosmetic
edits are provably safe).

**One PR per logical change**, merged on green:

| PR | Contents |
|---|---|
| 1 | Phase 2 — signing guard + publish-snapshot immutability + `ExecDefHash` |
| 2 | Phase 3 — the pin, end to end (the MVP) |
| 3 | Phase 5 — recovery and rebuild durability |
| 4 | Phase 4 + Phase 7 — upgrade proof and the read surface |
| 5 | Phase 6 — the hasher ordinal removal |
| 6 | Phase 8 — dead-resolver removal and documentation |

**T065 is ticked only after the live run has actually been executed and observed to pass** — never
inferred from a green suite. Step 6 of it (restart, then advance the old instance) is the one most
likely to fail and the one most worth having.
