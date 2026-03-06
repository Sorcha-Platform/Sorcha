# Tasks: Identity & Credentials Admin

**Input**: Design documents from `/specs/050-identity-credentials-admin/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — spec requires >85% coverage with ~90 tests.

**Organization**: Tasks grouped by user story. Each story is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US5)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Models)

**Purpose**: Create view models and request/response types shared across user stories

- [X] T001 [P] Create CredentialLifecycleRequest and CredentialLifecycleResult models in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialLifecycleModels.cs`
- [X] T002 [P] Create StatusListViewModel in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/StatusListViewModel.cs`
- [X] T003 [P] Create CreatePresentationRequestViewModel and PresentationRequestResultViewModel in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/PresentationAdminModels.cs`

---

## Phase 2: Foundational (Interface Extensions)

**Purpose**: Extend existing interfaces with new method signatures. MUST complete before user story implementation.

### CLI Refit Interface Extensions

- [X] T004 [P] Add SuspendCredentialAsync, ReinstateCredentialAsync, RefreshCredentialAsync, GetStatusListAsync Refit methods to `src/Apps/Sorcha.Cli/Services/ICredentialServiceClient.cs`
- [X] T005 [P] Add SuspendParticipantAsync, ReactivateParticipantAsync, PublishParticipantAsync, UnpublishParticipantAsync Refit methods to `src/Apps/Sorcha.Cli/Services/IParticipantServiceClient.cs`

### UI Service Interface Extensions

- [X] T006 Add SuspendCredentialAsync, ReinstateCredentialAsync, RefreshCredentialAsync to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/ICredentialApiService.cs`
- [X] T007 [P] Create IStatusListService interface with GetStatusListAsync in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IStatusListService.cs`
- [X] T008 [P] Create IPresentationAdminService interface with CreatePresentationRequestAsync and GetPresentationResultAsync in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/IPresentationAdminService.cs`

**Checkpoint**: All interfaces defined — story implementation can begin

---

## Phase 3: User Story 1 — Credential Lifecycle Management (Priority: P1) MVP

**Goal**: Issuers can suspend, reinstate, revoke, and refresh credentials through UI and CLI with explicit wallet selection

**Independent Test**: Issue a credential, then perform suspend/reinstate/revoke/refresh via UI and CLI. Verify status changes and status list updates.

### Tests for US1

- [X] T009 [P] [US1] Write CredentialApiService lifecycle unit tests (suspend success/wrong-state/forbidden/network, reinstate success/wrong-state/forbidden/network, refresh success/wrong-state/forbidden/network = 12 tests) in `tests/Sorcha.UI.Core.Tests/Services/CredentialApiServiceLifecycleTests.cs`
- [X] T010 [P] [US1] Write credential suspend/reinstate/refresh CLI command tests (success + error per command = 6 tests) in `tests/Sorcha.Cli.Tests/Commands/CredentialLifecycleCommandTests.cs`

### Implementation for US1

- [X] T011 [US1] Implement SuspendCredentialAsync, ReinstateCredentialAsync, RefreshCredentialAsync in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs` — POST to `/api/v1/credentials/{id}/suspend|reinstate|refresh` with LifecycleCredentialRequest body containing IssuerWallet and optional Reason/NewExpiryDuration
- [X] T012 [US1] Create CredentialLifecycleDialog.razor in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialLifecycleDialog.razor` — parameterized by action type (Suspend/Reinstate/Revoke/Refresh), includes MudSelect wallet picker populated from user's linked wallets, optional MudTextField for reason (suspend/reinstate/revoke) or expiry duration (refresh), irreversibility MudAlert warning for Revoke, confirm/cancel MudButtons
- [X] T013 [US1] Modify CredentialDetailView.razor to add lifecycle action buttons in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialDetailView.razor` — Active status: show Suspend + Revoke buttons; Suspended: show Reinstate + Revoke; Expired: show Refresh; Revoked: no buttons; No wallets: show MudAlert explaining linked wallet required. Each button opens CredentialLifecycleDialog via DialogService.
- [X] T014 [US1] Add `credential suspend`, `credential reinstate`, `credential refresh` CLI commands in `src/Apps/Sorcha.Cli/Commands/CredentialCommands.cs` — each with `--id` (required), `--wallet` (required), `--reason` (optional for suspend/reinstate), `--expires-in-days` (optional for refresh). Use BaseCommand.OutputOption for table/JSON output. Call corresponding ICredentialServiceClient Refit methods.

**Checkpoint**: Credential lifecycle fully functional via UI and CLI

---

## Phase 4: User Story 2 — Participant Suspend & Reactivate (Priority: P1)

**Goal**: Organization admins can temporarily suspend participants and reactivate them, with visual status indicators on list views

**Independent Test**: Create a participant, suspend via UI/CLI, verify status changes to Suspended with amber chip, reactivate, verify status returns to Active with green chip.

### Tests for US2

- [X] T015 [P] [US2] Write participant suspend/reactivate CLI command tests (suspend success/error, reactivate success/error = 4 tests) in `tests/Sorcha.Cli.Tests/Commands/ParticipantSuspendCommandTests.cs`

### Implementation for US2

- [X] T016 [US2] Modify ParticipantDetail.razor to add suspend/reactivate buttons in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/ParticipantDetail.razor` — Active: show MudButton "Suspend" (Color.Warning); Suspended: show MudButton "Reactivate" (Color.Success); Inactive: no buttons. Both require MudMessageBox confirmation dialog. Call existing IParticipantApiService.SuspendParticipantAsync / ReactivateParticipantAsync.
- [X] T017 [US2] Modify ParticipantList.razor to add status chips in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/ParticipantList.razor` — add MudChip per participant row: Color.Success for Active, Color.Warning for Suspended, Color.Default for Inactive
- [X] T018 [US2] Add `participant suspend` and `participant reactivate` CLI commands in `src/Apps/Sorcha.Cli/Commands/ParticipantCommands.cs` — each with `--org-id` (required), `--id` (required). Use BaseCommand.OutputOption for table/JSON output. Call IParticipantServiceClient.SuspendParticipantAsync / ReactivateParticipantAsync.

**Checkpoint**: Participant suspend/reactivate fully functional via UI and CLI

---

## Phase 5: User Story 3 — Participant Publishing to Register (Priority: P2)

**Goal**: Admins can publish participant identity records to registers, view published status, and revoke published records

**Independent Test**: Publish a participant to a register via UI/CLI, verify it appears as published on detail page, revoke the published record.

### Tests for US3

- [X] T019 [P] [US3] Write participant publish/unpublish CLI command tests (publish success/error, unpublish success/error = 4 tests) in `tests/Sorcha.Cli.Tests/Commands/ParticipantPublishCommandTests.cs`

### Implementation for US3

- [X] T020 [US3] Create ParticipantPublishDialog.razor (already exists as PublishParticipantDialog.razor) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/ParticipantPublishDialog.razor` — MudDialog with: MudTextField for register ID (required), pre-filled MudTextField for participant name and org name (from CascadingParameter or dialog parameter), MudSelect multi-select for wallet addresses (from participant's linked wallets, max 10), MudSelect for signer wallet, submit/cancel MudButtons. On submit: call IParticipantPublishingService.PublishAsync with PublishParticipantRequest body.
- [X] T021 [US3] Modify ParticipantDetail.razor to add publish button and published register indicators in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/ParticipantDetail.razor` — show "Publish to Register" MudButton for Active participants with Administrator role (use AuthorizeView). Display published registers section with MudChips showing register IDs, with "Update" and "Revoke" options per published record. Opens ParticipantPublishDialog via DialogService.
- [X] T022 [US3] Add `participant publish` and `participant unpublish` CLI commands in `src/Apps/Sorcha.Cli/Commands/ParticipantCommands.cs` — publish: `--org-id`, `--register-id`, `--name`, `--org-name`, `--wallet` (required), `--signer` (required). unpublish: `--org-id`, `--id`, `--register-id`, `--signer` (required). Call IParticipantServiceClient.PublishParticipantAsync / UnpublishParticipantAsync.

**Checkpoint**: Participant publishing fully functional via UI and CLI

---

## Phase 6: User Story 4 — Verifiable Presentations (Priority: P2)

**Goal**: Holders can review and respond to presentation requests with selective disclosure. Verifiers can create requests and view results with QR codes.

**Independent Test**: Verifier creates presentation request via admin page, receives QR/link. Holder views request on credentials page, approves with selected claims. Verifier polls and sees completed result.

### Tests for US4

- [X] T023 [P] [US4] Write PresentationAdminService unit tests (createRequest success/validation/auth, getResult completed/pending/expired/notFound = 7 tests) in `tests/Sorcha.UI.Core.Tests/Services/PresentationAdminServiceTests.cs`

### Implementation for US4

- [X] T024 [US4] Implement PresentationAdminService in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/PresentationAdminService.cs` — CreatePresentationRequestAsync: POST `/api/v1/presentations/request` with CreatePresentationRequestViewModel body, returns PresentationRequestResultViewModel. GetPresentationResultAsync: GET `/api/v1/presentations/{requestId}/result`, returns result or 202 (pending) or 410 (expired). Inject ILogger<PresentationAdminService> and add structured log statements for request creation and result retrieval (LogInformation for success, LogWarning for 4xx responses).
- [X] T025 [US4] Register IPresentationAdminService/PresentationAdminService in DI in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` — add to AddAdminServices method using same HttpClient + AuthenticatedHttpMessageHandler pattern as existing services
- [X] T026 [US4] Create PresentationRequestList.razor in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestList.razor` — holder-side component showing pending presentation requests from existing ICredentialApiService.GetPresentationRequestsAsync. MudTable with columns: Verifier, Credential Type, Expiry, Status. Click row navigates to detail.
- [X] T027 [US4] Create PresentationRequestDetail.razor in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationRequestDetail.razor` — shows verifier identity, required claims, matching credentials from wallet, expiry countdown. MudButton "Approve" opens PresentationSubmitDialog. MudButton "Deny" with confirmation calls ICredentialApiService.DenyPresentation. Expired requests show MudAlert with disabled actions.
- [X] T028 [US4] Create PresentationSubmitDialog.razor in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/PresentationSubmitDialog.razor` — MudDialog with: credential selector (MudSelect from matching credentials), claim checkboxes (MudCheckBox per claim, required claims pre-checked and disabled), confirm/cancel. On submit: call ICredentialApiService.SubmitPresentationAsync.
- [X] T029 [US4] Create PresentationAdmin.razor verifier page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PresentationAdmin.razor` — @page "/admin/presentations". Form with: MudTextField credential type, MudChipSet for accepted issuers (tag input), MudChipSet for required claims (tag input), MudTextField target wallet, MudTextField callback URL (HTTPS validation), MudNumericField TTL seconds. On submit: call IPresentationAdminService.CreatePresentationRequestAsync. After creation: display QR code via JS interop with openid4vp:// URL, shareable link, MudCountDown expiry timer. Request history MudTable below form.
- [X] T030 [US4] DEFERRED — QR code JS interop deferred; PresentationAdmin.razor displays URL as text (Sorcha.Admin.Client is empty; page lives in UI.Web.Client) — vendor `qrcode.min.js` (https://github.com/davidshimjs/qrcodejs, MIT license) into `src/Apps/Sorcha.Admin/wwwroot/lib/qrcodejs/qrcode.min.js`, add script reference in `_Host.cshtml` or `App.razor`. Create `src/Apps/Sorcha.Admin/wwwroot/js/qrcode-interop.js` with `window.generateQrCode = function(elementId, data)` that creates a QRCode instance targeting the element. Create `src/Apps/Sorcha.Admin/Sorcha.Admin.Client/Interop/QrCodeInterop.cs` wrapper class calling `IJSRuntime.InvokeVoidAsync("generateQrCode", elementId, url)`.

**Checkpoint**: Verifiable presentations fully functional for both holder and verifier flows

---

## Phase 7: User Story 5 — Status List Viewer (Priority: P3)

**Goal**: Admins can inspect W3C Bitstring Status Lists by ID with metadata display and raw JSON viewer

**Independent Test**: Navigate to status list admin page, enter a known list ID, view metadata card and raw W3C document. Test empty state and not-found error.

### Tests for US5

- [X] T031 [P] [US5] Write StatusListService unit tests (success, notFound, networkError = 3 tests) in `tests/Sorcha.UI.Core.Tests/Services/StatusListServiceTests.cs`
- [X] T032 [P] [US5] Write credential status-list get CLI command tests (success, notFound = 2 tests) in `tests/Sorcha.Cli.Tests/Commands/CredentialStatusListCommandTests.cs`

### Implementation for US5

- [X] T033 [US5] Implement StatusListService in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/StatusListService.cs` — GetStatusListAsync: GET `/api/v1/credentials/status-lists/{listId}` (public, no auth), returns StatusListViewModel. Handle 404 → null. Inject ILogger<StatusListService> and add structured log statements for lookups (LogInformation for success, LogWarning for not found).
- [X] T034 [US5] Register IStatusListService/StatusListService in DI in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` — add to AddAdminServices method (no auth required for this service)
- [X] T035 [US5] Create StatusLists.razor admin page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/StatusLists.razor` — @page "/admin/status-lists". MudTextField for list ID + MudButton "Lookup". Results: MudCard with metadata fields (ID, Purpose, Issuer DID, Valid From). Expandable MudExpansionPanel with raw JSON viewer (MudText with Typo.Body2 and pre formatting). Recently-viewed list from localStorage (max 10) displayed as MudChips for quick re-lookup. Empty state MudAlert when no lists viewed yet. Handle not-found with MudAlert Severity.Warning.
- [X] T036 [US5] Add `credential status-list get` CLI command in `src/Apps/Sorcha.Cli/Commands/CredentialCommands.cs` — `--id` (required). Table output: List ID, Purpose, Issuer, Valid From. JSON output: full W3C BitstringStatusListCredential document. Call ICredentialServiceClient.GetStatusListAsync.

**Checkpoint**: Status list viewer fully functional via UI and CLI

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Integration verification, documentation, and cleanup

- [X] T037 [P] Add admin navigation entries for new pages in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — add "Status Lists" under Credentials section pointing to /admin/status-lists, add "Presentations" under Credentials section pointing to /admin/presentations
- [X] T038 [P] Wire PresentationRequestList component into existing credentials page (already exists in MyCredentials.razor Inbox tab) — replace presentation placeholder in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/` with PresentationRequestList.razor for holder-side display
- [X] T039 Verify YARP routes exist for all API calls in `src/Services/Sorcha.ApiGateway/` — confirm routes for `/api/v1/credentials/{**catch-all}`, `/api/v1/credentials/status-lists/{**catch-all}`, `/api/v1/presentations/{**catch-all}`, `/api/organizations/{**catch-all}`
- [X] T040 Run full test suite and verify >85% coverage for new code — `dotnet test` across Sorcha.UI.Core.Tests and Sorcha.Cli.Tests
- [X] T041 Update MASTER-TASKS.md with Feature 050 completion status in `.specify/MASTER-TASKS.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (models referenced by interfaces)
- **US1 (Phase 3)**: Depends on Phase 2 (needs ICredentialApiService extensions + CLI Refit)
- **US2 (Phase 4)**: Depends on Phase 2 (needs CLI Refit extensions). Independent of US1.
- **US3 (Phase 5)**: Depends on Phase 2 (needs CLI Refit extensions). Independent of US1/US2.
- **US4 (Phase 6)**: Depends on Phase 2 (needs IPresentationAdminService interface). Independent of US1-US3.
- **US5 (Phase 7)**: Depends on Phase 2 (needs IStatusListService interface + CLI Refit). Independent of US1-US4.
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Independence

All 5 user stories can proceed in parallel after Phase 2 completes:
- **US1** touches: CredentialApiService, CredentialDetailView, CredentialLifecycleDialog, CredentialCommands
- **US2** touches: ParticipantDetail, ParticipantList, ParticipantCommands
- **US3** touches: ParticipantPublishDialog, ParticipantDetail (different section from US2), ParticipantCommands (different subcommands from US2)
- **US4** touches: PresentationAdminService, PresentationAdmin page, PresentationRequestList/Detail/SubmitDialog
- **US5** touches: StatusListService, StatusLists page, CredentialCommands (different subcommand from US1)

**Note**: US2 and US3 both modify ParticipantDetail.razor and ParticipantCommands.cs — if running in parallel, coordinate file edits. US1 and US5 both modify CredentialCommands.cs — same applies.

### Within Each User Story

1. Tests written first (verify they fail)
2. Service implementation
3. UI components / CLI commands
4. Verify tests pass

---

## Parallel Example: After Phase 2 Completes

```
Wave 1 (all parallel — different files):
  Agent A: T009 [US1] CredentialApiService lifecycle tests
  Agent B: T015 [US2] Participant suspend CLI tests
  Agent C: T019 [US3] Participant publish CLI tests
  Agent D: T023 [US4] PresentationAdminService tests
  Agent E: T031+T032 [US5] StatusList tests

Wave 2 (per-story implementation, stories in parallel):
  Agent A: T011→T012→T013→T014 [US1] Credential lifecycle implementation
  Agent B: T016→T017→T018 [US2] Participant suspend implementation
  Agent C: T020→T021→T022 [US3] Participant publish implementation
  Agent D: T024→T025→T026→T027→T028→T029→T030 [US4] Presentations implementation
  Agent E: T033→T034→T035→T036 [US5] Status list implementation
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (5 tasks)
3. Complete Phase 3: US1 — Credential Lifecycle (6 tasks)
4. **STOP and VALIDATE**: Test credential suspend/reinstate/refresh via UI and CLI
5. Delivers immediate value for credential governance

### Incremental Delivery

1. Setup + Foundational (8 tasks) → Interfaces ready
2. US1 Credential Lifecycle (6 tasks) → MVP
3. US2 Participant Suspend (4 tasks) → Core identity management
4. US3 Participant Publishing (4 tasks) → Register publishing
5. US4 Verifiable Presentations (8 tasks) → OID4VP flows
6. US5 Status List Viewer (6 tasks) → Audit capability
7. Polish (5 tasks) → Navigation, integration, docs

---

## Summary

| Phase | Story | Tasks | Parallel |
|-------|-------|-------|----------|
| 1. Setup | — | 3 | All [P] |
| 2. Foundational | — | 5 | 4 of 5 [P] |
| 3. US1 Credential Lifecycle | P1 | 6 | 2 test [P] |
| 4. US2 Participant Suspend | P1 | 4 | 1 test [P] |
| 5. US3 Participant Publishing | P2 | 4 | 1 test [P] |
| 6. US4 Presentations | P2 | 8 | 1 test [P] |
| 7. US5 Status List Viewer | P3 | 6 | 2 test [P] |
| 8. Polish | — | 5 | 2 of 5 [P] |
| **Total** | | **41** | |
