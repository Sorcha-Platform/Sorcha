# Tasks: Blueprint Definition Identity

**Feature**: 195 | **Branch**: `195-blueprint-definition-identity`
**Input**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/](./contracts/) · [quickstart.md](./quickstart.md)
**Issues**: #1563, #1566, #1567, #1568, #1570

---

## Conventions for this feature

- **`dotnet test` runs in MTP mode** (`global.json`). Use `--project x.csproj` and
  `--filter-class "*Name*"`. `--collect` is dead. Solution-wide local runs report contention
  failures — judge per project.
- **Every guard is mutation-tested.** A guard written after the code is green never ran red naturally
  and proves nothing. Each mutation task names the test it must kill, and **only** that test.
- **Guards land before the code they guard**, where the guard is the specification (Phase 2).
- **Use Write/Edit for `.cs` and `.ps1`** — bash heredocs mangle backticks and `$vars`.
- **No migration.** Pre-release; register wipe authorised. Do not write compatibility paths.

---

## Phase 1: Setup

- [x] T001 Record the pre-change test baseline per project (`Sorcha.Blueprint.Models.Tests`, `Sorcha.Blueprint.Engine.Tests`, `Sorcha.Blueprint.Service.Tests`, `Sorcha.Validator.Service.Tests`, `Sorcha.Register.Service.Tests`) into `specs/195-blueprint-definition-identity/baseline.md`, so a later count change is attributable rather than assumed
- [x] T002 [P] Run `dotnet ef migrations has-pending-model-changes` for the Blueprint context and record the result in `specs/195-blueprint-definition-identity/baseline.md` — F194 lost a deploy to skipping this, and the container reports HEALTHY when migrations fail

---

## Phase 2: Foundational — canonical form and identity (BLOCKING)

**Everything downstream is defined in terms of this.** No user story may start until Phase 2 is green.
Per contracts/publication-identity.md §5, the golden vector is written **first** and must be **seen to
fail**.

- [ ] T003 [P] Write the failing golden-vector test in `tests/Sorcha.Blueprint.Models.Tests/Canonical/BlueprintCanonicalJsonGoldenVectorTests.cs` — a committed fixture blueprint plus its expected canonical bytes and expected publication id. Run it and record that it fails for the right reason (type does not exist yet)
- [ ] T004 [P] Write the failing preimage-property tests in `tests/Sorcha.Blueprint.Models.Tests/Canonical/BlueprintPublicationIdTests.cs`: two-register distinctness, two-blueprint distinctness, domain-tag separation from `InstanceIdentity.Derive` over the same first two fields, and `0x1F` boundary ambiguity (`("ab","c")` vs `("a","bc")`)
- [ ] T005 [P] Write the failing canonical-form tests in `tests/Sorcha.Blueprint.Models.Tests/Canonical/BlueprintCanonicalJsonTests.cs`: recursive key sorting, array order preserved, whitespace and escaping normalised by the parse, **duplicate object keys rejected**, and a decision test pinning the number rule
- [ ] T006 Implement `src/Common/Sorcha.Blueprint.Models/Canonical/BlueprintCanonicalJson.cs` — parse → serialize with recursively sorted object keys, arrays preserved, duplicate keys rejected. **Do not reuse** `RegisterSerializationOptions.Canonical` or `BlueprintContentHash`: neither sorts keys, so both are serializer-output addresses (research R-002)
- [ ] T007 Implement `src/Common/Sorcha.Blueprint.Models/BlueprintPublicationId.cs` — `Compute(registerId, blueprintId, canonicalJson)` per contracts/publication-identity.md §1, with the `sorcha:blueprint-publication:v1` domain tag and `0x1F` separators, mirroring `InstanceIdentity.Derive`'s style
- [ ] T008 Settle the open number-normalisation question from research R-002 and record the decision inline in `BlueprintCanonicalJson.cs` — normalise, or preserve-and-pin-by-vector. Either is defensible; leaving it undecided is not
- [ ] T009 Run T003–T005 green, then mutation-test: remove key sorting, drop the domain tag, drop `registerId`, drop `blueprintId`, replace `0x1F` with plain concatenation, accept duplicate keys. **Each must kill exactly its own named test.** Record the matrix in `specs/195-blueprint-definition-identity/mutations.md`
- [ ] T010 Add the architecture gate `scripts/check-publication-id-owner.ps1` + a CI job, asserting no caller of `BlueprintPublicationId.Compute` outside `Sorcha.Register.Service` and the test projects (contracts/publication-identity.md §3). Model it on `scripts/check-derivation-contexts.ps1`, which derives its guarded set from the canonical file so it cannot itself drift

**Checkpoint:** the identity is computable, guarded, and provably owned. Nothing consumes it yet.

---

## Phase 3: User Story 1 — A published definition survives (P1) 🎯 MVP

**Goal:** every definition ever published is permanently recorded and independently retrievable.
**Independent test:** publish → start instance → behavioural republish → restart → the in-flight
instance still resolves and advances. Closes #1563 and #1570.

### Producer

- [ ] T011 [US1] Replace the inline txId derivation in `src/Services/Sorcha.Register.Service/Program.cs:2018` with `BlueprintPublicationId.Compute` over the canonicalised request payload. This is the sole producer
- [ ] T012 [US1] Add the `alreadyPublished` discriminator to the publish response (contracts/blueprint-definitions.openapi.yaml) — identical content is an idempotent no-op that still returns 200, but must be **distinguishable** from a real publish. Indistinguishability is how #1563 stayed invisible
- [ ] T013 [US1] Remove the `contentHash` metadata key from the publish transaction in `src/Services/Sorcha.Register.Service/Program.cs` — absorbed, since the transaction id is itself the digest (data-model.md)
- [ ] T014 [P] [US1] Delete `src/Common/Sorcha.ServiceClients.Http/Register/BlueprintContentHash.cs` and its references

### One writer

- [ ] T015 [US1] Delete the instance-creation publish branch at `src/Services/Sorcha.Blueprint.Service/Program.cs:2305-2318`. It pushes the **unflattened draft** where `PublishService` pushes a flattened deep-copied snapshot — two shapes of one blueprint, invisible today only because the version-blind id dedupes the second away (#1570)
- [ ] T016 [US1] Add `PublicationTxId` to `PublishedBlueprint` (`src/Services/Sorcha.Blueprint.Service/Program.cs:4114`), populated in `PublishAsync` from the Register Service's response. **Never computed locally** — this absence is what created the whole problem (research R-004)
- [ ] T017 [US1] Change `IRegisterServiceClient.PublishBlueprintToRegisterAsync` to return the txId and `alreadyPublished` rather than `bool`, in `src/Common/Sorcha.ServiceClients.Http/Register/`

### Recovery

- [ ] T018 [US1] Change `BlueprintRecoveryService` dedupe from the recomputed `execDefHash` to `PublicationTxId`, and replace `TryVerifyProvenance` with recompute-and-compare against the transaction's own id — self-anchoring, so a tampered payload cannot match its own id
- [ ] T019 [US1] Remove `contentHash` from `PublishedBlueprintEntry` and the recovery endpoint's projection in `src/Services/Sorcha.Register.Service/Program.cs:2117-2190`

### Tests

- [ ] T020 [P] [US1] `tests/Sorcha.Register.Service.Tests` — publish produces the expected id; identical republish is idempotent with `alreadyPublished: true` and writes no second transaction; **behaviourally different republish writes a second transaction** (the check that fails today)
- [ ] T021 [P] [US1] `tests/Sorcha.Blueprint.Service.Tests` — recovery restores every definition on a register, deduped by publication id, and rejects a payload whose recomputed id does not match its transaction id
- [ ] T022 [US1] Mutation-test: restore the version-blind txId (must kill the second-transaction test); drop the recovery id verification (must kill the tamper test); reinstate the instance-creation publish branch (must kill a single-writer test). Record in `mutations.md`

**Checkpoint:** a republished definition reaches the ledger and survives a restart. Deliverable alone.

---

## Phase 4: User Story 2 — The submitted action is judged by the instance's definition (P1)

**Goal:** the form shown, the rules applied, the calculations run and the route taken all come from
the definition the instance is running. **Independent test:** edit the draft so it disagrees with the
published definition, submit, and confirm the draft had no effect. Closes #1567.

> **Sequencing note.** This story can be delivered standalone against the *existing* pin value if
> Phase 3 slips — the defect is that the resolver takes no pin at all, independent of what the pin
> denotes. Delivered here, it uses the publication id.

### The pin becomes a publication id

- [ ] T023 [US2] Rename `RoutingDecision.BlueprintExecDefHash` → `BlueprintDefinitionTxId` (`blueprintDefinitionTxId`) in `src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs`, **including the field-by-field rebuild in `ComputeSignableBytes()` at `:112`**. A property omitted there rides the wire unauthenticated while appearing signed
- [ ] T024 [US2] Confirm `RoutingDecisionSigningCoverageTests` still passes and still discriminates — it is reflection-driven and must fail on a property it cannot mutate
- [ ] T025 [US2] Rename `Instance.BlueprintExecDefHash` → `BlueprintDefinitionTxId` in `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs:52` and in `EfCoreInstanceStore.UpdateAsync`'s hand-written model→entity copy list — a field missing from that list is written in memory, reported saved, and lost
- [ ] T026 [US2] Fold the schema change into the Blueprint `InitialCreate` migration per CLAUDE.md §19 (amend `InitialCreate.cs`, its `.Designer.cs` and `*ModelSnapshot.cs` together), then verify with `dotnet ef migrations has-pending-model-changes` — writing them is not enough if what you write is not what EF would generate

### Resolution at submit

- [ ] T027 [US2] Change `IActionResolverService.GetBlueprintAsync` (`src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IActionResolverService.cs:19`) to take the pin as a **required** parameter. Optional preserves the defect for every caller that omits it — which is how it survived F194
- [ ] T028 [US2] In `ActionResolverService.cs:45-104`: remove the draft store from the execution path, resolve by publication id, and add the pin to the distributed cache key at `:54`
- [ ] T029 [US2] Add the pin to the **static** `_actionIndexCache` at `ActionResolverService.cs:30`, or remove the cache. It is process-wide, so a bare-id key serves the wrong definition to a *different instance* than the one that populated it
- [ ] T030 [US2] Update `ActionExecutionService.ExecuteAsync:238` to pass `instance.BlueprintDefinitionTxId`, and confirm the pin stamped at `:1293`/`:1771` is now the same definition that was resolved

### Anchor and instance creation

- [ ] T031 [US2] Replace `ComputeBlueprintPublishTxId(...)` at `ActionExecutionService.cs:459` with a read of `instance.BlueprintDefinitionTxId`. **Keep** `WaitForTransactionConfirmationAsync` — it is a genuine precondition and now asserts something stronger (the exact definition, not merely the blueprint)
- [ ] T032 [P] [US2] Delete `ActionExecutionService.ComputeBlueprintPublishTxId:2989`, its second caller at `src/Services/Sorcha.Blueprint.Service/Program.cs:1622`, and the formula's fifth copy at `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionServiceTests.cs:1265-1277`
- [ ] T033 [US2] In `POST /api/instances` (`src/Services/Sorcha.Blueprint.Service/Program.cs:2244-2415`): resolve the definition **once** and use it for both initialisation (`:2346-2365`) and the pin (`:2372-2376`). Today it initialises from the draft and pins the latest published

### Validator

- [ ] T034 [US2] Add the register fallback arm to `ValidationEngine.ResolvePinnedBlueprintAsync:2482-2516` — cache → Blueprint Service → **read transaction `{pin}` from the register** → refuse. Keep the explicit no-fallback-to-latest refusal
- [ ] T035 [P] [US2] Rename `IBlueprintFetcher.FetchBlueprintByHashAsync` → `FetchBlueprintByPublicationAsync` and its endpoint path segment, so the name does not lie about what the parameter denotes
- [ ] T036 [P] [US2] Rename the `execDefHash` parameter on `BlueprintCacheKey.For` to `publicationTxId` (`src/Common/Sorcha.Blueprint.Models/BlueprintCacheKey.cs`). Key **shape** is unchanged; the by-id tier stays — system blueprints have no instance and therefore no pin

### Tests

- [ ] T037 [P] [US2] `tests/Sorcha.Blueprint.Service.Tests` — a submission is validated/calculated/routed by the instance's definition while a divergent draft exists; two instances on different definitions are each judged by their own; an unresolvable pin refuses with a diagnosable reason
- [ ] T038 [P] [US2] `tests/Sorcha.Validator.Service.Tests` — the register fallback arm resolves a definition absent from cache and Blueprint Service; an unresolvable pin still refuses and **never** falls back to latest; an unpinned (system) blueprint still resolves by id
- [ ] T039 [US2] Mutation-test: make the pin parameter optional (kills the submit-path test); drop the pin from either cache key (kills the cross-instance isolation test); let the validator fall back to latest (kills the pinned-refusal test); reinstate draft-first resolution (kills the divergent-draft test). Record in `mutations.md`

**Checkpoint:** the participant-facing guarantee is true. Both P1 stories delivered.

---

## Phase 5: User Story 3 — A behavioural change is recognised as one (P2)

**Goal:** behavioural edits require a fresh rehearsal; presentational ones do not. Closes #1566.
**Independent test:** each behavioural edit in turn is recognised; each presentational edit is not.

> Severity is reduced by Phase 3 — the publication id already covers the whole definition, so this is
> now about the **rehearsal** direction, which is F142's original tolerance.

- [ ] T040 [US3] Write the failing reflection guard `tests/Sorcha.Blueprint.Engine.Tests/ExecutableDefinitionCoverageTests.cs` — walk `Blueprint`/`Action`/`Route`/`Participant` properties against an explicit presentational deny-list and **fail on a property it cannot classify**
- [ ] T041 [US3] Add the omitted execution-affecting fields to `ExecutableDefinitionHasher.BuildExecutableDefinition` (`:78-96`), `BuildActions` (`:126`) and `BuildRoutes` (`:170`): `Action.RejectionConfig`, `Action.Participants`, `Action.RequiredActionData`, `Route.BranchDeadline`, `Route.DecisionNotice`, `Blueprint.PresentationConfig`, `Blueprint.InstanceReference`
- [ ] T042 [US3] Keep `Action.AdditionalRecipients` **out** of the projection and record why inline — it is inert (readers are `McpServer/Tools/Designer/BlueprintGetTool.cs` display and a doc comment). It failed the original probe and was nearly written up as a disclosure defect
- [ ] T043 [US3] Delete the `OrderByDescending(v => v.Version).First()` tie-break in `GetByExecDefHashAsync` (`src/Services/Sorcha.Blueprint.Service/Program.cs:2904`) and rename the method to `GetByPublicationAsync`. Its justifying comment's premise is false, and a publication id has nothing to tie-break
- [ ] T044 [US3] Narrow `ExecutableDefinitionHasher`'s documented job to the F142 rehearsal gate, in its XML docs — it is no longer an identity
- [ ] T045 [P] [US3] `tests/Sorcha.Blueprint.Engine.Tests` — each of the seven newly-covered fields changes the signature; a relabel/reword/reorder does **not**. Both directions
- [ ] T046 [US3] Mutation-test: **add a property to `Blueprint`/`Action`/`Route` and omit it from the projection.** T040 must fail and **every hand-written test in `ExecutableDefinitionHasherTests.cs` must stay green** — that contrast is the entire argument for reflection over a list. Record in `mutations.md`

---

## Phase 6: User Story 4 — One honest upgrade path (P3)

**Goal:** one upgrade path, and version labels that mean the same thing every time. Closes #1568.

- [ ] T047 [US4] In `src/Services/Sorcha.Blueprint.Service/Endpoints/BlueprintFromPublishedEndpoint.cs:152`, keep the **same** `blueprintId` on the clone instead of minting a GUID, so an amendment is a version of its blueprint and appears in its version history
- [ ] T048 [US4] Change the amend source selector at `:116` from `GetVersionAsync(blueprintId, version)` to selection by `publicationTxId`, and update the request contract
- [ ] T049 [US4] Retain the `x-source-*` lineage metadata at `:163-166` for the designer rail's "Amending vN" display, with the version key carrying the publication id
- [ ] T050 [US4] Derive `PublishedBlueprint.Version` on read from ledger order (oldest first) instead of storing `versions.Count + 1`, and add `publicationTxId` to `GET /api/blueprints/{id}/versions` entries
- [ ] T051 [P] [US4] Delete `Blueprint.VersionMajor` (`:106`) and `Blueprint.VersionMinor` (`:112`) and their writers in the amend clone and the designer properties panel — both are wholly dead
- [ ] T052 [P] [US4] `tests/Sorcha.Blueprint.Service.Tests` — an amendment appears in its blueprint's version history; ordinal labels are identical across a simulated restart; selecting a source by publication id is stable where selecting by ordinal was not
- [ ] T053 [US4] Mutation-test: restore ordinal-based amend selection (kills the source-stability test); restore the GUID mint (kills the version-history test). Record in `mutations.md`

---

## Phase 7: Polish & cross-cutting

- [ ] T054 [P] Update `.claude/skills/blueprint-builder/SKILL.md` — the three live traps recorded in the F194 republish section are resolved by this feature; rewrite rather than delete, so the reader learns what changed and why
- [ ] T055 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` — replace the "what F194 does NOT yet deliver" block with the delivered identity model
- [ ] T056 [P] Update `docs/reference/API-DOCUMENTATION.md` for the changed endpoints, and confirm every touched Minimal API endpoint has `.WithSummary()` and `.WithDescription()` (constitution III)
- [ ] T057 [P] Add XML docs to every new public type and member (`BlueprintCanonicalJson`, `BlueprintPublicationId`, the changed interfaces), recording the *why* — these are exactly the values that acquire two homes when the reason is not written down
- [ ] T058 Extend `walkthroughs/VersionPinning/run-acceptance.ps1` with the quickstart checks, including the two vacuous-prone ones: presentational republish **paired** with a behavioural one, and a **deliberate** identical publish to a second register
- [ ] T059 Run the full live acceptance per [quickstart.md](./quickstart.md) on a **re-genesised** node. Deploy order: `validator-service` → `blueprint-service` → **`register-service`**. Assert `pin_fallback` reads **zero** — the positive check
- [ ] T060 Update `.specify/MASTER-TASKS.md` with the outcome, including anything found live that the design did not have. F194 found five such things; expect some
- [ ] T061 Close #1563, #1566, #1567, #1568, #1570 with the live evidence, not with the merge

---

## Dependencies

```
Phase 1 (Setup)
   ↓
Phase 2 (Foundational — canonicaliser + identity + gate)   ← BLOCKS EVERYTHING
   ↓
   ├─→ Phase 3 (US1, P1) ─┐
   │                       ├─→ Phase 5 (US3, P2) ─→ Phase 6 (US4, P3)
   └─→ Phase 4 (US2, P1) ─┘                              ↓
                                                    Phase 7 (Polish + LIVE)
```

- **Phase 2 blocks all stories.** Every downstream correctness claim is defined in terms of the
  canonical form.
- **US1 and US2 are both P1 and are largely independent.** US2 can be delivered first, or standalone
  against the existing pin value if US1 slips — its defect (the resolver takes no pin at all) is
  independent of what the pin denotes. Delivered in order, US2 consumes US1's publication id.
- **US3 depends on US1** only for its severity reduction, not mechanically. It could ship first.
- **US4 depends on US1** for the publication id it selects by.
- **T059 (live run) depends on everything.** It is not substitutable by the suite.

## Parallel opportunities

- **Phase 2**: T003, T004, T005 are three independent test files — write all three before T006.
- **Phase 3**: T014 and T020/T021 are independent of the producer edits.
- **Phase 4**: T032, T035, T036 touch different files; T037 and T038 are different test projects.
- **Phase 6**: T051 and T052 are independent.
- **Phase 7**: T054–T057 are four different documents.

## Implementation strategy

**MVP = Phase 1 + Phase 2 + Phase 3.** That alone makes every published definition permanently
recorded and retrievable, which is the platform guarantee that does not hold today. It is shippable
and independently valuable.

**Then Phase 4**, which makes the guarantee true at the point the participant meets it. The two
together are the feature; Phases 5 and 6 are hygiene that becomes cheap once identity is settled.

**Do not defer Phase 7's live run.** Every defect this feature addresses degrades to plausible
behaviour with a green suite — a cache re-keyed on one side only, a producer that stops stamping, a
pin dropped from a copy list. The suite cannot see any of them.

## Task count

| Phase | Tasks |
|---|---|
| 1 — Setup | 2 |
| 2 — Foundational | 8 |
| 3 — US1 (P1) | 12 |
| 4 — US2 (P1) | 17 |
| 5 — US3 (P2) | 7 |
| 6 — US4 (P3) | 7 |
| 7 — Polish + live | 8 |
| **Total** | **61** |
