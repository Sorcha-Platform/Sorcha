# Tasks: Assured Identity Demo Environment

**Input**: Design documents from `/specs/144-assured-identity-demo/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Pester unit tests ARE included for the four pure-logic units (per research §R8: deterministic logic gets unit coverage; the integrated flow is gated by a live green run). No xUnit — this feature adds no .NET code.

**Organization**: Tasks grouped by user story. MVP = User Story 1 (stand up + complete the loop).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete dependencies)
- **[Story]**: US1–US6 maps to spec.md user stories
- All paths are repo-relative; the toolkit lives under `demos/AssuredIdentity/`

## Path Conventions

Toolkit: `demos/AssuredIdentity/` (module, `lib/`, `agent/`, `blueprints/`, `tests/`). Reuses `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` unchanged. Consumes existing Sorcha HTTP endpoints + the existing `sorcha-agent` CLI.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Demo tree, module skeleton, config + blueprint templates, test harness.

- [ ] T001 Create the demo directory tree `demos/AssuredIdentity/` with subdirs `lib/`, `agent/`, `blueprints/`, `tests/`
- [ ] T002 [P] Create module skeleton `demos/AssuredIdentity/AssuredIdentityDemo.psm1` — SPDX header, `Import-Module` of `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1`, stub-export the four commands (`New-IssuingAuthority`, `Connect-Subscriber`, `Reset-Demo`, `Get-DemoStatus`)
- [ ] T003 [P] Create `demos/AssuredIdentity/blueprints/assured-identity.template.json` by copying `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` and replacing the `x-review.header.issuerName` literal with a `{{issuerName}}` token (keep the `holderKeys`/`holderKeySourceField` F137 wiring intact)
- [ ] T004 [P] Create `demos/AssuredIdentity/demo-nodes.example.json` conforming to `contracts/demo-nodes.schema.json` (tiny=issuer, n1=subscriber defaults)
- [ ] T005 [P] Add Pester runner `demos/AssuredIdentity/tests/Invoke-DemoTests.ps1` (Invoke-Pester over `tests/*.Tests.ps1`) and a `.gitignore` entry block for `demo-nodes.json`, `state.json`, rendered `agent/analyst.*.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared logic every command depends on — inventory, state IO, auth bootstrap.

**⚠️ CRITICAL**: No user-story command can be built until this phase is complete.

- [ ] T006 Implement `demos/AssuredIdentity/lib/NodeInventory.ps1` — load + schema-validate `demo-nodes.json`, select node by `id`, resolve by `role`, fail-fast on duplicate id / malformed URL / missing required field (FR-006, FR-007)
- [ ] T007 [P] Pester tests `demos/AssuredIdentity/tests/NodeInventory.Tests.ps1` — valid load, duplicate-id rejection, malformed-URL rejection, missing-field rejection, select-by-id, select-by-role
- [ ] T008 Implement `demos/AssuredIdentity/lib/DemoState.ps1` — read/write/merge `state.json` per `contracts/demo-state.schema.json`, plus a `Test-DemoStateStale` helper (registerId present in state but unreadable on the issuer node → stale)
- [ ] T009 [P] Implement auth bootstrap in `demos/AssuredIdentity/lib/Auth.ps1` — load `deploy/keys.env`, per-node sysadmin login via SorchaWalkthrough `Connect-SorchaAdmin`, never echo secrets

**Checkpoint**: Inventory + state + auth available — command implementation can begin.

---

## Phase 3: User Story 1 - Stand up a demo and complete the credential loop (Priority: P1) 🎯 MVP

**Goal**: One provision command on the issuer + one connect command on a subscriber → a tester completes anonymous→credential through real UIs, with the default deterministic approver and no transient `blueprint_not_available`.

**Independent Test**: From clean, run `New-IssuingAuthority` (default name + rules) then `Connect-Subscriber`; a first-time person completes the journey in `quickstart.md` and the credential lands in their wallet (SC-001, SC-002).

### Tests for User Story 1

- [ ] T010 [P] [US1] Pester `demos/AssuredIdentity/tests/AgencyNaming.Tests.ps1` — a chosen name is injected into all four sites (org/register/published-participant/blueprint `issuerName`) and leaves no `{{issuerName}}` token
- [ ] T011 [P] [US1] Pester `demos/AssuredIdentity/tests/Readiness.Tests.ps1` — predicate is `Ready` only when subscription `Active` ∧ sync-state `CaughtUp` ∧ target blueprint present; partial signals → `NotReady` with reasons; timeout path yields `NotReady(recovery-in-progress)`
- [ ] T012 [P] [US1] Pester `demos/AssuredIdentity/tests/Idempotency.Tests.ps1` — existing org/register/blueprint detected → reuse (no duplicate); stale subscription-vs-missing-register → reconcile decision

### Implementation for User Story 1

- [ ] T013 [P] [US1] Implement `demos/AssuredIdentity/lib/AgencyNaming.ps1` — single-source `-AgencyName` → org name, register name, published-participant org name, and blueprint `{{issuerName}}` token replacement (FR-002, R3)
- [ ] T014 [P] [US1] Implement `demos/AssuredIdentity/lib/Readiness.ps1` — poll `GET /api/organizations/{orgId}/register-subscriptions/{registerId}` (Active), `GET /api/registers/{id}/sync-state` (CaughtUp), `GET /api/registers/{id}/blueprints/published` (blueprint present); bounded backoff to `-TimeoutSeconds`; return `{status, reasons[]}` (FR-004, R4)
- [ ] T015 [P] [US1] Implement `demos/AssuredIdentity/lib/Idempotency.ps1` — probe-and-reuse existing authority artefacts; detect+reconcile stale subscription-vs-missing-register (FR-003, R5)
- [ ] T016 [P] [US1] Create `demos/AssuredIdentity/agent/analyst.rules.template.json` — deterministic auto-approve actor for Action 2, tokenised (`{{analystWallet}}`, `{{registerId}}`, `{{orgId}}`, gateway) for `{{placeholder}}` substitution from `state.json`
- [ ] T017 [US1] Implement `demos/AssuredIdentity/lib/AgentLaunch.ps1` (rules path) — render `analyst.rules.json` from the template and launch `sorcha-agent run --config <rendered> --state state.json` as a tracked child process (FR-011 rules branch)
- [ ] T018 [US1] Implement `New-IssuingAuthority` in `AssuredIdentityDemo.psm1` — resolve issuer node + creds → idempotency probe (T015) → provision (public org, verification-admin Tier-2, analyst Tier-3, wallets, participants, advertised DevMode register, publish analyst participant) → publish blueprint from template with injected name (T013) → launch rules agent (T017) → write `state.json` (T008). Default `-AgencyName "Strathcarron Identity Authority"`, default `-AgentMode rules` (FR-001)
- [ ] T019 [US1] Implement `Connect-Subscriber` in `AssuredIdentityDemo.psm1` — resolve subscriber node + public org → discover advertised register on issuer → `POST /api/organizations/{orgId}/register-subscriptions` (reuse Active / reconcile stale) → readiness gate (T014) → append `subscribers[]` in `state.json` with status + `lastReadyAt` (FR-004)
- [ ] T020 [US1] Write the **tester-journey** section of `demos/AssuredIdentity/DEMO.md` — sign up → F128 add-device → `/new-submissions` → Start → submit → receive in PWA; explicitly record PWA `/applications` + `samples/strathcarron-portal` as off-path (FR-014, FR-015, FR-016)

**Checkpoint**: Default-named demo stands up and a tester completes the loop (MVP — SC-001, SC-002, SC-003 basic reuse).

---

## Phase 4: User Story 2 - Choose how applications are approved (Priority: P2)

**Goal**: `-AgentMode rules|ai|human` selects the approver per run, with an AI guardrail and human instructions.

**Independent Test**: Provision three times, one per mode; each yields an approved credential, and human mode prints approval steps instead of launching an agent (SC-005).

- [ ] T021 [P] [US2] Create `demos/AssuredIdentity/agent/analyst.ai.template.json` + `demos/AssuredIdentity/agent/analyst.persona.md` — AI-persona approver actor (`"mode":"ai"`, persona file, `apiKeyEnvVar: ANTHROPIC_API_KEY`), tokenised like the rules template (FR-010)
- [ ] T022 [US2] Extend `demos/AssuredIdentity/lib/AgentLaunch.ps1` — `ai` path (precheck `ANTHROPIC_API_KEY`, set decision-wait guardrail = 90s, launch) and `human` path (no launch; print "log into the issuer as the analyst and approve Action 2" steps) (FR-011, FR-012, R6)
- [ ] T023 [US2] Wire `-AgentMode rules|ai|human` parameter through `New-IssuingAuthority` to `AgentLaunch` (default `rules`) and record `agentMode` in `state.json` (FR-010)
- [ ] T024 [US2] Add the **agent-mode** section to `demos/AssuredIdentity/DEMO.md` — three modes, the AI guardrail behaviour (`Get-DemoStatus` surfaces "decision pending"; retry or switch), and the human steps

**Checkpoint**: All three modes produce an approved credential (SC-005).

---

## Phase 5: User Story 3 - Rebrand and customise the issuing authority (Priority: P2)

**Goal**: Re-run with a different `-AgencyName` produces a coherently rebranded authority; deeper workflow edits go through the real Designer.

**Independent Test**: Provision with a non-default name; complete the loop; the credential's displayed issuer matches with zero manual edits (SC-004).

- [ ] T025 [US3] Harden rename coherence in `New-IssuingAuthority` — re-running with a changed `-AgencyName` updates all tester-visible sites and reconciles prior-name artefacts via Idempotency (T015), leaving no stale prior-name reference on the tester path (FR-005)
- [ ] T026 [P] [US3] Pester `demos/AssuredIdentity/tests/RenameCoherence.Tests.ps1` — a name change re-injects every site and surfaces any residual prior-name reference as a failure
- [ ] T027 [US3] Add the **deep-customise** section to `demos/AssuredIdentity/DEMO.md` — amend the published blueprint via the real F142 Designer (Describe→Understand→Rehearse→Go-live) and republish to the same register; identity (org/wallet/register/DID) stays intact

**Checkpoint**: Rebrand is coherent end-to-end (SC-004).

---

## Phase 6: User Story 4 - Add more independent nodes to the demo (Priority: P3)

**Goal**: Point the demo at swapped/renamed installations and connect additional independent subscribers of the same advertised register.

**Independent Test**: With an authority provisioned, connect a second subscriber id; a tester on that node completes the loop (SC-006).

- [ ] T028 [US4] Make `Connect-Subscriber` repeatable across N subscriber ids — append/update per-node `subscribers[]` entries, each independently readiness-gated (T014); no cross-node coupling (FR-008)
- [ ] T029 [US4] Add the **multi-node** section to `demos/AssuredIdentity/DEMO.md` — add a subscriber entry to `demo-nodes.json`, re-run `Connect-Subscriber -SubscriberNode <id>`; note swap/rename via inventory only (FR-006, FR-007)

**Checkpoint**: A second independent subscriber serves a tester through the full loop (SC-006).

---

## Phase 7: User Story 5 - Reset and check demo health (Priority: P3)

**Goal**: Clean reset of a node or the whole demo, plus an at-a-glance cross-node readiness verdict.

**Independent Test**: After a run, reset and re-provision cleanly; query status before/after and confirm the verdict predicts tester success (SC-007).

- [ ] T030 [P] [US5] Implement `Reset-Demo` in `AssuredIdentityDemo.psm1` — `-Scope issuer|subscriber|all` per the documented reset recipe (demo wallets, non-system register Mongo DBs, `OrganizationRegisterSubscriptions` rows, replicated state, `state.json`), stop any tracked `sorcha-agent` process, idempotent no-op on already-clean (FR-017)
- [ ] T031 [US5] Implement `demos/AssuredIdentity/lib/StatusVerdict.ps1` — gather per-node signals (gateway health, subscription, sync-state, blueprint-published, approver state) and compute `{verdict: Ready|NotReady, perNode[], reasons[]}` (FR-018, data-model "Derived")
- [ ] T032 [US5] Implement `Get-DemoStatus` in `AssuredIdentityDemo.psm1` — render the verdict table from `StatusVerdict` across issuer + all `subscribers[]` (FR-018)
- [ ] T033 [P] [US5] Pester `demos/AssuredIdentity/tests/StatusVerdict.Tests.ps1` — verdict combinations map correctly (all-green→Ready; each missing signal→NotReady with the right reason)
- [ ] T034 [US5] Add the **reset / status** section to `demos/AssuredIdentity/DEMO.md`

**Checkpoint**: Reset+re-provision is clean; status verdict matches reality (SC-007).

---

## Phase 8: User Story 6 - Graduate the walkthrough to a demo (Priority: P3)

**Goal**: After a proven green run, retire the legacy scripts and align skills + memory to "a demo is a mature walkthrough".

**Independent Test**: Post-cleanup, no legacy AssuredIdentity walkthrough/scratch script remains and project guidance points to the demo (SC-008).

- [ ] T035 [US6] **GREEN-RUN GATE** — execute the full `quickstart.md` on the default node pair across all modes/multi-node and record results against SC-001…SC-007 in `demos/AssuredIdentity/DEMO.md` (a "Verified" note). This task BLOCKS T036–T037 (do not delete the legacy path until the replacement is proven)
- [ ] T036 [US6] Remove legacy `walkthroughs/AssuredIdentity/**` (`setup.ps1`, `run-phase1-identity.ps1`, `run-phase2-licence.ps1`, `run-agents.ps1`, `run-crossnode-*.ps1`, `run-multi-peer.ps1`, scratch logs/state) — **preserve** `walkthroughs/modules/SorchaWalkthrough/` (FR-021)
- [ ] T037 [US6] Remove `deploy/twoinstall-issuer.ps1`, `deploy/twoinstall-citizen-n1.ps1`, `deploy/twoinstall-state.json`, `deploy/twoinstall-citizen-state.json` (FR-021)
- [ ] T038 [P] [US6] Update `.claude/skills/walkthrough-builder/SKILL.md` — add the "a demo is a mature walkthrough" concept + `demos/AssuredIdentity/` location (FR-022)
- [ ] T039 [P] [US6] Update `.claude/skills/sorcha-architecture/SKILL.md` (F143 section → points at the demo) and the `n1-deploy` / `network-bootstrap` skills' AssuredIdentity references → `demos/AssuredIdentity/` (FR-022)
- [ ] T040 [P] [US6] Update memory `f143-two-installation-demo.md` and `MEMORY.md` — "Assured Identity walkthrough" resolves to the demo; record node-agnostic + graduation concept (FR-022)
- [ ] T041 [P] [US6] Add a brief `demos/` taxonomy line to `CLAUDE.md` if warranted (FR-022)

**Checkpoint**: Single canonical demo; legacy retired; guidance aligned (SC-008).

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T042 [P] Assemble the full `demos/AssuredIdentity/DEMO.md` operator runbook (provision/connect/reset/status/agent-modes/multi-node) from the per-story sections; cross-check against `quickstart.md`
- [ ] T043 Run the `quickstart.md` validation end-to-end one final time post-cleanup and confirm the acceptance-checkpoint table passes
- [ ] T044 [P] Verify `.gitignore` covers `demo-nodes.json`, `state.json`, rendered `agent/analyst.*.json`; confirm no secret leaked into committed files
- [ ] T045 [P] Final Pester suite green + PSScriptAnalyzer pass over `demos/AssuredIdentity/**`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: depends on Setup. BLOCKS all user stories.
- **US1 (P3)**: depends on Foundational. The MVP.
- **US2 (P4)**: depends on Foundational + US1's `AgentLaunch.ps1` (T017) and `New-IssuingAuthority` (T018).
- **US3 (P5)**: depends on US1 (`AgencyNaming` T013, `Idempotency` T015, `New-IssuingAuthority` T018).
- **US4 (P6)**: depends on US1 (`Connect-Subscriber` T019, `Readiness` T014).
- **US5 (P7)**: depends on US1 (state + provisioned artefacts to reset/report); independent of US2–US4.
- **US6 (P8)**: GREEN-RUN GATE (T035) depends on US1–US5 being complete (the green run exercises all SCs); T036–T037 depend on T035.
- **Polish (P9)**: depends on all desired stories.

### Within-story order

- Pester tests (T007/T010–T012/T026/T033) author alongside or before their unit; ensure they fail first.
- lib units before the commands that compose them.
- `state.json` IO (T008) before any command that writes it.

### Parallel Opportunities

- Setup: T002, T003, T004, T005 in parallel after T001.
- Foundational: T007, T009 parallel to T006/T008.
- US1: T010–T012 (tests) parallel; T013, T014, T015, T016 parallel (distinct files); then T017 → T018/T019; T020 anytime.
- US6 alignment: T038, T039, T040, T041 parallel (distinct files) after the gate.

---

## Parallel Example: User Story 1

```text
# Tests (write first, expect fail):
T010 AgencyNaming.Tests.ps1   |  T011 Readiness.Tests.ps1   |  T012 Idempotency.Tests.ps1

# Independent lib units (distinct files):
T013 AgencyNaming.ps1  |  T014 Readiness.ps1  |  T015 Idempotency.ps1  |  T016 analyst.rules.template.json

# Then sequential composition:
T017 AgentLaunch.ps1 (rules) → T018 New-IssuingAuthority → T019 Connect-Subscriber
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP and VALIDATE**: stand up the default demo and have someone complete the loop. That alone is a demonstrable product (SC-001, SC-002).

### Incremental Delivery

US1 (MVP) → US2 (agent modes) → US3 (rebrand) → US4 (multi-node) → US5 (reset/status) → **green-run gate** → US6 (graduate + retire legacy) → Polish. Each story is independently demonstrable; the legacy walkthrough stays intact until the green run proves the replacement.

### Notes

- [P] = different files, no incomplete dependency.
- The green-run gate (T035) is the single hard ordering constraint protecting the legacy path — never delete before it passes.
- Commit after each task or logical group.
