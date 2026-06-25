---
description: "Task list for Feature 162 — Fix Inbox/Bell Drawer Overflowing Phone Width"
---

# Tasks: Fix Inbox/Bell Drawer Overflowing Phone Width

**Feature**: 162-fix-inbox-drawer-overflow | **Branch**: `162-fix-inbox-drawer-overflow`

**Input**: Design documents from `specs/162-fix-inbox-drawer-overflow/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | quickstart.md ✅

**Tests**: Playwright E2E (viewport-width assertions) are part of this feature per spec; bUnit regression must stay green.

**Organization**: CSS-only fix touching 4 files across 2 hosts. Tasks grouped by user story for independent verification at each step.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: User story — US1 (phone fix), US2 (tablet/desktop regression), US3 (cross-host consistency)
- Exact file paths included in every task description

---

## Phase 1: Setup (Baseline Verification)

**Purpose**: Confirm the current broken state and orient against the touch-point files before making changes.

- [ ] T001 Read `InboxPanel.razor.css` and verify the dead `::deep .mud-drawer` rule is present at `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor.css`
- [ ] T002 [P] Read `InboxPanel.razor` and confirm `data-testid="inbox-drawer"` and `Width="420px"` are present at `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor:26`
- [ ] T003 [P] Read `wwwroot/css/app.css` and confirm it is linked from the PWA index and does NOT yet contain the scoped global rule at `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`
- [ ] T004 [P] Read `app/index.html` and locate the inline `<style>` block in `<head>` — confirm it does NOT yet contain the scoped global rule at `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html`

**Checkpoint**: All 4 touch-point files located and baseline state understood.

---

## Phase 2: Foundational (Core CSS Fix)

**Purpose**: Apply the actual fix across both hosts and remove the dead isolated rule. These changes are the prerequisite for every user story verification. All three implementation tasks touch different files and are parallelisable once T001–T004 are done.

- [ ] T005 [P] Remove the `::deep .mud-drawer { width: min(420px, 100vw) !important; max-width: 100vw; }` block from `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor.css` (leave only the SPDX header or remove the file if it would otherwise be empty)
- [ ] T006 [P] Add the scoped global rule to `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css` — append below the existing rules:

  ```css
  /* Inbox/bell drawer width cap — global rule required because MudDrawer temporary
     overlays render out-of-tree; Blazor CSS isolation never stamps the scope attribute
     onto the drawer element so ::deep rules in InboxPanel.razor.css are silently dropped. */
  .mud-drawer[data-testid="inbox-drawer"] {
      width: min(420px, 100vw) !important;
      max-width: 100vw;
  }
  ```

- [ ] T007 [P] Add the identical scoped global rule to the inline `<style>` block in `<head>` of `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html` (same selector and values as T006; add a matching comment block)

**Checkpoint**: The width-cap rule now lives in both loaded global stylesheets, and the dead isolated rule is gone. All three user stories can now be verified.

---

## Phase 3: User Story 1 — Citizen opens the inbox/bell drawer on a phone (P1) 🎯 MVP

**Goal**: The drawer fits within the phone viewport — header, chips, titles, and timestamps are fully readable with nothing clipped.

**Independent Test**: Open PWA at 390×844 (phone), open the inbox drawer, confirm drawer width == viewport width, no horizontal scrollbar, no left-edge clipping.

### Implementation for User Story 1

- [ ] T008 [US1] Run the existing bUnit tests and confirm they are green after T005: `dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~InboxPanel"`
- [ ] T009 [US1] Write Playwright E2E test file at `tests/Playwright/InboxDrawerWidthTests.cs` targeting the PWA host:
  - For viewports `[320, 390, 420]` (phone sizes):
    - Open the inbox drawer
    - Select `[data-testid="inbox-drawer"]`
    - Assert `drawerBox.width == viewportWidth` (within 1px) — covers SC-001/FR-001/FR-002
    - Assert `drawerBox.x >= 0` (left edge on-screen) — covers FR-004
    - Assert `document.scrollingElement.scrollWidth <= innerWidth` (no horizontal overflow) — covers SC-003
- [ ] T010 [US1] Run the Playwright E2E (phone viewports only) via Docker test infra and confirm assertions pass for SC-001, FR-001, FR-002, FR-004, SC-003

**Checkpoint**: User Story 1 is fully functional and independently verified — phone users can read the inbox drawer.

---

## Phase 4: User Story 2 — Inbox drawer on tablet or desktop stays a 420px side panel (P2)

**Goal**: Viewports wider than 420px still render the inbox drawer at the established 420px side-panel width — no full-width regression.

**Independent Test**: Open the drawer at 1280×800, confirm drawer width == 420px, not full-width.

### Implementation for User Story 2

- [ ] T011 [US2] Extend the Playwright E2E at `tests/Playwright/InboxDrawerWidthTests.cs` with tablet/desktop viewport assertions:
  - For viewports `[768, 1280]`:
    - Open the inbox drawer
    - Assert `drawerBox.width == 420` (within 1px) — covers SC-002/FR-003/FR-009
    - Assert no horizontal overflow — covers SC-003
- [ ] T012 [US2] Add the boundary edge-case assertion at exactly 420px viewport width:
  - Assert `drawerBox.width == 420`, zero horizontal overflow — covers the boundary edge case from spec
- [ ] T013 [US2] Run the Playwright E2E (tablet + desktop viewports) and confirm assertions pass for SC-002, FR-003, FR-009

**Checkpoint**: Tablet/desktop side-panel presentation is confirmed unchanged — no regression.

---

## Phase 5: User Story 3 — Consistent behaviour across both hosts (P3)

**Goal**: Both the Sorcha Wallet PWA and the web host apply the identical width cap with no divergence.

**Independent Test**: Run the same Playwright viewport assertions against the web host (`/app`) and confirm results match the PWA.

### Implementation for User Story 3

- [ ] T014 [US3] Extend `tests/Playwright/InboxDrawerWidthTests.cs` to parameterise over both hosts (PWA and web host at `/app`), using the same viewport set `[320, 390, 420, 768, 1280]`
- [ ] T015 [US3] Add the nav-drawer isolation assertion to the test: open the host's navigation drawer (second `.mud-drawer` without the inbox `data-testid`) and assert its width is **unchanged** from before the fix — covers FR-007/SC-005
- [ ] T016 [US3] Run the full Playwright E2E suite (both hosts, all viewports, nav-drawer isolation check) and confirm all assertions pass, covering SC-001–SC-005

**Checkpoint**: Cross-host consistency confirmed and no other drawer resized — all 5 success criteria green.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, license headers, spec file hygiene, and final sign-off.

- [ ] T017 Verify `InboxPanel.razor.css` file state is correct post T005 — SPDX header present if file retained; file removed or near-empty with only the licence header
- [ ] T018 Confirm SPDX licence comment is present in any CSS added by T006 (`src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`) per Pattern #7 (licence header requirement)
- [ ] T019 [P] Confirm SPDX licence comment or equivalent is present in the inline `<style>` block edit in `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html` (a brief note is sufficient for an inline block)
- [ ] T020 [P] Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.sln && dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj` and confirm both build with no warnings
- [ ] T021 Update `specs/162-fix-inbox-drawer-overflow/spec.md` status field from `Draft` to `Implemented`

---

## Dependencies

```
T001–T004 (baseline reads — parallelisable)
    ↓
T005, T006, T007 (core CSS fix — parallelisable, depend on T001–T004)
    ↓
T008 (bUnit regression — depends on T005)
T009 → T010 (US1 Playwright phone — depends on T006, T007)
    ↓
T011 → T012 → T013 (US2 tablet/desktop — extends T009 file)
    ↓
T014 → T015 → T016 (US3 cross-host — extends T014 file)
    ↓
T017–T021 (polish — parallelisable after T016)
```

**User story dependencies**: US1 → US2 → US3 (all share the same Playwright file; build incrementally)

**Story independence**: Each user story has its own observable outcome and can be demoed independently on the relevant viewport. US3 depends on the same E2E file as US1/US2 but its assertions are additive.

---

## Parallel Execution Examples

```bash
# Phase 2 (all three touch different files):
# Run T005, T006, T007 simultaneously — no shared files

# Phase 3+4+5 (Playwright extension chain):
# T009 (write US1 assertions) → T011 (extend with US2) → T014 (extend with US3)
# This is sequential within the E2E file but fast; each run takes <5 min

# Final phase:
# T017, T018, T019, T020 can all run in parallel
```

---

## Implementation Strategy

**MVP** (deliverable after Phase 2 + Phase 3 = T001–T010):
- Dead isolated rule removed
- Global scoped rule in PWA `app.css`
- Global scoped rule in web host `index.html`
- Phone behaviour verified (SC-001, FR-001, FR-004, SC-003)

**Full delivery** (Phases 4–5 + Final):
- Tablet/desktop regression confirmed
- Cross-host consistency confirmed, nav-drawer isolation verified
- All 5 success criteria proven by Playwright

**Rollback**: Revert the 3 edited files (T005/T006/T007) — no database, no API, no service changes.
