---
description: "Task list for Feature 173: Web Step-Up Social Account Linking (B-UI)"
---

# Tasks: Web Step-Up Social Account Linking (B-UI)

**Feature**: 173 — `173-web-step-up-account-linking`
**Input**: Design documents from `/specs/173-web-step-up-account-linking/`
**Branch**: `173-web-step-up-account-linking`

**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**⚠ F168 sequencing risk**: Unit tests mock the HTTP boundary and run without Feature 168.
Playwright E2E requires F168 to be merged/available first (see `research.md` R1).

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[US#]**: User story label (maps to spec.md user stories)

---

## Phase 1: Setup (Shared Models & Interfaces)

**Purpose**: Create the new types and interface that all subsequent phases depend on.

- [X] T001 Create `AnonymousSocialLinkModels.cs` with all new client-side types in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Authentication/AnonymousSocialLinkModels.cs` — `LinkPendingOutcome`, `AnonymousLinkInitiateRequest`, `AnonymousLinkInitiateResult`, `AnonymousLinkVerifyRequest`, `AnonymousLinkVerifyResult`, `AnonymousLinkConfirmResult`, and the two new enums `InitiateOutcome` (`Ok`, `Expired`, `UnsupportedV1Method`, `RateLimited`, `Failed`) and `ConfirmOutcome` (`Linked`, `Expired`, `ProofInvalid`, `Conflict`, `RateLimited`, `Failed`); reuse existing `ChallengeMethod` and `ChallengeVerifyError` (no redefinition)
- [X] T002 [P] Create `IAnonymousSocialLinkClientService` interface in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/IAnonymousSocialLinkClientService.cs` with three methods: `InitiateAsync(string linkPendingToken, ChallengeMethod? preferred = null, CancellationToken ct = default)`, `VerifyAsync(string linkPendingToken, ChallengeMethod method, JsonElement proof, CancellationToken ct = default)`, `ConfirmAsync(string linkPendingToken, string challengeToken, CancellationToken ct = default)`

**Checkpoint**: Models and interface defined — no compilation yet; foundational phase can start.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before any user story can be implemented.
All three user stories depend on the fragment-handoff JS extension, the client service, DI registration,
the boot-time gate, and the prompt shell.

**⚠ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 Extend `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/js/fragment-handoff.js` to detect `outcome === "LinkRequired"` fragments: (1) stage `{ outcome: "LinkRequired", linkPendingToken }` in `window.__sorcha_link_pending` and `localStorage['sorcha:link-pending']`; (2) call `history.replaceState(null, '', pathname + search)` immediately to strip the token from the address bar (FR-002, SC-005); (3) export `getLinkPending()` → `{ linkPendingToken } | null` and `clearLinkPending()` → `void` on `window.sorcha.fragmentHandoff`; leave existing `token` staging path untouched
- [X] T004 Implement `AnonymousSocialLinkClientService.cs` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/AnonymousSocialLinkClientService.cs` consuming the three F168 endpoints via the configured `HttpClient` base address (no `Authorization` header): `POST /api/auth/social/link/challenge/initiate` → map 401→`Expired`, 400→`UnsupportedV1Method`, 429→`RateLimited`; `POST /api/auth/social/link/challenge/verify` → map 401 body-discriminated→`ProofRejected`/`Expired`, 403 `proof_tier_insufficient`→`ProofTierInsufficient`, 429→`Failed`; `POST /api/auth/social/link/confirm` with `X-Auth-Challenge: ch_<token>` header → map 401→`Expired`/`ProofInvalid`, 403→`ProofInvalid`, 409→`Conflict`, 429→`RateLimited`
- [X] T005 Register `IAnonymousSocialLinkClientService` / `AnonymousSocialLinkClientService` in the `Sorcha.UI.Components.User` DI extension method (alongside other client services that use `AddCoreServices` `HttpClient` base address) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Extensions/`
- [X] T006 [P] Create stub `LinkExistingAccountPrompt.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/AccountLink/LinkExistingAccountPrompt.razor` with: `[Parameter] string LinkPendingToken` parameter; `Detecting` initial state that transitions to `Explaining` when token is non-null; `Explaining` state rendering a MudCard explaining "an existing account matched — confirm it's you before the new sign-in method is connected"; a Cancel button binding (navigates to signed-out home, calls `clearLinkPending()`, no link/session); `IInlineFeedback` injected; stubs for all remaining states defined in the data-model state machine (empty branches, no logic yet); SPDX license header
- [X] T007 Create `LinkRequiredGate.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/AccountLink/LinkRequiredGate.razor`: `OnAfterRenderAsync(firstRender)` calls `getLinkPending()` via `IJSRuntime`; if `linkPendingToken` is non-null, sets `_showPrompt = true` and `_token` and calls `StateHasChanged()`; renders `<LinkExistingAccountPrompt LinkPendingToken="@_token" />` when `_showPrompt` is true, otherwise renders nothing (transparent passthrough); runs with no `ClaimsPrincipal` (mounted outside `AuthorizeRouteView`)
- [X] T008 Mount `<LinkRequiredGate />` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Routes.razor` alongside `<FragmentTokenHandler />` (outside `AuthorizeRouteView`), so the gate runs on every boot with no session

**Checkpoint**: Fragment staging works; gate renders stub prompt for any `LinkRequired` landing;
DI is wired; project compiles. All three user stories can now be implemented in parallel.

---

## Phase 3: User Story 1 — Prove ownership via passkey (Priority: P1) 🎯 MVP

**Goal**: A person whose social email matches an existing passkey-enrolled account can complete
the passkey ceremony, link the new social identity, and reach signed-in state in ≤3 interactions
(SC-001). Link is permanent (SC-006). Token never persists in address bar (SC-005).

**Independent Test**: Trigger a social sign-in that returns `LinkRequired` for a passkey-enrolled
account. Confirm the prompt appears (not the signed-out home), complete the passkey check, and
confirm the social identity is linked and a full web session is established. Verify address bar
shows no fragment after capture; verify reload/back shows no token and falls back to signed-out home.

- [X] T009 [P] [US1] Create `AnonymousSocialLinkClientServiceTests.cs` in `tests/Sorcha.UI.Components.User.Tests/AccountLink/AnonymousSocialLinkClientServiceTests.cs` with xUnit + FluentAssertions + Moq unit tests covering: `InitiateAsync` with Passkey result → maps 200 correctly; `VerifyAsync` with WebAuthn assertion → maps 200 (ChallengeToken returned); `ConfirmAsync` sends `X-Auth-Challenge` header → maps 200 (AccessToken + RefreshToken returned); `InitiateAsync` 401 → `InitiateOutcome.Expired`; `ConfirmAsync` 409 → `ConfirmOutcome.Conflict`
- [X] T010 [US1] Add PasskeyCeremony state to `LinkExistingAccountPrompt.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/AccountLink/LinkExistingAccountPrompt.razor`: in the `Initiating` state call `_linkService.InitiateAsync(LinkPendingToken, ChallengeMethod.Passkey)` → on `Ok` with `Method == Passkey` transition to `PasskeyCeremony` holding the `Payload`; in `PasskeyCeremony` call `PasskeyInteropService.GetCredentialAsync(payload)` to run `navigator.credentials.get()`; on assertion success call `_linkService.VerifyAsync(LinkPendingToken, ChallengeMethod.Passkey, assertion)` → on `Succeeded` transition to `Confirming` holding `ChallengeToken`; on passkey cancellation or no-WebAuthn transition to `ErrorRetry` with inline feedback
- [X] T011 [US1] Add Confirming → signed-in path to `LinkExistingAccountPrompt.razor`: call `_linkService.ConfirmAsync(LinkPendingToken, challengeToken)` → on `ConfirmOutcome.Linked` persist `{accessToken, refreshToken}` via `ITokenCache.StoreTokenAsync`, call `clearLinkPending()` via `IJSRuntime`, trigger `CustomAuthenticationStateProvider.NotifyAuthenticationStateChanged` (or equivalent re-evaluate), then navigate to `/` (identical path to normal social sign-in per R6 / contracts/fragment-and-session.md §C); on non-Linked outcomes transition to appropriate terminal or retry states
- [X] T012 [US1] Wire inline feedback for passkey-path errors in `LinkExistingAccountPrompt.razor`: `ErrorRetry` state shows `IInlineFeedback.ShowError` for retryable failures (passkey cancelled, wrong proof — non-leaky message); `Expired` terminal state shows `IInlineFeedback.ShowError(autoDismissMs: 0)` with "This request has expired — please sign in again" (FR-015); use `MudAlert` inline inside the component for pre-auth context where `InlineFeedbackHost` layout mount is not guaranteed (R8)

**Checkpoint**: US1 fully functional. A passkey-enrolled account can complete the link-required
flow end-to-end. Unit tests for the passkey path pass. Token strips from address bar on capture.

---

## Phase 4: User Story 2 — Prove ownership via authenticator code (Priority: P1)

**Goal**: A person whose existing account has TOTP (but no passkey) can enter a 6-digit code,
link the social identity, and reach signed-in state (SC-002). Invalid or expired codes are
rejected without a link or session; retry is allowed within rate limits.

**Independent Test**: Trigger `LinkRequired` for a TOTP-only account. Confirm the authenticator-code
challenge is offered. Enter a valid 6-digit code → link + session. Enter an invalid code → "code not
accepted", no link, no session; retry allowed (subject to server rate limits).

- [X] T013 [P] [US2] Add unit tests in `tests/Sorcha.UI.Components.User.Tests/AccountLink/AnonymousSocialLinkClientServiceTests.cs`: `InitiateAsync` TOTP → `Method == Totp`, null `Payload`; `VerifyAsync` with `{ "code": "123456" }` proof → 200 `Succeeded`; `VerifyAsync` with wrong code → 401 → `ChallengeVerifyError.ProofRejected` (retry allowed, no link); TOTP `VerifyAsync` 429 → `ChallengeVerifyError.Failed` (throttled)
- [X] T014 [US2] Add `AwaitingCode` state to `LinkExistingAccountPrompt.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/AccountLink/LinkExistingAccountPrompt.razor`: render a `MudTextField` (numeric, `MaxLength="6"`) for the authenticator code; a Submit button (enabled only when `_code` is exactly 6 digits, trimmed); client-side shape check only — acceptance is server-side (data-model.md Validation rules)
- [X] T015 [US2] Wire TOTP code submission in `LinkExistingAccountPrompt.razor`: on Submit build `JsonElement` from `{ "code": "<_code>" }` via `JsonSerializer.Deserialize<JsonElement>(...)`; call `_linkService.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof)`; on `Succeeded` transition to `Confirming` (shared confirm path from T011); on `ProofRejected` show inline error "Code not accepted — please try again" and remain in `AwaitingCode` (retry allowed, FR-016); on `Expired` transition to `Expired` terminal state; on `Failed` / rate-limited show throttled message
- [X] T016 [US2] Add "use authenticator code instead" switch to `LinkExistingAccountPrompt.razor`: when server initially indicates `Passkey` and the user activates the switch, call `InitiateAsync(LinkPendingToken, ChallengeMethod.Totp)` and transition to `AwaitingCode`; expose the switch only when a re-initiate with `Totp` is plausible (server will return 400 if TOTP not enrolled, handled gracefully); this satisfies FR-007 method preference + switch

**Checkpoint**: US2 fully functional. Both v1 proof methods (passkey + TOTP) are usable independently.
Unit tests for TOTP paths pass.

---

## Phase 5: User Story 3 — Safe failure and abandonment (Priority: P2)

**Goal**: Every failure mode (expired token, tampered token, cancelled proof, wrong-proof rejection,
insufficient proof tier, link conflict, token replay, no-v1-method, fragment reload) ends with no
link, no session, and a clear non-leaky message. 100% fail-closed (SC-004). Gate is inert on
absent/malformed fragment (FR-003).

**Independent Test**: Present the prompt with an expired token, a tampered token, and a cancelled
proof — confirm each produces no link and no session. Cancel → signed-out home. No-v1-method →
recovery guidance. Conflict → non-leaky failure. Reload after token cleared → signed-out home.

- [X] T017 [P] [US3] Add unit tests in `tests/Sorcha.UI.Components.User.Tests/AccountLink/AnonymousSocialLinkClientServiceTests.cs`: `InitiateAsync` 401 → `InitiateOutcome.Expired` (tampered/expired token, FR-015); `InitiateAsync` 400 → `InitiateOutcome.UnsupportedV1Method` (recovery path, FR-018); `ConfirmAsync` 409 → `ConfirmOutcome.Conflict` (non-leaky, no session, FR-016); `ConfirmAsync` 401 already-redeemed → `ConfirmOutcome.Expired` (replay edge); `VerifyAsync` 403 `proof_tier_insufficient` → `ChallengeVerifyError.ProofTierInsufficient` (→ Recovery, FR-016/FR-018); `ConfirmAsync` 429 → `ConfirmOutcome.RateLimited`
- [X] T018 [US3] Handle `Expired` terminal state in `LinkExistingAccountPrompt.razor`: any 401 on initiate/verify/confirm that maps to expired/invalid → transition to `Expired` state; render "This request has expired — please sign in again" with a "Sign in again" link/button that clears staging via `clearLinkPending()` and navigates to `/` (signed-out home); no link created, no session established (FR-015, SC-004); use `MudAlert Severity="Error"` inline
- [X] T019 [US3] Implement Cancel button in `LinkExistingAccountPrompt.razor` (stub was added in T006): on click call `clearLinkPending()` via `IJSRuntime`, set state to `Cancelled`, navigate to `/` (signed-out home); no link, no session; available in `Explaining` state and any non-terminal state (FR-017)
- [X] T020 [US3] Implement `UnsupportedV1Method` → `Recovery` state in `LinkExistingAccountPrompt.razor`: when `InitiateAsync` returns `InitiateOutcome.UnsupportedV1Method`, render `Recovery` state with message "Your account requires a sign-in method not yet supported here — please sign in with your existing method" and a "Go to sign in" link (no dead end, FR-018); no link, no session; call `clearLinkPending()` when the user navigates away
- [X] T021 [US3] Implement `Conflict` → `ConflictFailure` terminal state in `LinkExistingAccountPrompt.razor`: when `ConfirmAsync` returns `ConfirmOutcome.Conflict` (409), render a non-leaky failure message ("This sign-in method couldn't be connected — it may already be linked to a different account") with a "Sign in again" link that calls `clearLinkPending()` and navigates to `/`; no session established (FR-016, SC-004)
- [X] T022 [US3] Complete remaining error-path wiring in `LinkExistingAccountPrompt.razor`: `ProofRejected` on verify → stay in `AwaitingCode` or `ErrorRetry` with "not accepted" message (retry allowed, non-leaky); `ProofTierInsufficient` (403) → transition to `Recovery` state (FR-016/FR-018); inert gate path — when `getLinkPending()` returns null in `LinkRequiredGate.razor` the gate renders nothing and the signed-out home shows normally (FR-003); verify reload after `clearLinkPending()` → `getLinkPending()` null → gate inert → no crash/partial link (edge: Fragment refresh / deep-link)

**Checkpoint**: All three user stories complete. Every failure mode produces no link and no session.
All unit tests pass. 100% fail-closed verified by test assertions (SC-004).

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Quality gates, documentation, and validation across all stories.

- [X] T023 [P] Add SPDX license header `// SPDX-License-Identifier: MIT\n// Copyright (c) 2026 Sorcha Contributors` to all new `.cs` and `.razor` files: `AnonymousSocialLinkModels.cs`, `IAnonymousSocialLinkClientService.cs`, `AnonymousSocialLinkClientService.cs`, `LinkExistingAccountPrompt.razor`, `LinkRequiredGate.razor`
- [X] T024 [P] Add `/// <summary>` XML doc comments to all public members in new files (`AnonymousSocialLinkModels.cs`, `IAnonymousSocialLinkClientService.cs`, `AnonymousSocialLinkClientService.cs`) to satisfy the build-warning-free policy (CLAUDE.md Critical Pattern §III); run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User` and confirm zero XML-doc warnings
- [X] T025 [P] Audit `IInlineFeedback` / `MudAlert` usage across `LinkExistingAccountPrompt.razor`: confirm zero `ISnackbar` / `Snackbar.Add` calls; confirm all user-facing state messages use either `IInlineFeedback` (layout-hosted) or inline `MudAlert` (for the pre-auth takeover where layout is not guaranteed mounted, per R8); matches CLAUDE.md Critical Pattern #12 and FR-019
- [X] T026 Run SC-007 isolation diff check per `quickstart.md`: `git diff --name-only master... | grep -E 'Security/(AuthChallengeDialog|SecurityHome|PasswordSection|PasskeysSection|SocialLinksSection|TwoFactorSection|AssuranceBadge)\.razor'` → confirm empty output (zero edits to Feature 150/116 component files)
- [X] T027 Build and run unit tests: `dotnet build src/Apps/Sorcha.UI && dotnet test --filter "FullyQualifiedName~AnonymousSocialLink"` → confirm build clean and all unit tests pass; document if any Playwright E2E tests are deferred pending F168
- [X] T028 [P] Update `.specify/MASTER-TASKS.md` to mark Feature 173 tasks as in-progress / complete per actual status; note F168 dependency gating for Playwright E2E

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **User Stories (Phases 3, 4, 5)**: All depend on Phase 2 completion
  - US1 (Phase 3) and US2 (Phase 4) are both P1; US2 can start after T010 defines the `AwaitingCode` state stub — they share the prompt component but in different state branches
  - US3 (Phase 5) fills in terminal states already stubbed in T006; can overlap with US1/US2 on separate state branches
- **Polish (Final Phase)**: Depends on Phases 3–5 completion

### User Story Dependencies

- **US1 (P1, Phase 3)**: No dependency on US2/US3 — independently deliverable after Phase 2
- **US2 (P1, Phase 4)**: No dependency on US1 — uses different state branch (`AwaitingCode`) of the same prompt; confirm path (T011) from US1 is shared, so T011 must be complete before T015 can reuse it
- **US3 (P2, Phase 5)**: Fills terminal states (Expired, Cancelled, Recovery, ConflictFailure) that are independent of US1/US2 proof paths — can proceed in parallel on separate state branches

### Within Each User Story

- Models (T001) before interface (T002) — both before service implementation (T004)
- JS extension (T003) before gate (T007) before Routes mount (T008)
- Stub prompt (T006) before gate (T007) to ensure type reference compiles
- Unit tests [P] tasks can run in parallel with implementation tasks (different files)

### Parallel Opportunities

- T002 [P] (interface) can run alongside T001 (models) — different files
- T006 [P] (prompt stub) can run alongside T003/T004/T005 once Phase 1 is done
- T009 [P] [US1], T013 [P] [US2], T017 [P] [US3] (unit tests) can all run in parallel with their respective implementation tasks — different files
- T023–T025, T028 [P] (polish) can run in parallel once implementation phases are done

---

## Parallel Example: Phase 2

```bash
# Can run in parallel once Phase 1 is complete:
T003: Extend fragment-handoff.js                # src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/js/
T004: Implement AnonymousSocialLinkClientService  # Sorcha.UI.Components.User/Services/User/
T005: Register DI                               # Sorcha.UI.Components.User/Extensions/
T006: Create LinkExistingAccountPrompt stub     # Sorcha.UI.Components.User/Components/AccountLink/

# T007 depends on T006 (type reference); T008 depends on T007:
T007: Create LinkRequiredGate                   # Sorcha.UI.Web.Client/Components/AccountLink/
T008: Mount gate in Routes.razor               # Sorcha.UI.Web.Client/Routes.razor
```

## Parallel Example: User Story 1

```bash
# After Phase 2, these can run in parallel:
T009 [P]: Write unit tests (tests/ directory)
T010: Implement PasskeyCeremony state (prompt .razor)
# T011 depends on T010 (Confirming state); T012 depends on T010 (error branches)
```

---

## Implementation Strategy

### MVP First (US1 Only — 12 tasks)

1. Complete Phase 1 (T001–T002): models + interface
2. Complete Phase 2 (T003–T008): JS, service, DI, gate, prompt stub
3. Complete Phase 3 (T009–T012): US1 passkey happy path + error feedback
4. **STOP and VALIDATE**: passkey link end-to-end; unit tests green; SC-007 diff clean; token strip verified
5. Ship US1 independently — TOTP and fail-closed polish can follow

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation ready (gate inert for everyone; prompt stub registered)
2. Add Phase 3 (US1) → Passkey linking works → validate → Demo (MVP)
3. Add Phase 4 (US2) → TOTP linking works → validate → Demo
4. Add Phase 5 (US3) → All failure modes hardened → validate → SC-004 verified
5. Final Phase → Polish, docs, CI gates → ready for merge

### Constraint Reminder (SC-007)

No edits to any file under `Security/` in `Sorcha.UI.Components.User/Components/Security/`.
All new code is net-new files under `AccountLink/`. The only edits to shipped files are:
- `fragment-handoff.js` (additive extension)
- `Routes.razor` (one-line mount)

---

## Notes

- **[P]** tasks write to different files and have no dependency on incomplete sibling tasks
- **[US#]** label maps each task to its user story for traceability and independent testing
- F168 unit test boundary: mock `HttpMessageHandler` so all client service tests run without F168
- Playwright E2E tests (US1/US2/US3 journeys) are explicitly deferred in this task list pending F168 availability; add them once F168 is merged
- SC-004 (fail-closed) is verified structurally — terminal states (`Expired`, `Cancelled`, `Recovery`, `ConflictFailure`) have no code paths that call `ITokenCache.StoreTokenAsync` or `NotifyAuthenticationStateChanged`
- Reuse invariants: `PasskeyInteropService.GetCredentialAsync` (R4), existing `ChallengeMethod`/`ChallengeVerifyError` enums, `ITokenCache`, `CustomAuthenticationStateProvider` — no new ceremony code
