---
description: "Executable task list for feature 110-agent-persona-mode"
---

# Tasks: Agent Persona Mode

**Input**: Design documents in `specs/110-agent-persona-mode/`
**Prerequisites**: plan.md, spec.md (P1–P3 user stories), research.md, data-model.md, contracts/persona-schema.json, quickstart.md

**Tests**: Included. Constitution Principle IV mandates ≥85% coverage for new code with deterministic xUnit tests, so every functional task has paired unit and/or integration tests.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. User Story 1 (P1) is the MVP and delivers the value that unblocks the TradeFinance walkthrough.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Paths are absolute-style relative to repo root (`C:\projects\Sorcha`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create folders and confirm the new feature compiles against the existing agent project.

- [x] T001 Create folder `src/Apps/Sorcha.Agent/Persona/` with a `.gitkeep` placeholder so subsequent [P] tasks can land in parallel
- [x] T002 Create folder `tests/Sorcha.Agent.Tests/Persona/` with a `.gitkeep` placeholder
- [x] T003 [P] Copy `specs/110-agent-persona-mode/contracts/persona-schema.json` to `src/Apps/Sorcha.Agent/Persona/Schemas/persona-schema.json` and set its `.csproj` build action to `EmbeddedResource` so it ships with the binary

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Wiring, core types, and the validation/submission machinery that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Actor config extension

- [x] T004 Add `public string? PersonaFile { get; init; }` to `ActorDefinition` in `src/Apps/Sorcha.Agent/Configuration/ActorDefinition.cs` (optional, null when absent; JSON property name `personaFile`)
- [x] T005 Update `src/Apps/Sorcha.Agent/Configuration/ActorDefinitionLoader.cs` to resolve `PersonaFile` as a path relative to the actor-config file's directory, leaving the value null if the field is absent — do NOT load the persona here, just normalise the path
- [x] T006 [P] Regression test in `tests/Sorcha.Agent.Tests/Configuration/ActorDefinitionLoaderTests.cs` asserting that an existing actor config with no `personaFile` field loads with `PersonaFile == null` and every other property unchanged (FR-012 / SC-005)

### Core persona types (data-model.md)

- [x] T007 [P] Create `src/Apps/Sorcha.Agent/Persona/PersonaDefinition.cs` with records `PersonaDefinition`, `PersonaTarget`, `PersonaTrigger` (abstract/discriminated), `OnceTrigger`, `IntervalTrigger`, and `PersonaFireContext` matching data-model.md §Top-level types
- [x] T008 [P] Create `src/Apps/Sorcha.Agent/Persona/IRandomSource.cs` and `src/Apps/Sorcha.Agent/Persona/RandomSource.cs` wrapping `System.Random` with methods `int NextInt(int min, int max)`, `decimal NextDecimal(decimal min, decimal max, int precision)`, `T Choose<T>(IReadOnlyList<T> options)`

### Payload token resolver

- [x] T009 [P] Create `src/Apps/Sorcha.Agent/Persona/IPayloadTokenResolver.cs` with `JsonObject Resolve(JsonNode template, PersonaFireContext ctx)` and `IReadOnlyList<string> ValidateTokens(JsonNode template)`
- [x] T010 Create `src/Apps/Sorcha.Agent/Persona/PayloadTokenResolver.cs` implementing the six tokens (`${now}`, `${uuid}`, `${counter}`, `${random.int}`, `${random.decimal}`, `${random.choice}`) per data-model.md §Token grammar — string-that-is-exactly-token preserves typed JSON result; embedded token performs string interpolation (depends on T007, T008, T009)
- [x] T011 [P] Unit tests in `tests/Sorcha.Agent.Tests/Persona/PayloadTokenResolverTests.cs` covering: each token type, typed-vs-interpolated resolution, seeded `IRandomSource` determinism, `ValidateTokens` returns errors for `${randm.int(...)}`, malformed `${random.int()}`, empty `${random.choice([])}`

### Schema validator

- [x] T012 [P] Create `src/Apps/Sorcha.Agent/Persona/PersonaSchemaValidator.cs` that validates a parsed JSON file against the embedded `persona-schema.json` using `JsonSchema.Net` (already a Sorcha dependency) and returns a typed result with errors
- [x] T013 [P] Unit tests in `tests/Sorcha.Agent.Tests/Persona/PersonaSchemaValidatorTests.cs` covering: valid once/interval examples pass; missing required fields fail; `oneOf actionName/actionIndex` violation fails; `oneOf everySeconds/everyMinutes` violation fails; `until` with malformed date fails

### Persona definition loader

- [x] T014 Create `src/Apps/Sorcha.Agent/Persona/PersonaDefinitionLoader.cs` with `static PersonaLoadResult Load(string personaFilePath, string? statePath)` that: reads the file, runs `VariableResolver` over `{{...}}` placeholders, runs `PersonaSchemaValidator`, runs `PayloadTokenResolver.ValidateTokens`, deserialises into `PersonaDefinition`, returns `PersonaLoadResult` with success + errors (depends on T007, T010, T012)
- [x] T015 [P] Unit tests in `tests/Sorcha.Agent.Tests/Persona/PersonaDefinitionLoaderTests.cs` covering: valid one-shot example loads; valid interval example loads; `{{blueprints.X.id}}` placeholder resolved from mock state; token typo surfaces at load time; missing file returns structured error

### Persona submitter

- [x] T016 Create `src/Apps/Sorcha.Agent/Persona/IPersonaSubmitter.cs` with `Task<PersonaSubmissionResult> SubmitAsync(PersonaDefinition persona, JsonObject payload, CancellationToken ct)` and enum `PersonaSubmissionOutcome { Submitted, TransientFailure, HardFailure }`
- [x] T017 Create `src/Apps/Sorcha.Agent/Persona/PersonaSubmitter.cs` that wraps the same `POST /api/instances/{instanceId}/actions/{actionIndex}/execute` path used by `ActionExecutor` (see `src/Apps/Sorcha.Agent/Execution/ActionExecutor.cs`) — take `HttpClient`, `AgentAuthService`, wallet address, register ID via constructor; classify HTTP 5xx / timeout as TransientFailure, HTTP 4xx as HardFailure (depends on T016)
- [x] T018 [P] Unit tests in `tests/Sorcha.Agent.Tests/Persona/PersonaSubmitterTests.cs` with a mocked `HttpMessageHandler`: 200 → Submitted; 503 → TransientFailure; 400 → HardFailure; request body matches `{ blueprintId, actionId, instanceId, payload }` shape

### Persona loop interface

- [x] T019 [P] Create `src/Apps/Sorcha.Agent/Persona/IPersonaLoop.cs` with `Task RunAsync(CancellationToken ct)` and `Task<int> CompletedIterations { get; }` property for test observability

### Persona host + RunCommand wiring

- [x] T020 Create `src/Apps/Sorcha.Agent/Persona/PersonaHost.cs` that constructs the correct `IPersonaLoop` implementation based on `persona.Trigger.Kind` and exposes `Task RunAsync(CancellationToken)`; constructor injects `PersonaDefinition`, `IPersonaSubmitter`, `IPayloadTokenResolver`, `ILogger<PersonaHost>`, `TimeProvider`, `IRandomSource`, `AuditLogger` (depends on T010, T017, T019 — loop implementations land in story phases)
- [x] T021 Update `src/Apps/Sorcha.Agent/Commands/RunCommand.cs` to: after authentication, if `definition.PersonaFile != null`, call `PersonaDefinitionLoader.Load(...)`, fail-fast with `ExitCodes.ConfigurationError` on load errors (FR-014), build a `PersonaHost`, launch `_ = personaHost.RunAsync(cts.Token)` as a peer task before entering the existing inbox `await foreach`; on shutdown, await the persona task with a short grace period (depends on T014, T020)

**Checkpoint**: Foundation ready — any `PersonaTrigger` subtype can now be added by implementing `IPersonaLoop` in a user-story phase. Existing actor configs without `personaFile` still work identically (T006 regression passes).

---

## Phase 3: User Story 1 — Unblock Walkthrough Kickoff (Priority: P1) 🎯 MVP

**Goal**: A `once` trigger fires one submission at agent start, submitting the starting action of a walkthrough blueprint, and TradeFinance `run-agents.ps1` progresses end-to-end without manual kickoff.

**Independent Test**: Run `pwsh walkthroughs/TradeFinance/run-agents.ps1` after `setup.ps1`. Within 30 seconds the procurement-to-pay instance has action 1 ("Raise Purchase Order") submitted by `procurement-mgr` and downstream agents progress the workflow. Satisfies SC-001.

### Tests for User Story 1 ⚠️

> Write tests first; confirm they fail before implementing T024.

- [x] T022 [P] [US1] Unit tests in `tests/Sorcha.Agent.Tests/Persona/OnceTriggerLoopTests.cs` covering: fires exactly once; honours `delaySeconds` using a fake `TimeProvider`; stops immediately on cancellation; surfaces submitter HardFailure as loop exit with logged error; TransientFailure does not re-fire (once == once)
- [x] T023 [P] [US1] Integration test in `tests/Sorcha.Agent.Tests/Persona/PersonaHostOneShotIntegrationTests.cs` that wires `PersonaHost` + `OnceTriggerLoop` + mocked `HttpMessageHandler` and asserts exactly one POST lands on `/api/instances/.../execute` with the resolved payload

### Implementation for User Story 1

- [x] T024 [US1] Create `src/Apps/Sorcha.Agent/Persona/OnceTriggerLoop.cs` implementing `IPersonaLoop` — awaits `Task.Delay(DelaySeconds, TimeProvider)`, resolves payload via `IPayloadTokenResolver`, submits via `IPersonaSubmitter`, logs fire + outcome, exits (depends on T019, T010, T017)
- [x] T025 [US1] Extend `PersonaHost.Run` dispatcher (from T020) to route `OnceTrigger` to `OnceTriggerLoop`
- [x] T026 [P] [US1] Create `walkthroughs/TradeFinance/personas/procurement-mgr-kickoff.persona.json` per the quickstart.md example — one-shot, targets `procurement-to-pay` blueprint, action "Raise Purchase Order", payload copied from the existing `rules[0].payload` in `walkthroughs/TradeFinance/actors/procurement-mgr.json`
- [x] T027 [US1] Add `"personaFile": "../personas/procurement-mgr-kickoff.persona.json"` to `walkthroughs/TradeFinance/actors/procurement-mgr.json` (preserve all other fields including the existing `rules` array, which still governs reactive "Approve/Dispute Invoice" responses)
- [x] T028 [US1] Add a section "Persona-driven kickoff" to `walkthroughs/TradeFinance/README.md` describing what the persona does and how to disable it (remove the `personaFile` line)
- [ ] T029 [US1] End-to-end validation: run `pwsh walkthroughs/TradeFinance/setup.ps1` then `pwsh walkthroughs/TradeFinance/run-agents.ps1`; observe persona fire log line in `logs/procurement-mgr.log`; confirm procurement-to-pay instance reaches at least action 2 in the register; record result in a PR comment

**Checkpoint**: TradeFinance walkthrough runs end-to-end from a single command. SC-001 demonstrably satisfied. MVP deliverable.

---

## Phase 4: User Story 2 — Generate Scenario Register Data (Priority: P2)

**Goal**: An `interval` trigger with `maxIterations` and/or `until` submits repeated, varied workflow instances until a declared limit, using `${random.*}` tokens for payload variation.

**Independent Test**: Declare a persona on a spare agent config with `trigger: interval, everySeconds: 5, maxIterations: 3` and a `${random.decimal}` amount. Run the agent. The register contains exactly 3 instances within ~20 seconds, each with a distinct amount in the declared range. Satisfies SC-003 (the 20-iteration variant is the scaled-up confidence check).

### Tests for User Story 2 ⚠️

- [x] T030 [P] [US2] Unit tests in `tests/Sorcha.Agent.Tests/Persona/IntervalTriggerLoopTests.cs` covering: fires `maxIterations` times when only that is set; stops at `until` when only that is set; stops at whichever hits first when both set; `startDelaySeconds` honoured; TransientFailure does NOT increment counter (FR-015); three consecutive HardFailures exit the loop with Error log; cancellation mid-interval exits immediately; uses fake `TimeProvider` for determinism
- [x] T031 [P] [US2] Integration test in `tests/Sorcha.Agent.Tests/Persona/PersonaHostIntervalIntegrationTests.cs` asserts exactly `maxIterations` POSTs with distinct `${counter}` / `${random.decimal}` values visible in the mock handler's captured request bodies

### Implementation for User Story 2

- [x] T032 [US2] Create `src/Apps/Sorcha.Agent/Persona/IntervalTriggerLoop.cs` implementing `IPersonaLoop` — honours `StartDelaySeconds`, `EverySeconds`/`EveryMinutes`, `MaxIterations`, `Until`; tracks iteration counter; increments only on Submitted; three-strike rule on HardFailure per research.md R-006; uses injected `TimeProvider` for all waits (depends on T019, T010, T017)
- [x] T033 [US2] Extend `PersonaHost` dispatcher (from T020) to route `IntervalTrigger` to `IntervalTriggerLoop`
- [x] T034 [P] [US2] Create a demonstration persona file `walkthroughs/TradeFinance/personas/invoice-generator.persona.json` matching the recurring example in quickstart.md §4 (20 iterations, 30s interval, varying amounts and currencies)
- [x] T035 [P] [US2] Add a "Scenario data generation" section to `walkthroughs/README.md` describing how to attach `invoice-generator.persona.json` to an otherwise-unused agent config for demo data seeding

**Checkpoint**: Recurring persona mechanism works end-to-end. SC-003 satisfied. Feature is functionally complete for scenario authoring.

---

## Phase 5: User Story 3 — Coexistence with Reactive Behaviour (Priority: P2)

**Goal**: An agent with a persona and at least one pending reactive action services both without one starving or skewing the other.

**Independent Test**: Run `procurement-mgr` with its one-shot persona AND a pre-queued inbox action (e.g. an "Approve/Dispute Invoice" routed from a prior register state). Both actions complete exactly once within expected time bounds; reactive-path latency is within 25% of a no-persona baseline. Satisfies SC-004.

### Tests for User Story 3 ⚠️

- [ ] T036 [P] [US3] Integration test in `tests/Sorcha.Agent.Tests/Persona/PersonaReactiveCoexistenceTests.cs` wires `RunCommand.ExecuteAsync` with a mocked inbox listener that yields one pending action and a mocked HTTP handler that captures submissions; asserts two distinct POSTs occur (one persona-initiated, one reactive-initiated) with each originating from its own code path; asserts neither blocks the other (use `TaskCompletionSource` to gate the inbox yield until after the persona fires, then reverse the gate, and confirm both orderings complete)
- [ ] T037 [P] [US3] Benchmark-style test in `tests/Sorcha.Agent.Tests/Persona/PersonaReactiveLatencyTests.cs`: measures reactive-path round-trip with persona absent vs present; asserts ratio ≤ 1.25 (SC-004 threshold); tagged as a perf test so CI can keep it reliable with generous tolerance if flaky

### Implementation for User Story 3

- [ ] T038 [US3] Review `RunCommand.ExecuteAsync` (already modified in T021) and confirm persona task is launched via `_ = personaHost.RunAsync(cts.Token)` BEFORE the inbox `await foreach`, so the persona begins its `delaySeconds` wait while the inbox listener is being constructed; no additional code change anticipated but capture the decision in a short code comment explaining the ordering
- [ ] T039 [US3] Ensure cancellation propagates correctly on `Ctrl+C` / shutdown signal: the `CancellationTokenSource` created at the top of `ExecuteAsync` is shared between inbox loop and persona task; on cancellation both observe it within ≤ 1 s. Add a regression test in `tests/Sorcha.Agent.Tests/Persona/PersonaShutdownTests.cs`

**Checkpoint**: Coexistence proven. No regression in reactive latency beyond tolerance. FR-011 and SC-004 satisfied.

---

## Phase 6: User Story 4 — Human-Editable Scenario Tuning (Priority: P3)

**Goal**: A non-developer can change persona parameters and re-run without touching code.

**Independent Test**: Hand a reviewer the `invoice-generator.persona.json` file and ask them to change interval to 15 s, iterations to 10, and value range to 500–5000. Re-run. Register shows 10 instances, 15 s apart, values in the new range. Reviewer completes in under 10 minutes without reading agent source. Satisfies SC-006.

- [ ] T040 [P] [US4] Add a top-of-file comment block (JSONC-style, permitted by `JsonDocumentOptions.AllowTrailingCommas = true, CommentHandling = Skip` in the loader) to both shipped persona files (`procurement-mgr-kickoff.persona.json`, `invoice-generator.persona.json`) explaining each field in one line each. Confirm the existing `PersonaDefinitionLoader` tolerates `//` comments; if not, enable that option in T014
- [ ] T041 [P] [US4] Apply the same mechanism to ConstructionPermit: create `walkthroughs/ConstructionPermit/personas/<first-action-agent>-kickoff.persona.json` and add `personaFile` to the corresponding actor config — no agent-binary changes (this demonstrates SC-002)
- [ ] T042 [P] [US4] Publish `contracts/persona-schema.json` to `src/Apps/Sorcha.Agent/Persona/Schemas/persona-schema.json` (already done by T003) and add a `$schema` link in the shipped persona files pointing to a stable URL (`https://sorcha.dev/schemas/agent-persona/v1.json` — documented as a TODO since the site publish step is out of scope for this feature; schema ships as an embedded resource for local validation)
- [ ] T043 [US4] Smoke-test the tuning flow: a second developer (or the reviewer) changes the `invoice-generator.persona.json` parameters per the Independent Test and re-runs; record timing and any friction in the PR description

**Checkpoint**: Persona mechanism generalises beyond TradeFinance. SC-002 and SC-006 satisfied.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T044 [P] Update `docs/reference/development-status.md` with Feature 110 status (from 📋 Planned to 🚧 In progress at start, ✅ Complete at end of PR)
- [ ] T045 [P] Update `.specify/MASTER-TASKS.md` to register Feature 110 under the appropriate theme (walkthrough infrastructure)
- [ ] T046 [P] Update `src/Apps/Sorcha.Agent/README.md` with a "Persona mode" section pointing at `specs/110-agent-persona-mode/quickstart.md`
- [ ] T047 [P] Update `walkthroughs/README.md` with a reference to persona-driven kickoff and scenario data generation
- [ ] T048 Run full `dotnet test` and confirm coverage on `src/Apps/Sorcha.Agent/Persona/**` is ≥ 85% (Constitution Principle IV); add tests for any uncovered branches
- [ ] T049 Run `dotnet format` across changed files and confirm `dotnet build -warnaserror` produces zero warnings (Constitution Principle V)
- [ ] T050 Execute `quickstart.md` verbatim end-to-end on a clean checkout to confirm the happy path described there still reflects reality; fix any drift
- [ ] T051 Update memory: replace the "Blocked: sorcha-agent doesn't auto-submit starting actions" bullet in `C:\Users\StuartFraser\.claude\projects\C--projects-Sorcha\memory\MEMORY.md` with the resolution; update `project_tradefinance_agent_gap.md` to note the fix shipped in Feature 110

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1.
- **User Story 1 (Phase 3, P1)**: Depends on Phase 2.
- **User Story 2 (Phase 4, P2)**: Depends on Phase 2. Independent of US1 (both add their own `IPersonaLoop` impl).
- **User Story 3 (Phase 5, P2)**: Depends on US1 (needs at least one loop implementation live to prove coexistence). Uses OnceTriggerLoop from US1 in its integration tests.
- **User Story 4 (Phase 6, P3)**: Depends on US1 and US2 (uses both loop types in its generalisation tasks).
- **Polish (Phase 7)**: Depends on whichever user stories are being shipped.

### Within Each User Story

- Tests authored first; confirm they fail before implementing.
- For US1: T022, T023 before T024.
- For US2: T030, T031 before T032.
- For US3: T036, T037 before T038, T039.
- Commit after each task or tightly coupled group.

### Parallel Opportunities

- T006, T007, T008, T009 can all start together after T004/T005.
- T011, T013, T015, T018 (unit test files) can all run in parallel with each other and with their corresponding implementation tasks once interfaces land.
- T026, T027 (walkthrough data changes) are independent of T024 (agent code) and can land as a separate commit once T024 is merged.
- T044–T047 (documentation) are all independent of each other.

---

## Parallel Example: Foundational Phase

```text
# After T004–T005 are merged, launch in parallel:
T006  Regression test for actor-config-without-persona
T007  Persona record types
T008  IRandomSource + RandomSource
T009  IPayloadTokenResolver interface

# Then after T007, T008, T009 land, launch in parallel:
T010  PayloadTokenResolver implementation
T012  PersonaSchemaValidator
T016  IPersonaSubmitter interface

# Then test tasks in parallel with each other:
T011, T013, T015, T018
```

## Parallel Example: User Story 1

```text
# Tests first, in parallel:
T022  OnceTriggerLoopTests
T023  PersonaHostOneShotIntegrationTests

# Implementation:
T024  OnceTriggerLoop (depends on T019, T010, T017 from foundation)
T025  PersonaHost dispatcher update

# Walkthrough data, in parallel with T024:
T026  procurement-mgr-kickoff.persona.json
T027  actor config personaFile addition  (must follow T026)
T028  README update                      (independent)

# End-to-end validation (sequential, last):
T029
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational) — the largest phase; everything else is cheap afterwards.
3. Complete Phase 3 (US1). At this point TradeFinance walkthrough runs end-to-end — ship this even if US2–US4 defer.
4. **STOP and validate**: run `walkthroughs/TradeFinance/run-agents.ps1` on a clean checkout; capture in PR.
5. Merge, ship, update docs.

### Incremental Delivery

1. Merge Phase 1+2+3 as PR #1 (≈ 80% of effort, unblocks walkthroughs).
2. Merge Phase 4 (US2) as PR #2 (recurring personas + invoice-generator demo file).
3. Merge Phase 5 (US3) as PR #3 (coexistence guarantees proven).
4. Merge Phase 6 (US4) as PR #4 (ConstructionPermit parity + tuning docs).
5. Phase 7 polish tasks fold into each PR as relevant.

### Parallel Team Strategy

Single-developer feature in practice; if parallelised:
- Developer A: Foundational + US1 (critical path).
- Developer B: US2 after Foundational checkpoint.
- Developer C: US4 walkthrough data + docs (can start after US1 merges).

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- [Story] label maps task to spec.md user story for traceability.
- Every user story is independently shippable (US1 alone solves the stated blocker).
- Tests must fail before implementation lands (verifies they are real).
- Commit after each task or logical group; push frequently (per user preference in memory).
- Documentation updates (Phase 7) are non-optional per CLAUDE.md Documentation Sync Policy.
- Branch protection requires PR — no direct pushes to master.
