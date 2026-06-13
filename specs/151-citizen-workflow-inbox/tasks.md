# Tasks: PWA Citizen Workflow Inbox

**Input**: Design documents from `/specs/151-citizen-workflow-inbox/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/consumed-endpoints.md, quickstart.md

**Tests**: INCLUDED (TDD). Constitution §IV (>85% new-code coverage, TDD encouraged) and design §8
request tests; test tasks are written before their implementation and must fail first.

**Organization**: Tasks grouped by user story (US1 P1 = MVP, US2 P2, US3 P3). No backend changes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (user-story phases only)
- All paths are repo-relative.

## Conventions (from research.md — apply to every UI task)

- **Base-relative navigation only** under the `/wallet/` prefix: `NavigateTo("actions")`,
  `NavigateTo($"applications/{instanceId}")` — never origin-absolute.
- **No `ISnackbar`** (Critical Pattern #12): use `IInlineFeedback` for refresh-failure / stale messages.
- **No backend changes**; consume existing endpoints only. Do NOT add a consumer guard to
  `/api/actions/pending` (the web shares it).
- bUnit tests use `JSRuntimeMode.Loose`; client tests use a stub `HttpMessageHandler`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Client DTOs the rest of the feature maps onto.

- [ ] T001 [P] Create `src/Apps/Sorcha.Wallet.Pwa/Services/Actions/Models/PendingActionItem.cs` with the `Urgency` enum (`Normal`/`Warning`/`Urgent`) and the fields per `data-model.md` (InstanceId, ActionId, Title, WorkflowTitle, Reference, Summary, Urgency, Deadline, ReceivedAt, NavigationPath).
- [ ] T002 [P] Create `src/Apps/Sorcha.Wallet.Pwa/Services/Actions/Models/PendingActionsCount.cs` (`Count`, `UrgentCount`) per `data-model.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared typed client over the two existing endpoints. **Blocks US1 and US2.**

**⚠️ CRITICAL**: No user-story work begins until this phase is complete.

- [ ] T003 Create `src/Apps/Sorcha.Wallet.Pwa/Services/Actions/IMyActionsClient.cs` — interface with `Task<IReadOnlyList<PendingActionItem>> GetPendingAsync(int page, int pageSize, CancellationToken)` and `Task<PendingActionsCount> GetCountAsync(CancellationToken)`.
- [ ] T004 [P] (TDD) Write **failing** `tests/Sorcha.Wallet.Pwa.Tests/Actions/MyActionsClientTests.cs` using a stub `HttpMessageHandler`: maps a representative `/api/actions/pending` JSON body to `PendingActionItem[]` (Urgency parse incl. unknown→`Normal`; `Title` fallback to `BlueprintTitle` then `"Action {id}"`); maps `/api/actions/pending/count` JSON to `PendingActionsCount`. Per `contracts/consumed-endpoints.md`.
- [ ] T005 Implement `src/Apps/Sorcha.Wallet.Pwa/Services/Actions/HttpMyActionsClient.cs` — `GET /api/actions/pending?page&pageSize` and `GET /api/actions/pending/count`, mapping to the DTOs; tolerant of unknown urgency / missing fields. Make T004 pass.
- [ ] T006 Register `IMyActionsClient` → `HttpMyActionsClient` in `src/Apps/Sorcha.Wallet.Pwa/Program.cs` DI, on the existing PWA `HttpClient` with the consumer-tier bearer handler (`BearerTokenHandler`).

**Checkpoint**: A consumer-tier citizen's pending actions + count are fetchable in code (covered by T004).

---

## Phase 3: User Story 1 — Discover and complete an action waiting on me (Priority: P1) 🎯 MVP

**Goal**: A citizen opens a "Things to do" inbox, sees the actions awaiting them (their turn), taps
one, fills + submits it via the existing flow, and returns to find it cleared.

**Independent Test**: With a citizen who has ≥1 outstanding action, open the inbox, see it listed,
open + submit it, confirm it is gone. Testable without the count badge (US2) or in-review banner (US3).

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [ ] T007 [P] [US1] Write **failing** `tests/Sorcha.Wallet.Pwa.Tests/Actions/PendingActionOrderingTests.cs`: comparer orders Urgent→Warning→Normal, then `Deadline` ascending (nulls last), then `ReceivedAt` ascending.
- [ ] T008 [P] [US1] Write **failing** `tests/Sorcha.Wallet.Pwa.Tests/Pages/ActionsInboxTests.cs` (bUnit, stub `IMyActionsClient`): renders a row per item with title (+ deadline/urgency chip where present); shows empty-state when none; **row tap calls `NavigateTo` with base-relative `applications/{InstanceId}`**; on a client throw, retains last-known rows and shows a non-blocking inline notice (no blank/error).

### Implementation for User Story 1

- [ ] T009 [US1] Create `src/Apps/Sorcha.Wallet.Pwa/Services/Actions/PendingActionOrdering.cs` (the comparer from `data-model.md`). Make T007 pass.
- [ ] T010 [US1] Create `src/Apps/Sorcha.Wallet.Pwa/Pages/Actions.razor` (`@page "actions"`): inject `IMyActionsClient` + `NavigationManager`; load + order via the comparer; render rows (title, optional deadline, urgency chip) using shared `Sorcha.UI.Components.User` primitives; friendly empty-state (FR-008); refresh-failure → retain last-known list + `IInlineFeedback` (FR-010); row tap → `NavigateTo($"applications/{InstanceId}")`. Make T008 pass.
- [ ] T011 [US1] Add the "To do" destination to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/FloatingTabBar.razor` (route `actions`, icon, `data-testid="footer-nav-todo"`; **no badge yet** — badge is US2) and wire its active-route in `src/Apps/Sorcha.Wallet.Pwa/MainLayout.razor` (`_activeRoute`). Adjust `FloatingTabBar.razor.css` for a 5-item layout. (Verified PWA-only consumer — see research Decision 4.)
- [ ] T012 [US1] Confirm the stale-action path: opening an action that is no longer outstanding lands on the existing `ApplicationInstance` "no current action" handling; surface it via `IInlineFeedback` rather than a broken form (FR-011). Add/extend a bUnit assertion in `ActionsInboxTests` or an `ApplicationInstance` test as appropriate.

**Checkpoint**: US1 fully functional and independently testable — the MVP. A citizen can find,
open, complete, and clear an action; reachable via the new tab.

---

## Phase 4: User Story 2 — Know at a glance how many things need me (Priority: P2)

**Goal**: A live count badge on the "To do" tab that updates on its own while the app is open.

**Independent Test**: With N outstanding actions, the tab shows N; a new action arriving while open
increments it without manual refresh; completing one decrements it.

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL)

- [ ] T013 [P] [US2] Write **failing** `tests/Sorcha.Wallet.Pwa.Tests/Pages/ActionsBadgeTests.cs` (bUnit): the "To do" tab shows the count from a stubbed `IMyActionsClient.GetCountAsync`; shows **no** badge when count is 0; a simulated hub signal triggers a re-fetch that updates the badge.

### Implementation for User Story 2

- [ ] T014 [US2] Add a `Badge` (count) parameter to the "To do" tab in `FloatingTabBar.razor` (render only when > 0) + badge styling in `FloatingTabBar.razor.css`.
- [ ] T015 [US2] In `MainLayout.razor`, fetch the count via `IMyActionsClient.GetCountAsync` and pass it to `FloatingTabBar`; subscribe to the existing `CitizenWalletHubConnection` signal to re-fetch count (and refresh the inbox list if mounted) within ~10s (SC-004); refresh after a successful submit so the badge decrements (FR-007). Make T013 pass.
- [ ] T016 [US2] Ensure the inbox page (`Actions.razor`) also re-loads its list on the same hub signal while mounted (live list refresh, FR-007), reusing the MainLayout subscription or a scoped page subscription.

**Checkpoint**: US1 + US2 both work independently; the badge reflects live outstanding work.

---

## Phase 5: User Story 3 — See what I've submitted and is in review (Priority: P3)

**Goal**: A distinct, lightweight "In review" indication for submitted-and-awaiting-others.

**Independent Test**: With a submitted application awaiting another party, the inbox shows an
"in review" indication distinct from the "needs you" rows; hidden when there is nothing in review.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL)

- [ ] T017 [P] [US3] Write **failing** bUnit test in `tests/Sorcha.Wallet.Pwa.Tests/Pages/ActionsInReviewBannerTests.cs`: the banner renders (visually distinct from the action rows) when the stubbed `IPendingApplicationClient` returns a notice; hidden when it returns none.

### Implementation for User Story 3

- [ ] T018 [US3] In `Actions.razor`, inject the existing `IPendingApplicationClient`, fetch the Feature-124 notice, and render it as a distinct "In review" banner below the "needs you" list (FR-009). Make T017 pass.

**Checkpoint**: All three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Run `scripts/check-no-snackbar.ps1` and confirm no new `ISnackbar`/`Snackbar.Add(` references were introduced by this feature.
- [ ] T020 [P] Update `src/Apps/Sorcha.Wallet.Pwa` README / `Sorcha.UI.Components.User` README if the FloatingTabBar destination set is documented there; note the new "To do" inbox.
- [ ] T021 [P] Confirm new-code coverage ≥85% for the added client + page logic (constitution §IV); add focused unit tests if any branch (urgency mapping, ordering edge cases, refresh-failure retention) is uncovered.
- [ ] T022 Run `quickstart.md` manual verification against Docker/n1 (steps 1–8), including the "other participant's action does not appear" check (SC-002) and the offline-retain check (FR-010). Record results.
- [ ] T023 Build `Sorcha.Wallet.Pwa` + run `tests/Sorcha.Wallet.Pwa.Tests` green with no new warnings (constitution §V).

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks US1 and US2** (both consume `IMyActionsClient`). US3 does not depend on it (uses the existing `IPendingApplicationClient`).
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational **and** on US1's `FloatingTabBar` "To do" tab existing (the badge attaches to that tab; T014 builds on T011).
- **US3 (Phase 5)**: depends only on US1's `Actions.razor` existing (T018 adds the banner to it); independent of Foundational.
- **Polish (Phase 6)**: after the desired stories are complete.

### Within each story

- Tests written first and FAIL before implementation (T004 before T005; T007/T008 before T009/T010; T013 before T014/T015; T017 before T018).
- DTOs → client → page → nav.

### Parallel opportunities

- T001, T002 (Setup DTOs) in parallel.
- T004 (client test) authored in parallel with T003 (interface).
- T007 + T008 (US1 tests) in parallel.
- Polish T019–T021 in parallel.

---

## Parallel Example: User Story 1

```text
# Author US1 tests together (they must fail first):
Task: "PendingActionOrderingTests.cs — comparer ordering" (T007)
Task: "ActionsInboxTests.cs — list/empty/nav/refresh-failure" (T008)
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP & VALIDATE** (citizen can
   find, open, complete, clear an action) → demo. This alone closes the "no discovery on the phone"
   gap and proves the discovery→fill→submit loop.

### Incremental delivery

- + US2 (live count badge) → demo.
- + US3 (in-review banner) → demo.
- Each story is additive and independently testable; none breaks a prior one.

---

## Notes

- No backend project is touched; the only shared-library change is the PWA-only `FloatingTabBar`.
- This is sub-project **A**. B (catalogue), C (offline/drafts/camera), D (dual-tier/org-role) are
  separate spec→plan→tasks→implement cycles and explicitly out of scope here.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
