---
description: "Task list for Feature 125 — Sorcha Wallet (Full User-Agent v1)"
---

# Tasks: Sorcha Wallet (Full User-Agent v1)

**Input**: Design documents from `/specs/125-sorcha-wallet-user-agent/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Tests ARE included. Sorcha's constitution requires >85% coverage on new code (Principle IV), the spec defines measurable success criteria (SC-006, SC-007, SC-008) requiring automated verification, and FR-034/FR-035/FR-036 mandate specific test coverage.

**Organization**: Tasks are grouped by user story. Each story's checkpoint is a fully working, independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps to user stories from spec.md (US1–US6)
- All file paths are repo-rooted

## Path Conventions

Sorcha multi-project layout. Paths in this document are repo-rooted:
- PWA: `src/Apps/Sorcha.Wallet.Pwa/` (was `Sorcha.Citizen.Wallet/`)
- Verifier: `src/Apps/Sorcha.Verifier/` (was `Sorcha.Citizen.Verifier/`)
- Shared library: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`
- Tenant Service: `src/Services/Sorcha.Tenant.Service/`
- Wallet Service: `src/Services/Sorcha.Wallet.Service/`
- PWA tests: `tests/Sorcha.Wallet.Pwa.Tests/` (was `Sorcha.Citizen.Wallet.Tests/`)
- Library tests: `tests/Sorcha.UI.Core.Tests/`
- E2E tests: `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/`
- Walkthroughs: `walkthroughs/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm prerequisites for the rest of the work. Sorcha's infrastructure is already in place — this phase is small.

- [ ] T001 Verify the F124 test suites pass on the freshly-created `125-sorcha-wallet-user-agent` branch as a regression baseline. Record the counts in tasks.md as a baseline comment.
- [ ] T002 [P] Create `walkthroughs/Strathcarron/` directory if absent. Set up empty scaffold for the two new walkthrough scripts the spec requires (doorstep-demo, multi-context-demo).

**Checkpoint**: Build clean, F124 regression baseline recorded. Ready for foundational work.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The project rename, cross-cutting abstractions, and the persona-schema delta. Every user story below depends on at least one of these. This phase = PR-A in the plan.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Rename (PR-A core)

- [ ] T003 Rename project `src/Apps/Sorcha.Citizen.Wallet/` → `src/Apps/Sorcha.Wallet.Pwa/`. Update the `.csproj`, every `using Sorcha.Citizen.Wallet.*` reference, every namespace declaration, project references in solution and test projects.
- [ ] T004 Rename project `src/Apps/Sorcha.Citizen.Verifier/` → `src/Apps/Sorcha.Verifier/`. Same approach as T003.
- [ ] T005 Rename test project `tests/Sorcha.Citizen.Wallet.Tests/` → `tests/Sorcha.Wallet.Pwa.Tests/`. Update solution file. All existing tests must pass post-rename.
- [ ] T006 Rename test project `tests/Sorcha.Citizen.Verifier.Tests/` → `tests/Sorcha.Verifier.Tests/`. Same approach.
- [ ] T007 Update `docker-compose.yml`, `docker-compose.n1.yml`, `docker-compose.ports.yml`: rename services `sorcha-citizen-wallet` → `sorcha-wallet-pwa`, `sorcha-citizen-verifier` → `sorcha-verifier`. Update image references `sorchadev/citizen-wallet` → `sorchadev/wallet-pwa`, `sorchadev/citizen-verifier` → `sorchadev/verifier`.
- [ ] T008 Update CI workflow files (`.github/workflows/`) for the new container image names.
- [ ] T009 Update user-visible app name in `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html` (`<title>`), `manifest.webmanifest`, the AppBar `MudText`, and any in-page copy referring to "Citizen Wallet" — all become "Sorcha Wallet".
- [ ] T010 [P] Update `CLAUDE.md`, `walkthroughs/AssuredIdentity/README.md`, and any other docs that reference "Citizen Wallet" — search-and-replace audit; replace user-visible references with "Sorcha Wallet". Leave historical references in spec docs unchanged.
- [ ] T011 [P] Update `.claude/skills/sorcha-architecture/SKILL.md`, `.claude/skills/sorcha-ui/SKILL.md`: reference new namespaces where appropriate; add a note in each that the rename happened in Feature 125 with link to spec.
- [ ] T012 Run `dotnet build` and `dotnet test` for the full solution. Verify all F124 tests pass (SC-006 baseline).

### Abstractions and stores (PR-A)

- [ ] T013 [P] Create `IUserSigner` interface + `UserCustodyMode` enum + `SigningRequest` / `SigningResult` records in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Signing/IUserSigner.cs` per `data-model.md`.
- [ ] T014 [P] Create `IEphemeralVerifierIdentityService` interface + skeleton implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Signing/IEphemeralVerifierIdentityService.cs` per `data-model.md` / R-006.
- [ ] T015 Create `ManagedUserSigner` (the v1 implementation of `IUserSigner`) in `src/Apps/Sorcha.Wallet.Pwa/Services/Signing/ManagedUserSigner.cs`. Wraps existing `IDeviceKeyService` + Wallet Service delegation flow. (Depends on T013)
- [ ] T016 [P] Create `IActiveContextStore` interface + `InMemoryActiveContextStore` + `IndexedDbActiveContextStore` in `src/Apps/Sorcha.Wallet.Pwa/Services/IActiveContextStore.cs` per the F114 / F124 pattern.
- [ ] T017 [P] Extend `WalletFlagsRecord` in `src/Apps/Sorcha.Wallet.Pwa/Services/IWalletFlagsStore.cs` with `TourDismissedAt: DateTimeOffset?` per `data-model.md`. Add migration logic for any existing record (legacy field absent → null).
- [ ] T018 [P] Create `IPerContextPersonaCache` interface + InMemory + IndexedDb impls in `src/Apps/Sorcha.Wallet.Pwa/Services/IPerContextPersonaCache.cs`.
- [ ] T019 [P] Create `IVerificationHistoryStore` interface + InMemory + IndexedDb impls + `VerificationRecord` record + `VerifyOutcome` enum in `src/Apps/Sorcha.Wallet.Pwa/Services/IVerificationHistoryStore.cs` per `data-model.md`.

### Persona schema delta (PR-A)

- [ ] T020 Add `ContextOrgId` nullable column to `PlatformUserPersona` entity in `src/Services/Sorcha.Tenant.Service/Models/PlatformUserPersona.cs`. Composite key becomes `(PlatformUserId, ContextOrgId)`. Existing rows get `ContextOrgId = null` (Personal context).
- [ ] T021 Squash the new column into the InitialCreate migration per the pre-release migration-squash rule from `feedback_migration_squash` memory. Update `TenantDbContextModelSnapshot.cs` to match.
- [ ] T022 Update `IPlatformUserPersonaService` and the repository in `src/Services/Sorcha.Tenant.Service/Services/` to accept an optional `ContextOrgId` parameter on Get/Save/Delete. Null `ContextOrgId` means Personal context.
- [ ] T023 Extend `PersonaEndpoints.cs` in `src/Services/Sorcha.Tenant.Service/Endpoints/` with the optional `?context=<orgId>` query parameter on GET/PUT/DELETE per `contracts/per-context-persona.openapi.yaml`. 403 if the caller's JWT lacks an OrgMembership for the requested context.
- [ ] T024 Update the Wallet Service's `/api/v1/wallets/{address}/persona/encrypt` and `/decrypt` endpoints to accept the context parameter (they call the persona service; no encryption-envelope change).

### DI registration (PR-A)

- [ ] T025 Register `IUserSigner` → `ManagedUserSigner`, `IEphemeralVerifierIdentityService`, `IActiveContextStore`, `IPerContextPersonaCache`, `IVerificationHistoryStore` in `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`.

### Cross-cutting tests (PR-A)

- [ ] T026 [P] Tests: `IUserSignerContractTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/Signing/` — assert `ManagedUserSigner` honours the contract (returns `Managed` custody mode, signs payloads under the active context, propagates errors via `SigningResult`).
- [ ] T027 [P] Tests: `IActiveContextStoreTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/` — round-trip for `InMemoryActiveContextStore`; falls back to Personal when stored context invalidates.
- [ ] T028 [P] Tests: `PersonaEndpointsContextTests` in `tests/Sorcha.Tenant.Service.Tests/Endpoints/` — 200 on valid context, 403 on context the caller doesn't hold, 200 on omitted context (defaults to Personal).
- [ ] T029 [P] Tests: `PerContextPersonaCacheTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/` — caches per-context, refreshes on edit, flushes on sign-out.
- [ ] T030 Re-run full test suite. Confirm SC-006: F124 suites green; all new foundational tests green.

**Checkpoint**: Foundation ready. PR-A can merge. The rename is complete, abstractions in place, persona schema extended, all existing tests still pass. Wallet still looks and behaves exactly like the post-F124 wallet; user-visible UX hasn't changed yet.

---

## Phase 3: User Story 1 — Doorstep verification (Priority: P1) 🎯 MVP

**Goal**: Margaret-the-elderly-homeowner can verify the gas engineer's credential at her door in under 30 seconds, starting from a closed wallet. Wallet adds a Verify hero action and a full doorstep verification flow.

**Independent Test**: Start from a wallet with no credentials of its own, scan a generated test QR or NFC tag carrying a known credential, observe the trust panel render the correct verdict. Doesn't require the citizen to hold any credentials, doesn't require submission flows, doesn't require multiple contexts.

### Implementation for User Story 1

- [ ] T031 [P] [US1] Create `IQrScannerService` interface in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Capture/IQrScannerService.cs`. PWA-side: bridges to a JS QR-detection library (preferred: `jsQR` or `qr-scanner` — research a small footprint option in T032).
- [ ] T032 [P] [US1] Bundle the chosen QR-detection JS library into `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/qr-scanner-bridge.js`. Expose `SorchaQr.scan` to .NET via JSRuntime. Keep bundle weight < 30 KB minified.
- [ ] T033 [P] [US1] Create `INfcReaderService` interface in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Capture/INfcReaderService.cs`. Wraps Web NFC API (Chromium-Android only). Reports unavailability gracefully via `IsSupported` property.
- [ ] T034 [P] [US1] Implement `WebNfcReaderService` in `src/Apps/Sorcha.Wallet.Pwa/Services/Capture/WebNfcReaderService.cs` and the JS bridge `wwwroot/js/web-nfc-bridge.js`.
- [ ] T035 [US1] Create `EphemeralVerifierIdentityService` implementation (fills out T014's interface) in `src/Apps/Sorcha.Wallet.Pwa/Services/Signing/EphemeralVerifierIdentityService.cs`. Generates per-session EC P-256 via WebCrypto, computes RFC 7638 thumbprint as `client_id`, zeroises on dispose.
- [ ] T036 [US1] Create `VerifyOutcome` enum + `VerificationResult` record in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/VerificationResult.cs` (separate from the persisted `VerificationRecord` — this is the live result type passed to UI).
- [ ] T037 [US1] Create `IVerifierEngine` interface + `VerifierEngine` implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Verification/`. Wraps `VerifiablePresentationValidator` + `IIssuerKeyResolver` + status-list check. Same engine `Sorcha.Verifier` uses, lifted into the library so both shells share it.
- [ ] T038 [US1] Create `VerifyFlow` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerifyFlow.razor`. Orchestrates: open scanner (NFC if available else QR) → receive presentation → call `IVerifierEngine.VerifyAsync` → display result via existing `VerificationTrustView` → persist `VerificationRecord` via `IVerificationHistoryStore`.
- [ ] T039 [US1] Create `VerifyHomeAction` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/VerifyHomeAction.razor`. The hero "Verify a credential" tile; tapping opens `VerifyFlow`.
- [ ] T040 [US1] Add `Pages/Verify.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor` — full-page host for `VerifyFlow` when launched from the hero action.
- [ ] T041 [US1] Wire `VerifyHomeAction` into `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor`'s Home layout (placeholder location until Home IA rebuild in US3; the action goes top-of-page for now).
- [ ] T042 [US1] Register `IQrScannerService`, `INfcReaderService`, `IVerifierEngine` in `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`.
- [ ] T043 [US1] Add OpenTelemetry counter `sorcha_wallet_verification_total{outcome ∈ {pass,warn,fail}}` to `VerifierEngine`.
- [ ] T044 [US1] Add `ErrorRecoveryScaffold` plumbing into `VerifyFlow` for the four failure paths (network unreachable, issuer unverifiable, revoked credential, QR/NFC scan timeout). Each path gets a plain-English message + recovery action per FR-013, FR-031.
- [ ] T045 [US1] Add `walkthroughs/Strathcarron/setup-doorstep-demo.ps1` per `quickstart.md` Beat 1: provision Caledonian Water org + Liam Buchanan PlatformUser + WaterEngineer/v1 credential.

### Tests for User Story 1

- [ ] T046 [P] [US1] Unit tests `VerifierEngineTests` in `tests/Sorcha.UI.Core.Tests/Services/Verification/`. Cover pass / warn / fail paths against fixture credentials.
- [ ] T047 [P] [US1] Unit tests `EphemeralVerifierIdentityServiceTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/Signing/`. Cover key generation, thumbprint computation, dispose-zeroises.
- [ ] T048 [P] [US1] Component test `VerifyFlowTests` in `tests/Sorcha.UI.Core.Tests/Components/Verify/`. Mocks `IQrScannerService` to inject a known presentation; mocks `IVerifierEngine` to return each outcome; asserts UI renders correctly per outcome.
- [ ] T049 [US1] E2E `DoorstepVerificationTests` in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/DoorstepVerificationTests.cs`. Tagged `[Demo("doorstep-verify")]`. Walks Beat 1 from `quickstart.md` end-to-end.

**Checkpoint**: US1 functional. A presenter can demo Beat 1 standalone. Wallet now has Present (existing) + Verify (new) hero actions in the Home layout, even though Home itself hasn't been rebuilt yet.

---

## Phase 4: User Story 2 — Application from phone (Priority: P1)

**Goal**: Sarah can submit a multi-page council application from her phone — including portrait camera capture and persona autofill — in under 5 minutes.

**Independent Test**: Open a wallet that holds an Assured Identity, tap into the application surface, complete the form, take a selfie, submit. The credential issuance that follows is exercised by Feature 124's existing flow; this story ends at submission.

### Implementation for User Story 2

- [ ] T050 [P] [US2] Create `IWebCameraService` interface in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Capture/IWebCameraService.cs`. Methods: `OpenCameraAsync`, `CaptureFrameAsync`, `ResizeToJpegTokenAsync` (client-side resize to 240×320 JPEG per F107's `embedAs: "image-token-jpeg-240x320"`).
- [ ] T051 [P] [US2] Implement `WebCameraService` in `src/Apps/Sorcha.Wallet.Pwa/Services/Capture/WebCameraService.cs` + JS bridge `wwwroot/js/webcamera-bridge.js`. Uses Web Camera API; full-screen capture; resize via canvas.
- [ ] T052 [US2] Create `PortraitCaptureControl` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Capture/PortraitCaptureControl.razor`. Full-screen capture overlay; retake; embed-as-token output. Mobile-camera-first; falls back to file upload on desktop or when camera unavailable.
- [ ] T053 [US2] Register `PortraitCaptureControl` with the existing `FileRenderer` in `Sorcha.UI.Components.User/Components/Forms/Controls/` so that fields with `x-file.capture: "user"` and `embedAs: "image-token-jpeg-240x320"` route to `PortraitCaptureControl` automatically.
- [ ] T054 [P] [US2] Create `Pages/Applications.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Applications.razor`. Lists blueprint-driven applications available to the active context with starting actions the user is eligible for. Empty state with CTA when no applications available.
- [ ] T055 [US2] Create `Pages/ApplicationInstance.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor`. Hosts `SorchaFormRenderer` for a blueprint instance; orchestrates: create instance → render form → submit action → show in-progress state.
- [ ] T056 [US2] Wire `PersonaAutofillResolver` (existing in `Sorcha.UI.Components.User`) into the PWA's form-rendering surface. Resolver fetches per-context persona via `IPerContextPersonaCache` from Phase 2.
- [ ] T057 [US2] Wire `IUserSigner` (from Phase 2) into the action-submission flow. The submission payload is signed via `IUserSigner.SignAsync` with operation `ActionSubmission` and the active context id.
- [ ] T058 [US2] Reuse F124's pending-application notice mechanism. When `Pages/ApplicationInstance.razor` submits, post a notice for the issuing application label so Home's Needs-attention band picks it up.
- [ ] T059 [US2] Add OpenTelemetry counter `sorcha_wallet_application_submission_total{outcome ∈ {success,validation-failed,server-error}}` to `Pages/ApplicationInstance.razor`'s submission path.
- [ ] T060 [US2] Add `ErrorRecoveryScaffold` plumbing for submission failures (camera permission denied, session expired mid-submission, network drop). Each path preserves form data per FR-019.
- [ ] T061 [US2] Add Home-side surface: in `Pages/Index.razor` (still pre-Home-rebuild), surface "Recommended for you" applications based on existing credentials and active context.

### Tests for User Story 2

- [ ] T062 [P] [US2] Unit tests `WebCameraServiceTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/Capture/`. Mock JSRuntime to verify the bridge call shapes.
- [ ] T063 [P] [US2] Component test `PortraitCaptureControlTests` in `tests/Sorcha.UI.Core.Tests/Components/Capture/`. Verify retake, resize, embed-token-output shape.
- [ ] T064 [P] [US2] Integration test `ApplicationInstanceTests` in `tests/Sorcha.Wallet.Pwa.Tests/Pages/`. Stub the blueprint service; verify form-data → submission → in-progress state.
- [ ] T065 [US2] E2E `ApplicationFromPhoneTests` in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/ApplicationFromPhoneTests.cs`. Tagged `[Demo("application-from-phone")]`. Walks Beat 2 from `quickstart.md` end-to-end. Uses a fixed-image test mode for the camera step.

**Checkpoint**: US2 functional. Demo Beat 2 standalone. Sarah can submit an application from her phone, persona auto-fills, portrait camera captures and resizes, submission lands and Home shows the in-progress state via F124's pending-application notice.

---

## Phase 5: User Story 3 — Context switching (Priority: P1)

**Goal**: Ben can switch between Personal and Caledonian Builders contexts in 2 taps; all visible content reflects the new context within 1 second.

**Independent Test**: Single test account with two memberships and ≥1 credential per context. Switch context, observe content swap; switch back, observe revert. Doesn't require US1 verify or US2 application flows.

### Implementation for User Story 3 — Home IA rebuild + multi-context UI

- [ ] T066 [US3] Create `IUserContext` interface + `ManagedUserContext` implementation in `src/Apps/Sorcha.Wallet.Pwa/Services/Context/`. Holds the active context id, exposes `OnContextChanged` event, reads/writes `IActiveContextStore` from Phase 2.
- [ ] T067 [US3] Register `IUserContext` as a Singleton in the PWA's DI. Components consume via injection.
- [ ] T068 [US3] Create `ContextChipSwitcher` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Context/ContextChipSwitcher.razor`. Renders the active-context chip; tap opens bottom-sheet picker; switch triggers `/auth/switch-org` + token-store update + `IUserContext.SetActiveContextAsync`.
- [ ] T069 [US3] Wire `ContextChipSwitcher` into `src/Apps/Sorcha.Wallet.Pwa/MainLayout.razor` top bar — always visible, indicates active context, tappable when user has ≥2 memberships.
- [ ] T070 [P] [US3] Create `PresentHomeAction` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/PresentHomeAction.razor`. Hero "Present a credential" tile; tap → if 1 credential, opens it; if 2+, opens picker.
- [ ] T071 [P] [US3] Create `NeedsAttentionBand` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/NeedsAttentionBand.razor`. Renders pending applications, expiring credentials, action-required items. Hides when empty.
- [ ] T072 [P] [US3] Create `RecentActivityFeed` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/RecentActivityFeed.razor`. Last 3-5 events; tap → full Activity page.
- [ ] T073 [P] [US3] Create `ContextPeekFooter` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Context/ContextPeekFooter.razor`. Renders the "+ N credentials in another context" hint. Hidden when user has zero content in non-active contexts.
- [ ] T074 [US3] Rebuild `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor` to compose the library components: ContextChipSwitcher → PresentHomeAction + VerifyHomeAction (hero row) → NeedsAttentionBand (conditional) → Your-credentials band → RecentActivityFeed → ContextPeekFooter. Use `CredentialCardList` from the library instead of inline MudCard reimplementation (closes FR-001-implicit drift from PR #698 era).
- [ ] T075 [US3] Wire `IUserContext.OnContextChanged` to refresh Home content: re-fetch credentials, recent activity, applications, persona for the new context. Target: full refresh within 1 second per SC-003.
- [ ] T076 [US3] Add per-context filtering to `CitizenWalletClient` (rename: keep the type name but make it context-aware via the active JWT — the server enforces, the client just sends the right bearer token).
- [ ] T077 [US3] Add `walkthroughs/Strathcarron/setup-multi-context-demo.ps1` per `quickstart.md` Beat 3: provision Caledonian Builders Ltd + Ben's OrgMembership + SiteSafetyCert/v1.
- [ ] T078 [US3] Add OpenTelemetry counter `sorcha_wallet_context_switch_total{from,to}` to `IUserContext.SetActiveContextAsync`.
- [ ] T079 [US3] Edge case: when an in-flight signing operation is active and the user switches context, cancel the operation gracefully and present a "switched context — please retry" toast per spec acceptance scenario US3 #4.

### Tests for User Story 3

- [ ] T080 [P] [US3] Unit tests `ManagedUserContextTests` in `tests/Sorcha.Wallet.Pwa.Tests/Services/Context/`. Verify context switch → token update → event fires; verify fallback to Personal when stored context is invalid.
- [ ] T081 [P] [US3] Component test `ContextChipSwitcherTests` in `tests/Sorcha.UI.Core.Tests/Components/Context/`. Single-context vs multi-context rendering; click triggers picker.
- [ ] T082 [P] [US3] Component test `NeedsAttentionBandTests` in `tests/Sorcha.UI.Core.Tests/Components/Home/`. Hidden when empty; rendered with items otherwise.
- [ ] T083 [US3] E2E `ContextSwitchingTests` in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/ContextSwitchingTests.cs`. Tagged `[Demo("context-switching")]`. Walks Beat 3 from `quickstart.md` end-to-end including mid-task context-switch cancellation.

**Checkpoint**: All three P1 stories done. Demo all three beats. Home is rebuilt around library primitives. Multi-context UI is first-class. Wallet is now a credible "Sorcha Wallet" — clearly the in-pocket agent for anyone.

---

## Phase 6: User Story 4 — Transaction history (Priority: P2)

**Goal**: Sarah can review her credential and presentation history from the wallet's Activity page.

**Independent Test**: Wallet with at least one issuance and one presentation in history; open Activity; observe ordered feed; tap an entry; observe detail drawer with receipt and trust display.

### Implementation for User Story 4

- [ ] T084 [P] [US4] Create `TransactionHistoryFeed` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/History/TransactionHistoryFeed.razor`. Composes existing `TransactionLifecycleTicks` + `TransactionDetailDrawer` + `ReceiptProofCard` from the library. Mobile-first thumb-scroll feed.
- [ ] T085 [P] [US4] Create `IUserHistoryClient` interface in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/History/IUserHistoryClient.cs`. Fetches a citizen's per-context transaction history from the existing Wallet Service surface.
- [ ] T086 [US4] Rebuild `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` (was a stub) to host `TransactionHistoryFeed`. Per-context scoping via `IUserContext.ActiveContextId`.
- [ ] T087 [US4] Wire `IUserContext.OnContextChanged` to refresh Activity content when the user switches context.
- [ ] T088 [US4] Add 30-second polling on visible entries to refresh lifecycle ticks (matches main web shell cadence).
- [ ] T089 [US4] Wire SignalR hub events (`TransactionConfirmed`) to update the feed in real-time when the page is open.

### Tests for User Story 4

- [ ] T090 [P] [US4] Component test `TransactionHistoryFeedTests` in `tests/Sorcha.UI.Core.Tests/Components/History/`. Mock data: stub `IUserHistoryClient` to return varied event types; verify ordering, lifecycle-tick rendering, detail-drawer open.

**Checkpoint**: US4 functional. Activity page is no longer a stub; Sarah can audit her history.

---

## Phase 7: User Story 5 — Devices & auth (Priority: P2)

**Goal**: Sarah can view her devices, revoke one from another, and manage auth methods (passkey, social, email-password). Includes "lost my phone" recovery copy.

**Independent Test**: Two devices enrolled to one account; revoke from one, observe other refuses to operate. Add/remove a passkey; verify recovery copy explains diverse-methods benefits.

### Implementation for User Story 5 — Migration from Sorcha.UI.Web

- [ ] T091 [P] [US5] Migrate `MyDevices` page surface from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Settings/MyDevices.razor`. Update existing web-shell consumers to call the library version (thin shell wrapper).
- [ ] T092 [P] [US5] Migrate `MyAuthMethods` page surface from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Settings/MyAuthMethods.razor`. Same approach.
- [ ] T093 [P] [US5] Migrate `MyProfile` page surface from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Settings/MyProfile.razor`. Make it per-context-aware via `IUserContext` + `IPerContextPersonaCache`.
- [ ] T094 [US5] Lift the PWA-local `ConfirmRevokeDialog` and `RenameDeviceDialog` from `src/Apps/Sorcha.Wallet.Pwa/Components/` into the shared library at `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Settings/Dialogs/`.
- [ ] T095 [P] [US5] Create `Pages/Devices.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Devices.razor` (PWA route at `/devices`). Wraps the library `MyDevices` component.
- [ ] T096 [P] [US5] Create `Pages/AuthMethods.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/AuthMethods.razor` (PWA route at `/auth-methods`). Wraps the library `MyAuthMethods`.
- [ ] T097 [P] [US5] Create `Pages/Profile.razor` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Profile.razor` (PWA route at `/profile`). Wraps the library `MyProfile`. Per-context aware.
- [ ] T098 [US5] Rebuild `src/Apps/Sorcha.Wallet.Pwa/Pages/Settings.razor` (was bare) into a Settings hub linking to Profile, Devices, Auth methods, Contexts (list), Notifications, About (with Replay tour), Sign out. Each section gets a clear icon + one-line description.
- [ ] T099 [US5] Add "Lost my phone" copy + recovery flow on `MyDevices`: clear explanation that revoking a lost phone from another device is the recovery path; CTA for users with only one device to add a second (or set up a recovery email).
- [ ] T100 [US5] Update existing web-shell consumers in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` to consume the library versions of MyDevices/MyAuthMethods/MyProfile rather than their local copies. Web-shell pages become thin wrappers.

### Tests for User Story 5

- [ ] T101 [P] [US5] Component test `MyDevicesTests` in `tests/Sorcha.UI.Core.Tests/Components/Settings/`. Lift existing test from web-shell tests project; ensure passes against migrated component.
- [ ] T102 [P] [US5] Component test `MyAuthMethodsTests` in `tests/Sorcha.UI.Core.Tests/Components/Settings/`. Lift existing.
- [ ] T103 [P] [US5] Component test `MyProfilePerContextTests` in `tests/Sorcha.UI.Core.Tests/Components/Settings/`. New test: verify per-context persona load/save via mocked `IPerContextPersonaCache`.

**Checkpoint**: US5 functional. Devices and Auth pages mature. Web-shell still consumes the same components (migration verified no regression). Settings hub matches the design doc §9.

---

## Phase 8: User Story 6 — Novice-user guided tour + polish (Priority: P2)

**Goal**: First-time wallet users see a guided tour walking them through hero actions, context chip, footer nav. Empty states have CTAs everywhere. Errors have recovery scaffolds.

**Independent Test**: Clear site data, run enrolment, observe tour fires after welcome takeover, dismiss, observe tour does not re-fire on reload, replay from Settings, observe full sequence.

### Implementation for User Story 6

- [ ] T104 [US6] Create `GuidedTourScaffold` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Onboarding/GuidedTourScaffold.razor`. Takes a steps array, renders overlay with highlight + tooltip per step, dismiss + completion callbacks. Hand-rolled (no third-party tour library) to keep bundle weight low.
- [ ] T105 [US6] Define tour steps for the Sorcha Wallet in `src/Apps/Sorcha.Wallet.Pwa/Services/Onboarding/WalletTourSteps.cs`: Step 1 hero Present, Step 2 hero Verify, Step 3 context chip, Step 4 footer nav. Plain-English copy per FR-031 and the reading-age bar.
- [ ] T106 [US6] Wire `GuidedTourScaffold` into `MainLayout.razor` — checks `IGuidedTourStore.TourDismissedAt`; if null and the wallet is past first paint after enrolment, run the tour.
- [ ] T107 [US6] Persist `TourDismissedAt` via `IGuidedTourStore` from Phase 2 on dismiss / complete.
- [ ] T108 [US6] Add "Replay tour" affordance in `Pages/Settings.razor` → About section. Resets `TourDismissedAt` to null, navigates to Home, tour re-fires.
- [ ] T109 [US6] Add OpenTelemetry counter `sorcha_wallet_tour_completion_total{outcome ∈ {completed,dismissed-early}}`.

### Cross-cutting polish

- [ ] T110 [P] [US6] Create `ErrorRecoveryScaffold` component in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Errors/ErrorRecoveryScaffold.razor`. Standard error display: title + plain-English description + "What just happened" expandable + recovery action button.
- [ ] T111 [P] [US6] Refactor `EmptyState` → `EmptyStateWithCta` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Shared/EmptyStateWithCta.razor`. Backward-compatible (keep the old name as a thin wrapper if needed for existing consumers).
- [ ] T112 [US6] Sweep PWA pages applying `EmptyStateWithCta` per FR-030: Home Credentials band when zero, Activity when empty, Devices when zero (shouldn't happen post-enrolment but render gracefully), Applications when none available.
- [ ] T113 [US6] Sweep PWA error surfaces applying `ErrorRecoveryScaffold` per FR-031: session expired, camera permission denied, network unreachable, verification could-not-complete, application submission failed.
- [ ] T114 [P] [US6] Enhance `IdCardLayout` body rendering per the design doc §4 — full claim disclosures, issuer org branding via the existing `XReviewColourTheme`. Fixes the Spec 1 placeholder where the takeover's id-card body was empty.

### Tests for User Story 6

- [ ] T115 [P] [US6] Component test `GuidedTourScaffoldTests` in `tests/Sorcha.UI.Core.Tests/Components/Onboarding/`. Step navigation, dismiss-persists, replay-resets.
- [ ] T116 [P] [US6] Component test `ErrorRecoveryScaffoldTests` in `tests/Sorcha.UI.Core.Tests/Components/Errors/`. Title + description + recovery action render correctly.
- [ ] T117 [US6] E2E `GuidedTourTests` in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/GuidedTourTests.cs`. Tagged `[Demo("guided-tour")]`. Clear site data, run enrolment, verify tour fires, dismiss, verify no re-fire.

**Checkpoint**: All six user stories done. Wallet UX is mature for novice users. All three demo beats land.

---

## Phase 9: Polish, Issue #700 Phase 2 closure, audits, docs

**Purpose**: Cross-cutting concerns that close out the spec.

### Closing issue #700 Phase 2

- [ ] T118 [P] Create `PostRedeployCacheTests` Playwright test in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/PostRedeployCacheTests.cs` per issue #700 Phase 2: boot stack, visit `/wallet/`, force a wasm-fingerprint rotation via a tagged content change + container recreate, navigate again **without clearing browser state**, assert no wasm fetches return 404.
- [ ] T119 [P] Create `AuthGatedNavigationTests` Playwright test in `tests/Sorcha.UI.E2E.Tests/Docker/Wallet/AuthGatedNavigationTests.cs` per issue #700 Phase 2: enrol wallet via the existing flow, verify Enrol Done → Home navigation lands at `/wallet/`, verify credential-card tap-through, verify both CredentialDetail nav buttons.

### Audits (closes SC-009, SC-010)

- [ ] T120 [P] Create `scripts/audit-pwa-library-consumption.ps1`. Walks the PWA's `.razor` files; counts UI primitive consumption (CredentialCardList, CredentialDetailView, MyDevices, MyAuthMethods, MyProfile, IdCardLayout) vs inline MudBlazor reimplementation; reports the % consumed-from-library. SC-009 target: ≥ 90%.
- [ ] T121 [P] Create `scripts/audit-reading-age.ps1`. Walks `.razor` files; extracts user-visible string literals; runs them through Flesch-Kincaid; reports average grade level + outliers requiring rewrite. SC-010 target: average ≤ 8.0.
- [ ] T122 Run T120 + T121 audits; fix any outliers requiring rewording (especially in `ErrorRecoveryScaffold`, `EmptyStateWithCta`, the welcome-takeover, the tour steps, and any tooltip copy).

### Documentation propagation

- [ ] T123 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` Sorcha Wallet section. Document: rename, full user-agent v1 scope, multi-context UI, verify capability, IUserSigner abstraction, per-context persona schema delta. Replace the existing "Citizen Wallet PWA (Feature 114)" section title with "Sorcha Wallet (Features 114 + 124 + 125)" and consolidate.
- [ ] T124 [P] Update `.claude/skills/sorcha-ui/SKILL.md` with the new library components (ContextChipSwitcher, VerifyFlow, VerifyHomeAction, PresentHomeAction, NeedsAttentionBand, RecentActivityFeed, ContextPeekFooter, PortraitCaptureControl, TransactionHistoryFeed, GuidedTourScaffold, ErrorRecoveryScaffold, EmptyStateWithCta) and the form-factor adaptation rules from design doc §10.
- [ ] T125 [P] Update `docs/reference/API-DOCUMENTATION.md` with the per-context persona endpoint extension (link to `contracts/per-context-persona.openapi.yaml`).
- [ ] T126 [P] Update `.specify/MASTER-TASKS.md` with the Feature 125 entry under "Completed Features (not in themes above)".
- [ ] T127 [P] Update `walkthroughs/AssuredIdentity/README.md` to reference "Sorcha Wallet" rather than "Citizen Wallet" and link to the Strathcarron multi-context + doorstep walkthroughs.

### Final verification

- [ ] T128 Run the full `specs/125-sorcha-wallet-user-agent/quickstart.md` runbook end-to-end. Record SC-001..SC-010 outcomes in the PR description or as a follow-up comment.
- [ ] T129 Tag the merge commit `spec-125-complete` once master receives the feature.
- [ ] T130 Close issue #685 (the F125 placeholder) with a comment linking to `specs/125-sorcha-wallet-user-agent/` and noting the broader Spec 2 absorbed its narrower scope.
- [ ] T131 Close issue #700 with a comment noting Phase 2 tests are now in CI; link to T118 + T119.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No prerequisites — runs first.
- **Foundational (Phase 2)**: Depends on Setup. **BLOCKS all user stories.** Within Phase 2:
  - T003–T011 (rename) must complete before T012 (sanity build) and before any abstraction work.
  - T013–T019 (abstractions and stores) can run in parallel ([P] tasks).
  - T020–T022 (persona migration + service) sequential within that bundle, parallel to abstractions.
  - T023–T024 (endpoints) depend on T022.
  - T025 (DI registration) depends on T013–T019.
  - T026–T029 ([P] tests) can run as soon as their target lands.
  - T030 (regression run) is the gate to leave Phase 2.
- **User Stories (Phase 3–8)**: All depend on Foundational completion. Within each story, `[P]` markers indicate parallel execution.
- **Polish (Phase 9)**: Depends on the user stories whose behaviour it verifies; audit + doc tasks can run in parallel.

### User Story Dependencies

- **US1 (Doorstep verification)**: independent. Uses Phase 2's `IUserSigner` + `IEphemeralVerifierIdentityService` + `IVerificationHistoryStore`.
- **US2 (Application from phone)**: independent. Uses Phase 2's `IUserSigner` + `IPerContextPersonaCache`. Doesn't depend on US1 or US3.
- **US3 (Context switching + Home rebuild)**: independent **but rebuilds `Index.razor`**. US1 and US2 may land their hero actions and pages before US3; US3 rewires them through the new Home structure.
- **US4 (Transaction history)**: independent; uses existing library primitives.
- **US5 (Devices & auth)**: independent; mostly migration from web shell + new Settings hub.
- **US6 (Guided tour + polish)**: depends on US1/US2/US3 hero actions existing (so the tour can highlight them), and ideally lands last so polish covers the full mature surface.

### Within Each User Story

- Tests SHOULD be written alongside implementation. The tasks list them after implementation tasks but `[P]` markers indicate they can run concurrently within the same story.
- Components in the shared library before consumers (e.g., `VerifyFlow` before `Pages/Verify.razor`).
- DI registration last in each layer.

### Parallel Opportunities

- **Phase 2 parallel batch (after rename)**: T013, T014, T016, T017, T018, T019, T026, T027, T028, T029 — ten tasks across different files.
- **US1 parallel batch**: T031, T032, T033, T034, T036, T046, T047, T048 — eight tasks.
- **US2 parallel batch**: T050, T051, T054, T062, T063, T064 — six tasks.
- **US3 parallel batch**: T070, T071, T072, T073, T080, T081, T082 — seven tasks.
- **US5 parallel batch**: T091, T092, T093, T095, T096, T097, T101, T102, T103 — nine tasks (migration work is naturally parallel by file).
- **US6 parallel batch**: T110, T111, T114, T115, T116 — five tasks.
- **Phase 9 parallel batch**: T118, T119, T120, T121, T123, T124, T125, T126, T127 — nine tasks.

---

## PR-shaped delivery

Maps the task phases to the 6-PR sketch from `plan.md`:

| PR | Phase coverage | Tasks |
|---|---|---|
| **PR-A** | Phase 1 + Phase 2 | T001–T030. Rename + foundations + persona schema. Gate: SC-006 baseline preserved. |
| **PR-B** | Phase 5 (US3) | T066–T083. Home IA rebuild + multi-context UI. First user-visible UX change after the rename. |
| **PR-C** | Phase 3 (US1) | T031–T049. Verify capability. **MVP-shippable on its own** after PR-A + PR-B. |
| **PR-D** | Phase 4 (US2) | T050–T065. Application from phone. |
| **PR-E** | Phase 6 + Phase 7 (US4 + US5) | T084–T103. History + Devices + Auth. |
| **PR-F** | Phase 8 + Phase 9 (US6 + Polish) | T104–T131. Tour + scaffolds + audits + docs + issue closures. |

PR-A merges first; PR-B can land before PR-C / PR-D / PR-E if a parallel-team strategy is used (PR-B's Home rebuild is foundational for the demos to land in the right shape).

---

## Implementation Strategy

### MVP First (US1 + US3 = P1 minimum to demo)

The spec ships three P1 stories. The smallest demoable MVP is US3 (Home rebuild + context switching, since the wallet has to look like Sorcha Wallet before any new capability lands) plus US1 (doorstep verification, the most differentiating story).

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (PR-A)
3. Complete Phase 5: US3 — Home IA + context switching (PR-B)
4. Complete Phase 3: US1 — doorstep verification (PR-C)
5. **STOP and VALIDATE**: Demo doorstep verification on the rebuilt Home.

US2 (application from phone) and US4–US6 layer on after the MVP.

### Incremental Delivery

1. After PR-A: foundations + rename land; wallet looks identical to post-F124 but is renamed and has the abstractions in place.
2. After PR-B: Home IA visibly different — multi-context chip, hero actions in place, dashboard structure visible.
3. After PR-C: doorstep verification works. **Demo Beat 1 ships.**
4. After PR-D: application-from-phone works. **Demo Beat 2 ships.**
5. After PR-E: history + devices + auth all polished. The wallet is feature-complete for the user-agent v1 vision.
6. After PR-F: novice-user polish + audits + Issue #700 closure. Feature complete.

### Parallel Team Strategy

With two developers:

1. Both drive Phase 2 (Foundational) together — the rename is the cornerstone.
2. After PR-A merges:
   - Developer A: PR-B (Home rebuild), then PR-D (application from phone) — UI-heavy line.
   - Developer B: PR-C (verify) — has its own capability area; can land out of order with PR-B's Home, just lands the hero action in a placeholder location that PR-B's Home consumes.
3. After PR-B/C land: PR-D and PR-E in parallel.
4. PR-F lands last as polish.

---

## Notes

- All file paths are exact. No `src/[location]/[file]` placeholders remain.
- The PR-A rename is the **load-bearing** Foundational task — every subsequent file path assumes the new namespaces. Get it right in one atomic PR, validate F124 suites pass (T030), then move on.
- Pre-release migration squash applies to the new `ContextOrgId` column (T021).
- The PWA tests project rename (T005) requires the `data-testid` selectors from PR #701 to be preserved — they aren't tied to the namespace, but the test discovery and pipeline references are.
- Each E2E test is tagged `[Demo("<beat-name>")]` so the demo verification can run only those tests as a group (e.g., `dotnet test --filter "Demo"`).
- The library-consumption audit (T120) is a one-shot script for v1; future cleanup specs may turn it into a build-time gate.
- The reading-age audit (T121) is one-shot for v1; equivalent rationale.
