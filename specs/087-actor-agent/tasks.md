# Tasks: Autonomous Actor Agent Framework

**Input**: Design documents from `/specs/087-actor-agent/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the Sorcha.Agent project and test project with all dependencies configured

- [x] T001 Create project file with dependencies in src/Apps/Sorcha.Agent/Sorcha.Agent.csproj (System.CommandLine, Sorcha.ServiceClients.Http, Sorcha.Blueprint.Models, Sorcha.Blueprint.Engine, Microsoft.Extensions.Http.Polly, Spectre.Console)
- [x] T002 Create test project in tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj (xUnit, FluentAssertions, Moq)
- [x] T003 Add both projects to Sorcha.sln and verify `dotnet build` succeeds
- [x] T004 Create ExitCodes.cs in src/Apps/Sorcha.Agent/ExitCodes.cs mirroring Sorcha.Cli exit codes (0=Success, 1=General, 2=Auth, 4=Validation, 6=Config, 7=Network, 8=Service)
- [x] T005 Create Program.cs entry point in src/Apps/Sorcha.Agent/Program.cs with System.CommandLine root command, DI container setup, and logging configuration

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, variable resolution, and configuration loading that ALL user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 [P] Create ActorDefinition and all nested config models (ActorIdentity, ConnectionConfig, CredentialsConfig, InboxConfig, SignalRConfig, PollingConfig, ActorRule, AiConfig, ResilienceConfig, LoggingConfig) in src/Apps/Sorcha.Agent/Configuration/ActorDefinition.cs per data-model.md
- [x] T007 [P] Create PendingAction record in src/Apps/Sorcha.Agent/Models/PendingAction.cs per data-model.md
- [x] T008 [P] Create ActionDecision record in src/Apps/Sorcha.Agent/Models/ActionDecision.cs per data-model.md
- [x] T009 [P] Create ActionAuditEntry record in src/Apps/Sorcha.Agent/Models/ActionAuditEntry.cs per data-model.md
- [x] T010 Create VariableResolver in src/Apps/Sorcha.Agent/Configuration/VariableResolver.cs — resolve `$env:VAR_NAME` from environment variables and `{{placeholder}}` from state.json dictionary. Return list of unresolved variables for diagnostics.
- [x] T011 [P] Write VariableResolverTests in tests/Sorcha.Agent.Tests/Configuration/VariableResolverTests.cs — test $env resolution (present/missing), {{placeholder}} resolution (present/missing/no state file), mixed resolution, edge cases (nested, escaped)
- [x] T012 Create ActorDefinitionLoader in src/Apps/Sorcha.Agent/Configuration/ActorDefinitionLoader.cs — load JSON file, deserialize to ActorDefinition, call VariableResolver, validate required fields (mode matches rules/ai block presence, at least one inbox channel enabled)
- [x] T013 [P] Write ActorDefinitionLoaderTests in tests/Sorcha.Agent.Tests/Configuration/ActorDefinitionLoaderTests.cs — test valid load, missing file, invalid JSON, unresolved vars, mode mismatch (rules mode with no rules array), no inbox channels enabled
- [x] T014 Create IDecisionEngine interface in src/Apps/Sorcha.Agent/Decision/IDecisionEngine.cs per contracts/cli-interface.md
- [x] T015 [P] Create IInboxListener interface in src/Apps/Sorcha.Agent/Inbox/IInboxListener.cs — `IAsyncEnumerable<PendingAction> ListenAsync(CancellationToken ct)`
- [x] T016 [P] Create IActionExecutor interface in src/Apps/Sorcha.Agent/Execution/IActionExecutor.cs — `Task<bool> ExecuteAsync(PendingAction action, ActionDecision decision, CancellationToken ct)`
- [x] T017 Create AuditLogger in src/Apps/Sorcha.Agent/Execution/AuditLogger.cs — append ActionAuditEntry as JSON line to configured file path, handle file creation and flush
- [x] T018 [P] Write AuditLoggerTests in tests/Sorcha.Agent.Tests/Execution/AuditLoggerTests.cs — test append, file creation, JSON format, concurrent writes

**Checkpoint**: Foundation ready — all models, interfaces, config loading, and variable resolution work. User story implementation can begin.

---

## Phase 3: User Story 1 - Run a Rules-Based Actor on Localhost (Priority: P1) MVP

**Goal**: A single actor authenticates, discovers pending actions via SignalR + polling, evaluates JSON Logic rules, and submits valid payloads autonomously.

**Independent Test**: Launch one actor against a single pending action in ConstructionPermit. Verify it discovers, evaluates, and submits.

### Tests for User Story 1

- [x] T019 [P] [US1] Write RulesDecisionEngineTests in tests/Sorcha.Agent.Tests/Decision/RulesDecisionEngineTests.cs — test: single rule match, first-match-wins with multiple rules, no match returns skip, condition evaluation against payload vars, action name filtering, approve/reject/skip decisions
- [x] T020 [P] [US1] Write CompositeInboxListenerTests in tests/Sorcha.Agent.Tests/Inbox/CompositeInboxListenerTests.cs — test: deduplication by action ID, merge from two sources, skip already-processed actions
- [x] T021 [P] [US1] Write ActionExecutorTests in tests/Sorcha.Agent.Tests/Execution/ActionExecutorTests.cs — test: successful submission (mock IValidatorServiceClient + IWalletServiceClient), payload schema validation failure skips, 401 triggers re-auth

### Implementation for User Story 1

- [x] T022 [US1] Create AgentAuthService in src/Apps/Sorcha.Agent/Auth/AgentAuthService.cs — authenticate with email/password via Tenant Service (Sorcha.ServiceClients.Http), select organisation, cache JWT, expose token provider func for SignalR, auto-refresh on 401
- [x] T023 [US1] Create RulesDecisionEngine in src/Apps/Sorcha.Agent/Decision/RulesDecisionEngine.cs — match incoming PendingAction by actionName, evaluate JSON Logic conditions via IJsonLogicEvaluator (from Sorcha.Blueprint.Engine), first match wins, no match returns skip, build payload from rule template
- [x] T024 [US1] Create PollingInboxListener in src/Apps/Sorcha.Agent/Inbox/PollingInboxListener.cs — poll IRegisterServiceClient.GetTransactionsByWalletAsync on configurable timer, map results to PendingAction, yield via IAsyncEnumerable
- [x] T025 [US1] Create SignalRInboxListener in src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs — connect via SorchaHubConnectionBuilder.Build() with AgentAuthService token provider, subscribe to InboundActionEvent, map to PendingAction, yield via Channel-backed IAsyncEnumerable
- [x] T026 [US1] Create CompositeInboxListener in src/Apps/Sorcha.Agent/Inbox/CompositeInboxListener.cs — merge SignalR + polling listeners, deduplicate by HashSet<string> of action IDs, yield unique actions sequentially, on SignalR reconnect trigger immediate poll
- [x] T027 [US1] Create ActionExecutor in src/Apps/Sorcha.Agent/Execution/ActionExecutor.cs — validate payload against action JSON schema, get sequence number via IValidatorServiceClient.GetNextSequenceNumberAsync, sign via IWalletServiceClient.SignTransactionAsync, submit via IValidatorServiceClient.SubmitTransactionAsync, log via AuditLogger
- [x] T028 [US1] Create RunCommand in src/Apps/Sorcha.Agent/Commands/RunCommand.cs — System.CommandLine command with --config and --state options, wire DI (load config → auth → create inbox listener → create decision engine → enter main loop: foreach action in inbox → decide → execute → log), handle CancellationToken for graceful shutdown, print summary on exit
- [x] T029 [US1] Register all services in Program.cs DI container in src/Apps/Sorcha.Agent/Program.cs — add RunCommand to root command, register AgentAuthService, IDecisionEngine (factory based on mode), IInboxListener, IActionExecutor, AuditLogger, configure Polly on HttpClient via IHttpClientFactory with defaults from ResilienceConfig

**Checkpoint**: User Story 1 complete. A single actor can run `sorcha-agent run --config actor.json --state state.json`, authenticate, listen for actions, evaluate rules, and submit payloads. Test with one ConstructionPermit action.

---

## Phase 4: User Story 2 - Validate Actor Configuration (Priority: P1)

**Goal**: The `validate` command checks config structure, variable resolution, credentials, and SignalR reachability before running.

**Independent Test**: Run validate against valid and invalid configs, verify exit codes and diagnostic messages.

### Tests for User Story 2

- [x] T030 [P] [US2] Write ValidateCommandTests in tests/Sorcha.Agent.Tests/Commands/ValidateCommandTests.cs — test: valid config passes all checks (exit 0), missing env var fails (exit 6), unresolved placeholder fails (exit 6), bad credentials fail (exit 2), all checks report PASS/FAIL/SKIP correctly

### Implementation for User Story 2

- [x] T031 [US2] Create ValidateCommand in src/Apps/Sorcha.Agent/Commands/ValidateCommand.cs — System.CommandLine command with --config and --state options, run sequential checks: (1) JSON schema validation of actor file against contracts/actor-definition-schema.json, (2) $env: resolution check, (3) {{placeholder}} resolution against state file, (4) credential connectivity test (login + org select), (5) SignalR hub reachability test. Report each check as [PASS]/[FAIL]/[SKIP]. Exit 0 if all pass, appropriate error code on first failure. Skip downstream checks when a prerequisite fails.
- [x] T032 [US2] Register ValidateCommand in Program.cs root command in src/Apps/Sorcha.Agent/Program.cs

**Checkpoint**: User Story 2 complete. `sorcha-agent validate` reports config health with actionable diagnostics.

---

## Phase 5: User Story 3 - Multi-Actor ConstructionPermit Port (Priority: P1)

**Goal**: 5 independent actor processes complete the ConstructionPermit workflow end-to-end without manual coordination.

**Independent Test**: Run setup.ps1, then launch all 5 actors. Workflow completes within timeout.

### Implementation for User Story 3

- [x] T033 [P] [US3] Create actor definition files in walkthroughs/ConstructionPermit/actors/ — contractor.json, structural-engineer.json, planning-officer.json, building-inspector.json, council-admin.json. Each with connection (localhost gateway), credentials ($env: passwords), wallet ({{placeholder}}), rules matching their assigned actions with appropriate payloads per the ConstructionPermit blueprint
- [x] T034 [US3] Create run-agents.ps1 launcher in walkthroughs/ConstructionPermit/run-agents.ps1 — accept -Profile parameter, resolve state.json path, start 5 `sorcha-agent run` processes in background (one per actor file), monitor for workflow completion (poll register for final action or timeout after 5 minutes), on completion or timeout send SIGTERM to all processes, print summary
- [x] T035 [US3] End-to-end validation: run ConstructionPermit setup.ps1, then run-agents.ps1, verify workflow completes with all actions submitted by the correct actors. Document the test procedure in walkthroughs/ConstructionPermit/actors/README.md

**Checkpoint**: User Story 3 complete. ConstructionPermit runs entirely via autonomous actors. No changes to setup.ps1 or shared module.

---

## Phase 6: User Story 4 - Cross-Machine Actor Execution (Priority: P2)

**Goal**: Actors on different machines (localhost + n1.sorcha.dev) participate in the same workflow.

**Independent Test**: Run 2 actors locally, 3 on remote machine. Workflow completes.

### Implementation for User Story 4

- [x] T036 [US4] Create cross-machine actor configs in walkthroughs/ConstructionPermit/actors/ — duplicate 3 actor files with gatewayUrl pointing to https://n1.sorcha.dev (e.g., planning-officer-remote.json, building-inspector-remote.json, council-admin-remote.json)
- [x] T037 [US4] Create cross-machine test script in walkthroughs/ConstructionPermit/run-agents-distributed.ps1 — launch 2 local actors + instructions for copying files and launching 3 remote actors, monitor workflow completion
- [x] T038 [US4] Document cross-machine deployment in walkthroughs/ConstructionPermit/actors/README.md — add section on copying actor.json + state.json to remote, setting env vars, running agents

**Checkpoint**: User Story 4 complete. Actors work identically on localhost and remote machines with only gatewayUrl changed.

---

## Phase 7: User Story 5 - AI-Powered Actor (Priority: P2)

**Goal**: An actor in "ai" mode calls the Claude API with a persona prompt, generates schema-valid payloads, and submits actions.

**Independent Test**: Launch one AI-mode actor against a single pending action. Verify it generates and submits a valid payload.

### Tests for User Story 5

- [x] T039 [P] [US5] Write AiDecisionEngineTests in tests/Sorcha.Agent.Tests/Decision/AiDecisionEngineTests.cs — test: prompt construction includes action schema + previous payload + persona, successful payload generation, schema validation failure triggers retry with errors in context, double failure returns skip, mock Claude API client

### Implementation for User Story 5

- [x] T040 [US5] Create AiDecisionEngine in src/Apps/Sorcha.Agent/Decision/AiDecisionEngine.cs — load persona markdown from promptFile, construct message with action context (name, schema, previous payload, participant role), call Claude API via Anthropic SDK with configured model and temperature, parse JSON payload from response, validate against action schema, retry once on validation failure with errors fed back, return skip on double failure
- [x] T041 [US5] Create sample persona prompt in walkthroughs/ConstructionPermit/prompts/planning-officer-ai.md — persona description, decision criteria, example payloads, instructions to output valid JSON matching the action schema
- [x] T042 [US5] Create AI-mode actor config in walkthroughs/ConstructionPermit/actors/planning-officer-ai.json — mode: "ai", promptFile pointing to persona, model and temperature configured, $env:ANTHROPIC_API_KEY for API authentication
- [x] T043 [US5] Update DI registration in src/Apps/Sorcha.Agent/Program.cs to resolve AiDecisionEngine when mode is "ai", inject Anthropic SDK HttpClient

**Checkpoint**: User Story 5 complete. AI-mode actor generates contextually appropriate, schema-valid payloads.

---

## Phase 8: User Story 6 - Resilient Operation (Priority: P2)

**Goal**: Actor survives network instability via Polly retry, circuit breaker, and SignalR reconnection.

**Independent Test**: Run actor, stop gateway, verify retries and circuit breaker, restart gateway, verify recovery.

### Tests for User Story 6

- [x] T044 [P] [US6] Write resilience integration tests in tests/Sorcha.Agent.Tests/Execution/ResilienceTests.cs — test: retry on 5xx with mock HttpMessageHandler, circuit breaker opens after threshold failures, circuit closes after duration, 401 triggers re-auth then retry

### Implementation for User Story 6

- [x] T045 [US6] Configure Polly resilience pipeline on IHttpClientFactory in src/Apps/Sorcha.Agent/Program.cs — Timeout (30s) → Retry (configurable count, exponential backoff from retryDelaySeconds) → Circuit Breaker (configurable threshold and duration). Read defaults from ResilienceConfig, log policy events (retry attempt, circuit open/close).
- [x] T046 [US6] Add 401 re-authentication handler in src/Apps/Sorcha.Agent/Auth/AgentAuthService.cs — on 401 response, re-authenticate with stored credentials, update cached token, retry the failed request once
- [x] T047 [US6] Add SignalR reconnection poll in src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs — on Reconnected event, trigger an immediate poll via CompositeInboxListener to catch missed events during disconnect

**Checkpoint**: User Story 6 complete. Actor handles transient failures, circuit breaker protects services, SignalR reconnects seamlessly.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, code quality, and final validation

- [x] T048 [P] Add license headers (SPDX-License-Identifier: MIT) to all new .cs files in src/Apps/Sorcha.Agent/ and tests/Sorcha.Agent.Tests/
- [x] T049 [P] Add XML documentation comments to all public types and methods in src/Apps/Sorcha.Agent/
- [x] T050 [P] Update walkthroughs/README.md to document the Sorcha.Agent tool and actor-based execution model
- [x] T051 Verify `dotnet test` passes for all Sorcha.Agent.Tests with >85% coverage
- [x] T052 Run quickstart.md validation — follow the quickstart guide end-to-end on a clean environment, verify all steps work
- [x] T053 Update CLAUDE.md project structure section to include Sorcha.Agent under Apps

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 — Rules Actor)**: Depends on Phase 2 — core MVP
- **Phase 4 (US2 — Validate)**: Depends on Phase 2 — can parallel with Phase 3
- **Phase 5 (US3 — Multi-Actor Port)**: Depends on Phase 3 (needs working run command)
- **Phase 6 (US4 — Cross-Machine)**: Depends on Phase 5 (needs actor configs)
- **Phase 7 (US5 — AI Mode)**: Depends on Phase 2 — can parallel with Phase 3/4/5
- **Phase 8 (US6 — Resilience)**: Depends on Phase 3 (needs working execution pipeline)
- **Phase 9 (Polish)**: Depends on all desired user stories

### User Story Dependencies

- **US1 (P1)**: After Phase 2 — no story dependencies. **This is the MVP.**
- **US2 (P1)**: After Phase 2 — independent of US1, can run in parallel
- **US3 (P1)**: After US1 — needs the run command working
- **US4 (P2)**: After US3 — needs actor config files
- **US5 (P2)**: After Phase 2 — independent of US1, can run in parallel
- **US6 (P2)**: After US1 — needs execution pipeline

### Within Each User Story

- Tests written first, verified to fail before implementation
- Models/interfaces before services
- Services before commands
- Core implementation before integration

### Parallel Opportunities

- **Phase 2**: T006, T007, T008, T009 (all models) can run in parallel. T011, T013, T018 (all tests) can run in parallel.
- **Phase 3**: T019, T020, T021 (tests) can run in parallel. Then T024, T025 (listeners) can run in parallel.
- **Phase 4+5+7**: US2 (validate), and US5 (AI mode) can run in parallel with US1 and each other.

---

## Parallel Example: Phase 2 Foundational

```bash
# Wave 1 — All model files (no dependencies between them):
Task: T006 "Create ActorDefinition models in src/Apps/Sorcha.Agent/Configuration/ActorDefinition.cs"
Task: T007 "Create PendingAction record in src/Apps/Sorcha.Agent/Models/PendingAction.cs"
Task: T008 "Create ActionDecision record in src/Apps/Sorcha.Agent/Models/ActionDecision.cs"
Task: T009 "Create ActionAuditEntry record in src/Apps/Sorcha.Agent/Models/ActionAuditEntry.cs"

# Wave 2 — Variable resolver + interfaces (depend on models):
Task: T010 "Create VariableResolver in src/Apps/Sorcha.Agent/Configuration/VariableResolver.cs"
Task: T014 "Create IDecisionEngine interface"
Task: T015 "Create IInboxListener interface"
Task: T016 "Create IActionExecutor interface"

# Wave 3 — Tests and loader (depend on resolver + models):
Task: T011 "Write VariableResolverTests"
Task: T012 "Create ActorDefinitionLoader"
Task: T017 "Create AuditLogger"
Task: T018 "Write AuditLoggerTests"
```

## Parallel Example: Phase 3 User Story 1

```bash
# Wave 1 — Tests (write first, verify they fail):
Task: T019 "Write RulesDecisionEngineTests"
Task: T020 "Write CompositeInboxListenerTests"
Task: T021 "Write ActionExecutorTests"

# Wave 2 — Core services (independent implementations):
Task: T022 "Create AgentAuthService"
Task: T023 "Create RulesDecisionEngine"
Task: T024 "Create PollingInboxListener"
Task: T025 "Create SignalRInboxListener"

# Wave 3 — Composition (depends on Wave 2):
Task: T026 "Create CompositeInboxListener"
Task: T027 "Create ActionExecutor"

# Wave 4 — Command + DI (depends on everything):
Task: T028 "Create RunCommand"
Task: T029 "Register services in Program.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T018)
3. Complete Phase 3: User Story 1 (T019-T029)
4. **STOP and VALIDATE**: Test single actor against one ConstructionPermit action
5. If working → proceed to US2/US3

### Incremental Delivery

1. Setup + Foundational → Project builds and tests pass
2. Add US1 (Rules Actor) → Single actor works → **MVP!**
3. Add US2 (Validate) → Config checking works → Better DX
4. Add US3 (Multi-Actor Port) → ConstructionPermit runs with 5 actors → **Primary goal achieved**
5. Add US4 (Cross-Machine) → Distributed execution works
6. Add US5 (AI Mode) → AI-powered actors work
7. Add US6 (Resilience) → Production-grade reliability
8. Polish → Documentation, coverage, cleanup

### Parallel Team Strategy

With multiple developers after Phase 2 completes:

- **Dev A**: US1 (Rules Actor) → US3 (Multi-Actor Port) → US4 (Cross-Machine)
- **Dev B**: US2 (Validate) → US5 (AI Mode) → US6 (Resilience)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- All new files require license headers and XML docs (Phase 9)
- Use existing Sorcha.Cli patterns for DI, auth, Polly, and exit codes
- JSON Logic evaluation uses existing IJsonLogicEvaluator from Sorcha.Blueprint.Engine (research.md Decision 1)
- Actor definition JSON schema at contracts/actor-definition-schema.json for validate command
- Commit after each task or logical group
