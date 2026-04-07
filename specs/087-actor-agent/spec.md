# Feature Specification: Autonomous Actor Agent Framework

**Feature Branch**: `087-actor-agent`
**Created**: 2026-04-07
**Status**: Draft
**Input**: User description: "Autonomous Actor Agent Framework — a standalone CLI tool for running autonomous walkthrough actors with pluggable decision engines (JSON Logic rules or AI persona), real-time inbox listening, and portable deployment across machines."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a Rules-Based Actor on Localhost (Priority: P1)

A developer runs a walkthrough setup script to create orgs, wallets, participants, and a register. They then launch the actor agent with a JSON actor definition file containing deterministic rules. The actor connects to the local Sorcha instance, listens for pending actions in its inbox, evaluates them against the configured rules, and submits responses automatically. The workflow completes end-to-end without manual intervention.

**Why this priority**: This is the core value proposition — proving that a single actor can autonomously participate in a workflow using rules, replacing the manual run.ps1 pattern.

**Independent Test**: Launch one actor against a single-action workflow (e.g., the first action in ConstructionPermit). Verify the actor discovers the pending action, evaluates its rules, and submits a valid response.

**Acceptance Scenarios**:

1. **Given** a published blueprint with a pending action assigned to my participant, **When** I run `sorcha-agent run --config actor.json --state state.json`, **Then** the actor authenticates, discovers the pending action via inbox polling, evaluates the matching rule, and submits a valid payload.
2. **Given** an actor definition with multiple rules for the same action name, **When** a pending action arrives, **Then** rules are evaluated top-to-bottom and the first matching condition determines the response.
3. **Given** an actor definition where no rule matches the pending action, **When** the action arrives, **Then** the actor logs a warning and skips the action (does not submit, action remains in inbox).
4. **Given** an actor running with SignalR enabled, **When** a new action is assigned to the participant, **Then** the actor receives the notification in real-time and responds within seconds, without waiting for the poll interval.

---

### User Story 2 - Validate Actor Configuration Before Running (Priority: P1)

A developer creates an actor definition JSON file and wants to verify it is correct before launching a long-running process. They run the validate command which checks the file structure, resolves environment variables and state placeholders, tests credential connectivity, and reports any issues.

**Why this priority**: Configuration errors discovered only at runtime waste time. Early validation is essential for a good developer experience, especially when deploying to remote machines.

**Independent Test**: Run `sorcha-agent validate --config actor.json --state state.json` against a valid and an invalid config. Verify success/failure exit codes and diagnostic messages.

**Acceptance Scenarios**:

1. **Given** a valid actor definition with correct credentials and resolvable placeholders, **When** I run validate, **Then** the tool exits with code 0 and reports all checks passed.
2. **Given** an actor definition referencing `$env:MISSING_VAR` where the variable is not set, **When** I run validate, **Then** the tool exits with a non-zero code and reports which environment variable is missing.
3. **Given** an actor definition with `{{registerId}}` but no `--state` file provided, **When** I run validate, **Then** the tool reports unresolved placeholders and exits with a non-zero code.
4. **Given** an actor definition with invalid credentials, **When** I run validate, **Then** the tool reports the authentication failure with the specific error from the Tenant Service.

---

### User Story 3 - Run Multiple Actors to Complete a Multi-Participant Workflow (Priority: P1)

A developer launches 5 separate actor agent processes on the same machine, one per participant in the ConstructionPermit walkthrough. Each actor independently discovers its assigned actions, evaluates rules, and submits responses. The workflow progresses through all actions and completes without any manual coordination between the actors.

**Why this priority**: This validates the one-actor-one-process model and proves the framework can replace the sequential run.ps1 approach entirely.

**Independent Test**: Run the ConstructionPermit walkthrough setup, then launch all 5 actors. Verify the workflow completes end-to-end within a reasonable timeout.

**Acceptance Scenarios**:

1. **Given** a ConstructionPermit workflow is set up with 5 participants, **When** I launch 5 actor processes (one per participant) simultaneously, **Then** each actor handles its assigned actions and the workflow completes end-to-end.
2. **Given** 5 actors are running, **When** actor A submits an action that unlocks the next action for actor B, **Then** actor B discovers the new action via SignalR and responds without waiting for the poll interval.
3. **Given** a launcher script starts all 5 actors, **When** the workflow completes, **Then** the launcher detects completion and shuts down all actor processes gracefully.

---

### User Story 4 - Run Actors Across Multiple Machines (Priority: P2)

A developer runs the walkthrough setup on the local machine, then copies actor definition files and state.json to a remote machine (e.g., n1.sorcha.dev). Actors on both machines connect to the same Sorcha instance and participate in the same workflow. The workflow completes with actors distributed across the network.

**Why this priority**: This is the key advancement over existing walkthroughs — proving actors can run on different machines with nothing more than the actor file and state file.

**Independent Test**: Run 2 actors on localhost and 3 on a remote machine for the ConstructionPermit workflow. Verify workflow completion.

**Acceptance Scenarios**:

1. **Given** a ConstructionPermit workflow is set up, **When** I copy actor files and state.json to a remote machine and launch actors there alongside local actors, **Then** all actors participate in the same workflow and it completes successfully.
2. **Given** an actor running on a remote machine, **When** the gateway URL in the actor config points to the correct Sorcha instance, **Then** the actor authenticates and operates identically to a local actor.

---

### User Story 5 - Run an AI-Powered Actor (Priority: P2)

A developer configures an actor in "ai" mode with a persona prompt file. When a pending action arrives, the actor sends the action context (name, schema, previous payload) to the Claude API with the persona prompt. The AI generates a response payload which is validated against the action schema before submission.

**Why this priority**: AI mode enables realistic demos and exploratory testing where deterministic rules would be too rigid. It builds on the same framework infrastructure as rules mode.

**Independent Test**: Launch one AI-mode actor against a single pending action. Verify it calls the Claude API, receives a valid payload, validates it against the schema, and submits successfully.

**Acceptance Scenarios**:

1. **Given** an actor configured in "ai" mode with a persona prompt, **When** a pending action arrives, **Then** the actor sends the action context to the Claude API and submits the generated payload.
2. **Given** the AI generates a payload that fails schema validation, **When** the first attempt fails, **Then** the actor retries once with the validation errors included in the prompt context.
3. **Given** the AI generates a payload that fails schema validation on both attempts, **When** the retry also fails, **Then** the actor logs the error and skips the action.

---

### User Story 6 - Resilient Operation Under Network Instability (Priority: P2)

An actor is running against a Sorcha instance that experiences intermittent connectivity issues. The actor retries transient failures using exponential backoff, opens a circuit breaker after repeated failures to avoid overwhelming the service, and resumes normal operation when connectivity is restored.

**Why this priority**: Actors are long-running processes and must handle real-world network conditions gracefully, especially when running across machines.

**Independent Test**: Launch an actor, then temporarily stop the gateway. Verify the actor logs retries, opens the circuit breaker, and resumes operation when the gateway returns.

**Acceptance Scenarios**:

1. **Given** a running actor, **When** an action submission fails with a transient error (5xx), **Then** the actor retries with exponential backoff up to the configured retry count.
2. **Given** repeated transient failures exceeding the circuit breaker threshold, **When** the threshold is breached, **Then** the circuit opens and the actor defers actions until the circuit closes.
3. **Given** an open circuit, **When** the configured duration elapses, **Then** the circuit enters half-open state and the actor attempts the next action normally.
4. **Given** a SignalR disconnection, **When** the connection is lost, **Then** polling continues as normal and on reconnection an immediate poll catches any missed events.

---

### Edge Cases

- What happens when two actors receive the same action (misconfigured participant mapping)? The first submission succeeds; the second receives a 400/409 and the actor skips it permanently.
- What happens when the actor's JWT expires during operation? The actor re-authenticates using stored credentials before retrying.
- What happens when the state.json contains stale IDs (e.g., deleted register)? The validate command catches this; at runtime, the actor logs the error and exits.
- What happens when the actor process is killed mid-action-submission? The action was either submitted or not — stateless design means no cleanup needed. On restart, the actor re-evaluates the inbox.
- What happens when SignalR and polling both deliver the same action? The composite listener deduplicates by action ID.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a CLI command (`run`) that starts a long-running actor process from an actor definition JSON file and a state JSON file.
- **FR-002**: System MUST provide a CLI command (`validate`) that checks an actor definition file for structural validity, variable resolution, credential connectivity, and SignalR reachability.
- **FR-003**: System MUST support a `rules` decision mode that evaluates JSON Logic conditions against incoming action context and selects the first matching rule.
- **FR-004**: System MUST support an `ai` decision mode that sends action context to the Claude API with a persona prompt and validates the generated payload against the action schema.
- **FR-005**: System MUST discover pending actions via SignalR real-time notifications as the primary channel.
- **FR-006**: System MUST discover pending actions via periodic HTTP polling as a fallback channel, with a configurable interval (default 60 seconds).
- **FR-007**: System MUST deduplicate actions across both discovery channels using action ID.
- **FR-008**: System MUST validate generated payloads against the action's JSON schema before submission.
- **FR-009**: System MUST resolve `$env:VAR_NAME` placeholders from environment variables at load time.
- **FR-010**: System MUST resolve `{{placeholder}}` tokens from a companion state.json file.
- **FR-011**: System MUST authenticate using email/password credentials and organisation selection via the Tenant Service.
- **FR-012**: System MUST re-authenticate automatically when a JWT expires (401 response).
- **FR-013**: System MUST apply retry with exponential backoff on transient failures (5xx, timeouts).
- **FR-014**: System MUST apply a circuit breaker that opens after configurable consecutive failures and closes after a configurable duration.
- **FR-015**: System MUST shut down gracefully on SIGTERM/Ctrl+C, completing any in-flight action before exiting.
- **FR-016**: System MUST log every decision (approve, reject, skip) to an append-only JSONL audit file.
- **FR-017**: System MUST process actions sequentially (one at a time) to avoid race conditions on the same workflow instance.
- **FR-018**: System MUST use `Sorcha.ServiceClients.Http` for all HTTP and SignalR communication.

### Key Entities

- **ActorDefinition**: The complete configuration for one actor — identity (name, description), connection details (gateway URL, credentials, wallet address), inbox settings (SignalR/polling toggles and intervals), decision mode and rules/AI config, resilience settings, and logging config.
- **PendingAction**: An action discovered in the actor's inbox — action ID, action name, action index, expected schema, and the previous actor's submitted payload.
- **ActionDecision**: The output of a decision engine — decision type (approve/reject/skip), payload to submit, and reasoning (logged only).
- **ActionRule**: A single rule within a rules-mode actor — action name filter, JSON Logic condition, decision type, and payload template.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A single actor can discover and respond to a pending action within 5 seconds of it becoming available (via SignalR).
- **SC-002**: Five independent actor processes complete the ConstructionPermit walkthrough end-to-end without manual intervention.
- **SC-003**: The same actor definition file works on both localhost and a remote machine (n1.sorcha.dev) with only the gateway URL changed.
- **SC-004**: The validate command detects and reports all configuration errors (missing env vars, unresolved placeholders, bad credentials) with actionable diagnostic messages.
- **SC-005**: An actor survives 5 minutes of intermittent connectivity (gateway restarts) without crashing, resuming normal operation when connectivity is restored.
- **SC-006**: An AI-mode actor successfully completes at least one action by generating a schema-valid payload from a persona prompt.

## Assumptions

- The existing `Sorcha.ServiceClients.Http` package provides sufficient API surface for authentication, action execution, and SignalR hub connections. No changes to existing service clients are needed.
- The Blueprint Service inbox endpoint returns pending actions filterable by participant. If this endpoint does not exist or returns insufficient data, it will need to be added as a prerequisite.
- The Blueprint Service SignalR hub emits action notification events that include enough context (action ID, action name, participant) for the actor to decide whether to act.
- Walkthrough setup.ps1 scripts produce a state.json with all IDs needed by the actor (registerId, orgId, walletAddress, etc.). The actor does not create or provision any resources.
- A suitable JSON Logic evaluation library exists for .NET (e.g., `JsonLogic.Net`) and supports the `var` operator for accessing nested properties.
- The Claude API can be called with a standard API key for AI mode; no special MCP server connection is needed for v1.
