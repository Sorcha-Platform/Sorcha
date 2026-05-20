---
description: "Task list for CLI API Surface Catch-Up"
---

# Tasks: CLI API Surface Catch-Up

**Input**: Design documents from `/specs/133-cli-api-coverage/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: REQUIRED. FR-027 mandates automated tests for every new command, following the established CLI testing pattern (reflection-based static-handler invocation with mocked Refit clients; see `tests/Sorcha.Cli.Tests/Commands/*CommandsTests.cs`).

**Organization**: Grouped by user story. Phase 1 of the feature = US1–US4 (P1/P2). Phase 2 = US5–US9 (P3). Each story is an independently shippable increment.

## Path Conventions

- CLI app: `src/Apps/Sorcha.Cli/` — `Commands/`, `Services/` (Refit interfaces), `Models/`, `Program.cs`
- Tests: `tests/Sorcha.Cli.Tests/Commands/`
- Shared client (reuse target): `src/Common/Sorcha.ServiceClients.Http/`
- Command reference doc: `.claude/skills/sorcha-cli/references/commands.md` (the canonical CLI command reference, per the sorcha-cli skill `See Also`)
- Skill doc: `.claude/skills/sorcha-cli/SKILL.md`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wiring needed before any new command is built.

- [ ] T001 Add a ProjectReference to `Sorcha.ServiceClients.Http` in `src/Apps/Sorcha.Cli/Sorcha.Cli.csproj` (required so US4 can reuse the shared `IWalletServiceClient` org-key methods)
- [X] T002 Establish a green baseline: build `src/Apps/Sorcha.Cli` and run `dotnet test tests/Sorcha.Cli.Tests` — record current pass count so regressions are detectable

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting infrastructure and the rule that governs all client work.

**⚠️ CRITICAL**: Complete before user-story work begins.

- [ ] T003 Register the shared `Sorcha.ServiceClients.Http` `IWalletServiceClient` in CLI dependency injection so commands can resolve it via the factory, in `src/Apps/Sorcha.Cli/Services/HttpClientFactory.cs` (and `Program.cs` `ConfigureServices` if a Refit registration is needed)
- [X] T004 Document the selective-reuse rule (research R-001) in `.claude/skills/sorcha-cli/SKILL.md`: before adding a CLI Refit method, check `Sorcha.ServiceClients.Http` for the capability; reuse if present, add a thin CLI Refit method only for operator/admin endpoints absent from the shared library

**Checkpoint**: Foundation ready — user stories can proceed (in priority order, or in parallel by area).

---

## Phase 3: User Story 1 - Verify and revoke transactions (Priority: P1) 🎯 MVP

**Goal**: Generate/verify inclusion proofs, revoke a transaction with a reason, and report correct lifecycle status.

**Independent Test**: `transaction proof <txId> --register <id> --out f.json` → `transaction verify-proof --file f.json` (valid) → `transaction revoke <txId> --reason Erroneous` → `transaction status <txId>` reports `Revoked`.

### Tests for User Story 1 ⚠️

- [X] T005 [P] [US1] Add tests for `proof`, `verify-proof`, `revoke`, and the corrected `status` (asserting `TransactionLifecycleStatus` parsing, not `SubmitTransactionResponse`) in `tests/Sorcha.Cli.Tests/Commands/TransactionCommandsTests.cs`

### Implementation for User Story 1

- [X] T006 [P] [US1] Add transaction trust DTOs — `MerkleInclusionProof`, `MerkleProofStep`, `VerifyMerkleInclusionProofRequest`, `VerifyProofResult`, `RevokeTransactionRequest`, `RevocationReason` enum, `TransactionStatusResponse`, `TransactionLifecycleStatus` enum — in `src/Apps/Sorcha.Cli/Models/ActionModels.cs` or a new `src/Apps/Sorcha.Cli/Models/TransactionTrust.cs`
- [X] T007 [US1] Add `GetInclusionProofAsync`, `VerifyInclusionProofAsync`, `RevokeTransactionAsync` and **re-type** `GetTransactionStatusAsync` to return `TransactionStatusResponse` in `src/Apps/Sorcha.Cli/Services/IRegisterServiceClient.cs`
- [X] T008 [US1] Add `proof`, `verify-proof`, `revoke` subcommands and fix the `status` subcommand output in `src/Apps/Sorcha.Cli/Commands/TransactionCommands.cs` (honour global output flags; `--out` writes proof JSON; `revoke` requires `--reason`, validates `Superseded` needs `--superseded-by`)
- [X] T009 [US1] Add the four commands to the CLI command reference in `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US1 fully functional; the latent status-DTO bug (SC-002) is fixed.

---

## Phase 4: User Story 2 - Diagnose register sync state (Priority: P1)

**Goal**: Report a node's derived relationship, a register's sync state, and overall recovery sync health.

**Independent Test**: `register relationship <id>`, `register sync-state <id>`, `register sync-health` each return the expected diagnostic shape for a participating register.

### Tests for User Story 2 ⚠️

- [ ] T010 [P] [US2] Add tests for `relationship`, `sync-state`, `sync-health` in `tests/Sorcha.Cli.Tests/Commands/RegisterCommandsTests.cs`

### Implementation for User Story 2

- [ ] T011 [P] [US2] Add `RegisterLocalRelationship`, `RegisterSyncStateView`, `SyncHealthResponse`, `RegisterSyncStatus` DTOs (mirror `Sorcha.Register.Models` shapes; reference the models package if it is already a CLI dependency) in `src/Apps/Sorcha.Cli/Models/RegisterPolicy.cs` or a new `src/Apps/Sorcha.Cli/Models/RegisterSync.cs`
- [ ] T012 [US2] Add `GetLocalRelationshipAsync`, `GetSyncStateAsync`, `GetSyncHealthAsync` (route `/health/sync`) to `src/Apps/Sorcha.Cli/Services/IRegisterServiceClient.cs`
- [ ] T013 [US2] Add `relationship`, `sync-state`, `sync-health` subcommands in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs` (`sync-health` takes no register arg; table = one row per register)
- [ ] T014 [US2] Add the three commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 3 - Manage the validator roster (Priority: P1)

**Goal**: Self-register a validator and run the full roster lifecycle (count, audit, suspend, reactivate, revoke, sequence) beyond the existing approve/reject.

**Independent Test**: `validator register …` → `validator count` shows it → `validator suspend …` → `validator reactivate …` → `validator audit` shows the transitions.

### Tests for User Story 3 ⚠️

- [ ] T015 [P] [US3] Add tests for `register`, `count`, `audit`, `suspend`, `reactivate`, `revoke`, `sequence` in `tests/Sorcha.Cli.Tests/Commands/ValidatorCommandsTests.cs`

### Implementation for User Story 3

- [ ] T016 [P] [US3] Add roster DTOs — `RegisterValidatorRequest`/`Response`, `ValidatorCountResponse`, `ValidatorAuditResponse`/`ValidatorAuditEntry`, `SuspendValidatorRequest`, `ReactivateValidatorRequest`, `RevokeValidatorRequest`, `ValidatorSequenceResponse` — in `src/Apps/Sorcha.Cli/Models/Validator.cs`
- [ ] T017 [US3] Add `RegisterValidatorAsync`, `GetValidatorCountAsync`, `GetValidatorAuditAsync`, `SuspendValidatorAsync`, `ReactivateValidatorAsync`, `RevokeValidatorAsync`, `GetValidatorSequenceAsync` to `src/Apps/Sorcha.Cli/Services/IValidatorServiceClient.cs` (new `/api/validators/...` routes — distinct from existing `/api/admin/validators/...`)
- [ ] T018 [US3] Add `register`, `count`, `audit`, `suspend`, `reactivate`, `revoke`, `sequence` subcommands in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs` (`suspend`/`revoke` require explicit `<validatorId>` and `--reason`)
- [ ] T019 [US3] Add the seven commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: All three P1 stories independently functional — operator core complete.

---

## Phase 6: User Story 4 - Provision and rotate org keys (Priority: P2)

**Goal**: Provision an org master key (mnemonic shown once), derive a user key, rotate and revoke derived keys — **reusing the shared client, no CLI DTOs**.

**Independent Test**: `wallet org-key provision <orgId>` → `derive --user-id alice --usage Identity` → `rotate <orgId> <keyId>` → `revoke <orgId> <keyId>`.

### Tests for User Story 4 ⚠️

- [ ] T020 [P] [US4] Add tests for the `org-key` subcommands (mock the injected shared `IWalletServiceClient`; assert mnemonic surfaced once and not logged) in `tests/Sorcha.Cli.Tests/Commands/WalletCommandsTests.cs`

### Implementation for User Story 4

- [ ] T021 [US4] Add a `wallet org-key` subcommand group (`provision`/`derive`/`rotate`/`revoke`) in `src/Apps/Sorcha.Cli/Commands/WalletCommands.cs` that injects and calls the shared `Sorcha.ServiceClients.Http` `IWalletServiceClient` (`ProvisionOrgMasterKeyAsync`/`DeriveOrgKeyAsync`/`RotateOrgKeyAsync`/`RevokeOrgKeyAsync`) — do NOT add a CLI Refit method or DTO; `provision` prints the mnemonic once with a "not stored" warning and never writes it to the token cache or logs
- [ ] T022 [US4] Add the four `org-key` commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: Feature Phase 1 (operator/automation core) complete — shippable.

---

## Phase 7: User Story 5 - Inspect wallet internals (Priority: P3)

**Goal**: Read a wallet's DID document, gap status, accounts, addresses, delegations.

**Independent Test**: Each `wallet did-document|gap-status|accounts|addresses|delegations <address>` returns the expected shape.

### Tests for User Story 5 ⚠️

- [ ] T023 [P] [US5] Add tests for the five wallet-diagnostic subcommands in `tests/Sorcha.Cli.Tests/Commands/WalletCommandsTests.cs`

### Implementation for User Story 5

- [ ] T024 [US5] Confirm exact response shapes by reading `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` and `DelegationEndpoints.cs`, then add `GapStatusResponse`, account/address/delegation DTOs in `src/Apps/Sorcha.Cli/Models/Wallet.cs`
- [ ] T025 [US5] Add `GetWalletDidDocumentAsync`, `GetWalletGapStatusAsync`, `ListWalletAccountsAsync`, `ListWalletAddressesAsync`, `ListWalletDelegationsAsync` to `src/Apps/Sorcha.Cli/Services/IWalletServiceClient.cs`
- [ ] T026 [US5] Add `did-document`, `gap-status`, `accounts`, `addresses`, `delegations` subcommands in `src/Apps/Sorcha.Cli/Commands/WalletCommands.cs`
- [ ] T027 [US5] Add the five commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US5 functional.

---

## Phase 8: User Story 6 - Govern the system register (Priority: P3)

**Goal**: Initialise, publish a blueprint to the system register, classify a change, and list blueprint versions.

**Independent Test**: `system-register publish --blueprint bp.json --blueprint-id my-bp` → `system-register versions my-bp` lists it.

### Tests for User Story 6 ⚠️

- [ ] T028 [P] [US6] Add tests for `initialize`, `publish`, `classify-change`, `versions` in `tests/Sorcha.Cli.Tests/Commands/SystemRegisterGovernanceCommandTests.cs`

### Implementation for User Story 6

- [ ] T029 [P] [US6] Add `PublishBlueprintRequest`/`Response`, `ClassifyChangeRequest`/`Response`, `BlueprintVersionsResponse`/`BlueprintVersion` DTOs in `src/Apps/Sorcha.Cli/Models/Blueprint.cs`
- [ ] T030 [US6] Add `InitializeSystemRegisterAsync`, `PublishSystemBlueprintAsync`, `ClassifySystemBlueprintChangeAsync`, `GetSystemBlueprintVersionsAsync` to `src/Apps/Sorcha.Cli/Services/IRegisterServiceClient.cs`
- [ ] T031 [US6] Add `initialize`, `publish`, `classify-change`, `versions` subcommands in `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` (`publish`/`classify-change` read a blueprint JSON file)
- [ ] T032 [US6] Add the four commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US6 functional.

---

## Phase 9: User Story 7 - Administer citizen wallet devices (Priority: P3)

**Goal**: List the signed-in account's devices and revoke one.

**Independent Test**: `device list` → `device revoke <deviceId>` → device shows as revoked on next `device list`.

### Tests for User Story 7 ⚠️

- [ ] T033 [P] [US7] Add tests for `device list` and `device revoke` in `tests/Sorcha.Cli.Tests/Commands/DeviceCommandsTests.cs`

### Implementation for User Story 7

- [ ] T034 [P] [US7] Add `DeviceListResponse`, `DeviceSummary`, `DeviceStatus` enum DTOs in a new `src/Apps/Sorcha.Cli/Models/Device.cs`
- [ ] T035 [US7] Add `ListMyDevicesAsync` (`GET /api/v1/me/devices`) and `RevokeMyDeviceAsync` (`DELETE /api/v1/me/devices/{deviceId}`) to `src/Apps/Sorcha.Cli/Services/ITenantServiceClient.cs`
- [ ] T036 [US7] Create new command file `src/Apps/Sorcha.Cli/Commands/DeviceCommands.cs` with `list` and `revoke` subcommands (`revoke` requires explicit `<deviceId>`)
- [ ] T037 [US7] Register `DeviceCommand` in `src/Apps/Sorcha.Cli/Program.cs` `BuildRootCommand`
- [ ] T038 [US7] Add the two commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US7 functional.

---

## Phase 10: User Story 8 - Automate token and org context (Priority: P3)

**Goal**: List org memberships, switch active org (persisting the new token), and introspect the current token.

**Independent Test**: `auth orgs` → `auth switch-org <orgId>` → subsequent calls use the new org → `auth introspect` shows the switched claims.

### Tests for User Story 8 ⚠️

- [ ] T039 [P] [US8] Add tests for `orgs`, `switch-org` (assert token cache write), `introspect` in `tests/Sorcha.Cli.Tests/Commands/AuthCommandsTests.cs`

### Implementation for User Story 8

- [ ] T040 [P] [US8] Add `OrgMembershipListResponse`/`OrgMembershipEntry`, `SwitchOrgRequest`, `TokenIntrospectionRequest`/`Response` DTOs in `src/Apps/Sorcha.Cli/Models/User.cs` (reuse the existing CLI `TokenResponse`); check `Sorcha.ServiceClients.Http` `ITokenIntrospectionClient` for reuse before adding a Refit method
- [ ] T041 [US8] Add `ListMyOrganizationsAsync`, `SwitchOrgAsync`, `IntrospectTokenAsync` to `src/Apps/Sorcha.Cli/Services/ITenantServiceClient.cs`
- [ ] T042 [US8] Add `orgs`, `switch-org`, `introspect` subcommands in `src/Apps/Sorcha.Cli/Commands/AuthCommands.cs`; `switch-org` persists the re-issued `TokenResponse` via the existing token-cache path used by `AuthLoginCommand` (research R-006); `introspect` defaults to the cached token. Do NOT add an `auth refresh` command (FR-022)
- [ ] T043 [US8] Add the three commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: US8 functional.

---

## Phase 11: User Story 9 - Administer org trust anchors (Priority: P3)

**Goal**: Provision/get a tenant trust anchor, enrol/cert-chain/revoke an org, read the CRL (corrected scope, research R-003).

**Independent Test**: `trust anchor get <tenantId>`, `trust org cert-chain <tenantId> <orgAddr>`, `trust crl <tenantId>` return the expected PKI artefacts.

### Tests for User Story 9 ⚠️

- [ ] T044 [P] [US9] Add tests for `anchor provision/get`, `org enrol/cert-chain/revoke`, `crl` in `tests/Sorcha.Cli.Tests/Commands/TrustCommandsTests.cs`

### Implementation for User Story 9

- [ ] T045 [US9] Confirm exact shapes from `src/Services/Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs`; check `Sorcha.ServiceClients.Http/Trust/IOrgCertChainProvider` for reuse, then add the trust DTOs in a new `src/Apps/Sorcha.Cli/Models/Trust.cs`
- [ ] T046 [US9] Add the six trust methods (`/api/v1/trust/tenants/{tenantId}/...`: provision, trust-anchor, orgs/{addr}/enrol, orgs/{addr}/cert-chain, orgs/{addr}/revoke, crl) to `src/Apps/Sorcha.Cli/Services/ITenantServiceClient.cs` (or a dedicated `ITrustServiceClient.cs`)
- [ ] T047 [US9] Create new command file `src/Apps/Sorcha.Cli/Commands/TrustCommands.cs` with `anchor provision/get`, `org enrol/cert-chain/revoke`, `crl` subcommands (`org revoke` requires explicit tenant + org)
- [ ] T048 [US9] Register `TrustCommand` in `src/Apps/Sorcha.Cli/Program.cs` `BuildRootCommand`
- [ ] T049 [US9] Add the six commands to `.claude/skills/sorcha-cli/references/commands.md`

**Checkpoint**: All nine user stories independently functional.

---

## Phase 12: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Refresh `.claude/skills/sorcha-cli/SKILL.md` with the full new command surface (all areas) so it is discoverable (FR-026)
- [ ] T051 [P] Record the intentionally-excluded surfaces (persona, file-chunk upload, presentation lifecycle, enrolment/pairing, HAIP protocol, `/api/internal/*`) in `.claude/skills/sorcha-cli/references/commands.md` so the omission is a documented decision (FR-029 / SC-008)
- [ ] T052 Verify every new command honours global options (`--output table|json|csv|yaml`, `--machine-readable`, `--quiet`, `--verbose`) and the standard exit codes (FR-024/FR-025) — spot-check across areas
- [ ] T053 Run `dotnet test tests/Sorcha.Cli.Tests` — all green, new-code coverage ≥ 85% (Constitution IV)
- [ ] T054 Walk `specs/133-cli-api-coverage/quickstart.md` against a running platform (docker-compose or n1 profile) and confirm SC-001…SC-008

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. T003 blocks US4 (org-key reuse). T004 (rule) guides all stories.
- **User Stories (Phases 3–11)**: depend on Foundational. US1, US2, US3 (all P1) are independent and may run in parallel. US4 depends on T003. US5–US9 (P3) are independent of each other.
- **Polish (Phase 12)**: depends on all targeted stories.

### Within Each User Story

- Tests written first and failing → DTOs (models) → client (Refit) method → command class → `Program.cs` registration (new command files only) → docs entry.
- Models can be authored in parallel with the failing tests; client method depends on the DTOs; command depends on the client method.

### Parallel Opportunities

- T005/T006 (US1), T010/T011 (US2), T015/T016 (US3) — tests + DTOs per story run in parallel (different files).
- The three P1 stories (US1/US2/US3) can be worked by three people simultaneously after Phase 2.
- US5–US9 are mutually independent once Phase 2 is done.
- Note serialisation: US2, US6 both edit `IRegisterServiceClient.cs`; US7, US8, US9 may edit `ITenantServiceClient.cs`; US4, US5 both edit `WalletCommands.cs`. Sequence these within the shared file or split clients to parallelise.

---

## Parallel Example: User Story 1

```bash
# Author the failing tests and the DTOs together (different files):
Task: "T005 [US1] tests in tests/Sorcha.Cli.Tests/Commands/TransactionCommandsTests.cs"
Task: "T006 [US1] trust DTOs in src/Apps/Sorcha.Cli/Models/TransactionTrust.cs"
# Then sequentially: T007 (client) → T008 (command) → T009 (docs)
```

---

## Implementation Strategy

### MVP First (operator core)

1. Setup (Phase 1) + Foundational (Phase 2).
2. US1 (Phase 3) → **STOP & VALIDATE** (proof/verify/revoke + status fix — SC-001/SC-002). This alone is a shippable correctness fix + capability.
3. US2 + US3 → operator core complete (SC-003/SC-004).
4. US4 → org-key automation (SC-005). **Feature Phase 1 done — ship.**

### Incremental Delivery

- Ship after Phase 1 stories (US1–US4). Then add US5–US9 one at a time, each independently testable and documented, as Feature Phase 2.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- Every command lands with: test + DTO/reuse + client/reuse + command + (registration) + docs entry — this is what SC-006 checks.
- US4 and the org-key path must NOT duplicate the shared client (SC-007); inject `Sorcha.ServiceClients.Http` `IWalletServiceClient`.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
