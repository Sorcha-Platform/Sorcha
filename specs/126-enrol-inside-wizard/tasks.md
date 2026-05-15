---
description: "Task list for Feature 126 — Sorcha Wallet enrolment inside a council application wizard"
---

# Tasks: Sorcha Wallet enrolment inside a council application wizard

**Input**: Design documents from `/specs/126-enrol-inside-wizard/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Tests ARE included. Sorcha's constitution requires >85% coverage on new code (Principle IV), the spec defines measurable success criteria requiring automated verification (SC-007, SC-008, SC-009), and FR-014 / FR-024 / FR-025 mandate specific test coverage.

**Organization**: Tasks are grouped by user story. Each story's checkpoint is a fully working, independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps to user stories from spec.md (US1–US5)
- All file paths are repo-rooted

## Path Conventions

Sorcha multi-project layout. Paths in this document are repo-rooted:
- Tenant Service: `src/Services/Sorcha.Tenant.Service/`
- Shared library: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`
- Council web shell: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- PWA: `src/Apps/Sorcha.Wallet.Pwa/`
- Tenant tests: `tests/Sorcha.Tenant.Service.Tests/`
- Library tests: `tests/Sorcha.UI.Core.Tests/`
- PWA tests: `tests/Sorcha.Wallet.Pwa.Tests/`
- E2E tests: `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/`
- Walkthroughs: `walkthroughs/Strathcarron/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm prerequisites and stand up the demo scaffold every story relies on.

- [ ] T001 Verify the Feature 124 + Feature 125 test suites pass on the freshly-created `126-enrol-inside-wizard` branch as the SC-009 regression baseline. Record the counts in tasks.md as a baseline comment.
- [ ] T002 [P] Create `walkthroughs/Strathcarron/` directory if absent and a stub `setup-cold-start-demo.ps1` script with parameters but no body — fleshed out across Phases 3-5.
- [ ] T003 [P] Add `Auth:ReturnToAllowlist:Hosts` configuration block to `src/Services/Sorcha.Tenant.Service/appsettings.json` + `appsettings.Development.json` with the development hosts (`localhost`, `n1.sorcha.dev`, `strathcarron.gov`, `*.strathcarron.gov`).

**Checkpoint**: Build clean, baseline recorded, configuration template in place. Ready for foundational work.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Server-side enrolment-session machinery, hub event, return-to validation, library-side tier probe. Every user story below depends on at least one of these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Server-side: enrolment session endpoints

- [ ] T004 [P] Create `EnrolSessionDtos.cs` in `src/Services/Sorcha.Tenant.Service/Models/` with the request/response shapes from `contracts/enrol-session.openapi.yaml`: `MintEnrolSessionResponse`, `RedeemEnrolSessionRequest`, `RedeemEnrolSessionResponse`, `RedeemEnrolSessionErrorBody`, `RedeemEnrolSessionErrorCode` enum.
- [ ] T005 Create `IEnrolSessionService.cs` + `EnrolSessionService.cs` in `src/Services/Sorcha.Tenant.Service/Services/`. Methods: `Task<MintEnrolSessionResponse> MintAsync(Guid platformUserId, CancellationToken ct)` + `Task<RedeemResult> RedeemAsync(string sessionToken, CancellationToken ct)`. Uses `IAtomicDistributedCache` from `Sorcha.AtomicCache` for single-use enforcement per research §R-003.
- [ ] T006 Wire JWT mint + signature validation in `EnrolSessionService` using the existing Tenant Service signing key (`ITokenService` injection). Claims per `data-model.md`: `{ sub, scope: "enrol", jti, iat, exp = iat + 600 }`. Verify on redeem: signature, expiry, scope; reject with the corresponding `RedeemEnrolSessionErrorCode`.
- [ ] T007 Add `EnrolSessionMetrics.cs` in `src/Services/Sorcha.Tenant.Service/Services/` exposing the three OpenTelemetry instruments per research §R-010 on a new `Sorcha.Enrolment` meter: `sorcha_enrol_session_minted_total`, `sorcha_enrol_session_redeemed_total{outcome}`, `sorcha_enrol_pairing_signal_latency_seconds` histogram.
- [ ] T008 Create `EnrolSessionEndpoints.cs` in `src/Services/Sorcha.Tenant.Service/Endpoints/`. Maps `POST /api/auth/enrol-session` (auth required, mints) and `POST /api/auth/enrol-session/redeem` (anonymous; consumes). Both rate-limited by `RateLimitPolicies.PlatformAuth`. Scalar OpenAPI annotations per existing pattern.
- [ ] T009 Register `IEnrolSessionService` + `EnrolSessionMetrics` in `Program.cs` for Sorcha.Tenant.Service. Add `app.MapEnrolSessionEndpoints()` call.
- [ ] T010 Add `AddAtomicDistributedCache(builder.Configuration, "Tenant")` to `Sorcha.Tenant.Service/Program.cs` if not already registered (re-use of Feature 113 primitive).

### Server-side: TenantHub.DeviceEnrolled event

- [ ] T011 [P] Extend `ITenantHubClient.cs` in `src/Services/Sorcha.Tenant.Service/Hubs/` with `Task DeviceEnrolled(Guid platformUserId, Guid deviceId)` method.
- [ ] T012 [P] Extend `TenantHubGroups.cs` to expose the existing per-user group helper (or add one) — `User(Guid platformUserId)` returning a stable group name like `"user:{platformUserId:N}"`.
- [ ] T013 Wire `DeviceEnrolled` publishing into `PlatformUserDeviceService.RegisterAsync` after a successful `await _db.SaveChangesAsync(ct)`. Inject `IHubContext<TenantHub, ITenantHubClient>`; call `Clients.Group(TenantHubGroups.User(platformUserId)).DeviceEnrolled(platformUserId, device.Id)`. Wrap in try/log/swallow so a hub failure cannot fail the device registration.

### Server-side: ?returnTo= on Feature 116 signup endpoints

- [ ] T014 [P] Create `ReturnToAllowlistOptions.cs` in `src/Services/Sorcha.Tenant.Service/Models/` with `IReadOnlyList<string> Hosts { get; init; }` and an `IsAllowed(string returnTo)` helper that parses the URL, applies the matcher rules from research §R-004 (https-only except `http://localhost`, exact-host or `*.host` suffix). Bind to `Auth:ReturnToAllowlist`.
- [ ] T015 Extend `AuthEndpoints.Login` + `AuthEndpoints.Signup` (whichever handle the post-completion redirect) in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` to accept an optional `?returnTo=<url>` query parameter. Validate via `ReturnToAllowlistOptions.IsAllowed`. On match, redirect to the supplied URL after success; on miss, fall back to the existing default landing redirect. Log the rejection at `Information` level (not warning — citizen-driven, not adversarial by default).

### Library-side: tier probe + pairing-signal abstractions

- [ ] T016 [P] Create `ITierProbeService.cs` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Enrolment/` with `Task<CitizenTier> ProbeAsync(CancellationToken ct)` and `enum CitizenTier { ColdStart, MiniGate, FastPath }`.
- [ ] T017 [P] Create `HttpTierProbeService.cs` in the same folder. Calls `GET /api/auth/whoami` + `GET /api/me/devices`; returns `ColdStart` on 401, `MiniGate` when device count is 0, `FastPath` when ≥1. 200 ms timeout on both calls.
- [ ] T018 [P] Create `IEnrolPairingSignal.cs` + `EnrolPairingSignal.cs` in the same folder. `event Func<Guid, Task>? OnDeviceEnrolled;` plus `Task StartAsync(Guid platformUserId, CancellationToken ct)` + `Task StopAsync()`. Wraps `TenantHubConnection` (primary) with a polling fallback that calls `GET /me/devices` every 3 s if hub doesn't connect within 2 s. Per research §R-006.

### Library-side: EnrolGateComponent skeleton

- [ ] T019 Create the folder structure `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/EnrolGate/` and an `EnrolGateComponent.razor` shell that injects `ITierProbeService` + `IEnrolPairingSignal`, holds the tier state, renders a `<RenderFragment>` ChildContent only when Tier 1 reached. No tier-specific UI yet — that's T021-T023.

### DI registration (library + PWA)

- [ ] T020 Register `ITierProbeService` → `HttpTierProbeService` and `IEnrolPairingSignal` → `EnrolPairingSignal` in the council web shell's `Program.cs` (`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs`). HttpClient configured against the API gateway.

### Cross-cutting tests (foundational)

- [ ] T021 [P] Tests: `EnrolSessionServiceTests.cs` in `tests/Sorcha.Tenant.Service.Tests/Services/` — mint claims, redeem single-use via `InMemoryAtomicDistributedCache`, expired token rejection, scope-mismatch rejection, replay rejection.
- [ ] T022 [P] Tests: `EnrolSessionEndpointsTests.cs` in `tests/Sorcha.Tenant.Service.Tests/Endpoints/` — `POST /api/auth/enrol-session` happy path + auth required, `POST /api/auth/enrol-session/redeem` happy path (200) + replay (409) + expired (410) + scope-mismatch (400).
- [ ] T023 [P] Tests: `TenantHubDeviceEnrolledTests.cs` in `tests/Sorcha.Tenant.Service.Tests/Hubs/` — group-filtering (only the target user's group receives), payload shape, idempotent re-firing for the same `(platformUserId, deviceId)`.
- [ ] T024 [P] Tests: `ReturnToAllowlistOptionsTests.cs` in `tests/Sorcha.Tenant.Service.Tests/Models/` — exact host match, `*.host` suffix match, `http://localhost` allowed, every other non-https rejected, malformed URL rejected, host not on allowlist rejected.
- [ ] T025 [P] Tests: `HttpTierProbeServiceTests.cs` in `tests/Sorcha.UI.Core.Tests/Services/User/Enrolment/` — 401 → ColdStart, 200 + empty devices → MiniGate, 200 + ≥1 device → FastPath.
- [ ] T026 [P] Tests: `EnrolPairingSignalTests.cs` in `tests/Sorcha.UI.Core.Tests/Services/User/Enrolment/` — SignalR path fires `OnDeviceEnrolled`, polling fallback fires after 2 s hub failure, manual recovery after 60 s.
- [ ] T027 Re-run full Tenant Service + UI.Core test suites. Confirm SC-009 baseline holds; all new foundational tests green.

**Checkpoint**: Server endpoints work, hub event fires, allowlist enforces, library probes + signals work. The wallet PWA + council page can compile against the new interfaces.

---

## Phase 3: User Story 1 — Sarah onboards from scratch (Priority: P1) 🎯 MVP

**Goal**: Cold-start citizen (Tier 3) completes the journey from "click Apply" to "form ready" with no prior Sorcha account or device.

**Independent Test**: Fresh-state browser session arrives at the council page → preflight signup surface → completes F116 signup → wallet-pairing surface renders → scans QR / taps link on phone → confirmation dialog → device-pairing ceremony → council page transitions to form within 2 s → fills + submits → credential lands in PWA wallet.

### Implementation for User Story 1

- [ ] T028 [P] [US1] Create `PreflightSignupSurface.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/EnrolGate/`. Renders the plain-English explainer + "Sign in or create your account" button that links to the F116 signup flow with `?returnTo=<currentUrl>`. No QR code at this stage (FR-004).
- [ ] T029 [P] [US1] Create `HybridQrAffordance.razor` in the same folder. Takes `QrUrl` + `ExpiresAt` parameters, renders the QR via `QRCoder` (server-side rendered to base64), the tap-able link prominent on mobile (`MediaQueryService.IsMobile`), and a copy-link affordance.
- [ ] T030 [P] [US1] Create `WalletPairingSurface.razor` in the same folder. Consumes `HybridQrAffordance`. Calls the mint endpoint at component init via injected HttpClient to obtain the session token; subscribes to `IEnrolPairingSignal.OnDeviceEnrolled`; renders the "Waiting for your phone…" status line and updates to "Phone ready ✓" on event fire.
- [ ] T031 [US1] Extend `EnrolGateComponent.razor` (skeleton from T019) to branch on `CitizenTier` and render `PreflightSignupSurface` for `ColdStart` then `WalletPairingSurface` for the post-signup return. Emits `OnReady` callback when Tier transitions to `FastPath`.
- [ ] T032 [US1] Create `Pages/CouncilApplicationDrivingLicence.razor` (or extend an existing page) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` that hosts `EnrolGateComponent` wrapping a `DrivingLicenceForm` placeholder. Demonstrates the consumer-side API.
- [ ] T033 [US1] Create `IEnrolSessionRedeemer.cs` + `EnrolSessionRedeemer.cs` in `src/Apps/Sorcha.Wallet.Pwa/Services/Enrolment/`. Calls `POST /api/auth/enrol-session/redeem` with the session token; returns `RedeemEnrolSessionResponse` on success, structured `RedeemEnrolSessionErrorCode` on failure.
- [ ] T034 [US1] Create `EnrolmentRedeemConfirmDialog.razor` in `src/Apps/Sorcha.Wallet.Pwa/Components/`. Takes `DisplayName` + `Email` parameters; renders the confirmation copy from FR-010 + spec acceptance scenario US1 #3; emits `OnConfirm` + `OnCancel` callbacks.
- [ ] T035 [US1] Extend `Pages/Enrol.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/` to accept `?session=<token>` query parameter. On load: call `IEnrolSessionRedeemer.RedeemAsync` → render `EnrolmentRedeemConfirmDialog` with returned `displayName` + `email` → on Confirm, store the returned access token via `IAccessTokenStore` and proceed with the existing F114 enrolment ceremony → on Cancel, navigate to a "this isn't me" landing.
- [ ] T036 [US1] Register `IEnrolSessionRedeemer` in `Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs` as a typed HttpClient targeting the API gateway with `ServerClockHandler` (no `BearerTokenHandler` — the redeem call is anonymous).
- [ ] T037 [US1] Add structured logging (Serilog) to `EnrolSessionService.MintAsync` + `RedeemAsync` per Sorcha conventions — no token contents in logs, only metadata (PlatformUserId, jti, outcome).
- [ ] T038 [US1] Flesh out `walkthroughs/Strathcarron/setup-cold-start-demo.ps1` per `quickstart.md` Walk 1: provision Strathcarron Council org, publish `DrivingLicence` blueprint with `targetAudience: "SorchaLocalWallet"`, generate `cold-start-<random>@example.test` test citizen.
- [ ] T039 [US1] Add the `?form=current` form-state preservation per FR-019: `EnrolGateComponent` writes the consumer page's form state (via a parameter `FormStatePersistenceKey`) into `sessionStorage` before the gate fires; restores on `OnReady`.

### Tests for User Story 1

- [ ] T040 [P] [US1] Tests: `PreflightSignupSurfaceTests.cs` in `tests/Sorcha.UI.Core.Tests/Components/EnrolGate/` — renders explainer copy, signup link carries `?returnTo`, NO QR code rendered.
- [ ] T041 [P] [US1] Tests: `WalletPairingSurfaceTests.cs` in `tests/Sorcha.UI.Core.Tests/Components/EnrolGate/` — calls mint at init, subscribes to `OnDeviceEnrolled`, transitions status line on event fire.
- [ ] T042 [P] [US1] Tests: `HybridQrAffordanceTests.cs` in `tests/Sorcha.UI.Core.Tests/Components/EnrolGate/` — QR rendered, tap-link prominent on mobile viewport (mock `MediaQueryService`), copy-link button copies to clipboard via JS interop mock.
- [ ] T043 [P] [US1] Tests: `EnrolSessionRedeemerTests.cs` in `tests/Sorcha.Wallet.Pwa.Tests/Services/Enrolment/` — happy path 200 returns `{ accessToken, displayName, email }`, 409 surfaces `AlreadyUsed`, 410 surfaces `Expired`, 400 surfaces `MalformedToken`/`ScopeMismatch`/`InvalidSignature`.
- [ ] T044 [P] [US1] Tests: `EnrolmentRedeemConfirmDialogTests.cs` in `tests/Sorcha.Wallet.Pwa.Tests/Components/` — renders `DisplayName` + `Email`, `OnConfirm`/`OnCancel` fire with no extra effects.
- [ ] T045 [US1] E2E: `ColdStartEnrolmentTests.cs` in `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/`. Tagged `[Demo("cold-start-enrolment")]`. Walks Walk 1 from `quickstart.md` against the Docker stack: arrival → preflight → signup → QR scan via second browser context → confirmation dialog confirms → device pairs → council page advances → form fills → submission → credential lands in PWA. Stopwatch asserts SC-001 (<90 s).

**Checkpoint**: Demo Beat 1 (cold-start onboarding) shippable. Tier 3 → Tier 1 transition works end-to-end. SC-001 + SC-007 verifiable.

---

## Phase 4: User Story 2 — Returning citizen fast-path (Priority: P1)

**Goal**: Returning citizen (Tier 1) lands on the form immediately after a single sign-in screen.

**Independent Test**: Pre-enrolled test account arrives at council page → sign-in → form (no QR, no enrolment surface).

### Implementation for User Story 2

- [ ] T046 [US2] Extend `EnrolGateComponent.razor` branching: when `ITierProbeService.ProbeAsync` returns `FastPath`, render `ChildContent` directly (the consumer page's form) and fire `OnReady` immediately. No intermediate UI.
- [ ] T047 [US2] Extend `setup-cold-start-demo.ps1` to provision the Tier 1 test citizen: signup via F116 + run the F114 device-pairing ceremony so the account starts with one active device. Email: `returning-<random>@example.test`.

### Tests for User Story 2

- [ ] T048 [P] [US2] Tests: `EnrolGateComponentTests.cs` in `tests/Sorcha.UI.Core.Tests/Components/EnrolGate/` — when probe returns `FastPath`, `OnReady` fires synchronously and `ChildContent` renders.
- [ ] T049 [US2] E2E: `ReturningCitizenFastPathTests.cs` in `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/`. Walks Walk 2 from `quickstart.md` — verifies SC-002 (no QR, no enrolment surface visible) and stopwatch asserts <30 s click-Apply-to-form-ready.

**Checkpoint**: Fast-path verifiable. SC-002 + SC-007 verifiable.

---

## Phase 5: User Story 3 — Lost-phone mini-gate (Priority: P2)

**Goal**: Citizen with account but no active device (Tier 2) sees the wallet-pairing surface without a signup re-prompt.

**Independent Test**: Test account with all devices revoked arrives at council page → sign-in → mini-gate (QR with "Let's pair this device" copy) → pairing → form.

### Implementation for User Story 3

- [ ] T050 [US3] Extend `WalletPairingSurface.razor` to accept a `TierMode` parameter (`MiniGate` vs `PostSignup`) that selects copy. MiniGate copy: "Let's pair this device with your wallet" (FR-003 per-tier copy spec). PostSignup copy: "Almost there — open your wallet to receive your credential." Same hybrid QR component underneath.
- [ ] T051 [US3] Wire `EnrolGateComponent.razor` to render `WalletPairingSurface` with `TierMode="MiniGate"` when probe returns `MiniGate`.
- [ ] T052 [US3] Extend `setup-cold-start-demo.ps1` to provision the Tier 2 test citizen: F116 signup completed, no device-pairing run. Email: `mini-gate-<random>@example.test`.

### Tests for User Story 3

- [ ] T053 [P] [US3] Tests: extend `EnrolGateComponentTests.cs` (T048) — when probe returns `MiniGate`, renders `WalletPairingSurface` with `TierMode="MiniGate"`; no signup surface visible.
- [ ] T054 [P] [US3] Tests: extend `WalletPairingSurfaceTests.cs` (T041) — MiniGate mode renders the right copy; PostSignup mode renders the other copy; same QR underneath.
- [ ] T055 [US3] E2E: `MiniGateEnrolmentTests.cs` in `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/`. Walks Walk 3 from `quickstart.md` — verifies SC-003 (no signup surface visible at any point) and pairing-to-form transition.

**Checkpoint**: Mini-gate verifiable. SC-003 verifiable. Lost-phone recovery path covered.

---

## Phase 6: User Story 4 — Stranger scans QR (Priority: P2)

**Goal**: An unintended second party who opens the enrolment URL sees the confirmation dialog with the bound user's email + display name; cancelling leaves no device registered.

**Independent Test**: Generate session-token URL on Browser A's account, open the URL on Browser B → confirmation dialog renders Browser A's user details → cancel → `GET /me/devices` on Browser A's account returns empty.

### Implementation for User Story 4

- [ ] T056 [US4] Tighten the confirmation copy in `EnrolmentRedeemConfirmDialog.razor` to exactly match the spec wording: "You're about to enrol this device for `{email}` ({displayName}). If that's not you, close this page." Add a "this isn't me" button alongside Confirm; route the citizen back to a `/wallet/cancelled-enrolment` landing on Cancel.
- [ ] T057 [US4] Create `Pages/CancelledEnrolment.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/` — empty-state-with-CTA from Feature 125 PR-F's `EmptyStateWithCta` component. Copy: "Looks like that link wasn't for you. Close this page when you're done."

### Tests for User Story 4

- [ ] T058 [P] [US4] Tests: extend `EnrolmentRedeemConfirmDialogTests.cs` (T044) — Cancel button fires `OnCancel`, NOT `OnConfirm`. Confirm copy includes the exact "if that's not you, close this page" wording.
- [ ] T059 [P] [US4] Tests: extend `EnrolSessionEndpointsTests.cs` (T022) — redeem on Browser B's session returns the BOUND user's `displayName` + `email`, NOT the redeemer's. The redeem call does NOT register a device on Browser B's auth context.
- [ ] T060 [US4] E2E: `StrangerScansQrTests.cs` in `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/`. Mints a session URL on browser context A, opens it on browser context B (a different signed-in account). Verifies the confirmation dialog renders A's details; Cancel; verifies `GET /me/devices` on account A returns 0 active devices. Verifies SC-008.

**Checkpoint**: Friend-scans-by-mistake mitigation verifiable. SC-008 covered.

---

## Phase 7: User Story 5 — Same-device tap-link (Priority: P3)

**Goal**: Citizen on a mobile browser sees the tap-link more prominently than the QR; tapping the link opens the same enrolment URL on the same device.

**Independent Test**: Mobile-emulated browser session arrives at the council page during the wallet-pairing surface; the tap-link is the dominant affordance; tapping it proceeds through the same confirmation + pairing flow.

### Implementation for User Story 5

- [ ] T061 [US5] Update `HybridQrAffordance.razor` (T029) to render the tap-link above the QR on mobile viewports, with the QR collapsed into a "Show QR code" expander; on desktop, QR is dominant and the tap-link sits below it. Driven by `MediaQueryService.IsMobile`.
- [ ] T062 [US5] Add layout-adaptation parameters to `HybridQrAffordance.razor` (`Layout` + sensible defaults) per the Feature 125 §10 form-factor convention.

### Tests for User Story 5

- [ ] T063 [P] [US5] Tests: extend `HybridQrAffordanceTests.cs` (T042) — mobile viewport renders tap-link as the dominant affordance, QR behind an expander; desktop viewport renders QR dominantly with tap-link below; explicit `Layout` parameter overrides the auto-detection.

**Checkpoint**: Same-device path universal. SC-001 demonstrably works on a mobile-only session.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: SC verification, documentation propagation, audits.

### Failure-path polish

- [ ] T064 [P] Add the regenerate affordance to `WalletPairingSurface.razor`: status flips to "QR expired — let's get you a new one" when the session token's `expiresAt` passes without `OnDeviceEnrolled` firing; calls `POST /api/auth/enrol-session` to mint a fresh token. Per FR-017 / FR-018.
- [ ] T065 [P] Add the manual-recovery affordance to `WalletPairingSurface.razor`: after 60 s of no `OnDeviceEnrolled` (covering both SignalR + polling failure), surfaces "I've enrolled — continue" button that re-runs the tier probe and advances if the probe now reports `FastPath`. Per FR-016 / SC-005.
- [ ] T066 [P] Add the `ErrorRecoveryScaffold` (from Feature 125 PR-F) to `Pages/Enrol.razor` for the redeem error paths (`expired`, `already_used`, `malformed_token`) so each surfaces a user-safe recovery action.

### Tests for the polish phase

- [ ] T067 [P] Tests: extend `WalletPairingSurfaceTests.cs` — regenerate-on-expiry path, manual-recovery affordance after 60 s.
- [ ] T068 [P] Tests: extend `EnrolPairingSignalTests.cs` (T026) — manual-recovery affordance surfaces after 60 s of no signal.

### Documentation propagation

- [ ] T069 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with a new section: "Council application enrolment gate (Feature 126)". Document the three citizen tiers, the EnrolGateComponent, the two new Tenant Service endpoints, the new `TenantHub.DeviceEnrolled` event, the `?returnTo=` allowlist mechanic, and the session-token security properties.
- [ ] T070 [P] Update `.claude/skills/sorcha-ui/SKILL.md` with the new library components (`EnrolGateComponent`, `PreflightSignupSurface`, `WalletPairingSurface`, `HybridQrAffordance`) and the form-data preservation convention (sessionStorage keyed by form id + browser session id).
- [ ] T071 [P] Update `docs/reference/API-DOCUMENTATION.md` with the two new enrolment endpoints (link to `contracts/enrol-session.openapi.yaml`).
- [ ] T072 [P] Update `.specify/MASTER-TASKS.md` with the Feature 126 entry under "Completed Features (not in themes above)".
- [ ] T073 [P] Add an `### EnrolGate (Feature 126)` section to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/README.md` explaining the component's consumer-side API + the `OnReady` callback contract.

### Audits

- [ ] T074 Run `dotnet test` across all touched test projects. Confirm SC-009: F124 + F125 baselines preserved; all new Feature 126 tests green.
- [ ] T075 Manual smoke-test against `n1.sorcha.dev/strathcarron`: walk all three demo journeys from `quickstart.md`. Verify SC-001 + SC-002 + SC-003 timings.
- [ ] T076 Build clean: `dotnet build Sorcha.sln` with 0 errors. No new compiler warnings on Release builds (Constitution V).

### Final verification

- [ ] T077 Run the full `specs/126-enrol-inside-wizard/quickstart.md` runbook end-to-end. Record SC-001..SC-009 outcomes in the PR description or as a follow-up comment.
- [ ] T078 Tag the merge commit `spec-126-complete` once master receives the feature.
- [ ] T079 Update `MEMORY.md > Current Branch` section with the Feature 126 shipped state.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No prerequisites — runs first.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories.** Within Phase 2:
  - T004 (DTOs) before T005 (service impl) before T006 (JWT mint/redeem) before T008 (endpoints).
  - T011–T012 (hub interface + groups) before T013 (publishing wiring).
  - T014 (allowlist options) before T015 (signup endpoint extension).
  - T016 (tier probe interface) before T017 (HTTP impl).
  - T018 (pairing signal) independent of all others.
  - T019 (gate skeleton) independent.
  - T020 (DI) depends on T017 + T018.
  - T021–T026 ([P] tests) can run as soon as their target lands.
  - T027 (regression run) is the gate to leave Phase 2.
- **User Stories (Phase 3–7)**: All depend on Foundational completion. Within each story, `[P]` markers indicate parallel execution.
- **Polish (Phase 8)**: Depends on the user stories whose behaviour it polishes; tests + docs can run in parallel.

### User Story Dependencies

- **US1 (Cold-start)**: depends on full Phase 2. Independent of US2/US3/US4/US5.
- **US2 (Fast path)**: depends on T031 (EnrolGateComponent tier branching) from US1. Tier-1 branch is the smallest add.
- **US3 (Mini-gate)**: depends on T030 (WalletPairingSurface) from US1. Adds a TierMode parameter.
- **US4 (Stranger scans)**: depends on T034 (EnrolmentRedeemConfirmDialog) from US1. Adds cancel-routing.
- **US5 (Same-device)**: depends on T029 (HybridQrAffordance) from US1. Adds mobile-prominence parameters.

### Parallel Opportunities

- **Phase 2 parallel batch (after T009)**: T011, T012, T014, T016, T017, T018, T021, T023, T024, T025, T026 — eleven tasks across different files.
- **US1 parallel batch**: T028, T029, T030, T040, T041, T042, T043, T044 — eight tasks across different files. T031 + T032 + T033 + T034 + T035 sequential due to integration.
- **US3 parallel batch**: T053, T054 — two test tasks; T050 + T051 sequential.
- **US4 parallel batch**: T058, T059 — two test tasks; T056 + T057 sequential.
- **Polish parallel batch**: T064, T065, T066, T067, T068, T069, T070, T071, T072, T073 — ten tasks across different files.

---

## PR-shaped delivery

Single PR covers Phase 1 + Phase 2 + US1 (the headline beat) as the MVP increment. Subsequent PRs layer US2 → US3 → US4 → US5 → Polish.

| PR | Phase coverage | Tasks |
|---|---|---|
| **PR-A** | Phase 1 + Phase 2 + Phase 3 (US1) | T001–T045. Foundations + cold-start MVP. |
| **PR-B** | Phase 4 (US2) + Phase 5 (US3) | T046–T055. Fast-path + mini-gate. |
| **PR-C** | Phase 6 (US4) + Phase 7 (US5) | T056–T063. Security mitigation + mobile prominence. |
| **PR-D** | Phase 8 polish | T064–T079. Failure-path polish + docs + final verification. |

PR-A gates on SC-001 + SC-007 + SC-009. PR-B gates on SC-002 + SC-003. PR-C gates on SC-008. PR-D gates on SC-005 + SC-006 + the final quickstart walk.

---

## Implementation Strategy

### MVP First

The smallest demoable MVP is the cold-start journey (US1) plus the foundational server surface. After PR-A, you can demonstrate Beat 1 (Sarah onboards from scratch) end-to-end.

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: US1 cold-start
4. **STOP and VALIDATE**: Walk the cold-start journey on Docker (`quickstart.md` Walk 1).

US2 (fast-path), US3 (mini-gate), US4 (stranger-scans), US5 (same-device), Polish layer on after the MVP.

### Incremental Delivery

1. After PR-A: cold-start onboarding works; the headline beat demoable. SC-001 / SC-007 / SC-009 verifiable.
2. After PR-B: fast-path + mini-gate visible. SC-002 / SC-003 verifiable.
3. After PR-C: friend-scans-by-mistake mitigation + mobile prominence. SC-008 verifiable.
4. After PR-D: polish, audits, docs, n1 deploy. SC-004 / SC-005 / SC-006 verifiable.

### Parallel Team Strategy

With two developers:

1. Both drive Phase 2 (Foundational) together — server-side endpoints + library probe primitives are the cornerstone.
2. After PR-A merges:
   - Developer A: PR-B (US2 + US3) — both add small branches to existing components.
   - Developer B: PR-C (US4 + US5) — security mitigation + mobile polish.
3. Either developer takes PR-D (polish).

---

## Notes

- All file paths are exact. No `src/[location]/[file]` placeholders remain.
- Pre-release migration squash is N/A — Feature 126 adds no EF entities.
- Each E2E test is tagged with a Demo attribute so the demo verification can run only those tests as a group (e.g., `dotnet test --filter "Demo"`).
- The `?returnTo=` allowlist (T014/T015) is a small extension to the existing Feature 116 signup endpoints; the F116 mechanics themselves stay untouched.
- The session-token redeem call is anonymous (no `BearerTokenHandler` on the redeem HttpClient) — the token IS the authentication for that single call.
