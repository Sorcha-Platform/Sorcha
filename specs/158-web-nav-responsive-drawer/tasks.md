---
description: "Task list for Feature 158 — Web Nav Drawer Responsive (no mini rail)"
---

# Tasks: Web Nav Drawer — Responsive (no mini rail)

**Input**: Design documents from `specs/158-web-nav-responsive-drawer/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | contracts/drawer-behavior.md ✅ | quickstart.md ✅

**Key source files**:
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — the single edit target
- `tests/Sorcha.UI.E2E.Tests/Docker/NavigationTests.cs` — E2E assertions to refresh
- `tests/Sorcha.UI.E2E.Tests/PageObjects/NavigationComponent.cs` — page object locators to review

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Exact file paths in all descriptions

---

## Phase 1: Setup

**Purpose**: Confirm baseline build state before making changes.

- [X] T001 Verify baseline build with `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client` — confirm zero pre-existing errors in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`

**Checkpoint**: Build is clean — source edits can begin.

---

## Phase 2: User Story 1 — Reclaim full content width on desktop (Priority: P1) 🎯 MVP

**Goal**: Closing the nav drawer on a desktop-width viewport removes all horizontal space the navigation previously occupied — no icon rail remains, content expands to full width.

**Independent test**: On a desktop-width viewport, sign in, toggle the drawer closed, confirm the navigation strip disappears entirely and the content widens; toggle open and confirm the content is pushed aside without overlap.

**Contract refs**: C1, C2, C3 in `contracts/drawer-behavior.md` | FR-001, FR-002, SC-001

- [X] T002 [US1] Change `Variant="@DrawerVariant.Mini"` to `Variant="@DrawerVariant.Responsive"` on the `MudDrawer` at line 76 of `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- [X] T003 [US1] Remove the `OpenMiniOnHover="true"` attribute from the same `MudDrawer` element in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` (dead under Responsive — no rail to hover; spec Edge Cases §"No hover-expand expectation")
- [X] T004 [P] [US1] Review `IsDrawerOpenAsync()` and the drawer locator in `tests/Sorcha.UI.E2E.Tests/PageObjects/NavigationComponent.cs` — if either keys on `.mud-drawer-mini` or Mini-variant DOM, update to the Responsive variant's selector so open-detection remains correct
- [X] T005 [US1] In `tests/Sorcha.UI.E2E.Tests/Docker/NavigationTests.cs` add/update a desktop-width test asserting: (a) after `ToggleDrawerAsync()` the `.mud-drawer-mini` element is absent, (b) the main content element's width is greater than before toggle, (c) toggling back shows the full drawer and the content narrows

---

## Phase 3: User Story 2 — Unobstructed reading on phones, overlay when opened (Priority: P2)

**Goal**: On a phone-width viewport, the drawer is closed on first render (content full-width) and opens as an overlay with a dismissable scrim; selecting a nav item closes it.

**Independent test**: At phone viewport width, load the app signed in — drawer is closed; open it and confirm it overlays without reflowing content; select a nav destination and confirm the drawer closes.

**Contract refs**: C4, C5, C6 in `contracts/drawer-behavior.md` | FR-003, FR-007, SC-002, SC-003

- [X] T006 [P] [US2] In `tests/Sorcha.UI.E2E.Tests/Docker/NavigationTests.cs` add a phone-viewport test asserting the drawer is closed on fresh load (no open drawer element visible) and the content element occupies full viewport width
- [X] T007 [US2] In `tests/Sorcha.UI.E2E.Tests/Docker/NavigationTests.cs` add a phone-viewport test asserting: (a) opening the drawer renders it as an overlay (content width unchanged), (b) a scrim/backdrop element is visible, (c) selecting any nav item closes the drawer and the destination renders at full width

---

## Final Phase: Polish & Cross-cutting Concerns

**Purpose**: Optional structural clean-up, build validation, and full E2E green.

- [X] T008 Optionally remove the four now-dead `@if (_drawerOpen)` guards at lines 87, 116, 137, 242 in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` (existed to suppress text dividers in Mini icon-only mode; Responsive has no icon-only mode — guards are always-true; removal reduces dead complexity without changing visible behaviour)
- [X] T009 Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client` and confirm zero new build warnings from the edited `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- [X] T010 Run `dotnet test tests/Sorcha.UI.E2E.Tests --filter "FullyQualifiedName~Navigation"` and confirm all navigation tests pass (drawer toggle, visibility, overlay, contents intact per SC-004)

---

## Dependencies

```
T001
  └─► T002 → T003 → T005
               └─► T004 (parallel with T005)
  └─► T006 (parallel with T005, after T002–T003)
       └─► T007
T009 (after T002–T003–T008)
T010 (after T004–T005–T006–T007)
```

**US1 and US2 share the same source edit** (T002–T003). US2 E2E tasks (T006–T007) can begin once the source edit lands; they are independent of the US1 E2E tasks (T004–T005).

## Parallel execution opportunities

- T004 and T005 can proceed in parallel once T002–T003 are complete (different concerns within the same test file).
- T006 and T007 are sequential (T007 extends T006's phone test). Both are independent of T004–T005.

## Implementation strategy

**MVP (US1 only — 3 tasks)**: T001 → T002 → T003. These three tasks deliver the core feature: a closed desktop drawer that leaves zero navigation footprint. Independently demonstrable after T003.

**Full delivery**: extend to T004–T007 for E2E coverage, then T008–T010 for polish and sign-off.

**Risk note**: T004 is the only unknown — if `NavigationComponent.cs` locators are coupled to `.mud-drawer-mini`, they will need updating before the E2E suite can run against the Responsive variant.
