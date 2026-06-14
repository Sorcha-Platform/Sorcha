# Tasks: PWA Dual-Tier / Org-Role Work

**Input**: Design docs from `/specs/153-dual-tier-org-role/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: INCLUDED (TDD). **Depends on A** (inbox) + Feature 125 context infra (both merged).

## Conventions

- **No backend change.** Reuse `switch-org`, `me/organizations`, `/api/actions/pending`, `/execute`.
- **Never weaken the F136 boundary** — only `switch-org` elevates; the PWA never forges a capacity.
- After return to Personal the active token MUST be consumer (or signed-out) — never residual platform.
- Base-relative nav; no `ISnackbar`; bUnit `JSRuntimeMode.Loose`; thin JS adapters untested (logic is).

---

## Phase 1: Setup / Foundational — home-token slot

- [ ] T001 (TDD) Failing tests in `tests/Sorcha.Wallet.Pwa.Tests/` for the home-token slot on
  `IAccessTokenStore` (InMemory): SetHome/GetHome round-trip; GetHome returns null after ClearHome;
  GetHome purges an expired home record.
- [ ] T002 Add `GetHomeAsync` / `SetHomeAsync` / `ClearHomeAsync` to `IAccessTokenStore`;
  implement in `InMemoryAccessTokenStore` and `IndexedDbAccessTokenStore` (key `home-access-token`,
  same expiry-purge as the active token). Make T001 pass.
- [ ] T003 Ensure sign-out clears the home token too (call `ClearHomeAsync` wherever `ClearAsync` is
  called on sign-out).

**Checkpoint**: a personal/home token can be cached + restored + cleared.

---

## Phase 2: US1 — Capacity switch + personal restore (Priority: P1) 🎯 core

**Goal**: reliable Personal ⇄ Org switch; returning to Personal restores personal access.
**Independent Test**: switch to org then back to Personal; confirm a consumer token is active again.

- [ ] T004 (TDD) Failing tests for `ManagedUserContext` capacity transitions
  (`tests/.../Context/`): switching Personal→Org snapshots the current (consumer) token as Home and
  stores the org token active; switching Org→Personal restores the Home token as active; switch
  failure (switch-org non-2xx) leaves capacity + token unchanged and returns false. Use a stub
  `IAccessTokenStore` + stub `HttpMessageHandler`.
- [ ] T005 [US1] In `ManagedUserContext.SetActiveContextAsync`: before overwriting the token on a
  Personal→Org switch, `SetHomeAsync(current active token)`; on Org→Personal, `GetHomeAsync()` and
  `SetAsync` it active (replace the line-143 "keep existing token" gap). Preserve the no-op + failure
  semantics. Make T004 pass.
- [ ] T006 [US1] Capacity indicator: confirm `MainLayout` `ActiveLabel` reflects Personal vs
  "<Org>" across a switch (Feature 125 wiring); add the "acting as" framing string if missing. bUnit
  or assert via the existing label.

**Checkpoint**: Personal ⇄ Org switching is reliable and personal access survives the round trip.

---

## Phase 3: US2 — See & do org-role work, framed (Priority: P1)

**Goal**: in an org capacity the inbox shows org-role actions framed "acting as <Org>", performable.
**Independent Test**: in an org context, the inbox lists the org-role action with the banner; open+submit works (execute live-validated).

- [ ] T007 [P] [US2] (TDD) Failing bUnit test: `Actions.razor` renders an "acting as <Org>" banner
  (`data-testid=actions-acting-as`) when the stubbed `IUserContext.ActiveContextOrgId` is set, and no
  banner when Personal.
- [ ] T008 [US2] `Actions.razor`: inject `IUserContext` (+ label source); render the
  "acting as <Org>" banner in an org context. The pending list already uses the active token (org
  actions surface). Make T007 pass.
- [ ] T009 [US2] Confirm execute path: `ApplicationInstance` submits with the active (org) token; no
  code change expected. Document the **live-validation** requirement (analyst Action 2) in the PR.

**Checkpoint**: org-role work is visible + framed; execute rides the active token.

---

## Phase 4: US3 — Switch keeps inbox/count consistent (Priority: P2)

**Goal**: a capacity switch refreshes the inbox + count to the new capacity, no reload.
**Independent Test**: switch with the inbox open; list + badge update.

- [ ] T010 [P] [US3] (TDD) Failing bUnit test: `Actions.razor` reloads its list when
  `IUserContext.OnContextChanged` fires (stub raises the event → `GetPendingAsync` re-invoked).
- [ ] T011 [US3] `Actions.razor`: subscribe to `IUserContext.OnContextChanged` (and unsubscribe on
  dispose) → `RefreshAsync`. Make T010 pass.
- [ ] T012 [US3] `MainLayout`: on context change, refresh the To-do count + raise
  `OutstandingWorkChanged` (reuse the A/C hooks) so the badge + mounted inbox follow the capacity.

**Checkpoint**: switching is visually consistent; no stale cross-capacity work.

---

## Phase 5: US4 — Entitlement-aware switcher (Priority: P3)

**Goal**: only real memberships offered; failed/declined switch surfaces + stays put.
**Independent Test**: no-membership user sees only Personal; a declined switch shows a message.

- [ ] T013 [P] [US4] (TDD) Failing test: a `SetActiveContextAsync` that returns false (server
  declined) leaves `ActiveContextOrgId` unchanged; the UI shows a non-blocking notice.
- [ ] T014 [US4] Surface a switch failure to the user (inline feedback / chip state); confirm the
  switcher lists only `IUserOrgMembershipsClient` memberships (existing) and never offers elevation.

**Checkpoint**: capacity choices are safe + honest.

---

## Phase 6: Polish + PR

- [ ] T015 [P] `scripts/check-no-snackbar.ps1` clean.
- [ ] T016 [P] New-code coverage ≥85% (token-store home, ManagedUserContext transitions, inbox
  banner/refresh); add tests for any uncovered branch.
- [ ] T017 [P] Docs: note dual-tier/org-role capacity in the PWA/`sorcha-architecture` as appropriate.
- [ ] T018 Clean build (PWA + UI.Core) 0 warnings; `Sorcha.Wallet.Pwa.Tests` + `Sorcha.UI.Core.Tests` green.
- [ ] T019 `quickstart.md` manual verification — **including the primary live-validation** (analyst
  Action 2 from the PWA in org context + personal-restore). Record results / flag as pre-merge.
- [ ] T020 PR + merge-on-green.

---

## Dependencies & order

- Setup (home-token slot) → US1 (restore) → US2 (framing) → US3 (refresh) → US4 (entitlement) → polish.
- US1 is the core (personal-restore correctness). US2 is the value slice (live-validate execute).
- Tests precede implementation in each phase.

## Implementation strategy

MVP = Setup + US1 + US2 (switch in, do org work, switch back safely). US3/US4 harden consistency +
safety. No backend change throughout; live-validate execute-in-context before trusting D.
