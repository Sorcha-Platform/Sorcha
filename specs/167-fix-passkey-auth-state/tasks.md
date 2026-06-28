---
description: "Task list for Fix Passkey Login Auth-State Notification (Auth Hardening C)"
---

# Tasks: Fix Passkey Login Auth-State Notification (Auth Hardening C)

**Feature**: 167-fix-passkey-auth-state | **Branch**: `167-fix-passkey-auth-state`

**Input**: Design documents from `specs/167-fix-passkey-auth-state/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/auth-state-notification.md ✅ | quickstart.md ✅

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Exact file paths are included in every description

---

## Phase 1: Setup (Understand Existing Code)

**Purpose**: Read the code under change to ground all subsequent tasks in the real
implementation — no new files or infra needed.

- [X] T001 Read `CustomAuthenticationStateProvider.cs` in full — note `GetAuthenticationStateAsync` memoisation, `TryConsumeFragmentTokenAsync` store-and-clear flow, and every existing `NotifyAuthenticationStateChanged()` call site in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`
- [X] T002 [P] Read style template for unit tests in `tests/Sorcha.UI.Core.Tests/Services/Authentication/` (e.g. `TokenRefreshServiceTests.cs`) to align mocking and assertion patterns with what exists

**Checkpoint**: Provider internals understood, test style confirmed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the idempotency guard field and wire a fresh-consume signal that all US1/US2
work depends on. This is the only shared prerequisite — the field and signal shape must be stable
before unit or E2E tasks branch.

- [X] T003 Add `private bool _alreadyBroadcast;` field to `CustomAuthenticationStateProvider` and update `NotifyAuthenticationStateChanged()` to reset it (set to `false`) alongside the existing `_authStateTask = null` reset in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`
- [X] T004 Extend `GetAuthenticationStateCoreAsync()` to capture a `bool freshConsume` local variable from the return value of `TryConsumeFragmentTokenAsync` — set `true` only when it returns a non-null, non-expired entry (fresh token consumed) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`

**Checkpoint**: `_alreadyBroadcast` field exists, `freshConsume` detection compiles — US1 can start

---

## Phase 3: User Story 1 — Passkey sign-in lands on a correctly-signed-in app (Priority: P1) 🎯 MVP

**Goal**: After `TryConsumeFragmentTokenAsync` consumes a fresh, valid fragment token,
`CustomAuthenticationStateProvider` raises `AuthenticationStateChanged` exactly once (after the
current auth-state task resolves), so any component that rendered anonymous before the token
arrived updates to signed-in without a manual reload.

**Independent Test**: All US1 unit tests pass; manually sign in with a passkey and navigate to
Profile then Security — both show the signed-in experience with no reload.

### Implementation

- [X] T005 [US1] Add the re-broadcast continuation in `GetAuthenticationStateCoreAsync()`: after the auth-state task completes, if `freshConsume && state.User.Identity?.IsAuthenticated == true && !_alreadyBroadcast` then set `_alreadyBroadcast = true` and call `NotifyAuthenticationStateChanged()` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`
- [X] T006 [US1] Verify the continuation is fire-and-forget (not awaited inside `GetAuthenticationStateCoreAsync`) and cannot re-enter the core method while the first task is still in-flight — confirm no `await` before the `NotifyAuthenticationStateChanged()` call from within the task body in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`

### Unit Tests (contract C1–C5)

- [X] T007 [US1] Write test `FreshStagedToken_Consumed_RaisesAuthStateChangedOnce` (contract C1): mock `IJSRuntime` to return a valid staged token, mock `ITokenCache.StoreTokenAsync`; subscribe to `AuthenticationStateChanged`; call `GetAuthenticationStateAsync()`; assert event fired exactly once and final state is authenticated in `tests/Sorcha.UI.Core.Tests/Services/Authentication/CustomAuthenticationStateProviderTests.cs`
- [X] T008 [P] [US1] Write test `CacheOnly_NoFragmentToken_NoConsumePathEvent` (contract C2): mock `IJSRuntime` to return no staged token; confirm `AuthenticationStateChanged` is **not** raised from the consume path; confirm final state is authenticated from cache in `tests/Sorcha.UI.Core.Tests/Services/Authentication/CustomAuthenticationStateProviderTests.cs`
- [X] T009 [P] [US1] Write test `ExpiredOrAbsentToken_NoSignedInEvent_StateAnonymous` (contract C3): mock an expired / null staged token; confirm no signed-in event raised; confirm final state is anonymous in `tests/Sorcha.UI.Core.Tests/Services/Authentication/CustomAuthenticationStateProviderTests.cs`
- [X] T010 [P] [US1] Write test `SecondCallAfterConsume_NoDoubleStore_NoExtraEvent` (contract C4): consume once; call `GetAuthenticationStateAsync()` a second time; assert `ITokenCache.StoreTokenAsync` called exactly once (`Times.Once`) and no additional `AuthenticationStateChanged` event raised in `tests/Sorcha.UI.Core.Tests/Services/Authentication/CustomAuthenticationStateProviderTests.cs`
- [X] T011 [P] [US1] Write test `DirectNotifyCall_ResetsAndRebroadcasts` (contract C5): call `NotifyAuthenticationStateChanged()` directly as existing callers do; assert `_authStateTask` is reset (next `GetAuthenticationStateAsync()` re-evaluates) and the event is raised — confirming no regression for `TokenRefreshService`, `OrgSwitcher`, `LogoutConfirmDialog`, `MainLayout` callers in `tests/Sorcha.UI.Core.Tests/Services/Authentication/CustomAuthenticationStateProviderTests.cs`
- [X] T012 [US1] Run the unit test suite and confirm all C1–C5 tests pass: `dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~CustomAuthenticationStateProvider"`

---

## Phase 4: User Story 2 — Sign-in via other methods continues to land signed-in (Priority: P2)

**Goal**: Because the fix lives in the shared fragment-consume path, it covers passkey, social/SSO,
and password sign-ins. This phase adds E2E verification to confirm SC-001..SC-005 hold for all
handoff paths and that no regressions are introduced on anonymous navigation or re-login.

**Independent Test**: E2E suite passes with no flicker on anonymous paths and signed-in Profile/Security
after each supported sign-in method.

### E2E Tests

- [X] T013 [US2] Add or extend an E2E test `PasskeySignIn_ProfileAndSecurity_ShowSignedIn` in `tests/Sorcha.UI.E2E.Tests/`: sign in with a passkey via `GlobalAuthSetup.PerformLoginAsync`; navigate to Profile then Security without reloading; assert signed-in content renders (SC-001, SC-002, SC-003)
- [X] T014 [P] [US2] Add E2E test `AnonymousNavigation_NoSignedInFlicker` in `tests/Sorcha.UI.E2E.Tests/`: navigate directly to a protected page without any sign-in; assert the page prompts sign-in and no brief signed-in flash is observable (FR-005, SC-004)
- [X] T015 [P] [US2] Add E2E test `OtherHandoffMethods_ProfileAndSecurity_ShowSignedIn` in `tests/Sorcha.UI.E2E.Tests/`: repeat the Profile/Security signed-in assertion after sign-in via a non-passkey method that returns through the same fragment handoff (social/SSO or password); assert signed-in experience without reload (FR-007, SC-005)
- [X] T016 [US2] Run the E2E auth filter and confirm all new and existing auth tests pass: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "FullyQualifiedName~Auth"`

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: XML documentation, structured logging, and documentation hygiene.

- [X] T017 Add `/// <summary>` XML doc to the modified `GetAuthenticationStateCoreAsync()` override and to any new private helper method introduced in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs` (required by Principle III — no build warnings)
- [X] T018 [P] Add a single `_logger.LogDebug("Auth state re-broadcast after fresh fragment-token consume")` structured log (no string interpolation, no PII) inside the fresh-consume broadcast gate in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`
- [X] T019 [P] Run `dotnet build` on the solution and confirm zero warnings and zero errors: `dotnet restore && dotnet build`
- [X] T020 Update `.specify/MASTER-TASKS.md` to mark feature 167 tasks complete

---

## Dependencies

```
T001 → T003 → T004 → T005 → T006 → T012
T002 ─────────────────────────────────→ T007, T008, T009, T010, T011
T003, T004 → T007
T012 → T013 → T016
T005 → T014, T015 (in parallel with T013)
T016 → T017 → T019 → T020
T018 can run after T005 (same file, different concern)
```

## Parallel Execution

**Within Phase 3** (after T005 completes):
- T007, T008, T009, T010, T011 are all independent test cases — write in parallel

**Within Phase 4** (after T013 starts):
- T014 and T015 can be written while T013 is being written (separate test files/methods)

**Within Phase 5** (after T016 completes):
- T017, T018, T019 can proceed in parallel

## Implementation Strategy

**MVP scope**: Phase 3 (US1) alone delivers the core fix and unit test coverage. Profile and
Security show signed-in after passkey login. E2E (Phase 4) can follow in the same session or a
fast follow-up once unit tests are green.

**Incremental delivery**:
1. Phases 1–2: reading and guard field (< 30 min)
2. Phase 3, implementation (T005–T006): one method change (~20 lines)
3. Phase 3, unit tests (T007–T012): the deterministic contract lock
4. Phase 4, E2E (T013–T016): user-visible verification
5. Phase 5: hygiene, zero warnings confirmed
