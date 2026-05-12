---

description: "Task list for Feature 123 — UI.Core User/Admin Type-Level Boundary Refactor"
---

# Tasks: UI.Core User/Admin Type-Level Boundary Refactor

**Input**: Design documents from `/specs/123-ui-core-boundary-split/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests update for tracking — existing tests in `tests/Sorcha.UI.Core.Tests/` switch their `Mock<IBiModalInterface>` setups to `Mock<INarrowerInterface>` where the interface they exercise has been split. Test assertion bodies are unchanged (FR-007 / SC-004). No new test classes are introduced by this feature.

**Organization**: Tasks are grouped by user story. The three user stories (US1 — Feature 122 unblocked; US2 — web app unchanged; US3 — convention discoverable) are properties of the same body of work rather than independent work streams, so phase-to-story mapping is by which story each phase's tasks most directly serve.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- Repository root: `C:\projects\Sorcha\`
- UI.Core project: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- Host apps: `src/Apps/Sorcha.UI/Sorcha.UI.{Admin,App,Designer,Explorer,Web,Web.Client}/`
- Tests: `tests/Sorcha.UI.Core.Tests/`

---

## Phase 1: Setup — Audience Folder Structure

**Purpose**: Create the three audience subfolders under `Services/` and `Models/` plus the canonical Shared subject subfolders. No code moves yet; no behaviour change. The empty folders establish the convention before any file touches it.

- [ ] T001 Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/` with an empty `.gitkeep` file so git tracks the empty folder.
- [ ] T002 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/` with `.gitkeep`.
- [ ] T003 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/` with `.gitkeep`.
- [ ] T004 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/` with `.gitkeep` for the upcoming DTO extraction.
- [ ] T005 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Blueprints/` with `.gitkeep` for `SchemaOverlayFieldInfo` extraction.
- [ ] T006 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/User/` with `.gitkeep`.
- [ ] T007 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/` with `.gitkeep`.
- [ ] T008 [P] Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Shared/` with `.gitkeep`.
- [ ] T009 Verify `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj` succeeds with zero errors — empty folder additions do not affect compilation.
- [ ] T010 Commit Phase 1 with message `chore(123): scaffold audience folder structure under Services/ and Models/`.

**Checkpoint**: Audience subfolders exist. Nothing else has changed. The repository is in the same state functionally as pre-feature.

---

## Phase 2: Foundational — Interface Splits + DTO Extractions

**Purpose**: Execute the two architectural changes that unblock everything else — the `IRegisterService` bi-modal split and the `IOrganizationAdminService` DTO extraction. These are the highest-leverage changes; once they land, the bulk of the remaining work is mechanical folder moves and grep-and-replace.

**⚠️ CRITICAL**: Phase 2 must complete before any audience folder moves in Phase 3 begin. Splitting `IRegisterService` requires updating consumers; consumers update before files move so namespaces don't shift mid-flight.

### IRegisterService split

- [ ] T011 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/IRegisterReadService.cs` with the two user-facing methods (`GetRegistersAsync`, `GetRegisterAsync`). Namespace `Sorcha.UI.Core.Services`. Full XML doc comments matching the contract in `contracts/register-service-split.md`.
- [ ] T012 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IRegisterGovernanceService.cs` with the seven admin/governance methods (`GetGovernanceRosterAsync`, `InitiateRegisterAsync`, `FinalizeRegisterAsync`, `GetPolicyAsync`, `GetPolicyHistoryAsync`, `ProposePolicyUpdateAsync`, `DisableDevModeAsync`). Namespace `Sorcha.UI.Core.Services`. Full XML doc comments.
- [ ] T013 Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterService.cs` to implement both narrower interfaces: `public class RegisterService : IRegisterReadService, IRegisterGovernanceService`. No method body changes — the existing method implementations satisfy both interfaces verbatim.
- [ ] T014 Update DI registration in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`: replace `services.AddScoped<IRegisterService, RegisterService>();` with the two-stage pattern from `contracts/register-service-split.md` so both interfaces resolve to the same scoped instance.
- [ ] T015 Grep the entire repository for `@inject IRegisterService` (Razor files) and `IRegisterService` constructor parameters (C# files). For each match, classify which methods it uses and switch to the narrower interface (`IRegisterReadService`, `IRegisterGovernanceService`, or both). Files affected likely span `Sorcha.UI.{Admin,App,Designer,Explorer,Web,Web.Client}/Pages/` and components.
- [ ] T016 Update test code: grep `tests/Sorcha.UI.Core.Tests/` for `Mock<IRegisterService>` and switch each test class's setup to the matching narrower interface mock. Test assertions are unchanged.
- [ ] T017 Delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IRegisterService.cs`.
- [ ] T018 Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.sln`. **Required**: zero errors. Any consumer that fails to build indicates T015 missed a match.
- [ ] T019 Run `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj`. **Required**: all previously-passing tests still pass.

### IOrganizationAdminService DTO extraction + new IOrganizationReadService

- [ ] T020 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/OrganizationDto.cs` containing only the `OrganizationDto` record extracted verbatim from `IOrganizationAdminService.cs`. Namespace `Sorcha.UI.Core.Services` preserved. Add the Feature 123 extraction comment from `contracts/shared-dto-extraction-pattern.md`.
- [ ] T021 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/BrandingDto.cs` containing only the `BrandingDto` record extracted verbatim.
- [ ] T022 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/UserDto.cs` containing only the `UserDto` record extracted verbatim.
- [ ] T023 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/OrganizationAdminDtos.cs` containing the admin-only DTOs grouped: `AddUserDto`, `UpdateUserDto`, `CreateOrganizationDto`, `UpdateOrganizationDto`, `SubdomainValidationResult`, `OrganizationListResult`, `UserListResult`, `PlatformKpis`. Namespace preserved.
- [ ] T024 Edit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IOrganizationAdminService.cs` to delete the eleven now-extracted record definitions. The file retains only the `IOrganizationAdminService` interface declaration plus a one-line comment indicating where each DTO group moved.
- [ ] T025 Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IOrganizationAdminService.cs` to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IOrganizationAdminService.cs` (`git mv`).
- [ ] T026 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/IOrganizationReadService.cs` defining the narrow `Task<OrganizationDto?> GetOrganizationAsync(Guid id, CancellationToken)` user-facing read interface. Full XML doc comments.
- [ ] T027 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Organization/OrganizationReadService.cs` concrete implementation that hits the same HTTP endpoint as `IOrganizationAdminService.GetOrganizationAsync`. (Internal options: thin wrapper that delegates to the admin service, OR independent class. Pick the wrapper option for simplicity — registers `IOrganizationReadService` against a delegating instance that calls into the admin service.)
- [ ] T028 Add DI registration for `IOrganizationReadService` in `ServiceCollectionExtensions.cs`.
- [ ] T029 Run `dotnet build` for the full Sorcha.UI solution. **Required**: zero errors.

### SchemaOverlayFieldInfo extraction

- [ ] T030 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Shared/Blueprints/SchemaOverlayFieldInfo.cs` containing only the `SchemaOverlayFieldInfo` record extracted verbatim from `Services/BlueprintSchemaService.cs`. Namespace preserved. Add the Feature 123 extraction comment.
- [ ] T031 Edit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/BlueprintSchemaService.cs` to delete the record definition. Replace with a one-line comment indicating the extraction. The file retains `IBlueprintSchemaService` + `BlueprintSchemaService`.
- [ ] T032 Run `dotnet build`. **Required**: zero errors.

### Phase 2 commit

- [ ] T033 Commit Phase 2 with message `refactor(123): split IRegisterService + extract shared DTOs from admin services`.

**Checkpoint**: Bi-modal interfaces split. Shared DTOs in dedicated files. Concrete classes implement both narrower interfaces. All consumers updated. Tests pass. Solution builds. The architectural change is done; remaining work is folder reorganisation.

---

## Phase 3: User Story 1 — Audience folder moves (Priority: P1) 🎯

**Goal**: Every existing interface and model file lives in an audience-classified location (`User/`, `Admin/`, `Shared/`). After this phase, the file system itself encodes audience for every type in `Sorcha.UI.Core`.

**Independent Test**: A reviewer browses `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/` and sees only three subfolders (`User/`, `Admin/`, `Shared/`) plus interface files that are being moved next. A reviewer browses `Sorcha.UI.Core/Models/` and sees the same three audience folders. Every type's audience is identifiable from its path.

### Top-level interface moves (USER)

- [ ] T034 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IRegisterSubscriptionService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T035 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterSubscriptionService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T036 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IDashboardService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T037 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/DashboardService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T038 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IInboxApiService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T039 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/InboxApiService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T040 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IDocketService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T041 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/DocketService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T042 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ITransactionService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T043 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TransactionService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T044 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IWorkflowService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T045 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WorkflowService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T046 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IOfflineSyncService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T047 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/OfflineSyncService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T048 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IWalletPreferenceService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`
- [ ] T049 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WalletPreferenceService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/`

### Top-level interface moves (ADMIN)

- [ ] T050 [US1] [P] `git mv` admin services to `Services/Admin/`: `IPlatformSettingsAdminService.cs`, `PlatformSettingsAdminService.cs`, `IValidatorAdminService.cs`, `ValidatorAdminService.cs`, `ISystemRegisterService.cs`, `SystemRegisterService.cs`. Single task because the file set is uniform and tightly related.
- [ ] T051 [US1] [P] `git mv` admin services to `Services/Admin/`: `IBlueprintApiService.cs`, `BlueprintApiService.cs`, `ISchemaLibraryApiService.cs`, `SchemaLibraryApiService.cs`, `ITemplateApiService.cs`, `TemplateApiService.cs`, `IBlueprintStorageService.cs`, `BlueprintStorageService.cs`, `BlueprintLayoutService.cs`, `BlueprintSchemaService.cs`, `BlueprintSerializationService.cs`, `BlueprintHubConnection.cs`.
- [ ] T052 [US1] [P] `git mv` admin services to `Services/Admin/`: `IHealthCheckService.cs`, `HealthCheckService.cs`, `IAuditService.cs`, `AuditService.cs`, `IServicePrincipalService.cs`, `ServicePrincipalService.cs`, `IODataQueryService.cs`, `ODataQueryService.cs`, `OrganizationAdminService.cs` (concrete, paired with the already-moved interface), `OrgProvisioningClientService.cs`, `ActivityLogService.cs`.
- [ ] T053 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IWalletAccessService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/` and `git mv WalletAccessService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/` (wallet-access grant/revoke is org-admin-only per R5).
- [ ] T054 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IChatHubConnection.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/` and `git mv ChatHubConnection.cs ChatHubOptions.cs` alongside (AI designer chat — admin/designer surface per R5 reclassification).
- [ ] T055 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/CurrentUserWalletService.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/` (re-audit during execution; provisional ADMIN based on its name suggesting org-context awareness — Layer-3 closure walk may reclassify).

### Top-level interface moves (SHARED)

- [ ] T056 [US1] [P] `git mv` shared services to `Services/Shared/`: `IAlertService.cs`, `AlertService.cs`, `IAlertDismissalService.cs`, `AlertDismissalService.cs`, `IPayloadDecoderService.cs`, `PayloadDecoderService.cs`.
- [ ] T057 [US1] [P] `git mv` shared services to `Services/Shared/`: `EventLogService.cs`, `GraphLayoutService.cs`, `LocalizationService.cs`, `ThemeService.cs`, `TimeFormatService.cs`, `UserPreferencesService.cs`, `NavigationStateService.cs`, `RegisterHubConnection.cs`, `TenantHubConnection.cs`, `WalletHubConnection.cs`. (Audit during execution — services that turn out to be admin-only get reclassified.)
- [ ] T058 [US1] [P] `git mv` paired files to `Services/Shared/`: `AuthMethodsClientService.cs`, `TotpClientService.cs`, `PendingActionService.cs`, `InstructionExportService.cs`, `EncryptionOperationTracker.cs` (provisional — Encryption audit deferred to T065).

### Service subfolder moves

- [ ] T059 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/User/Forms`
- [ ] T060 [US1] [P] `git mv .../Services/Persona .../Services/User/Persona`
- [ ] T061 [US1] [P] `git mv .../Services/Credentials .../Services/User/Credentials`
- [ ] T062 [US1] [P] `git mv .../Services/AddressLookup .../Services/User/AddressLookup`
- [ ] T063 [US1] [P] `git mv .../Services/Participants .../Services/User/Participants`
- [ ] T064 [US1] [P] `git mv .../Services/Wallet .../Services/User/Wallet`
- [ ] T065 [US1] [P] `git mv .../Services/Admin/* .../Services/Admin/` (flatten — move contents directly into the Admin folder rather than creating an `Admin/Admin/` nest). Likely manual file-by-file since the names won't collide.
- [ ] T066 [US1] [P] `git mv .../Services/Designer .../Services/Admin/Designer`
- [ ] T067 [US1] [P] `git mv .../Services/Configuration .../Services/Admin/Configuration`
- [ ] T068 [US1] [P] `git mv .../Services/Identity .../Services/Shared/Identity`
- [ ] T069 [US1] [P] `git mv .../Services/Navigation .../Services/Shared/Navigation`
- [ ] T070 [US1] [P] `git mv .../Services/Http .../Services/Shared/Http`
- [ ] T071 [US1] [P] `git mv .../Services/Authentication .../Services/Shared/Authentication`

### Deferred audit — Encryption

- [ ] T072 [US1] Audit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Encryption/` file-by-file using the three-layer methodology (R1). Classify each file as USER, ADMIN, or SHARED. Apply moves per the verdicts. Likely outcome: SHARED for most files; possibly ADMIN for any tightly-coupled-to-designer encryption.

### Model folder moves (USER)

- [ ] T073 [US1] [P] `git mv .../Models/Actions .../Models/User/Actions`
- [ ] T074 [US1] [P] `git mv .../Models/Credentials .../Models/User/Credentials`
- [ ] T075 [US1] [P] `git mv .../Models/Dashboard .../Models/User/Dashboard`
- [ ] T076 [US1] [P] `git mv .../Models/Forms .../Models/User/Forms`
- [ ] T077 [US1] [P] `git mv .../Models/Participants .../Models/User/Participants`
- [ ] T078 [US1] [P] `git mv .../Models/Wallet .../Models/User/Wallet`
- [ ] T079 [US1] [P] `git mv .../Models/Workflows .../Models/User/Workflows`

### Model folder moves (ADMIN)

- [ ] T080 [US1] [P] `git mv .../Models/Admin/* .../Models/Admin/` (flatten — same approach as T065).
- [ ] T081 [US1] [P] `git mv .../Models/Chat .../Models/Admin/Chat`
- [ ] T082 [US1] [P] `git mv .../Models/Configuration .../Models/Admin/Configuration`
- [ ] T083 [US1] [P] `git mv .../Models/Designer .../Models/Admin/Designer`
- [ ] T084 [US1] [P] `git mv .../Models/Explorer .../Models/Admin/Explorer`
- [ ] T085 [US1] [P] `git mv .../Models/SchemaLibrary .../Models/Admin/SchemaLibrary`
- [ ] T086 [US1] [P] `git mv .../Models/Templates .../Models/Admin/Templates`

### Model folder moves (SHARED)

- [ ] T087 [US1] [P] `git mv .../Models/Common .../Models/Shared/Common`

### Models/Registers per-file split (the mixed-audience case)

- [ ] T088 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/User/Registers/` and `git mv` the user-facing register types into it: `TransactionViewModel.cs`, `RegisterViewModel.cs`, `WalletViewModel.cs`, `PayloadViewModel.cs`, `TransactionListResponse.cs`, `TransactionGraphNode.cs`, `TransactionQueryState.cs`, `RegisterFilterState.cs`, `ConnectionState.cs`, `NavigationContext.cs`.
- [ ] T089 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/Registers/` and `git mv` the admin/governance register types: `RegisterPolicyViewModel.cs`, `RegisterPolicyFields.cs`, `PolicyUpdateProposalViewModel.cs`, `PolicyHistoryViewModel.cs`, `RegisterCreationState.cs`.
- [ ] T090 [US1] Delete the now-empty `Models/Registers/` folder (use `rmdir` or git's empty-dir handling).

### Models/Blueprints partial classification

- [ ] T091 [US1] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Blueprints/GovernanceRosterViewModel.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/Governance/GovernanceRosterViewModel.cs` (the type that defeated Feature 122 Phase 2).
- [ ] T092 [US1] `git mv` the remaining `Models/Blueprints/` contents (designer-flavoured schema/canvas types) to `Models/Admin/Blueprints/`.

### Models/Authentication audit + split

- [ ] T093 [US1] Audit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Authentication/` file-by-file. Move user-side types to `Models/User/Authentication/`; admin-side types to `Models/Admin/Authentication/`. Specific verdicts recorded in commit message.

### Models/Encryption — deferred audit

- [ ] T094 [US1] Audit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Encryption/` per R6 deferred-audit note. Likely outcome: SHARED. Apply moves per verdicts.

### Loose top-level model files

- [ ] T095 [US1] [P] `git mv src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/ActivityEventDto.cs src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Shared/`
- [ ] T096 [US1] [P] `git mv .../Models/AuthMethodsModels.cs .../Models/User/Authentication/`
- [ ] T097 [US1] [P] `git mv .../Models/PendingActionNotificationDto.cs .../Models/User/Actions/`
- [ ] T098 [US1] [P] `git mv .../Models/TotpDtos.cs .../Models/User/Authentication/`
- [ ] T099 [US1] [P] `git mv .../Models/UserPreferencesDto.cs .../Models/User/`
- [ ] T100 [US1] [P] `git mv .../Models/WalletAccessModels.cs .../Models/Admin/Wallet/`

### Build verification

- [ ] T101 [US1] Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.sln`. **Required**: zero errors. Namespace preservation policy (Phase 1 of plan) means consumer `using` directives remain valid across all moves — any build error indicates a file with implicit namespace dependency that needs an explicit declaration.
- [ ] T102 [US1] Remove `.gitkeep` files from any audience folder that now has real files: `Services/User/.gitkeep`, `Services/Admin/.gitkeep`, etc. Keep `.gitkeep` only in folders that remain empty (none expected after Phase 3).

### Phase 3 commit

- [ ] T103 [US1] Commit Phase 3 with message `refactor(123): move all UI.Core interfaces and models into audience folders (User/Admin/Shared)`. Large diff but each file is a tracked rename — review-commit-by-commit is feasible.

**Checkpoint**: Every type in `Sorcha.UI.Core` is in an audience-classified location. User Story 1 satisfied — a future Feature 122 attempt can move `Services/User/*` and `Models/User/*` wholesale.

---

## Phase 4: User Story 2 — Web app verified unchanged (Priority: P1)

**Goal**: The full Sorcha.UI web app family renders and behaves identically after Phases 1-3. Tests pass. Walkthroughs run cleanly. No console errors.

**Independent Test**: Full `dotnet test` for the Sorcha.UI test family passes with no test source changes beyond the mock-interface renames done in T016. AssuredIdentity walkthrough completes end-to-end.

- [ ] T104 [US2] Run `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`. **Required**: every previously-passing test still passes.
- [ ] T105 [US2] Run `dotnet test tests/Sorcha.UI.Integration.Tests/Sorcha.UI.Integration.Tests.csproj --nologo`. **Required**: zero regressions.
- [ ] T106 [US2] Run the full Sorcha-wide test suite that includes UI: `dotnet test --nologo --filter "FullyQualifiedName~Sorcha.UI"`. **Required**: zero regressions.
- [ ] T107 [US2] Launch the AssuredIdentity walkthrough via `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway`. **Required**: completes end-to-end without errors. Document the run time and outcome in the commit message.
- [ ] T108 [US2] Manual sanity check via the running stack — login to Sorcha.UI, navigate through representative pages: dashboard, registers, blueprints, designer, settings, my profile, a credential view, a participant edit, an admin org page. **Required**: no missing components, no styling drift, no browser-console errors.
- [ ] T109 [US2] Commit Phase 4 with message `test(123): verify Sorcha.UI behaviour unchanged after audience-folder refactor`. (Likely an empty commit if no further changes; alternatively roll the verification evidence into the Phase 3 PR description.)

**Checkpoint**: Web app verified to behave identically. User Story 2 satisfied.

---

## Phase 5: User Story 3 — Convention documented (Priority: P2)

**Goal**: A future contributor can pick the right interface, the right folder, and the right namespace for new work without senior review by reading the documentation alone.

**Independent Test**: A new contributor reads `Sorcha.UI.Core/README.md` and the related sections, then writes a new user-facing service interface — places it in `Services/User/` on the first attempt. Same for an admin model type.

- [ ] T110 [US3] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/README.md` with the condensed audience-tag convention from `specs/123-ui-core-boundary-split/quickstart.md`: the folder-split rule, the interface-naming rule, the bi-modal smell detector, the shared-DTO extraction pattern reference, and the namespace-preservation policy.
- [ ] T111 [US3] [P] Update `CLAUDE.md` to add a short pointer under the existing Sorcha.UI architecture subsection: "User-facing and admin-facing code in `Sorcha.UI.Core` are partitioned at folder level into `Services/User/`, `Services/Admin/`, `Services/Shared/` (and the same pattern under `Models/`). See `src/Apps/Sorcha.UI/Sorcha.UI.Core/README.md` for the full convention."
- [ ] T112 [US3] [P] Update `.claude/skills/sorcha-ui/SKILL.md` with a section "Audience-tag convention (Feature 123)" pointing at the README and summarising the bi-modal smell detector for skill-driven work to place new files correctly on the first attempt.
- [ ] T113 [US3] Commit Phase 5 with message `docs(123): document UI.Core audience-tag convention in README + CLAUDE.md + sorcha-ui skill`.

**Checkpoint**: Convention documented in three discoverable locations. User Story 3 satisfied.

---

## Phase 6: Polish + PR

**Purpose**: Final integration sanity check, PR open, await CI, merge.

- [ ] T114 Run `dotnet build` from solution root with `/warnaserror` (or inspect output) to confirm zero new compiler warnings introduced by the refactor. Pre-existing warnings (XML-doc gaps in `Sorcha.ServiceClients.Http`, MUD0002 in Citizen.Wallet) are unchanged and acceptable.
- [ ] T115 Update `MEMORY.md` "Current Branch" to record Feature 123 completion: tip commit, audit verdict counts, where Feature 122 should pick up from.
- [ ] T116 Open PR against master via `gh pr create --fill` with title `feat(123): UI.Core user/admin type-level boundary refactor` and body summarising the architectural change (R1-R10 highlights), the verdict counts (1 interface split, 12 DTO extractions, all interfaces and models in audience folders, X consumer-page updates, 0 test-behaviour changes), and the AssuredIdentity walkthrough outcome from T107. Reference `specs/122-shared-user-components/phase-2-discovery.md` as the motivating context. Wait for CI checks to pass (claude-review, discoverability, link-check, build-and-test).
- [ ] T117 After merge, update `specs/122-shared-user-components/plan.md` and `tasks.md` to remove the "BLOCKED ON FEATURE 123" banners and note that Feature 122 can now resume. (Optional — can also happen as the first commit of the resumed Feature 122 branch.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1, T001–T010)**: No dependencies — can start immediately.
- **Foundational (Phase 2, T011–T033)**: Depends on Setup. Splits + DTO extractions + consumer/test updates. **Must complete before Phase 3** so consumers reference the narrower interfaces before files move into folders.
- **US1 (Phase 3, T034–T103)**: Depends on Foundational. Pure folder reorganisation; high parallelism within.
- **US2 (Phase 4, T104–T109)**: Depends on US1 — the verification runs against the fully-moved codebase.
- **US3 (Phase 5, T110–T113)**: Depends on US1 — the documentation describes the new structure. Can run in parallel with US2.
- **Polish (Phase 6, T114–T117)**: Depends on US1 + US2 + US3.

### Within-Phase Parallel Opportunities

**Phase 1**: T002–T008 all parallel (independent folder creations). T009 + T010 sequential.

**Phase 2**: Two largely-independent tracks — the IRegisterService split (T011–T019) and the DTO extraction (T020–T029) can interleave. T030–T032 (SchemaOverlayFieldInfo) parallel with both.

**Phase 3**: Huge parallel surface — every `git mv` task is independent of every other (different files, no dependencies). Practical batches:
- T034–T049 (USER moves) — 16 parallel
- T050–T058 (ADMIN/SHARED moves) — 9 parallel
- T059–T072 (subfolder moves + Encryption audit) — 14 parallel
- T073–T100 (model moves) — 28 parallel

A single developer executes these sequentially with quick `dotnet build` checkpoints; multiple developers could parallel-claim ranges. The atomicity that mattered for Feature 122 Phase 2 (move everything in one commit) does NOT apply here — Feature 123 can build successfully after each chunk of moves because the namespace policy ensures consumer compatibility throughout.

**Phase 4**: T104–T108 sequential (each verification step gates the next). T109 final.

**Phase 5**: T110 sequential (creates the README). T111 + T112 parallel after.

**Phase 6**: Sequential.

### Parallel Example: Phase 3 USER-folder moves

```bash
# After Phase 2 completes, the following 16 git mv operations can run in parallel:
git mv .../Services/IRegisterSubscriptionService.cs .../Services/User/
git mv .../Services/RegisterSubscriptionService.cs .../Services/User/
git mv .../Services/IDashboardService.cs .../Services/User/
# ... (T034 through T049)
# Then dotnet build to verify, then proceed to ADMIN moves.
```

---

## Implementation Strategy

### Single-developer sequential (most realistic for this codebase)

This is the recommended path for one person on one machine:

1. Session 1: Phase 1 (T001–T010) + Phase 2 (T011–T033). The architectural changes are the riskiest part; doing them in one focused session keeps the consumer-update grep coherent.
2. Session 2: Phase 3 USER moves (T034–T049 + T059–T072). Build after every chunk.
3. Session 3: Phase 3 ADMIN + SHARED moves (T050–T058) + Phase 3 model moves (T073–T100) + per-file split (T088–T093) + deferred audits (T072, T094). Build after every chunk.
4. Session 4: Phase 4 verification + Phase 5 documentation + Phase 6 PR.

Build time per session is the dominant cost. The plan's "namespace preservation" policy means build errors during Phase 3 are immediate and local — the offending file is the one that just moved.

### Atomicity strategy

Unlike Feature 122 Phase 2, Feature 123 does NOT require all moves to be atomic in one commit. The namespace-preservation policy means consumer `using` directives stay valid throughout, so each chunk-of-moves can commit and build independently. This makes the work resumable across sessions — partial completion is a stable state.

### MVP scope

The MVP that fully unblocks Feature 122 is **Phases 1 + 2 + 3 completed through T101**. At that point:
- Bi-modal interfaces are split (the architectural property)
- Every type is in an audience folder (the navigation property)
- Tests pass (the regression property)

US2 (manual walkthrough verification) and US3 (documentation) are post-MVP polish. Phase 6 PR can be opened at MVP and US2/US3 commits append before merge.

---

## Notes

- [P] tasks operate on different files with no dependencies between them.
- [Story] label maps each task to its user story (US1/US2/US3) for traceability.
- Use `git mv` for every file relocation so renames stay tracked and the PR diff is reviewable as renames not delete+add.
- **Namespace preservation is load-bearing.** Every move preserves the type's existing namespace. The audience signal lives in the folder, not the namespace. This is what keeps consumer `using` directives unchanged.
- The two architectural changes (Phase 2) are the only places where consumer code changes. Phase 3's folder moves change no consumer code at all — they're pure file relocations under the same namespace.
- The deferred audits (T072 Services/Encryption/, T094 Models/Encryption/) apply the three-layer methodology from research.md R1; record the verdict in the commit message.
- Avoid: changing namespaces during the refactor (would create a separate massive consumer-update diff); attempting to rename `Sorcha.UI.Core` (explicit out-of-scope); reordering Phase 2 to happen after Phase 3 (consumer code must reference narrower interfaces before files move).
