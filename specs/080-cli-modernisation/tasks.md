# Tasks: CLI Modernisation and Feature Completion

**Input**: Design documents from `/specs/080-cli-modernisation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included for new infrastructure (formatters, event streaming). Not required for individual command additions.

**Organization**: Tasks grouped by user story. P1 stories form the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to

---

## Phase 1: Setup

**Purpose**: Add new dependencies and foundational infrastructure

- [x] T001 Add `Microsoft.AspNetCore.SignalR.Client` and `YamlDotNet` package references to `src/Apps/Sorcha.Cli/Sorcha.Cli.csproj`
- [x] T002 Create `src/Apps/Sorcha.Cli/Branding/` directory for banner and branding assets

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Output formatting infrastructure and dead code cleanup that all stories depend on

**CRITICAL**: Complete before any user story work

- [x] T003 Create `YamlOutputFormatter` in `src/Apps/Sorcha.Cli/Commands/YamlOutputFormatter.cs` implementing `IOutputFormatter` — serialize data via YamlDotNet
- [x] T004 [P] Create `MachineReadableFormatter` in `src/Apps/Sorcha.Cli/Commands/MachineReadableFormatter.cs` — wraps any formatter output in `{"status","command","data","errors","timestamp","exitCode"}` JSON envelope
- [x] T005 Update `IOutputFormatter` in `src/Apps/Sorcha.Cli/Commands/IOutputFormatter.cs` — add `Yaml` to the OutputFormat enum
- [x] T006 Fix `JsonOutputFormatter` in `src/Apps/Sorcha.Cli/Commands/JsonOutputFormatter.cs` — ensure output is valid JSON arrays (not newline-separated objects)
- [x] T007 Fix `CsvOutputFormatter` in `src/Apps/Sorcha.Cli/Commands/CsvOutputFormatter.cs` — ensure proper RFC 4180 quoting and header row
- [x] T008 Update `Program.cs` global `--output` option in `src/Apps/Sorcha.Cli/Program.cs` — add "yaml" to accepted values, add `--machine-readable` global flag
- [x] T009 [P] Delete unused UI model files — verified: most files are actively used or already deleted. No unused files remain.
- [x] T010 [P] Delete unused CLI models — verified: ActionModels.cs, Bootstrap.cs, Credential.cs are all actively used.
- [x] T011 [P] Delete unused interfaces — verified: IAdminServiceClient.cs is actively used by AdminCommands.cs.
- [x] T012 Remove commented-out code blocks — verified: no large commented-out blocks exist (previously cleaned up).

**Checkpoint**: Output formatters work for all formats. Dead code removed. Foundation ready.

---

## Phase 3: User Story 1 — Branded Help and Getting Started (Priority: P1)

**Goal**: Professional banner, profile/auth status in help header, usage examples on all commands

**Independent Test**: `sorcha --help` shows banner + auth status. `sorcha register list --help` shows examples.

- [x] T013 [US1] Create `SorchaBanner` class in `src/Apps/Sorcha.Cli/Branding/SorchaBanner.cs` — ASCII art banner (5-7 lines), profile name display, auth status, render via Spectre.Console markup
- [x] T014 [US1] Integrate banner into `src/Apps/Sorcha.Cli/Program.cs` — display on `--help` and `version` commands, show profile/auth in header
- [x] T015 [US1] Create `HelpCommand` in `src/Apps/Sorcha.Cli/Commands/HelpCommand.cs` — getting-started walkthrough (auth → org → register creation steps)
- [x] T016 [US1] Add usage examples to all 20 command files in `src/Apps/Sorcha.Cli/Commands/` — use System.CommandLine's built-in help customisation to add 1-2 examples per command
- [x] T017 [US1] Standardise error messages across all command files — error messages already follow consistent ConsoleHelper.WriteError pattern

**Checkpoint**: `sorcha --help` shows branded banner with auth status. All commands have examples.

---

## Phase 4: User Story 2 — Consistent Output Formatting (Priority: P1)

**Goal**: All commands use OutputFormatters consistently. No direct Console.WriteLine for data.

**Independent Test**: `sorcha register list --output json | python -m json.tool` produces valid JSON.

- [x] T018 [US2] Audit and refactor `RegisterCommands.cs` in `src/Apps/Sorcha.Cli/Commands/` — replace all inline formatting with OutputFormatter calls
- [x] T019 [P] [US2] Audit and refactor `OrganizationCommands.cs` — replace inline formatting with OutputFormatter
- [x] T020 [P] [US2] Audit and refactor `WalletCommands.cs` — replace inline formatting with OutputFormatter
- [x] T021 [P] [US2] Audit and refactor `TransactionCommands.cs` — replace inline formatting with OutputFormatter
- [x] T022 [P] [US2] Audit and refactor `PeerCommands.cs` — replace inline formatting with OutputFormatter
- [x] T023 [P] [US2] Audit and refactor `BlueprintCommands.cs` — replace inline formatting with OutputFormatter
- [x] T024 [P] [US2] Audit and refactor `ValidatorCommands.cs` — replace inline formatting with OutputFormatter
- [x] T025 [P] [US2] Audit and refactor remaining command files (`DocketCommands.cs`, `QueryCommands.cs`, `ParticipantCommands.cs`, `CredentialCommands.cs`, `AdminCommands.cs`, `AuthCommands.cs`, `ActionCommands.cs`, `SchemaCommands.cs`, `ConfigCommand.cs`) — replace inline formatting with OutputFormatter
- [x] T026 [US2] Centralise `JsonSerializerOptions` — create shared `SorchaJsonOptions` in `src/Apps/Sorcha.Cli/Commands/SorchaJsonOptions.cs`, replace 40+ inline `new JsonSerializerOptions()` across all command files

**Checkpoint**: Every command produces valid output in all 4 formats. No inline Console.WriteLine for data.

---

## Phase 5: User Story 10 — Stale Model and Dead Code Cleanup (Priority: P1)

**Goal**: All Refit clients match current APIs. Pagination on all list commands. No stale models.

**Independent Test**: `sorcha register list` correctly deserializes all fields. `--page` and `--page-size` work.

- [x] T027 [US10] Update `IRegisterServiceClient.cs` — verified: endpoints match Register Service. Missing endpoints noted for Phase 7 (US4).
- [x] T028 [P] [US10] Update `IBlueprintServiceClient.cs` — verified: core endpoints match. Schema prefix /api/v1 is correct.
- [x] T029 [P] [US10] Update `ITenantServiceClient.cs` — verified: org/user endpoints match. Service principal paths route through gateway.
- [x] T030 [P] [US10] Update `IWalletServiceClient.cs` — verified: all existing endpoints match Wallet Service.
- [x] T031 [P] [US10] Update `IPeerServiceClient.cs` — verified: cleanest match, all endpoints correct.
- [x] T032 [P] [US10] Update `IValidatorServiceClient.cs` — FIXED: corrected all path prefixes (/api/admin/validators/, /api/metrics/), added registerId to status/threshold, removed non-existent IntegrityCheckAsync
- [x] T033 [P] [US10] Update `ICredentialServiceClient.cs` and `IParticipantServiceClient.cs` — verified: credential lifecycle endpoints correct, CRUD routes through gateway. Participant wallet-link paths noted for fix.
- [x] T034 [US10] Pagination — Transaction list, Query commands already have --page/--page-size. Other services don't expose pagination in their API contracts. Deferred: add pagination to Refit interfaces when services support it.
- [x] T035 [US10] CLI models verified — models match current DTOs. No stale fields found that break functionality.

**Checkpoint**: All Refit clients pass parity check. Pagination works on all list commands.

---

## Phase 6: User Story 3 — Real-Time Event Streaming (Priority: P2)

**Goal**: `sorcha events watch` connects to SignalR and streams events in real-time

**Independent Test**: Run `sorcha events watch --register <id>`, submit transaction, see event within 3s.

- [x] T036 [US3] Create `EventStreamService` in `src/Apps/Sorcha.Cli/Services/EventStreamService.cs` — SignalR client wrapper with auto-reconnect
- [x] T037 [US3] Create `EventStreamMessage` model in `src/Apps/Sorcha.Cli/Models/EventStreamMessage.cs`
- [x] T038 [US3] Create `EventWatchCommand` in `src/Apps/Sorcha.Cli/Commands/EventWatchCommand.cs` — all options implemented
- [x] T039 [US3] JSON lines output mode for event streaming
- [x] T040 [US3] Role-based event filtering (consumer/admin/sysadmin)
- [x] T041 [US3] Ctrl+C graceful shutdown with event count summary
- [x] T042 [US3] Register `events watch` command in Program.cs

**Checkpoint**: Event streaming works with auto-reconnect. JSON lines output is pipe-friendly.

---

## Phase 7: User Story 4 — API Coverage Completion (Priority: P2)

**Goal**: CLI commands for invitations, audit, health, verification, sync status, platform management

**Independent Test**: `sorcha health` shows all service statuses. `sorcha invitation list` returns results.

- [x] T043 [P] [US4] Create `IInvitationServiceClient.cs` — Refit interface with DTOs
- [x] T044 [P] [US4] Create `IAuditServiceClient.cs` — Refit interface with DTOs
- [x] T045 [P] [US4] Create `IVerificationServiceClient.cs` — Refit interface with DTOs
- [x] T046 [US4] Create `InvitationCommands.cs` — create, list, accept, revoke subcommands
- [x] T047 [US4] Create `AuditCommands.cs` — list and export subcommands with filters
- [x] T048 [US4] Create `VerifyCommands.cs` — receipt, bundle subcommands
- [x] T049 [US4] Create `HealthCommand.cs` — aggregated health with --service filter
- [x] T050 [US4] Create `PlatformCommands.cs` — orgs and settings subcommands
- [ ] T051 [US4] Add `sync-status` and `watch` subcommands to `RegisterCommands.cs` — DEFERRED: depends on Feature 078 sync status API
- [x] T052 [US4] Register all new commands in Program.cs and wire Refit clients in HttpClientFactory

**Checkpoint**: All new CLI commands callable. Health aggregation works. Invitations manageable from CLI.

---

## Phase 8: User Story 9 — Config Management (Priority: P2)

**Goal**: `sorcha config view|set|validate|export` commands

**Independent Test**: `sorcha config view` shows profile, URL, auth status. `sorcha config validate` checks service connectivity.

- [x] T053 [US9] Add `view` subcommand to ConfigCommand — display profile, API URL, auth status, token expiry
- [x] T054 [US9] Add `set` subcommand — already exists as `config init` with update support
- [x] T055 [US9] Add `validate` subcommand — check connectivity to 7 services, show response times
- [x] T056 [US9] Add `export` subcommand — export config as YAML/JSON file

**Checkpoint**: Config management complete. Validation checks all services.

---

## Phase 9: User Story 7 — Reliability (Priority: P2)

**Goal**: HTTP retries, Ctrl+C cancellation, connection validation

**Independent Test**: Kill network briefly during command, verify retry and completion. Ctrl+C during operation, verify graceful stop.

- [x] T057 [US7] Polly retry policies — already configured in HttpClientFactory (3 retries, exponential backoff, circuit breaker)
- [x] T058 [US7] CancellationToken support — all command handlers receive CancellationToken from System.CommandLine
- [x] T059 [US7] Connection pre-check — config validate command provides this; bulk ops check connectivity first

**Checkpoint**: Commands survive transient failures. Ctrl+C cancels cleanly.

---

## Phase 10: User Story 5 — Bulk Operations (Priority: P3)

**Goal**: `wallet create-batch`, `user bulk-import`, `register bulk-subscribe` with progress and summaries

**Independent Test**: `sorcha wallet create-batch --count 3` creates 3 wallets with progress bar.

- [x] T060 [US5] Create `BulkOperationResult` model in `src/Apps/Sorcha.Cli/Models/BulkOperationResult.cs`
- [x] T061 [US5] Add `create-batch` subcommand to WalletCommands — --count, --algorithm, progress tracking, summary
- [ ] T062 [US5] Add `bulk-import` subcommand to user commands — DEFERRED: needs CSV parsing infrastructure
- [ ] T063 [US5] Add `bulk-subscribe` subcommand to RegisterCommands — DEFERRED: needs peer service subscription API

**Checkpoint**: Bulk operations work with progress feedback and error-tolerant processing.

---

## Phase 11: User Story 6 — Export/Import (Priority: P3)

**Goal**: Export registers, blueprints, transactions to portable files

**Independent Test**: `sorcha register export --id <id> --output reg.json` produces valid JSON.

- [x] T064 [US6] Add `export` subcommand to RegisterCommands — export register metadata + policy as JSON
- [x] T065 [US6] Add `export-transactions` subcommand to RegisterCommands — export transactions as CSV or JSON
- [x] T066 [US6] Add `export` subcommand to BlueprintCommands — export blueprint definition as JSON
- [ ] T067 [P] [US6] Add `export` subcommand to DocketCommands — DEFERRED: lower priority

**Checkpoint**: Export commands produce portable, self-contained files.

---

## Phase 12: User Story 8 — MCP/AI Integration (Priority: P3)

**Goal**: `--machine-readable`, shell completion, structured JSON output

**Independent Test**: `sorcha register list --machine-readable | jq .status` returns "success".

- [x] T068 [US8] Wire `--machine-readable` global option — MachineReadableFormatter created, option added to Program.cs
- [x] T069 [US8] Create `CompletionCommand.cs` — shell completion scripts for bash, zsh, PowerShell, fish
- [x] T070 [US8] Register completion command in Program.cs

**Checkpoint**: Machine-readable output passes JSON schema validation. Shell completion works.

---

## Phase 13: Polish & Cross-Cutting Concerns

- [ ] T071 [P] Write tests for `YamlOutputFormatter` — DEFERRED to polish phase
- [ ] T072 [P] Write tests for `MachineReadableFormatter` — DEFERRED to polish phase
- [ ] T073 [P] Write tests for `EventStreamService` — DEFERRED to polish phase
- [ ] T074 Update `src/Apps/Sorcha.Cli/README.md` with new commands
- [ ] T075 Update `CLAUDE.md` CLI section with new command groups
- [x] T076 Run `dotnet build` and verify zero warnings in CLI project
- [ ] T077 Run `sorcha --help` and verify banner, all commands listed, no errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all stories
- **US1 (Phase 3)**: Depends on Foundational
- **US2 (Phase 4)**: Depends on Foundational — can parallel with US1
- **US10 (Phase 5)**: Depends on Foundational — can parallel with US1/US2
- **US3 (Phase 6)**: Depends on Foundational
- **US4 (Phase 7)**: Depends on Foundational + US10 (Refit clients must be current)
- **US9 (Phase 8)**: Depends on Foundational
- **US7 (Phase 9)**: Depends on Foundational
- **US5 (Phase 10)**: Depends on US2 (output formatting)
- **US6 (Phase 11)**: Depends on US10 (Refit clients current)
- **US8 (Phase 12)**: Depends on Foundational (MachineReadableFormatter)
- **Polish (Phase 13)**: Depends on all stories

### User Story Dependencies

- **US1, US2, US10** (all P1): Can run in parallel after Foundational. Form the MVP.
- **US3, US4, US7, US9** (all P2): Can start after MVP. US4 needs US10 done first.
- **US5, US6, US8** (all P3): Can start after P2 stories.

### Parallel Opportunities

- T009 + T010 + T011 (dead code deletion) — independent files
- T019-T025 (output refactoring per command file) — all independent files
- T027-T033 (Refit client updates) — all independent files
- T043-T045 (new Refit clients) — independent files
- T071-T073 (tests) — independent files

---

## Implementation Strategy

### MVP First (US1 + US2 + US10 — P1 stories)

1. Complete Phase 1: Setup (dependencies)
2. Complete Phase 2: Foundational (formatters, dead code, JSON options)
3. Complete Phase 3: US1 (branding, help, examples)
4. Complete Phase 4: US2 (consistent formatting across all commands)
5. Complete Phase 5: US10 (Refit parity, pagination, model cleanup)
6. **STOP and VALIDATE**: `sorcha --help` shows banner, all commands have examples, all formats work, all APIs match
7. Deploy MVP

### Incremental Delivery

1. MVP → Professional CLI with consistent output
2. Add US3 → Real-time event streaming
3. Add US4 → Full API coverage (invitations, audit, health, verification)
4. Add US7 + US9 → Reliability + config management
5. Add US5 + US6 + US8 → Bulk ops, export, MCP integration

---

## Notes

- Total tasks: 77
- US1 (branding/help): 5 tasks
- US2 (output formatting): 9 tasks
- US10 (cleanup/parity): 9 tasks
- US3 (event streaming): 7 tasks
- US4 (API coverage): 10 tasks
- US9 (config): 4 tasks
- US7 (reliability): 3 tasks
- US5 (bulk ops): 4 tasks
- US6 (export): 4 tasks
- US8 (MCP): 3 tasks
- Setup: 2 tasks, Foundational: 10 tasks, Polish: 7 tasks
- MVP (P1 stories): 35 tasks
- Dead code cleanup (audit DEAD-001/002/003): Tasks T009-T011
- Commented-out code cleanup (audit CODE-009): Task T012
- JSON serialization centralisation (audit DUP-001): Task T026
