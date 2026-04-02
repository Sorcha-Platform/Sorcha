# Tasks: Trade Finance Walkthrough

**Input**: Design documents from `/specs/081-trade-finance-walkthrough/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not explicitly requested — test tasks omitted. Scripted scenarios serve as verification.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- Walkthrough content: `walkthroughs/TradeFinance/`
- Spec docs: `specs/081-trade-finance-walkthrough/`
- All files are JSON, PowerShell, or Markdown — no C# code

---

## Phase 1: Setup (Project Structure)

**Purpose**: Create the walkthrough directory structure and foundational config files

- [x] T001 Create walkthrough directory structure per plan.md at `walkthroughs/TradeFinance/` (dirs: `data/`, `prompts/`, `prompts/personas/`, `mcp-configs/`, `docs/`)
- [x] T002 Create extended manifest `walkthroughs/TradeFinance/config.json` with all 4 organisations, 6 participants, 2 registers, templates array, scenarios array, and agentAssignments per data-model.md
- [x] T003 [P] Create credit score lookup data `walkthroughs/TradeFinance/data/credit-scores.json` with cairngorm (score 85) and lowcredit (score 35) entries per data-model.md
- [x] T004 [P] Create MCP config template `walkthroughs/TradeFinance/mcp-configs/template.json` with JWT_TOKEN_PLACEHOLDER and GATEWAY_URL_PLACEHOLDER per data-model.md

---

## Phase 2: Foundational (Blueprint Templates)

**Purpose**: Author the two blueprint JSON templates that ALL user stories depend on. These must be valid and publishable before any workflow execution.

**⚠️ CRITICAL**: No workflow execution (US2–US7) can proceed without published blueprints.

- [x] T005 Create Procurement-to-Pay blueprint template `walkthroughs/TradeFinance/procurement-to-pay-template.json` with 6 actions (Raise PO, Acknowledge PO, Confirm Delivery, Confirm GRN, Raise Invoice, Approve Invoice), 4 participants (procurement-mgr, sales-mgr, site-mgr, credit-analyst as Funder observer), schemas per data-model.md field summary, disclosure matrix, instanceReference config, and VerifiedInvoiceCredential issuance on action 6. Follow ConstructionPermit/SelfBuildHouse JSON envelope pattern per research.md Decision 1.
- [x] T006 Create Invoice Finance blueprint template `walkthroughs/TradeFinance/invoice-finance-template.json` with 4 actions (Request Financing, Buyer Assessment, Evaluate Application, Approve/Decline), 4 participants (finance-director, assessment-svc, credit-analyst, and supplier observer), credential requirement for VerifiedInvoiceCredential on action 1, disclosure matrix per data-model.md, calculated fields (advanceAmount, feeAmount, netAdvance), routing logic (credit score ≥50 approve, <50 decline), and TradeFinanceCredential issuance on action 4. Follow same JSON envelope pattern.

**Checkpoint**: Both blueprints ready — can be published to registers during setup.

---

## Phase 3: User Story 1 — Data-Driven Platform Setup (Priority: P1) 🎯 MVP

**Goal**: Setup wizard reads config.json manifest, bootstraps all platform resources (orgs, users, wallets, participants, registers, blueprints) via CLI, outputs state.json and MCP configs. Idempotent and supports single/multi-machine deployments.

**Independent Test**: Run setup.ps1 on a clean remote peer with `-Profile demo`, verify state.json is written with all 4 orgs, 6 wallets, 2 registers, 2 blueprints. Re-run and verify no duplicates.

### Implementation for User Story 1

- [x] T007 [US1] Create PowerShell setup script `walkthroughs/TradeFinance/setup.ps1` that: imports SorchaWalkthrough shared module, reads config.json manifest, accepts `-Profile` and `-Organizations` parameters, and orchestrates the full bootstrap sequence
- [x] T008 [US1] Implement organisation creation logic in `walkthroughs/TradeFinance/setup.ps1`: iterate config.json organisations (filtered by `-Organizations` param or all), create each org via CLI (`sorcha org create`), store org IDs in state
- [x] T009 [US1] Implement user and wallet creation logic in `walkthroughs/TradeFinance/setup.ps1`: for each participant in selected orgs, create user (`sorcha user create`), create wallet with specified algorithm, store credentials and wallet addresses in state
- [x] T010 [US1] Implement participant registration logic in `walkthroughs/TradeFinance/setup.ps1`: register each participant identity, link wallet address, publish participant to register
- [x] T011 [US1] Implement register creation and blueprint publishing in `walkthroughs/TradeFinance/setup.ps1`: create registers owned by local orgs (per manifest ownership), publish associated blueprint templates, subscribe to remote registers in multi-machine mode
- [x] T012 [US1] Implement idempotency checks in `walkthroughs/TradeFinance/setup.ps1`: before each create operation, check if resource already exists (by name/subdomain), skip if found, log skip message
- [x] T013 [US1] Implement state file output in `walkthroughs/TradeFinance/setup.ps1`: write `state.json` with all org IDs, register IDs, blueprint IDs, wallet addresses, user credentials, and role metadata per data-model.md state.json schema
- [x] T014 [US1] Implement MCP config generation in `walkthroughs/TradeFinance/setup.ps1`: for each participant, clone `mcp-configs/template.json`, replace placeholders with actual JWT token and gateway URL, write to `mcp-configs/generated/sorcha-{participant-id}.json`

**Checkpoint**: Setup wizard fully functional — can bootstrap a clean instance and generate all configs.

---

## Phase 4: User Story 2 — Procurement-to-Pay Workflow (Priority: P1)

**Goal**: Scripted 6-action procurement-to-pay flow executes via CLI/run script. Each action uses correct participant identity and wallet. VerifiedInvoiceCredential issued on approval.

**Independent Test**: Run golden-path scenario for procurement flow. Verify all 6 actions complete in order, payload data matches scenario file, calculated values (invoiceTotal, daysSinceDelivery) are correct, and VC is issued.

### Implementation for User Story 2

- [x] T015 [US2] Create golden path scenario data `walkthroughs/TradeFinance/data/scenario-golden-path.json` with procurement and finance sub-objects, expected paths, expected calculated values, and all 10 action payloads per data-model.md
- [x] T016 [US2] Create scripted scenario runner `walkthroughs/TradeFinance/run.ps1` that: accepts `-Profile`, `-Scenario`, and `-Blueprint` parameters, loads state.json for credentials/IDs, loads scenario data, and executes actions sequentially via CLI
- [x] T017 [US2] Implement procurement flow execution in `walkthroughs/TradeFinance/run.ps1`: for each of the 6 procurement actions, authenticate as the correct participant, submit the action payload from scenario data, verify action completion, and log results
- [x] T018 [US2] Implement invoice dispute/rejection routing verification in `walkthroughs/TradeFinance/run.ps1`: when scenario expects rejection at action 6, verify workflow routes back to action 5 for resubmission

**Checkpoint**: Full procurement-to-pay flow runs end-to-end with scripted data.

---

## Phase 5: User Story 3 — Invoice Finance Workflow with Cross-Register Credentials (Priority: P1)

**Goal**: 4-action invoice finance flow on Register 2 consumes VerifiedInvoiceCredential from Register 1. Selective disclosure enforced. TradeFinanceCredential issued on approval, declined on low credit score.

**Independent Test**: After completing procurement flow (US2), run finance flow. Verify credential requirement enforced, Funder sees only disclosed fields, calculated values correct, and TradeFinanceCredential issued.

### Implementation for User Story 3

- [x] T019 [US3] Implement finance flow execution in `walkthroughs/TradeFinance/run.ps1`: for each of the 4 finance actions, authenticate as the correct participant (finance-director, assessment-svc, credit-analyst), submit action payload from scenario data, verify credential requirement on action 1
- [x] T020 [US3] Implement approval/decline routing in `walkthroughs/TradeFinance/run.ps1`: when buyer credit score ≥50, execute approve path (action 4 with approval payload); when <50, execute decline path (action 4 with decline, no VC issued)
- [x] T021 [US3] Implement cross-register credential verification in `walkthroughs/TradeFinance/run.ps1`: after finance flow completion, verify TradeFinanceCredential is issued with correct advanceAmount, feeAmount, netAdvance, and invoice reference

**Checkpoint**: Full 10-action flow (procurement + finance) runs end-to-end across two registers.

---

## Phase 6: User Story 4 — DevMode to FLE Transition (Priority: P2)

**Goal**: Operator runs workflow in DevMode (plaintext), disables DevMode (irreversible), re-runs workflow under FLE. Selective disclosure restricts what each participant sees.

**Independent Test**: Run golden path in DevMode — inspect payloads are plaintext. Disable DevMode. Re-run golden path — verify payloads encrypted and Funder query returns only disclosed fields.

### Implementation for User Story 4

- [x] T022 [US4] Add DevMode status check and transition commands to `walkthroughs/TradeFinance/run.ps1`: add `--devmode` flag to query current register mode, add transition step that calls `sorcha register update --devmode disable` on both registers
- [x] T023 [US4] Add FLE disclosure verification to `walkthroughs/TradeFinance/run.ps1`: after FLE-mode run, query register as Funder participant and verify only disclosed fields (invoiceTotal, paymentTerms, creditScore, financingTerms) are readable — all other fields encrypted/inaccessible

**Checkpoint**: DevMode-to-FLE transition demonstrated with before/after payload comparison.

---

## Phase 7: User Story 5 — Agent-Driven Parallel Execution (Priority: P2)

**Goal**: Two Claude Code sessions on separate machines coordinate through register replication. Each agent uses MCP connections to poll inbox, generate payloads, and submit actions autonomously.

**Independent Test**: Start two Claude sessions (Box 1: buyer+credit-insurer, Box 2: supplier+funder). Initiate flow from Box 2. Verify full 10-action flow completes without manual intervention.

### Implementation for User Story 5

- [x] T024 [P] [US5] Create setup wizard agent prompt `walkthroughs/TradeFinance/prompts/setup-wizard.md` with instructions for Claude to drive the setup.ps1 interactively, handle errors, and verify completion
- [x] T025 [P] [US5] Create buyer-agent prompt `walkthroughs/TradeFinance/prompts/buyer-agent.md` with identity (plays procurement-mgr, site-mgr, assessment-svc), MCP connection names, inbox polling behaviour, scripted vs persona mode instructions, and action submission rules per FR-012 through FR-017
- [x] T026 [P] [US5] Create supplier-agent prompt `walkthroughs/TradeFinance/prompts/supplier-agent.md` with identity (plays sales-mgr, finance-director, credit-analyst), MCP connection names, inbox polling behaviour, scripted vs persona mode instructions, and coordination-through-register-only rules
- [x] T027 [P] [US5] Create buyer persona `walkthroughs/TradeFinance/prompts/personas/cairngorm.md` with Cairngorm Construction company details (Highland construction firm, typical project types, order sizes £5k-£50k, payment terms)
- [x] T028 [P] [US5] Create supplier persona `walkthroughs/TradeFinance/prompts/personas/highland-timber.md` with Highland Timber Supplies company details (timber merchant, product lines, delivery areas, pricing ranges)
- [x] T029 [P] [US5] Create funder persona `walkthroughs/TradeFinance/prompts/personas/scottrade.md` with ScotTrade Finance company details (SME trade funder, advance rates 80-95%, fee rates 1.5-4%, risk appetite)
- [x] T030 [P] [US5] Create credit insurer persona `walkthroughs/TradeFinance/prompts/personas/trade-credit.md` with UK Trade Credit Bureau details (credit scoring methodology, risk ratings, assessment turnaround)

**Checkpoint**: Agent prompts and personas ready — two Claude sessions can drive the full walkthrough autonomously.

---

## Phase 8: User Story 6 — Single-Machine Mode (Priority: P3)

**Goal**: Full walkthrough runs on one machine with all 6 MCP connections in one Claude session. Agent plays all roles sequentially.

**Independent Test**: Run setup selecting all orgs on one machine. Start one Claude session with all 6 MCP configs. Run golden path — verify all 10 actions complete.

### Implementation for User Story 6

- [x] T031 [US6] Update `walkthroughs/TradeFinance/setup.ps1` to detect single-machine mode (all orgs selected) and generate a combined MCP config with all 6 participant connections pointing to the same gateway URL
- [x] T032 [US6] Update buyer-agent prompt `walkthroughs/TradeFinance/prompts/buyer-agent.md` with single-machine mode section: when all 6 MCP connections are present, play all roles sequentially instead of waiting for remote agent

**Checkpoint**: Single-machine mode works for development and testing.

---

## Phase 9: User Story 7 — Scripted Scenario Variations (Priority: P3)

**Goal**: Three scripted scenarios (golden path, disputed invoice, declined finance) with predefined payloads for deterministic runs. Each exercises different workflow routing.

**Independent Test**: Run each scenario independently. Verify action paths match expectations, calculated values correct, VCs issued/not-issued as expected.

### Implementation for User Story 7

- [x] T033 [P] [US7] Create disputed invoice scenario `walkthroughs/TradeFinance/data/scenario-disputed.json` with: procurement actions 1-5 as golden path, action 6 with decision "dispute", action 5 resubmitted with corrected amounts, action 6 approval on second attempt, then finance flow as golden path
- [x] T034 [P] [US7] Create declined finance scenario `walkthroughs/TradeFinance/data/scenario-declined.json` with: procurement flow identical to golden path, finance action 1 request, action 2 with low buyer credit score (35, from lowcredit entry in credit-scores.json), action 3 evaluation noting high risk, action 4 decline with no VC issued
- [x] T035 [US7] Update `walkthroughs/TradeFinance/run.ps1` to support scenario selection via `-Scenario` parameter (golden-path, disputed, declined), load correct scenario file, and handle branching action paths (dispute reroute, decline terminal)

**Checkpoint**: All 3 scenarios run deterministically with correct outcomes.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and final quality

- [x] T036 [P] Create full narrative documentation `walkthroughs/TradeFinance/docs/Trade-Finance-Walkthrough.md` covering: architecture overview, organisation topology, workflow descriptions, disclosure matrix, DevMode/FLE explanation, agent setup instructions, and all 3 scenario walkthroughs
- [x] T037 [P] Update `walkthroughs/README.md` to add TradeFinance entry to the walkthrough index table with status, description, and link
- [ ] T038 ⏳ Validate end-to-end by running quickstart.md steps: configure CLI profile, run setup.ps1, verify state.json, configure MCP, run golden-path scenario, verify both VCs issued
- [x] T039 Update `specs/081-trade-finance-walkthrough/` — mark spec.md status as Complete, update plan.md with any deviations discovered during implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (config.json must exist for blueprint parameterSchema references) — BLOCKS all workflow execution
- **US1 Setup Wizard (Phase 3)**: Depends on Phase 1 (config.json) + Phase 2 (blueprint templates)
- **US2 Procurement Flow (Phase 4)**: Depends on Phase 3 (setup must create platform state) + Phase 2 (blueprint must exist)
- **US3 Finance Flow (Phase 5)**: Depends on Phase 4 (needs VerifiedInvoiceCredential from procurement flow)
- **US4 DevMode/FLE (Phase 6)**: Depends on Phase 4 + Phase 5 (needs working end-to-end flow)
- **US5 Agent Prompts (Phase 7)**: Depends on Phase 1 (config.json for role assignments) — can run in PARALLEL with Phases 3–6 (prompts are documentation, not code)
- **US6 Single-Machine (Phase 8)**: Depends on Phase 3 (setup.ps1) + Phase 7 (agent prompts)
- **US7 Scenario Variations (Phase 9)**: Depends on Phase 4 (run.ps1 must support scenario execution)
- **Polish (Phase 10)**: Depends on all previous phases

### User Story Dependencies

- **US1 (P1)**: Needs config.json + blueprints → then independent
- **US2 (P1)**: Needs US1 complete (platform bootstrapped)
- **US3 (P1)**: Needs US2 complete (VerifiedInvoiceCredential from procurement flow)
- **US4 (P2)**: Needs US2 + US3 complete (full flow must work before DevMode transition)
- **US5 (P2)**: **Independent** — agent prompts can be written in parallel with all other stories
- **US6 (P3)**: Needs US1 + US5 (setup script + agent prompts)
- **US7 (P3)**: Needs US2 (run.ps1 scenario support)

### Within Each User Story

- Config/data files before scripts
- Core logic before edge cases
- Verification after implementation

### Parallel Opportunities

- **Phase 1**: T003 and T004 can run in parallel (independent files)
- **Phase 2**: T005 and T006 could run in parallel but are complex — sequential recommended for consistency
- **Phase 7 (US5)**: ALL prompt/persona tasks (T024–T030) can run in parallel — they are independent Markdown files
- **Phase 9 (US7)**: T033 and T034 can run in parallel (independent scenario files)
- **Cross-phase**: Phase 7 (agent prompts) can run in parallel with Phases 3–6

---

## Parallel Example: User Story 5 (Agent Prompts)

```
# Launch all persona files together:
Task: "Create buyer persona in walkthroughs/TradeFinance/prompts/personas/cairngorm.md"
Task: "Create supplier persona in walkthroughs/TradeFinance/prompts/personas/highland-timber.md"
Task: "Create funder persona in walkthroughs/TradeFinance/prompts/personas/scottrade.md"
Task: "Create credit insurer persona in walkthroughs/TradeFinance/prompts/personas/trade-credit.md"

# Launch both agent prompts together:
Task: "Create buyer-agent prompt in walkthroughs/TradeFinance/prompts/buyer-agent.md"
Task: "Create supplier-agent prompt in walkthroughs/TradeFinance/prompts/supplier-agent.md"
```

---

## Implementation Strategy

### MVP First (User Stories 1–3)

1. Complete Phase 1: Setup (config.json, credit-scores, MCP template)
2. Complete Phase 2: Foundational (both blueprint templates)
3. Complete Phase 3: US1 — Setup wizard creates all platform state
4. Complete Phase 4: US2 — Procurement flow runs end-to-end
5. Complete Phase 5: US3 — Finance flow with cross-register credential
6. **STOP and VALIDATE**: Full 10-action golden path across 2 registers works
7. This is the minimum viable demonstration

### Incremental Delivery

1. Setup + Blueprints + US1 → Platform bootstrapped (can verify via CLI)
2. Add US2 → Procurement flow works (6 actions, 1 VC) — demo-ready for single workflow
3. Add US3 → Full 10-action cross-register flow — core demo complete
4. Add US4 → DevMode/FLE transition — the pitch moment
5. Add US5 → Agent-driven execution — the novel differentiator
6. Add US6 → Single-machine convenience mode
7. Add US7 → Multiple scenario coverage

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- This is a **content walkthrough** — all deliverables are JSON, PowerShell, and Markdown files
- No C# code, no new services, no database changes
- Blueprint templates must follow the existing ConstructionPermit/SelfBuildHouse JSON envelope pattern (research.md Decision 1)
- Scenario data follows the SelfBuildHouse two-blueprint sub-object pattern (research.md Decision 3)
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
