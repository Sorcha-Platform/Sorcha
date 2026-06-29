# Tasks: AIAS Assured Identity with photo + autonomous Assure-ID agent (M1)

**Input**: Design documents from `/specs/174-aias-assured-identity/`

**Branch**: `174-aias-assured-identity`

**Implementation status**: Core M1 implementation landed in `ec63765b`. All checks, tests (147/147),
demo provisioning, and rehearsal hook are complete. Remaining work is documentation/admin.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Branch creation, project structure, and documentation scaffolding.

- [x] T001 Create branch `174-aias-assured-identity` and commit speckit design artefacts under `specs/174-aias-assured-identity/`
- [x] T002 [P] Create `src/Apps/Sorcha.Agent/Decision/Checks/` folder (new code surface per plan)
- [x] T003 [P] Create `demos/AIAS/` folder tree (mirroring `demos/AssuredIdentity/` layout)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The external-check hook contract — the one genuine new code surface in M1. All user story
work (checks, rules, provisioning) depends on this contract and runner being in place first.

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete.

- [x] T004 Define `IExternalCheck` interface + `ExternalCheckResult` record in `src/Apps/Sorcha.Agent/Decision/Checks/IExternalCheck.cs`
- [x] T005 Implement `ExternalCheckRunner` (concurrent execution, fault-contained fallback) in `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckRunner.cs`
- [x] T006 [P] Implement `ChecksConfig` deserialization shape in `src/Apps/Sorcha.Agent/Decision/Checks/ChecksConfig.cs`
- [x] T007 [P] Implement `ExternalCheckFactory` (builds concrete checks from config) in `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckFactory.cs`
- [x] T008 Add `PayloadPointer` JSON-Pointer helper in `src/Apps/Sorcha.Agent/Decision/Checks/PayloadPointer.cs`
- [x] T009 Extend `ActorDefinition` with `ChecksFile` property (mirrors `PersonaFile`) in `src/Apps/Sorcha.Agent/Configuration/ActorDefinition.cs`
- [x] T010 Wire `ExternalCheckRunner` into `RunCommand` (loads checks config; non-AIAS agents unaffected) in `src/Apps/Sorcha.Agent/Commands/RunCommand.cs`
- [x] T011 Extend `RulesDecisionEngine.EvaluateAsync` — run `ExternalCheckRunner` pre-step, merge facts under `"checks"` key before JSON-Logic evaluation in `src/Apps/Sorcha.Agent/Decision/RulesDecisionEngine.cs`

**Checkpoint**: Foundation ready — concrete checks and demo provisioning can now proceed in parallel.

---

## Phase 3: User Story 1 — An anonymous person becomes assured, with their face on the credential (Priority: P1) 🎯 MVP

**Goal**: End-to-end happy path: sign up → apply (with photo + real UK postcode + clean wording) → autonomous agent approves → Assured Identity credential carrying the photo lands in the wallet.

**Independent Test**: From a clean Docker stack, sign up as a new user, verify email, fill the AIAS
application form (name, valid UK postcode, photo), submit, and confirm an Assured Identity credential
bearing that photo arrives in the wallet within ~30 s with AIAS branding.

### Implementation for User Story 1

- [x] T012 [P] [US1] Implement `EmailVerifiedCheck` (reads email-verified signal from payload) in `src/Apps/Sorcha.Agent/Decision/Checks/EmailVerifiedCheck.cs`
- [x] T013 [P] [US1] Implement `FieldPresentCheck` (generic field-present for `photoPresent`) in `src/Apps/Sorcha.Agent/Decision/Checks/FieldPresentCheck.cs`
- [x] T014 [P] [US1] Implement `PostcodeExistsCheck` (postcodes.io live call + offline-fixture fallback, `offlineMode: auto`) in `src/Apps/Sorcha.Agent/Decision/Checks/PostcodeExistsCheck.cs`
- [x] T015 [P] [US1] Create AIAS blueprint template (AssuredIdentity base + AIAS branding + `{{issuerName}}` token + reject route) in `demos/AIAS/blueprints/aias-assured-identity.template.json`
- [x] T016 [P] [US1] Create offline postcode fixture for venue-without-internet (configurable allow-list) in `demos/AIAS/fixtures/postcodes.offline.json`
- [x] T017 [US1] Create `assure-id.checks.json` (emailVerified + photoPresent + postcodeExists + profanity check declarations) in `demos/AIAS/agent/assure-id.checks.json`
- [x] T018 [US1] Create `assure-id.rules.json` (JSON-Logic rules: reject bad-postcode / profane / unverified-email; catch-all approve) in `demos/AIAS/agent/assure-id.rules.json`

### Tests for User Story 1

- [x] T019 [P] [US1] Unit tests for `EmailVerifiedCheck` (verified / unverified payloads) in `tests/Sorcha.Agent.Tests/Decision/Checks/EmailVerifiedCheckTests.cs`
- [x] T020 [P] [US1] Unit tests for `FieldPresentCheck` (present / absent / empty field) in `tests/Sorcha.Agent.Tests/Decision/Checks/FieldPresentCheckTests.cs`
- [x] T021 [P] [US1] Unit tests for `PostcodeExistsCheck` — mocked-HTTP live shape (known/nonsense postcode) + offline-fixture fallback when HTTP throws in `tests/Sorcha.Agent.Tests/Decision/Checks/PostcodeExistsCheckTests.cs`
- [x] T022 [US1] Unit tests for extended `RulesDecisionEngine` — approve clean application (all checks pass) in `tests/Sorcha.Agent.Tests/Decision/Checks/RulesDecisionEngineChecksTests.cs`

**Checkpoint**: US1 is independently verifiable — sign up, apply with photo, credential issued. 147/147 agent tests pass.

---

## Phase 4: User Story 2 — AIAS turns down dodgy applications, with personality (Priority: P2)

**Goal**: Autonomous rejections: non-existent postcode → named on-brand reason; profane details →
humorous reason; unverified email → clear hold reason. No credential issued in any rejection case.

**Independent Test**: Submit applications designed to fail each check (ZZ99 9ZZ postcode; profanity in
details; unverified email). Confirm each is rejected within ~30 s with a distinct, human-readable,
on-brand reason and no credential is issued or offered.

### Implementation for User Story 2

- [x] T023 [P] [US2] Implement `ProfanityCheck` (local wordlist scan of configured free-text fields) in `src/Apps/Sorcha.Agent/Decision/Checks/ProfanityCheck.cs`
- [x] T024 [US2] Test support helper (`CheckTestSupport`) shared across check test files in `tests/Sorcha.Agent.Tests/Decision/Checks/CheckTestSupport.cs`

### Tests for User Story 2

- [x] T025 [P] [US2] Unit tests for `ProfanityCheck` (clean / profane payloads; multi-field scan) in `tests/Sorcha.Agent.Tests/Decision/Checks/ProfanityCheckTests.cs`
- [x] T026 [P] [US2] Unit tests for `ExternalCheckRunner` — fact-merge (multiple checks merged into `checks` dict) + fault containment (faulting check resolves to `false`, no throw) in `tests/Sorcha.Agent.Tests/Decision/Checks/ExternalCheckRunnerTests.cs`
- [x] T027 [P] [US2] Unit tests for `ExternalCheckFactory` (config → correct concrete check types) in `tests/Sorcha.Agent.Tests/Decision/Checks/ExternalCheckFactoryTests.cs`
- [x] T028 [US2] Unit tests for extended `RulesDecisionEngine` — reject bad-postcode (distinct reason) / reject profane (distinct reason) / reject unverified-email (distinct reason) in `tests/Sorcha.Agent.Tests/Decision/Checks/RulesDecisionEngineChecksTests.cs`

**Checkpoint**: US1 + US2 independently verifiable. Rejection theatre works. All 147 agent tests pass.

---

## Phase 5: User Story 3 — The whole of AIAS rebuilds from a clean network with one script (Priority: P2)

**Goal**: A single idempotent PowerShell script provisions AIAS (org + VC-issuance master key +
blueprint + agent config + running agent) from a clean Docker stack or n1, and is safe to re-run.

**Independent Test**: On a clean `docker-compose` stack run `./demos/AIAS/run-demo.ps1`; confirm AIAS
org exists, blueprint is published, agent is running. Re-run the script; confirm no duplicates or errors.

### Implementation for User Story 3

- [x] T029 [P] [US3] Author `demos/AIAS/AiasDemo.psm1` — idempotent: org create (skip if present), `Set-SorchaOrgMasterKey` (required for VC issuance), blueprint publish, agent-config write + agent launch
- [x] T030 [P] [US3] Author `demos/AIAS/run-demo.ps1` — entry point; Docker-first / `-Target n1` switch; delegates to `AiasDemo.psm1`
- [x] T031 [US3] Author `demos/AIAS/rehearse.ps1` — test/rehearsal hook: one approval (real postcode + photo) + one rejection (bad postcode) end to end; asserts credential issued on approval, no credential + reason on rejection
- [x] T032 [P] [US3] Create `demos/AIAS/.gitignore` — exclude runtime-generated files (`assure-id.config.json`, `state.json`)
- [x] T033 [US3] Author `demos/AIAS/README.md` — provision, happy-path walkthrough, rejection paths, offline mode, re-run safety, n1 instructions

**Checkpoint**: All three user stories independently verifiable. Single `run-demo.ps1` builds AIAS from scratch.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation updates and admin tasks not covered in the implementation commits.

- [ ] T034 [P] Add feature 174 (AIAS M1) entry to `.specify/MASTER-TASKS.md` — status ✅, summary of what shipped, link to `specs/174-aias-assured-identity/`
- [ ] T035 [P] Update `docs/reference/development-status.md` to reflect AIAS M1 as complete (if applicable)
- [ ] T036 Commit staged `CLAUDE.md` change (plan pointer update to `specs/174-aias-assured-identity/plan.md`)
- [ ] T037 Run `./demos/AIAS/rehearse.ps1` against the live Docker stack to validate end-to-end (approve + reject) before PR

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — complete immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — the happy-path spine
- **US2 (Phase 4)**: Depends on Phase 2 — the rejection theatre; can be parallelised with US1
- **US3 (Phase 5)**: Depends on Phase 2 — can be parallelised with US1 and US2 once Foundational is done
- **Polish (Phase 6)**: Depends on all user story phases — docs + admin

### Within Each User Story

- Models/interfaces before implementations
- Concrete checks before agent config that declares them
- Implementation before tests (or TDD — tests before implementation; either order is fine per the spec)

### Parallel Opportunities

- T012–T016 (US1 checks + blueprint template + fixture) can all run in parallel
- T025–T027 (US2 check tests) can all run in parallel
- US3 provisioning (T029–T032) can run in parallel with US1/US2 once foundational is done
- Polish tasks T034–T035 can run in parallel

---

## Parallel Example: User Story 1 (checks)

```bash
# Launch all concrete check implementations in parallel (different files, no deps between them):
Task: "Implement EmailVerifiedCheck"       → src/.../Checks/EmailVerifiedCheck.cs
Task: "Implement FieldPresentCheck"        → src/.../Checks/FieldPresentCheck.cs
Task: "Implement PostcodeExistsCheck"      → src/.../Checks/PostcodeExistsCheck.cs
Task: "Create AIAS blueprint template"     → demos/AIAS/blueprints/aias-assured-identity.template.json
Task: "Create offline postcode fixture"    → demos/AIAS/fixtures/postcodes.offline.json
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. ✅ Complete Phase 1: Setup
2. ✅ Complete Phase 2: Foundational (external-check hook contract + runner + factory)
3. ✅ Complete Phase 3: User Story 1 (EmailVerified + FieldPresent + PostcodeExists checks + blueprint + tests)
4. **VALIDATE**: sign up → apply → credential-with-photo in wallet
5. Deploy / demo this slice if needed

### Incremental Delivery

1. ✅ Setup + Foundational → hook infrastructure ready
2. ✅ US1 → happy-path approve + credential with photo → **MVP demo-ready**
3. ✅ US2 → add ProfanityCheck + rejection theatre → **live-decisioning visible on stage**
4. ✅ US3 → add provisioning + rehearsal hook → **reboot-proof, single-script**
5. 🔲 Polish → docs + MASTER-TASKS update → **branch ready for PR**

---

## Notes

- [P] tasks = different files, no dependencies between them
- [US1]/[US2]/[US3] labels map tasks to the user story they deliver
- `assure-id.config.json` is generated at runtime by `AiasDemo.psm1` and gitignored — it is NOT a committed artefact
- Photo capture (`FileRenderer` + `PhotoTokenResizer`) and credential issuance (`credentialIssuanceConfig` + HAIP) are reused from existing infra; no changes to those code paths
- Offline fallback is config-driven (`offlineMode: auto|always`) — the demo works without internet by design (SC-007)
- Non-AIAS agents are unaffected by the external-check hook (runner is only wired when `checksFile` is present in the actor config)
