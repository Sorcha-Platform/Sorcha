# Tasks: Mobile Package Infrastructure

**Input**: Design documents from `/specs/084-mobile-package-infra/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included — constitution requires >85% coverage. Key gate: all 638+ existing tests must pass (FR-014).

**Organization**: Tasks grouped by user story. US1 and US2 are the extraction work, US3 is the pipeline, US4 is backward compatibility verification.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (New Project Scaffolding)

**Purpose**: Create the two new project files and wire them into the solution

- [ ] T001 Create `Sorcha.Wallet.Portable.csproj` at `src/Core/Sorcha.Wallet.Portable/` with PackageId `Sorcha.Wallet.Portable`, description, net10.0 target, PackageReferences for NBitcoin and Microsoft.Extensions.Logging.Abstractions. Reference Sorcha.Cryptography. No EF Core, no Npgsql, no ASP.NET Core dependencies.
- [ ] T002 Create `Sorcha.ServiceClients.Http.csproj` at `src/Common/Sorcha.ServiceClients.Http/` with PackageId `Sorcha.ServiceClients.Http`, description, net10.0 target, PackageReferences for Microsoft.Extensions.Http, Microsoft.AspNetCore.SignalR.Client, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Logging.Abstractions, SimpleBase. No gRPC, no Google.Protobuf dependencies.
- [ ] T003 [P] Add `Sorcha.Wallet.Portable` to the solution file via `dotnet sln add src/Core/Sorcha.Wallet.Portable/Sorcha.Wallet.Portable.csproj`
- [ ] T004 [P] Add `Sorcha.ServiceClients.Http` to the solution file via `dotnet sln add src/Common/Sorcha.ServiceClients.Http/Sorcha.ServiceClients.Http.csproj`
- [ ] T005 Add ProjectReference from `Sorcha.Wallet.Core.csproj` to `Sorcha.Wallet.Portable.csproj` in `src/Core/Sorcha.Wallet.Core/Sorcha.Wallet.Core.csproj`
- [ ] T006 Add ProjectReference from `Sorcha.ServiceClients.csproj` to `Sorcha.ServiceClients.Http.csproj` in `src/Common/Sorcha.ServiceClients/Sorcha.ServiceClients.csproj`
- [ ] T007 Verify solution builds with empty new projects: `dotnet build`

**Checkpoint**: Two new empty projects in solution, wired as dependencies. Build green.

---

## Phase 2: Foundational (File Moves — Blocking All Stories)

**Purpose**: Move files from source projects to new portable projects. This is the bulk of the work and blocks everything else.

**⚠️ CRITICAL**: Must be done atomically — partial moves will break the build.

### Wallet.Core → Wallet.Portable extraction

- [ ] T008 Move all 12 entity files from `src/Core/Sorcha.Wallet.Core/Domain/Entities/` to `src/Core/Sorcha.Wallet.Portable/Domain/Entities/` — Wallet.cs, WalletAddress.cs, WalletAccess.cs, WalletTransaction.cs, CredentialEntity.cs, RecoveryKeyWrap.cs, RecoveryAuditLog.cs, OrgMasterKey.cs, DerivedKeyRecord.cs, ThresholdKeyGroup.cs, SigningKeyShare.cs, SigningSession.cs. **Preserve original namespaces.**
- [ ] T009 Move root enums file from `src/Core/Sorcha.Wallet.Core/Domain/Enums.cs` to `src/Core/Sorcha.Wallet.Portable/Domain/Enums.cs`. Preserve namespace `Sorcha.Wallet.Core.Domain`.
- [ ] T010 Move all 7 enum files from `src/Core/Sorcha.Wallet.Core/Domain/Enums/` to `src/Core/Sorcha.Wallet.Portable/Domain/Enums/` — KeyUsage.cs, CustodyMode.cs, DerivedKeyStatus.cs, OrgMasterKeyStatus.cs, SigningMode.cs, SigningSessionState.cs, ThresholdKeyGroupStatus.cs. Preserve namespaces.
- [ ] T011 Move all 7 service interface files from `src/Core/Sorcha.Wallet.Core/Services/Interfaces/` to `src/Core/Sorcha.Wallet.Portable/Services/Interfaces/` — IOrgKeyDerivationService.cs, IOrgKeyProtectionProvider.cs, IWalletService.cs, IKeyManagementService.cs, IDelegationService.cs, ITransactionService.cs, IRecoveryKeyService.cs. Preserve namespaces.
- [ ] T012 Move exception files from `src/Core/Sorcha.Wallet.Core/` to `src/Core/Sorcha.Wallet.Portable/` — WalletNotFoundException.cs, WalletAccessAlreadyExistsException.cs. Preserve namespaces.
- [ ] T013 Move constants from `src/Core/Sorcha.Wallet.Core/Constants/` to `src/Core/Sorcha.Wallet.Portable/Constants/`. Preserve namespaces.
- [ ] T014 Move domain events from `src/Core/Sorcha.Wallet.Core/Domain/Events/` to `src/Core/Sorcha.Wallet.Portable/Domain/Events/`. Preserve namespaces.
- [ ] T015 Move `DerivationPathBuilder.cs` from `src/Services/Sorcha.Wallet.Service/Services/Implementation/DerivationPathBuilder.cs` to `src/Core/Sorcha.Wallet.Portable/DerivationPathBuilder.cs`. Update namespace to `Sorcha.Wallet.Portable` (this is a new portable namespace — update all references in Wallet.Service and test projects).
- [ ] T016 Remove NBitcoin PackageReference from `Sorcha.Wallet.Core.csproj` (now in Portable). Remove Microsoft.Extensions.Logging.Abstractions if also moved. Keep EF Core, Npgsql, and encryption-related packages.
- [ ] T017 Update `Sorcha.Wallet.Core.csproj` to ensure all remaining files (Data/, Repositories/, Encryption/, Services/Implementation/) compile with Portable as a dependency.

### ServiceClients → ServiceClients.Http extraction

- [ ] T018 Move all Auth files from `src/Common/Sorcha.ServiceClients/Auth/` to `src/Common/Sorcha.ServiceClients.Http/Auth/` — IServiceAuthClient.cs, ServiceAuthClient.cs, IDelegationTokenClient.cs, DelegationTokenClient.cs, ITokenIntrospectionClient.cs, TokenIntrospectionClient.cs, TokenClaimConstants.cs. Preserve namespaces.
- [ ] T019 Move Wallet client files from `src/Common/Sorcha.ServiceClients/Wallet/` to `src/Common/Sorcha.ServiceClients.Http/Wallet/` — IWalletServiceClient.cs, WalletServiceClient.cs, plus Models/ subdirectory. Preserve namespaces.
- [ ] T020 [P] Move Blueprint client files from `src/Common/Sorcha.ServiceClients/Blueprint/` to `src/Common/Sorcha.ServiceClients.Http/Blueprint/`. Preserve namespaces.
- [ ] T021 [P] Move Register client files from `src/Common/Sorcha.ServiceClients/Register/` to `src/Common/Sorcha.ServiceClients.Http/Register/`. Preserve namespaces.
- [ ] T022 [P] Move Participant client files from `src/Common/Sorcha.ServiceClients/Participant/` to `src/Common/Sorcha.ServiceClients.Http/Participant/`. Preserve namespaces.
- [ ] T023 [P] Move Validator client files from `src/Common/Sorcha.ServiceClients/Validator/` to `src/Common/Sorcha.ServiceClients.Http/Validator/`. Preserve namespaces.
- [ ] T024 [P] Move Events client files from `src/Common/Sorcha.ServiceClients/Events/` to `src/Common/Sorcha.ServiceClients.Http/Events/`. Preserve namespaces.
- [ ] T025 [P] Move Subscription client files from `src/Common/Sorcha.ServiceClients/Subscription/` to `src/Common/Sorcha.ServiceClients.Http/Subscription/`. Preserve namespaces.
- [ ] T026 [P] Move Passkey client files from `src/Common/Sorcha.ServiceClients/Passkey/` to `src/Common/Sorcha.ServiceClients.Http/Passkey/`. Preserve namespaces.
- [ ] T027 [P] Move Did resolver files from `src/Common/Sorcha.ServiceClients/Did/` to `src/Common/Sorcha.ServiceClients.Http/Did/`. Preserve namespaces.
- [ ] T028 [P] Move SystemWallet files from `src/Common/Sorcha.ServiceClients/SystemWallet/` to `src/Common/Sorcha.ServiceClients.Http/SystemWallet/`. Preserve namespaces.
- [ ] T029 Move Helpers from `src/Common/Sorcha.ServiceClients/Helpers/` to `src/Common/Sorcha.ServiceClients.Http/Helpers/`. Preserve namespaces.
- [ ] T030 Move shared Models from `src/Common/Sorcha.ServiceClients/Models/` to `src/Common/Sorcha.ServiceClients.Http/Models/`. Preserve namespaces.
- [ ] T031 Remove HTTP-related PackageReferences from `Sorcha.ServiceClients.csproj` that are now in Http (Microsoft.Extensions.Http, SimpleBase, etc.). Keep gRPC packages (Grpc.Net.Client, Grpc.Net.ClientFactory, Grpc.Tools, Google.Protobuf).
- [ ] T032 Verify Grpc/ directory and Peer/ directory remain in `src/Common/Sorcha.ServiceClients/` with their gRPC-dependent code intact.

### Build verification

- [ ] T033 Full solution build: `dotnet build --force`. Fix any namespace resolution issues from moved files. All projects must compile with zero errors.
- [ ] T034 Run full test suite: `dotnet test`. All 638+ existing tests must pass with zero modifications (FR-014). Fix any test project references if needed.

**Checkpoint**: All files extracted. Build green. All existing tests pass. Foundation complete.

---

## Phase 3: User Story 1 — Mobile Developer Consumes Wallet Package (Priority: P1) 🎯 MVP

**Goal**: Portable wallet package is self-contained, no server dependencies, derivation works.

**Independent Test**: Install Sorcha.Wallet.Portable in a fresh .NET 10 project and use DerivationPathBuilder + entities.

### Tests for User Story 1

- [ ] T035 [P] [US1] Create dependency verification test: build Sorcha.Wallet.Portable in isolation, verify zero references to Microsoft.EntityFrameworkCore, Npgsql, Microsoft.AspNetCore in `tests/Sorcha.Wallet.Core.Tests/PortablePackageDependencyTests.cs`
- [ ] T036 [P] [US1] Verify DerivationPathBuilder tests still pass after relocation: `dotnet test --filter "DerivationPathBuilder"` — update test project reference if needed in `tests/Sorcha.Wallet.Core.Tests/Sorcha.Wallet.Core.Tests.csproj`

### Implementation for User Story 1

- [ ] T037 [US1] Verify `Sorcha.Wallet.Portable.csproj` has correct PackageId, Description, and only portable dependencies (NBitcoin, Sorcha.Cryptography, Microsoft.Extensions.Logging.Abstractions). Run `dotnet pack src/Core/Sorcha.Wallet.Portable/` and inspect the .nupkg dependency tree.
- [ ] T038 [US1] Add `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` to Sorcha.Wallet.Portable.csproj for NuGet packaging. Verify `dotnet pack` produces a valid .nupkg.

**Checkpoint**: Wallet.Portable is independently packable with zero server dependencies.

---

## Phase 4: User Story 2 — Mobile Developer Consumes REST Client Package (Priority: P1)

**Goal**: HTTP client package provides all REST clients + SignalR helper, no gRPC deps.

**Independent Test**: Install Sorcha.ServiceClients.Http in a fresh .NET 10 project and register HTTP clients.

### Tests for User Story 2

- [ ] T039 [P] [US2] Create dependency verification test: build Sorcha.ServiceClients.Http in isolation, verify zero references to Grpc.Net.Client, Google.Protobuf in `tests/Sorcha.ServiceClients.Tests/HttpPackageDependencyTests.cs`
- [ ] T040 [P] [US2] Verify all existing ServiceClient tests still pass after file moves: `dotnet test tests/Sorcha.ServiceClients.Tests/` — update project references if needed.

### Implementation for User Story 2

- [ ] T041 [US2] Create `SorchaHubConnectionBuilder` — static helper that builds `HubConnection` with JWT `AccessTokenProvider`, exponential backoff reconnection (1s, 2s, 5s, 10s, 30s), and configurable hub URL in `src/Common/Sorcha.ServiceClients.Http/Hub/SorchaHubConnectionBuilder.cs`
- [ ] T042 [US2] Create `HttpServiceCollectionExtensions` — `AddHttpServiceClients(IServiceCollection, IConfiguration)` method that registers all HTTP clients (auth, wallet, register, blueprint, participant, validator, events, subscription, passkey) and DID resolvers. Follow existing registration pattern but exclude gRPC clients. In `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs`
- [ ] T043 [US2] Update existing `ServiceCollectionExtensions.cs` in `src/Common/Sorcha.ServiceClients/Extensions/` to call `AddHttpServiceClients()` first, then add gRPC client registrations. Remove duplicated HTTP registrations. Existing `AddServiceClients()` method signature and behaviour unchanged.
- [ ] T044 [US2] Add `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` to Sorcha.ServiceClients.Http.csproj. Verify `dotnet pack` produces a valid .nupkg.
- [ ] T045 [P] [US2] Unit test for `SorchaHubConnectionBuilder`: verify JWT token provider is attached, verify reconnection policy is configured, verify hub URL is set in `tests/Sorcha.ServiceClients.Tests/Hub/SorchaHubConnectionBuilderTests.cs`

**Checkpoint**: HTTP client package independently packable with zero gRPC deps. SignalR helper works.

---

## Phase 5: User Story 3 — Automated Package Publishing (Priority: P1)

**Goal**: GitHub Actions publishes all 9 packages to NuGet.org on merge/tag.

**Independent Test**: Push to master, verify packages appear on NuGet.org.

### Implementation for User Story 3

- [ ] T046 [US3] Create GitHub Actions workflow at `.github/workflows/publish-nuget.yml` — triggers on push to master and tag push (v*). Steps: checkout, setup .NET 10, restore, build Release, test, pack all 9 projects, push to NuGet.org with `${{ secrets.NUGET_API_KEY }}`.
- [ ] T047 [US3] Configure versioning in workflow: tags → stable version (strip `v` prefix), master → `1.0.0-ci.${{ github.run_number }}`. Set version via `-p:Version=` in dotnet pack.
- [ ] T048 [US3] Add `<PackageId>` and `<Description>` to all 7 existing package .csproj files that will be published: Sorcha.Cryptography, Sorcha.Blueprint.Models, Sorcha.Register.Models, Sorcha.Tenant.Models, Sorcha.TransactionHandler, Sorcha.Validator.Core, Sorcha.Blueprint.Engine. Also add `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`. Verify each with `dotnet pack`.
- [ ] T049 [US3] Test the workflow locally: run `dotnet pack --configuration Release --output ./nupkgs` for all 9 projects and verify .nupkg files are created with correct metadata (`dotnet nuget inspect` or unzip).

**Checkpoint**: Pipeline tested locally. All 9 packages produce valid .nupkg files.

---

## Phase 6: User Story 4 — Existing Server Projects Continue Working (Priority: P1)

**Goal**: Zero regressions. All existing tests pass. No source changes in consumer projects.

**Independent Test**: Full solution build + complete test suite.

### Tests for User Story 4

- [ ] T050 [US4] Full solution build: `dotnet build --force` — all projects compile with zero errors after extraction.
- [ ] T051 [US4] Full test suite: `dotnet test` — all 638+ tests pass. Report any failures with file and test name.
- [ ] T052 [US4] Verify transitive references work: check that `Sorcha.Wallet.Service` can still access all wallet entities and enums through `Sorcha.Wallet.Core → Sorcha.Wallet.Portable` chain. Check that all 9 ServiceClients consumer projects access HTTP clients through `Sorcha.ServiceClients → Sorcha.ServiceClients.Http` chain.
- [ ] T053 [US4] Verify Docker build: `docker-compose build` — all service containers build successfully.

**Checkpoint**: Complete backward compatibility confirmed. Zero regressions.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and MOB task tracking updates

- [ ] T054 [P] Update CLAUDE.md project structure section to include Sorcha.Wallet.Portable and Sorcha.ServiceClients.Http in the project tree
- [ ] T055 [P] Update `.specify/MASTER-TASKS.md` — mark MOB-001 as ❌ Eliminated (SorchaMobile is .NET 10), MOB-002 as ✅ Complete, MOB-003 as ✅ Complete, MOB-004 as ✅ Complete
- [ ] T056 [P] Update `docs/reference/development-status.md` with Feature 084 completion
- [ ] T057 [P] Add Sorcha.Wallet.Portable and Sorcha.ServiceClients.Http to the Wallet Service README at `src/Services/Sorcha.Wallet.Service/README.md`
- [ ] T058 [P] Verify `.gitignore` includes `*.nupkg` and `nupkgs/` to prevent committing built packages
- [ ] T059 Run quickstart.md validation: execute `dotnet pack` for all 9 projects, verify .nupkg contents match documented package list

**Checkpoint**: Feature complete. Documentation synced. Ready for PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — create project files
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories (file moves)
- **US1 (Phase 3)**: Depends on Phase 2 — verify Wallet.Portable independence
- **US2 (Phase 4)**: Depends on Phase 2 — verify Http independence + add new code
- **US3 (Phase 5)**: Depends on Phases 3 + 4 — pipeline packs all packages
- **US4 (Phase 6)**: Depends on Phase 2 — can run in parallel with Phases 3-5
- **Polish (Phase 7)**: Depends on all stories complete

### User Story Independence

- **US1 + US2 can run in parallel** after Phase 2 — different projects
- **US3 depends on US1 + US2** — needs both packages packable
- **US4 can run in parallel** with US1/US2 — it's just build + test verification

### Within Phase 2 (Foundational)

- T008-T014 (Wallet.Core → Portable) and T018-T030 (ServiceClients → Http) can run **in parallel** — different project trees
- T015 (DerivationPathBuilder move) must happen after T008 (entity moves)
- T033-T034 (build + test verification) must happen LAST

### Parallel Opportunities

- Phase 2: Wallet moves (T008-T017) parallel with ServiceClient moves (T018-T032)
- Phase 2: Within ServiceClients, T020-T028 are all [P] (different subdirectories)
- Phase 3 + Phase 4 + Phase 6: All can run in parallel after Phase 2
- Phase 7: All tasks are [P]

---

## Parallel Example: Phase 2 (Foundational)

```
# Stream A: Wallet extraction
T008-T014: Move entities, enums, interfaces, exceptions, constants, events
T015: Move DerivationPathBuilder
T016-T017: Update Wallet.Core .csproj

# Stream B: ServiceClients extraction (parallel with Stream A)
T018-T019: Move Auth + Wallet clients
T020-T028: Move all other clients [P] (9 directories simultaneously)
T029-T030: Move Helpers + Models
T031-T032: Update ServiceClients .csproj

# Then sequentially:
T033: Build verification
T034: Test verification
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Project scaffolding
2. Complete Phase 2: File moves (CRITICAL)
3. Complete Phase 3: Verify Wallet.Portable is self-contained
4. **STOP and VALIDATE**: Build green, all tests pass, .nupkg has zero server deps

### Incremental Delivery

1. Setup + Foundational → Files moved, build green
2. US1 → Wallet.Portable verified → Packable
3. US2 → ServiceClients.Http verified + SignalR helper → Packable
4. US3 → Pipeline publishes all 9 packages
5. US4 → Full regression check + Docker build
6. Polish → Docs, MOB task updates

---

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 59 |
| Phase 1 (Setup) | 7 tasks |
| Phase 2 (Foundational) | 27 tasks |
| US1 — Wallet Package (P1) | 4 tasks |
| US2 — HTTP Client Package (P1) | 7 tasks |
| US3 — NuGet Pipeline (P1) | 4 tasks |
| US4 — Backward Compat (P1) | 4 tasks |
| Phase 7 (Polish) | 6 tasks |
| Parallel opportunities | Phase 2 dual-stream, Phase 3+4+6 parallel, Phase 7 all |
| MVP scope | US1 (Phases 1-3) |
