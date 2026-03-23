# Tasks: Register Subscriptions & Private Register Invitations

**Input**: Design documents from `/specs/master/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Organization**: Tasks grouped by user story across two phases (Phase 1: Public Subscriptions, Phase 2: Private Invitations). Each story is independently testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

## User Stories

| ID | Title | Priority | Phase |
|----|-------|----------|-------|
| US1 | System Register name fix | P1 | 1 |
| US2 | Org wallet provisioning | P1 | 1 |
| US3 | Subscription data model & CRUD | P1 | 1 |
| US4 | Auto-subscribe (register creation + bootstrap) | P1 | 1 |
| US5 | UI: Registers page scoping | P1 | 1 |
| US6 | UI: New Submission scoping + peer admin cleanup | P1 | 1 |
| US7 | Org DID method | P2 | 2 |
| US8 | Invitation creation & encryption | P2 | 2 |
| US9 | Invitation acceptance & verification | P2 | 2 |
| US10 | Invitation UI + Org Settings | P2 | 2 |
| US11 | Join Private Register blueprint | P2 | 2 |

---

## Phase 1: Setup

**Purpose**: EF migrations, model foundations, YARP routes

- [x] T001 [P] Add `OrganizationRegisterSubscription` entity with `SubscriptionType` and `SubscriptionStatus` enums in `src/Services/Sorcha.Tenant.Service/Models/OrganizationRegisterSubscription.cs`
- [x] T002 [P] Add `WalletAddress`, `PublicKey`, `EncryptionPublicKey`, `SigningAlgorithm` fields to `src/Services/Sorcha.Tenant.Service/Models/Organization.cs`
- [x] T003 Configure `OrganizationRegisterSubscription` DbSet, entity config (unique constraint on OrgId+RegisterId, indexes, `HasConversion<string>()` for enum columns) in `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`
- [x] T004 Squashed EF migration into regenerated `InitialCreate` in `src/Services/Sorcha.Tenant.Service/Migrations/`
- [x] T005 [P] Add YARP routes for `/api/organizations/{orgId}/register-subscriptions/*` and `/api/me/subscribed-registers` in `src/Services/Sorcha.ApiGateway/appsettings.json`

---

## Phase 2: Foundational — Subscription Service & DTOs

**Purpose**: Core business logic that US3-US6 depend on

**⚠️ CRITICAL**: No user story work (US3+) can begin until this phase is complete

- [x] T006 Create `RegisterSubscriptionDtos.cs` (SubscribeRequest, RegisterSubscriptionResponse) in `src/Services/Sorcha.Tenant.Service/Models/Dtos/RegisterSubscriptionDtos.cs`
- [x] T007 Create `IRegisterSubscriptionService` interface in `src/Services/Sorcha.Tenant.Service/Services/IRegisterSubscriptionService.cs` — ListAsync, GetAsync, SubscribeAsync, UnsubscribeAsync, CreateOwnerSubscriptionAsync
- [x] T008 Implement `RegisterSubscriptionService` in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs` — CRUD logic, RegisterId regex validation `^[a-f0-9]{32}$`, Owner unsubscribe guard, Pending→Active status
- [x] T009 Register `IRegisterSubscriptionService` in DI in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- [x] T010 Unit tests for `RegisterSubscriptionService` in `tests/Sorcha.Tenant.Service.Tests/Services/RegisterSubscriptionServiceTests.cs` — subscribe, unsubscribe, Owner guard, duplicate guard, validation

**Checkpoint**: Foundation ready — user story implementation can begin

---

## Phase 3: User Story 1 — System Register Name Fix (Priority: P1) 🎯 MVP

**Goal**: System Register appears with name "Sorcha System Register" in Peer Network admin UI.

**Independent Test**: Admin → Peer Network → Register Subscriptions tab shows "Sorcha System Register" name.

- [x] T011 [US1] Ensure System Register bootstrap includes `Name = SystemRegisterConstants.SystemRegisterName` in peer advertisement call in `src/Services/Sorcha.Register.Service/`
- [x] T012 [US1] Verify `RegisterAdvertisementService.AdvertiseRegister()` propagates `Name` field in `src/Services/Sorcha.Peer.Service/Replication/RegisterAdvertisementService.cs`
- [x] T013 [US1] Test: System Register name present in `GET /api/registers/subscriptions` response

**Checkpoint**: System Register name visible in UI

---

## Phase 4: User Story 2 — Org Wallet Provisioning (Priority: P1)

**Goal**: Every org gets an HD wallet (ED25519) at creation. Existing orgs backfilled via reconciliation.

**Independent Test**: Bootstrap fresh → org `walletAddress` not null. Create new org → wallet provisioned.

- [x] T014 [US2] Extend `BootstrapEndpoints.cs` — create org wallet via Wallet Service (`owner: "org:{orgId}"`, `Tags: {ownerType: Organization}`), store WalletAddress/PublicKey/SigningAlgorithm on org in `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs`
- [x] T015 [US2] Extend `OrganizationService.CreateOrganizationAsync()` — create org wallet on new org creation in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs`
- [x] T016 [US2] Implement `OrgWalletReconciliationService` (BackgroundService) — scan interval 60s, per-org retry with exponential backoff (30s/60s/120s/240s/480s, max 5 retries per service lifetime) in `src/Services/Sorcha.Tenant.Service/Services/OrgWalletReconciliationService.cs`
- [x] T017 [US2] Register hosted service in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs`
- [x] T018 [US2] Unit tests for wallet provisioning in bootstrap and reconciliation in `tests/Sorcha.Tenant.Service.Tests/Services/OrgWalletProvisioningTests.cs`

**Checkpoint**: All orgs have wallets

---

## Phase 5: User Story 3 — Subscription CRUD Endpoints (Priority: P1)

**Goal**: Admins can list, subscribe to, and unsubscribe from public registers via REST API.

**Independent Test**: POST subscribe → 201. GET list → includes subscription. DELETE → removed. Owner DELETE → 400.

- [x] T019 [US3] Create `RegisterSubscriptionEndpoints.cs` — MapGet list (paginated), MapGet single, MapPost subscribe, MapDelete unsubscribe in `src/Services/Sorcha.Tenant.Service/Endpoints/RegisterSubscriptionEndpoints.cs`
- [x] T020 [US3] Add `/api/me/subscribed-registers` convenience endpoint (resolves orgId from JWT) in `src/Services/Sorcha.Tenant.Service/Endpoints/RegisterSubscriptionEndpoints.cs`
- [x] T021 [US3] Wire `MapRegisterSubscriptionEndpoints()` in `src/Services/Sorcha.Tenant.Service/Program.cs`
- [x] T022 [US3] POST subscribe: validate register via Peer Service client, create Pending record, trigger peer subscription in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs`
- [x] T023 [US3] Background retry for Pending→Active promotion in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs`
- [x] T024 [US3] Integration tests for endpoints (subscribe, list, get, unsubscribe, Owner guard, 409 duplicate) in `tests/Sorcha.Tenant.Service.Tests/Endpoints/RegisterSubscriptionEndpointsTests.cs`

**Checkpoint**: Subscription CRUD working

---

## Phase 6: User Story 4 — Auto-Subscribe (Priority: P1)

**Goal**: Register creation auto-subscribes creating org as Owner. Bootstrap auto-subscribes System Admin to System Register.

**Independent Test**: Create register → subscribed as Owner. Bootstrap → System Register Owner subscription exists.

- [x] T025 [US4] After register creation in UI/gateway, call subscription endpoint to create Owner subscription in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/` or API Gateway
- [x] T026 [US4] Extend bootstrap to create Owner subscription for System Admin org → System Register in `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs`
- [x] T027 [US4] Test: bootstrap creates System Register Owner subscription in `tests/Sorcha.Tenant.Service.Tests/Endpoints/BootstrapEndpointsTests.cs`

**Checkpoint**: Auto-subscription working

---

## Phase 7: User Story 5 — UI: Registers Page Scoping (Priority: P1)

**Goal**: Registers page shows only subscribed registers with type badges and subscribe dialog.

**Independent Test**: Login → Registers page → only subscribed registers. Subscribe to public register → appears in list.

- [x] T028 [P] [US5] Create `RegisterSubscriptionService` HTTP client in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterSubscriptionService.cs`
- [x] T029 [US5] Register HTTP client in DI in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`
- [x] T030 [US5] Update Registers page to load from subscription API in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`
- [x] T031 [US5] Add subscription type badge (Owner/Public/Invited) to register cards in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`
- [x] T032 [US5] Create `SubscribeDialog.razor` — loads public registers from peer network, subscribe action in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/SubscribeDialog.razor`
- [x] T033 [US5] Add unsubscribe action to cards (disabled for Owner) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`

**Checkpoint**: Registers page subscription-scoped

---

## Phase 8: User Story 6 — New Submission Scoping + Peer Admin Cleanup (Priority: P1)

**Goal**: New Submission dropdown shows only subscribed registers. Available Registers tab removed from Peer Admin.

**Independent Test**: New Submission → only subscribed registers in dropdown. Peer Network admin → no Available Registers tab.

- [x] T034 [US6] Filter register dropdown via subscribed registers API in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyWorkflows.razor`
- [x] T035 [US6] Remove "Available Registers" tab from Peer Admin in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/PeerServiceAdmin.razor`
- [x] T036 [US6] Fix tab numbering after removal in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/PeerServiceAdmin.razor`

**Checkpoint**: Phase 1 complete — all subscription scoping working end-to-end

---

## Phase 9: User Story 7 — Org DID Method (Priority: P2)

**Goal**: `did:sorcha:org:<walletAddress>` parseable and resolvable to org's public key.

**Independent Test**: Parse returns Organization type. Resolver returns public key.

- [ ] T037 [P] [US7] Add `Organization` to `SorchaDidType` enum in `src/Common/Sorcha.Register.Models/SorchaDidIdentifier.cs`
- [ ] T038 [US7] Add `FromOrganization()` factory, update `TryParse`/`ToString`, regex `^did:sorcha:org:([A-Za-z1-9]+)$` in `src/Common/Sorcha.Register.Models/SorchaDidIdentifier.cs`
- [ ] T039 [US7] Extend `SorchaDidResolver` for `org` method — resolve via Wallet Service in `src/Common/Sorcha.ServiceClients/Did/SorchaDidResolver.cs`
- [ ] T040 [P] [US7] Unit tests for org DID parsing in `tests/Sorcha.Register.Models.Tests/SorchaDidIdentifierTests.cs`
- [ ] T041 [P] [US7] Unit tests for org DID resolution in `tests/Sorcha.ServiceClients.Tests/Did/SorchaDidResolverTests.cs`

**Checkpoint**: Org DID infrastructure ready

---

## Phase 10: User Story 8 — Invitation Creation & Encryption (Priority: P2)

**Goal**: Register owner creates signed, encrypted invitation token for target org.

**Independent Test**: POST create → returns base64 invitation token containing encrypted payload.

- [ ] T042 [P] [US8] Check `Sorcha.Cryptography` for existing X25519/ECDH support; add ED25519→X25519 conversion + AES-256-GCM envelope encrypt/decrypt utilities if not present in `src/Common/Sorcha.Cryptography/`
- [ ] T043 [US8] Store `EncryptionPublicKey` (X25519) during wallet provisioning in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs` and `BootstrapEndpoints.cs`
- [ ] T044 [P] [US8] Create `RegisterInvitationDtos.cs` in `src/Services/Sorcha.Tenant.Service/Models/Dtos/RegisterInvitationDtos.cs`
- [ ] T045 [US8] Create `IRegisterInvitationService` interface in `src/Services/Sorcha.Tenant.Service/Services/IRegisterInvitationService.cs`
- [ ] T046 [US8] Implement `RegisterInvitationService.CreateAsync()` — resolve target DID, build payload, sign ED25519, encrypt X25519 in `src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs`
- [ ] T047 [US8] Create `RegisterInvitationEndpoints.cs` (POST create, GET list) in `src/Services/Sorcha.Tenant.Service/Endpoints/RegisterInvitationEndpoints.cs`
- [ ] T048 [US8] Wire endpoints + DI, add YARP routes in Program.cs, ServiceCollectionExtensions.cs, appsettings.json
- [ ] T049 [US8] Unit tests for invitation creation in `tests/Sorcha.Tenant.Service.Tests/Services/RegisterInvitationServiceTests.cs`

**Checkpoint**: Invitation creation working

---

## Phase 11: User Story 9 — Invitation Acceptance & Verification (Priority: P2)

**Goal**: Target org accepts token → subscription created. Replay/expired/wrong-org rejected.

**Independent Test**: Accept valid → subscription created. Replay → rejected. Expired → rejected.

- [ ] T050 [US9] Add `InvitationNonce` entity in `src/Services/Sorcha.Tenant.Service/Models/InvitationNonce.cs`
- [ ] T051 [US9] Add DbSet + unique index config in `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`
- [ ] T052 [US9] Generate EF migration `AddInvitationNonces` in `src/Services/Sorcha.Tenant.Service/Migrations/`
- [ ] T053 [US9] Implement `AcceptAsync()` — decrypt, verify signature, check nonce, validate expiry/target/genesis, consume nonce, create Invited subscription in `src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs`
- [ ] T054 [US9] Add POST accept and DELETE revoke endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/RegisterInvitationEndpoints.cs`
- [ ] T055 [US9] On-ledger record: create blueprint instance for audit trail in `src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs`
- [ ] T056 [US9] Unit tests (valid, replay, expired, wrong org, genesis mismatch) in `tests/Sorcha.Tenant.Service.Tests/Services/RegisterInvitationAcceptanceTests.cs`
- [ ] T057 [US9] Integration tests for round-trip in `tests/Sorcha.Tenant.Service.Tests/Endpoints/RegisterInvitationEndpointsTests.cs`

**Checkpoint**: Full invitation round-trip working

---

## Phase 12: User Story 10 — Invitation UI + Org Settings (Priority: P2)

**Goal**: UI for creating, sharing, accepting invitations. Org Settings shows wallet/DID.

**Independent Test**: Invite dialog → token generated. Accept dialog → subscription created. Org Settings → DID visible.

- [ ] T058 [P] [US10] Create invitation HTTP client in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterInvitationService.cs`
- [ ] T059 [US10] Add "Invite Organisation" button on owned register cards in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`
- [ ] T060 [US10] Create `InviteOrganisationDialog.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/InviteOrganisationDialog.razor`
- [ ] T061 [US10] Create `AcceptInvitationDialog.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/AcceptInvitationDialog.razor`
- [ ] T062 [US10] Create invitations panel (sent/received) in Registers page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`
- [ ] T063 [US10] Add Org Settings section with wallet/DID display in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/`

**Checkpoint**: Full invitation UI working

---

## Phase 13: User Story 11 — Join Private Register Blueprint (Priority: P2)

**Goal**: Governance blueprint published to System Register. Invitation acceptance creates blueprint instance.

**Independent Test**: Query System Register → blueprint exists. Accept invitation → instance created on register.

- [ ] T064 [US11] Create `join-private-register.json` blueprint template in `src/Services/Sorcha.Register.Service/Blueprints/` or `src/Common/`
- [ ] T065 [US11] Publish blueprint to System Register during bootstrap in bootstrap endpoint or Register Service startup
- [ ] T066 [US11] Verify invitation acceptance creates blueprint instance in `src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs`
- [ ] T067 [US11] Test: blueprint exists after bootstrap in `tests/Sorcha.Tenant.Service.Tests/`

**Checkpoint**: Blueprint governance complete

---

## Phase 14: Polish & Cross-Cutting Concerns

- [ ] T068 [P] Rate limiting on invitation endpoints (10/hour/org, 50 max pending) in `src/Services/Sorcha.Tenant.Service/Endpoints/RegisterInvitationEndpoints.cs`
- [ ] T069 [P] Genesis hash verification on peer subscription in `src/Services/Sorcha.Peer.Service/`
- [ ] T070 [P] Update Tenant Service README in `src/Services/Sorcha.Tenant.Service/README.md`
- [ ] T071 [P] Update API documentation in `docs/reference/API-DOCUMENTATION.md`
- [ ] T072 [P] Structured logging + OpenTelemetry activity spans for subscription and invitation operations across new services
- [ ] T073 Verify all 8 attack vectors from spec have test coverage (replay, impersonation, spoofing, rogue admin, enumeration, interception, expired keys, DoS)
- [ ] T074 Update `.specify/MASTER-TASKS.md` — mark UX-001 status, add Phase 2 tasks
- [ ] T075 Run `quickstart.md` validation end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1
- **US1 (Phase 3)**: Independent — can start after Phase 1
- **US2 (Phase 4)**: Depends on Phase 1 (org wallet fields)
- **US3 (Phase 5)**: Depends on Phase 2 (subscription service)
- **US4 (Phase 6)**: Depends on US2 + US3
- **US5 (Phase 7)**: Depends on US3
- **US6 (Phase 8)**: Depends on US5
- **US7 (Phase 9)**: Depends on US2 (org wallet)
- **US8 (Phase 10)**: Depends on US7
- **US9 (Phase 11)**: Depends on US8
- **US10 (Phase 12)**: Depends on US8 + US9
- **US11 (Phase 13)**: Depends on US9
- **Polish (Phase 14)**: After all stories

### Parallel Opportunities

- **Phase 1**: T001, T002, T005 (different files)
- **After Phase 1**: US1 parallel with US2
- **Phase 2 tasks**: T037, T040, T041 parallel (different files)
- **Phase 10**: T042, T044 parallel

---

## Implementation Strategy

### MVP First (Phase 1 — US1 through US6)

1. Setup + Foundational → models and service ready
2. US1: System Register name fix (quick win)
3. US2: Org wallet provisioning (Phase 2 foundation)
4. US3: Subscription CRUD
5. US4: Auto-subscribe
6. US5 + US6: UI scoping
7. **VALIDATE**: Phase 1 end-to-end on N1
8. Deploy

### Phase 2 Incremental

1. US7: Org DID → US8: Create invitations → US9: Accept invitations
2. US10: Invitation UI → US11: Blueprint governance
3. Polish: Rate limiting, docs, logging

---

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 75 |
| Phase 1 tasks (US1-US6) | 41 |
| Phase 2 tasks (US7-US11) | 25 |
| Polish tasks | 8 |
| Setup/Foundation | 10 |
| Parallel opportunities | 12 task groups |
| MVP scope | Phase 1 (US1-US6) |
