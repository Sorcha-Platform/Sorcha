# Tasks: Autonomous agent decides on disclosed application data

**Feature**: 176-agent-disclosed-payload | **Branch**: `176-agent-disclosed-payload`
**Inputs**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/disclosed-data-endpoint.md](./contracts/disclosed-data-endpoint.md),
[quickstart.md](./quickstart.md)

**Design (from research.md)**: Design A — implement the already-contracted disclosed-data endpoint in the
blueprint-service, backed by a shared disclosure resolver extracted from `ActionExecutionService`; the agent
fetches it per pending action and fails closed when it is unavailable.

**MVP scope**: **User Story 1** (Phase 3) is the minimum viable slice — with US1 done the agent decides on the
real application (bad postcode rejected, clean approved). US2 (fail-closed) and US3 (explainability) harden it.

**Test policy**: TDD — tests precede implementation within each phase (Constitution IV, >85% new code).

---

## Phase 1: Setup

- [x] T001 [P] Record the authoritative disclosed-data contract: read `GetDisclosedDataAsync` route + return type in `src/Common/Sorcha.ServiceClients.Http/Blueprint/IBlueprintServiceClient.cs` and `src/Common/Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs`, and the MCP consumer expectations in `src/Apps/Sorcha.McpServer/Tools/Participant/DisclosedDataTool.cs`; note the exact route + DTO the server MUST match into `specs/176-agent-disclosed-payload/contracts/disclosed-data-endpoint.md`.
- [x] T002 [P] Define/align the `DisclosedActionData` response model (reuse the existing client DTO if present) in `src/Services/Sorcha.Blueprint.Service/Models/` with XML docs on every member (Constitution III).

## Phase 2: Foundational (blocking prerequisites — MUST complete before user stories)

- [x] T003 Create the resolver seam `IActionDisclosureResolver` in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IActionDisclosureResolver.cs` (per contracts §2).
- [x] T004 Extract the disclosure logic from `ActionExecutionService.ApplyDisclosuresAsync` (`src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:1722-1783`) into `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionDisclosureResolver.cs` (engine `ApplyDisclosures` + participant→wallet resolution), and register it in DI in the service's Program/extensions.
- [x] T005 Refactor `ActionExecutionService` to depend on `IActionDisclosureResolver` (no behaviour change) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`.
- [x] T006 [P] Unit tests for `ActionDisclosureResolver` in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionDisclosureResolverTests.cs`: only-disclosed-fields returned; caller-wallet resolution (incl. multi-wallet); non-recipient → empty/`recipientResolved=false`; identical view for encrypted vs dev-mode payloads.
- [x] T007 Regression guard: an existing `ActionExecutionService` disclosure test (or a new one) proves the refactor (T005) did not change disclosure behaviour, in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionServiceTests.cs`.

**Checkpoint**: shared disclosure resolver exists, tested, and `ActionExecutionService` uses it with no drift.

---

## Phase 3: User Story 1 — Agent decides on the real application (Priority: P1) 🎯 MVP

**Goal**: The agent obtains the disclosed prior-action payload and decides on it — invalid application rejected
(no credential), valid application approved (credential delivered).

**Independent test**: Submit one valid + one invalid application; agent approves the first (credential) and
rejects the second (no credential), unattended (spec US1 / SC-001/002/003).

### Tests (write first)

- [x] T008 [P] [US1] Endpoint integration test in `tests/Sorcha.Blueprint.Service.Tests/Endpoints/WorkflowDisclosureEndpointsTests.cs`: authenticated recipient caller → `disclosedFields` populated with only disclosed fields; non-recipient caller → empty/`recipientResolved=false`; caller-wallet resolved via the Wallet-Service fallback (no `wallet_address` claim).
- [x] T009 [P] [US1] Agent inbox test in `tests/Sorcha.Agent.Tests/Inbox/DisclosedPayloadFetchTests.cs`: given a pending action, the inbox fetch calls `GetDisclosedDataAsync(instanceId, actionId)` and sets `PendingAction.PreviousPayload` to the disclosed fields.
- [x] T010 [P] [US1] Agent decision test in `tests/Sorcha.Agent.Tests/Decision/DecideOnRealDataTests.cs`: with a real disclosed payload, a non-existent postcode yields a `rejected` decision payload and a clean payload yields `approved` (rules over real `checks.*` facts).

### Implementation

- [x] T011 [US1] Implement `GET /api/workflows/{instanceId}/actions/{actionId}/disclosures` (match the T001 contract) in `src/Services/Sorcha.Blueprint.Service/Endpoints/WorkflowDisclosureEndpoints.cs` (or extend `ActionEndpoints.cs`): `.RequireAuthorization()`, resolve caller wallet(s) via the Wallet-Service fallback used in `ActionEndpoints.cs:178-184`, call `IActionDisclosureResolver`, return `DisclosedActionData`; add `.WithName`/`.WithSummary`/`.WithDescription` (Constitution III).
- [x] T012 [US1] Confirm/adjust `IBlueprintServiceClient.GetDisclosedDataAsync` + `BlueprintServiceClient` (`src/Common/Sorcha.ServiceClients.Http/Blueprint/`) so route + deserialization match the implemented endpoint.
- [x] T013 [US1] Wire the agent inbox to fetch disclosed data per pending action and populate `PreviousPayload`, and finalise the `dataSchema` mapping, in `src/Apps/Sorcha.Agent/Inbox/PollingInboxListener.cs` (+ a small fetch helper/service using `IBlueprintServiceClient`); ensure `PreviousPayload` is sourced from the fetch, not the pending summary.
- [ ] T014 [US1] Run the end-to-end regression `demos/AIAS/rehearse.ps1 -Target docker` (valid → approved + credential; invalid `ZZ99 9ZZ` → rejected + no credential) and confirm PASS (SC-001/002/003/006).

**Checkpoint**: US1 independently delivers a working autonomous assessment. This is the MVP.

---

## Phase 4: User Story 2 — The agent never decides on missing data (Priority: P2)

**Goal**: When the disclosed payload cannot be obtained or is missing a required field, the agent holds
(fail-closed) rather than approving/rejecting on blanks.

**Independent test**: Make the disclosed-data fetch fail for one application → agent holds (no approve/reject,
no credential, actionable reason); restore → same application decided correctly (spec US2 / SC-004).

### Tests (write first)

- [x] T015 [P] [US2] `RulesDecisionEngine` test in `tests/Sorcha.Agent.Tests/Decision/DisclosedPayloadFailClosedTests.cs`: rules require the application payload but it is empty → `hold` (not approve/reject); when the payload is later present → correct approve/reject.
- [x] T016 [P] [US2] Agent inbox test in `tests/Sorcha.Agent.Tests/Inbox/DisclosedPayloadFetchTests.cs`: a disclosed-data fetch failure results in a `hold` outcome and no action submission.

### Implementation

- [x] T017 [US2] Extend fail-closed in `src/Apps/Sorcha.Agent/Decision/RulesDecisionEngine.cs`: mirror the #1077 `_rulesRequireChecks` pattern to also hold when the disclosed payload the rules depend on is empty/unavailable, with a distinct logged reason ("Disclosed application data unavailable; held for manual review").
- [x] T018 [US2] In the agent inbox fetch path (`src/Apps/Sorcha.Agent/Inbox/…`): on fetch failure or `recipientResolved=false`, route the action to a hold outcome — never proceed to a decision on an empty payload; retry naturally on the next poll (FR-009).

**Checkpoint**: transient/permanent unavailability can never produce a wrong decision.

---

## Phase 5: User Story 3 — Every decision is explainable (Priority: P3)

**Goal**: The check results that drove a decision are recorded/observable so an outcome can be diagnosed.

**Independent test**: After a decision, the evaluated check facts (and the fields they came from) are
retrievable; for a rejection the failing check is identifiable (spec US3 / SC-005).

### Tests (write first)

- [x] T019 [P] [US3] Test in `tests/Sorcha.Agent.Tests/Decision/CheckFactsObservabilityTests.cs`: after a decision the evaluated `checks.*` facts and their source payload fields are emitted (structured), and a failing check is identifiable for a rejection.

### Implementation

- [x] T020 [US3] Finalise the structured check-facts log in `src/Apps/Sorcha.Agent/Decision/RulesDecisionEngine.cs` (the diagnostic staged on the branch): log evaluated facts + source fields at an appropriate level, structured (no string interpolation, Constitution VIII); ensure it reflects the real disclosed fields and identifies failing checks.

**Checkpoint**: a future recurrence is a two-minute diagnosis, not a multi-hour investigation.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T021 [P] Docs: add the endpoint to `docs/reference/API-DOCUMENTATION.md` and the blueprint-service `README.md`; update `.claude/skills/sorcha-architecture/SKILL.md` if it catalogs endpoints; note the agent's disclosed-payload consumption.
- [x] T022 [P] Verify coverage (>85% new code) for the resolver, endpoint, and agent paths; confirm no new Release warnings (Constitution IV/V).
- [x] T023 Reconcile the provisional field-name edit staged on the branch with the new `PreviousPayload` source (the `payload→prepopulatedPayload` change becomes moot for `PreviousPayload`; keep the `schema→dataSchema` correction); remove any purely-diagnostic scaffolding not intended to ship.
- [ ] T024 Full suite green: `dotnet test tests/Sorcha.Blueprint.Service.Tests` + `dotnet test tests/Sorcha.Agent.Tests`, and `demos/AIAS/rehearse.ps1` PASS end-to-end.

---

## Dependencies & completion order

```
Phase 1 (Setup: T001-T002)
   └─▶ Phase 2 (Foundational: T003-T007)   ← BLOCKS all user stories (resolver + response model)
         ├─▶ Phase 3 US1 (T008-T014)  🎯 MVP
         │       └─▶ Phase 4 US2 (T015-T018)   ← builds on US1's fetch + decision path
         │       └─▶ Phase 5 US3 (T019-T020)   ← builds on US1's decision path (independent of US2)
         └─────────────▶ Phase 6 Polish (T021-T024)   ← after the stories it documents/hardens
```

- **US1 depends on** Phase 2 (needs the resolver + endpoint + response model).
- **US2 and US3 depend on** US1 (they harden the fetch + decision path US1 introduces). US2 and US3 are
  **independent of each other** and can proceed in parallel once US1 is done.
- Phase 2's refactor regression guard (T007) must pass before building on `ActionExecutionService`.

## Parallel execution examples

- **Phase 1**: T001 ∥ T002 (different concerns/files).
- **Phase 2**: T006 (resolver unit tests) ∥ authoring, once T003-T005 land; T004→T005 are sequential (same class).
- **Phase 3 (US1) tests**: T008 ∥ T009 ∥ T010 (different test files) — write together before T011-T013.
- **After US1**: Phase 4 (T015-T018) ∥ Phase 5 (T019-T020) — different files, independent stories.
- **Phase 6**: T021 ∥ T022 (docs vs coverage).

## Implementation strategy

1. **Land the MVP first**: Phases 1→2→3. At the end of Phase 3, `rehearse.ps1` passes and the demo is
   correct — this is the shippable slice.
2. **Harden**: Phase 4 (fail-closed) then Phase 5 (explainability), in parallel if resourced.
3. **Finish**: Phase 6 docs/coverage/cleanup, then the full-suite + E2E green gate (T024).

## Task count summary

- **Total**: 24 tasks
- Setup: 2 · Foundational: 5 · US1 (MVP): 7 · US2: 4 · US3: 2 · Polish: 4
- **Parallelizable [P]**: 11 tasks
- **MVP = Phases 1-3 (T001-T014)**; independently testable via `rehearse.ps1`.
