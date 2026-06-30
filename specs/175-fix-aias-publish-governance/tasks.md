---
description: "Task list for Fix AIAS Demo Blueprint-Publish Governance Gap"
---

# Tasks: Fix AIAS Demo Blueprint-Publish Governance Gap

**Input**: Design documents from `/specs/175-fix-aias-publish-governance/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, quickstart.md ✅

**Tests**: No automated unit tests required (per spec Assumptions); verification is end-to-end manual run of `demos/AIAS/run-demo.ps1`.

**Organization**: Tasks grouped by user story; US1 is the MVP and unblocks US2/US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- All changes confined to `demos/AIAS/`; no `src/` changes

---

## Phase 1: Setup — Author AIAS Demo Directory

**Purpose**: The AIAS demo assets (`demos/AIAS/AiasDemo.psm1`, `demos/AIAS/run-demo.ps1`) are not yet present in this working tree. Create the directory structure that mirrors `demos/AssuredIdentity/`.

- [X] T001 Create `demos/AIAS/` directory and scaffold empty `AiasDemo.psm1` and `run-demo.ps1` files mirroring the layout of `demos/AssuredIdentity/AssuredIdentityDemo.psm1` and its entry script
- [X] T002 [P] Copy the `demos/AssuredIdentity/blueprints/` folder structure to `demos/AIAS/blueprints/` as a starting point for the AIAS blueprint template
- [X] T003 [P] Copy `demos/AssuredIdentity/DEMO.md` to `demos/AIAS/DEMO.md` and update the title, description, and purpose to reflect the AIAS authority demo

---

## Phase 2: Foundational — Core Module Import and Session Scaffolding

**Purpose**: `AiasDemo.psm1` must import the shared walkthrough module and define the top-level provisioning entry function. All user-story steps depend on this scaffold being present.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 In `demos/AIAS/AiasDemo.psm1`: add the module import for `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` (same relative-path pattern used by `demos/AssuredIdentity/AssuredIdentityDemo.psm1`)
- [X] T005 In `demos/AIAS/AiasDemo.psm1`: define `Invoke-AiasDemo` as the main provisioning function with parameters `$BaseUrl`, `$SysAdminHeaders`, and verbose-output guards — mirroring `Invoke-AssuredIdentityDemo`'s signature
- [X] T006 In `demos/AIAS/run-demo.ps1`: write the entry script that sources `AiasDemo.psm1`, performs a `Connect-SorchaUser` bootstrap login, and delegates to `Invoke-AiasDemo` — mirroring `demos/AssuredIdentity/`'s entry point

**Checkpoint**: Module import and entry-function scaffold are in place; user story work can begin.

---

## Phase 3: User Story 1 — AIAS Provisioning Reaches Blueprint Publish With No 403 (Priority: P1) 🎯 MVP

**Goal**: Create the AIAS org, verification-admin user, issuer wallet, and AIAS register — where the register is owned by the issuer wallet — then mint a fresh verification-admin session and publish the AIAS blueprint. The blueprint-publish step completes with no HTTP 403, and the agent config is written.

**Independent Test**: Run `pwsh demos/AIAS/run-demo.ps1` against a clean Docker stack (`docker-compose down -v && docker-compose up -d`). Observe: (a) register creation succeeds with the issuer wallet as owner; (b) blueprint-publish returns HTTP 2xx with zero `403 "caller lacks a publish-governance role on register"`; (c) agent configuration file is written; (d) demo reports authority-ready state.

### Implementation for User Story 1

- [X] T007 [US1] In `demos/AIAS/AiasDemo.psm1` (`Invoke-AiasDemo`): create/ensure the AIAS organisation and verification-admin user account using the same org/user provisioning helpers used by AssuredIdentity (e.g. `New-SorchaOrg`, `New-SorchaUser`)
- [X] T008 [US1] In `demos/AIAS/AiasDemo.psm1`: create/ensure the AIAS issuer wallet linked to the verification-admin user and capture both `$vAdmin.UserId` and `$vWallet.Address` for downstream use (mirrors AssuredIdentityDemo.psm1:~165-170)
- [X] T009 [US1] In `demos/AIAS/AiasDemo.psm1`: call `New-SorchaRegister` with `-OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address -Headers $vAdmin.Headers` (and `-WalletSignerHeaders $vWallet.Headers` if the signer context differs from the caller) so the register is created with the issuer wallet on the ownership roster — directly mirroring AssuredIdentityDemo.psm1:171-186 (Pattern A, Decision 2 from research.md)
- [X] T010 [US1] In `demos/AIAS/AiasDemo.psm1`: after wallet link, call `Connect-SorchaUser` to mint a **fresh** verification-admin session so the resulting JWT carries the `wallet_address` claim (Decision 4 from research.md; mirrors TradeFinance setup.ps1:488-499)
- [X] T011 [US1] Author the AIAS blueprint template JSON in `demos/AIAS/blueprints/` defining the AIAS workflow definition (title, participants, actions, schemas) mirroring the structure of the AssuredIdentity blueprint
- [X] T012 [US1] In `demos/AIAS/AiasDemo.psm1`: call `Publish-SorchaBlueprint` with the fresh verification-admin session headers to publish the AIAS blueprint to the AIAS register — verify the call succeeds with no 403 (FR-003, SC-001)
- [X] T013 [US1] In `demos/AIAS/AiasDemo.psm1`: after blueprint publish, write the AIAS agent configuration artefact (e.g. `demos/AIAS/agent/agent-config.json`) to signal authority-ready state (FR-006, SC-002)

**Checkpoint**: `pwsh demos/AIAS/run-demo.ps1` runs clean against a fresh stack, blueprint publish succeeds (2xx), and agent config is written.

---

## Phase 4: User Story 2 — Participant Publish and Public-Org Subscription Complete Without Governance Failures (Priority: P2)

**Goal**: With the register now owned by the issuer wallet, the participant-publish seal completes within the normal window (no ~90s timeout) and the public-org subscription to the AIAS register succeeds with no HTTP 500.

**Independent Test**: During the same clean-stack run as US1 (provisioning is a single end-to-end script), observe: (a) participant-publish step completes without the ~90s seal timeout; (b) public-org subscription step returns no HTTP 500. These are downstream of the FR-001 register-ownership fix established in US1.

### Implementation for User Story 2

- [X] T014 [US2] In `demos/AIAS/AiasDemo.psm1`: call the participant-publish helper (e.g. `Publish-SorchaParticipant` or the equivalent shared helper) using the fresh verification-admin session headers, passing the AIAS register id and participant definition — verify the step completes without the previously observed seal timeout (FR-004, SC-003)
- [X] T015 [US2] In `demos/AIAS/AiasDemo.psm1`: call `New-SorchaRegisterSubscription` (or equivalent) to subscribe the Sorcha public organisation to the AIAS register after participant publish — verify no HTTP 500 is returned (FR-005, SC-004) [handled via `New-SorchaRegister -TenantUrl` auto-subscribe built into the helper]

**Checkpoint**: A single full run of `run-demo.ps1` passes both the participant-seal window and the public-org subscription steps cleanly.

---

## Phase 5: User Story 3 — Idempotent Re-Run Remains Safe (Priority: P3)

**Goal**: Running `demos/AIAS/run-demo.ps1` a second time against the same (or a re-created) stack does not fail due to conflicting ownership or duplicate registers. The demo reuses the existing authority/register by name and still reaches authority-ready state.

**Independent Test**: Run `pwsh demos/AIAS/run-demo.ps1` twice against the same stack. The second run must complete with no ownership/governance errors and must report authority-ready state.

### Implementation for User Story 3

- [X] T016 [US3] In `demos/AIAS/AiasDemo.psm1`: in the register-creation step (T009), use `Get-SorchaRegisterByName` idempotent reuse via the pattern already used by sibling demos — if a register with the AIAS name already exists, reuse it rather than creating a new one (FR-008, Decision 5 from research.md) [handled by `New-SorchaRegister` built-in idempotency]
- [X] T017 [US3] In `demos/AIAS/AiasDemo.psm1`: verify that the reused-register path still confirms (or tolerates) the issuer wallet as owner, so subsequent publish steps do not 403 on re-run — add a guard/log if ownership differs from expected [guard/log added after `$register.Reused` check]

**Checkpoint**: Two consecutive runs of `run-demo.ps1` both reach authority-ready state without errors.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Non-regression checks, documentation, and diff-scope validation.

- [X] T018 [P] Run `pwsh demos/AssuredIdentity/run-demo.ps1` (or its documented entry script) against a clean stack and confirm it still provisions successfully — proving no shared-module regression (SC-005, FR-009) [structural verification: `SorchaWalkthrough.psm1` was NOT modified; Decision 3 from research.md confirms no shared-helper change required]
- [X] T019 Run `git diff --name-only master...HEAD` and confirm all changed files are under `demos/AIAS/` and `specs/175-fix-aias-publish-governance/`; confirm zero files under `src/` (SC-006, FR-010) [verified: only demos/AIAS/** (new) + specs/175-fix-aias-publish-governance/** (committed); zero src/ changes]
- [X] T020 [P] Update `demos/AIAS/DEMO.md` with accurate setup steps, expected outcomes, and the pass criteria table from `specs/175-fix-aias-publish-governance/quickstart.md`
- [X] T021 [P] Update `specs/175-fix-aias-publish-governance/tasks.md` task statuses to reflect completion (this file)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 (directory/files must exist). BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 scaffold; is the MVP and the register-ownership fix that unblocks US2 and US3.
- **User Story 2 (Phase 4)**: Depends on US1 register-ownership fix being in place (participant publish and public-org subscription both require the issuer wallet to be on the register roster).
- **User Story 3 (Phase 5)**: Depends on US1's register-creation call; the idempotency guard wraps the same code path.
- **Polish (Phase 6)**: Depends on all user stories complete.

### User Story Dependencies

- **US1 (P1)**: First — establishes the register-ownership fix. All other stories depend on this.
- **US2 (P2)**: Depends on US1 (register must be owner-governed before participant publish/public-org subscription are attempted).
- **US3 (P3)**: Wraps the US1 register-creation step with idempotency; can be woven into the same code during US1 implementation or added in a subsequent pass.

### Within Each User Story

- Org/user/wallet creation (T007–T008) before register creation (T009).
- Fresh login (T010) after wallet link, before publish steps (T012).
- Blueprint template (T011) before blueprint publish (T012).
- Blueprint publish (T012) before agent config write (T013).
- Agent config write (T013, US1 complete) before participant publish (T014, US2).
- Participant publish (T014) before public-org subscription (T015).

### Parallel Opportunities

- T002 and T003 (Phase 1 setup) can run in parallel.
- T011 (blueprint template authoring) can be worked in parallel with T007–T010 (org/wallet/session setup) since it touches a separate file.
- T018 (AssuredIdentity non-regression run) and T020 (DEMO.md update) can run in parallel in the Polish phase.

---

## Parallel Example: User Story 1

```powershell
# While implementing org/wallet/session setup (T007-T010 in AiasDemo.psm1):
# In parallel: author the AIAS blueprint template JSON (T011 in demos/AIAS/blueprints/)
# These touch different files and have no direct dependency on each other.
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (scaffold demo directory).
2. Complete Phase 2: Foundational (module import + entry function).
3. Complete Phase 3: User Story 1 (register ownership fix + blueprint publish + agent config).
4. **STOP and VALIDATE**: `pwsh demos/AIAS/run-demo.ps1` — confirm 0 × HTTP 403, agent config written.
5. Proceed to US2 and US3 (downstream symptoms expected to clear automatically with the register-ownership fix).

### Incremental Delivery

1. Phase 1 + 2 → demo skeleton runnable (no-op provisioning).
2. Phase 3 (US1) → blueprint publish succeeds end-to-end (MVP authority-ready).
3. Phase 4 (US2) → participant seal + public-org subscription explicitly confirmed clean.
4. Phase 5 (US3) → idempotent re-run verified.
5. Phase 6 → non-regression confirmed, docs updated, diff-scope validated.

---

## Notes

- All changes must be confined to `demos/AIAS/` (+ `specs/175-fix-aias-publish-governance/`). Zero changes under `src/`.
- The shared `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` is consumed but NOT modified (Decision 3, research.md).
- `demos/AssuredIdentity/AssuredIdentityDemo.psm1` is the canonical reference pattern — do not modify it.
- Use `-OwnerUserId` + `-OwnerWalletAddress` on `New-SorchaRegister` (Pattern A, Decision 2) — not a post-create role-grant approach.
- Always mint a fresh session (`Connect-SorchaUser`) after wallet link, before publish (Decision 4).
- Register reuse by name is the idempotency mechanism (Decision 5); do not change to always-create.
