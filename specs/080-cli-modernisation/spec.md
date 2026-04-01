# Feature Specification: CLI Modernisation and Feature Completion

**Feature Branch**: `080-cli-modernisation`
**Created**: 2026-04-01
**Status**: Draft
**Input**: CLI audit findings (120+ commands, significant coverage gaps, consistency issues, missing high-value features)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Branded Help and Getting Started (Priority: P1)

A new user installs the Sorcha CLI and runs `sorcha --help`. They see a professional branded banner, clear command categories, and a getting-started guide. Each command shows usage examples when invoked with `--help`. The help header shows the current profile and authentication status so users always know which environment they're targeting.

**Why this priority**: First impressions determine adoption. A polished help experience reduces support burden and makes the CLI feel production-ready.

**Independent Test**: Run `sorcha --help` and verify the ASCII art banner, command categories, and profile/auth status. Run `sorcha register list --help` and verify usage examples appear.

**Acceptance Scenarios**:

1. **Given** a user runs `sorcha --help`, **When** the output renders, **Then** a branded ASCII art Sorcha banner is displayed followed by organised command groups
2. **Given** a user runs `sorcha version`, **When** the output renders, **Then** the banner, version number, build date, and commit hash are shown
3. **Given** a user is authenticated with profile "prod", **When** they run `sorcha --help`, **Then** the header shows "Profile: prod | Authenticated as: admin@sorcha.local"
4. **Given** a user is not authenticated, **When** they run `sorcha --help`, **Then** the header shows "Profile: dev | Not authenticated — run 'sorcha auth login'"
5. **Given** a user runs `sorcha register list --help`, **When** the help renders, **Then** at least one usage example is shown (e.g., `sorcha register list --output json`)
6. **Given** a user runs `sorcha help`, **When** the guide renders, **Then** a step-by-step getting-started walkthrough covering auth, org, and register creation is displayed

---

### User Story 2 — Consistent Output Formatting (Priority: P1)

An operator uses the CLI to query registers, transactions, and peers. Regardless of which command they run, data output follows a consistent format: `--output table` produces well-aligned tables, `--output json` produces valid JSON arrays, `--output csv` produces proper CSV with headers, and `--output yaml` produces valid YAML. Error messages follow a standard format.

**Why this priority**: Inconsistent output breaks scripting and automation pipelines. This is a foundational fix that affects every command.

**Independent Test**: Run `sorcha register list --output json`, `--output table`, `--output csv`, and `--output yaml`. Verify each produces valid, parseable output. Pipe JSON output through a validator.

**Acceptance Scenarios**:

1. **Given** any list command with `--output json`, **When** executed, **Then** the output is a valid JSON array (not newline-separated objects)
2. **Given** any list command with `--output csv`, **When** executed, **Then** the output includes a header row and properly escaped values
3. **Given** any list command with `--output yaml`, **When** executed, **Then** the output is valid YAML
4. **Given** any list command with `--output table`, **When** executed, **Then** columns are aligned with consistent widths
5. **Given** a command fails with a 404 error, **When** the error renders, **Then** the message follows the format "Error: {description}. Run 'sorcha {relevant-command} --help' for help."
6. **Given** any two commands that show similar data, **When** compared, **Then** their output formatting is visually consistent

---

### User Story 3 — Real-Time Event Streaming (Priority: P2)

An operator or AI agent wants to monitor register activity in real-time. They run `sorcha events watch --register <id>` and see a live stream of transactions, dockets, and status changes as they happen. The output supports JSON lines mode for machine consumption. The stream reconnects automatically on disconnection.

**Why this priority**: Real-time visibility is essential for operations and MCP/AI integration. This is the highest-value new feature.

**Independent Test**: Start `sorcha events watch --register <id>`, submit a transaction on the register, verify the event appears within 3 seconds. Kill the connection, verify it reconnects and resumes.

**Acceptance Scenarios**:

1. **Given** a user runs `sorcha events watch --register <id>`, **When** a transaction is confirmed on that register, **Then** the event appears in the output within 3 seconds
2. **Given** a user runs `sorcha events watch --register <id>`, **When** a docket is sealed, **Then** a docket-sealed event appears in the output
3. **Given** a user runs `sorcha events watch --all`, **When** events occur across any register, **Then** all events appear with register IDs
4. **Given** the stream is running with `--output json`, **When** events arrive, **Then** each event is a single JSON line (suitable for piping to jq or MCP)
5. **Given** the server connection drops, **When** the client detects disconnection, **Then** it reconnects with exponential backoff and logs a reconnection message
6. **Given** a consumer-role user watches a register, **When** a governance event occurs, **Then** governance events are filtered out (consumer only sees transactions)
7. **Given** a user presses Ctrl+C during watch, **When** the interrupt is received, **Then** the stream closes gracefully with a summary of events received

---

### User Story 4 — API Coverage Completion (Priority: P2)

An administrator needs to manage register invitations, check verification bundles, query audit logs, and manage platform settings — all from the CLI. Currently these operations require the web UI. After this work, all major platform operations are accessible via CLI.

**Why this priority**: CLI completeness enables automation, scripting, and CI/CD integration that the web UI cannot provide.

**Independent Test**: Run `sorcha invitation create --register-id <id> --target-org-did <did>` and verify it creates an invitation. Run `sorcha audit list --since 2026-03-01` and verify audit entries are returned.

**Acceptance Scenarios**:

1. **Given** an admin runs `sorcha invitation create`, **When** valid parameters are provided, **Then** a register invitation is created and the token is returned
2. **Given** an admin runs `sorcha invitation list`, **When** invitations exist, **Then** they are listed with status, target, and expiration
3. **Given** an admin runs `sorcha audit list --since <date>`, **When** audit entries exist, **Then** they are returned with timestamp, action, user, and details
4. **Given** an admin runs `sorcha health`, **When** all services are running, **Then** a comprehensive health report shows each service status, database connectivity, and peer network state
5. **Given** an admin runs `sorcha health --service register`, **When** the register service is running, **Then** detailed health including sync status and recovery state is shown
6. **Given** an admin runs `sorcha verify receipt --tx-id <id>`, **When** a valid receipt exists, **Then** the receipt is displayed with signature verification status and inclusion proof
7. **Given** an admin runs `sorcha register sync-status --id <id>`, **When** the register is syncing, **Then** the sync state, progress percentage, and source peers are shown
8. **Given** an admin runs `sorcha platform settings`, **When** settings exist, **Then** platform configuration including public org status and max orgs per user is shown

---

### User Story 5 — Bulk Operations (Priority: P3)

An operator setting up a new environment needs to create 50 wallets, import 200 users, and subscribe to 10 registers. Instead of running 260 individual commands, they use bulk operations with progress reporting and summary output.

**Why this priority**: Bulk operations dramatically reduce setup time for new environments and testing.

**Independent Test**: Run `sorcha wallet create-batch --count 5 --algorithm ED25519` and verify 5 wallets are created with progress feedback and a summary.

**Acceptance Scenarios**:

1. **Given** an operator runs `sorcha wallet create-batch --count 5`, **When** wallets are created, **Then** a progress indicator shows creation status and a summary table shows all created wallet addresses
2. **Given** an operator runs `sorcha user bulk-import --file users.csv`, **When** the CSV contains valid user data, **Then** users are created with a progress bar and error rows are reported without stopping the batch
3. **Given** a CSV file has 3 valid rows and 1 invalid row, **When** bulk import runs, **Then** 3 succeed, 1 fails, and the summary shows both counts with the error details for the failed row

---

### User Story 6 — Export and Import (Priority: P3)

An operator needs to export a register's configuration and transaction history for backup, auditing, or migration purposes. They use `sorcha register export` to produce a portable JSON file, and `sorcha register export-transactions` for the full transaction history as CSV.

**Why this priority**: Data portability is essential for compliance, backup, and migration between environments.

**Independent Test**: Run `sorcha register export --id <id> --output register.json`, verify the file contains register metadata and policy. Run `sorcha register export-transactions --id <id> --format csv --output txs.csv`, verify the CSV contains all transactions.

**Acceptance Scenarios**:

1. **Given** a register exists, **When** `sorcha register export --id <id>` is run, **Then** a JSON file is produced containing register metadata, policy, and status
2. **Given** a register has 100 transactions, **When** `sorcha register export-transactions --id <id> --format csv` is run, **Then** a CSV file is produced with all 100 transactions
3. **Given** a blueprint exists, **When** `sorcha blueprint export --id <id>` is run, **Then** a JSON file is produced containing the full blueprint definition

---

### User Story 7 — Reliability and Cancellation (Priority: P2)

An operator running a long bulk operation loses network connectivity briefly. The CLI retries automatically and completes the operation. If they press Ctrl+C, the operation stops gracefully, reporting what was completed.

**Why this priority**: Reliability is essential for production use. Without retries and cancellation, operators cannot trust the CLI for critical operations.

**Independent Test**: Start a bulk operation, disconnect network briefly, verify the command retries and completes. Press Ctrl+C during a long operation, verify graceful shutdown with progress summary.

**Acceptance Scenarios**:

1. **Given** a command fails with a transient network error, **When** retries are enabled, **Then** the command retries up to 3 times with exponential backoff before reporting failure
2. **Given** a user presses Ctrl+C during any operation, **When** the signal is received, **Then** the operation stops within 2 seconds and reports what was completed
3. **Given** a bulk operation is in progress, **When** Ctrl+C is pressed, **Then** the current item completes but no new items start, and a summary shows completed vs remaining items

---

### User Story 8 — MCP/AI Integration (Priority: P3)

An AI assistant (Claude Desktop, etc.) uses the Sorcha CLI as an MCP tool to query and monitor the platform. The `--machine-readable` flag ensures all output is structured JSON. The `sorcha events watch` command with `--output json` provides a real-time event feed. Shell completion makes the CLI easy to use interactively.

**Why this priority**: AI-assisted operations are a differentiator. Making the CLI MCP-friendly enables powerful automation workflows.

**Independent Test**: Run any command with `--machine-readable`, verify output is valid JSON with a consistent schema (status, data, errors fields). Test shell completion in bash/zsh.

**Acceptance Scenarios**:

1. **Given** any command with `--machine-readable`, **When** executed, **Then** output is a JSON object with `{"status": "success|error", "data": ..., "errors": [...]}`
2. **Given** `sorcha completion bash` is run, **When** output is sourced, **Then** tab completion works for commands, subcommands, and options
3. **Given** `sorcha completion` is run without a shell argument, **When** executed, **Then** it detects the current shell and outputs the appropriate completion script

---

### User Story 9 — Config Management (Priority: P2)

An operator needs to view their current CLI configuration, update the API endpoint for a profile, and validate that their config connects to running services.

**Why this priority**: Configuration visibility and validation prevent common mistakes when targeting different environments.

**Independent Test**: Run `sorcha config view` and verify it shows the active profile, API URL, and auth status. Run `sorcha config validate` and verify it checks connectivity to all services.

**Acceptance Scenarios**:

1. **Given** a user runs `sorcha config view`, **When** a profile is active, **Then** the profile name, API URL, auth status, and token expiry are displayed
2. **Given** a user runs `sorcha config set api-url https://new.sorcha.dev`, **When** the value is valid, **Then** the profile is updated and confirmed
3. **Given** a user runs `sorcha config validate`, **When** all services are reachable, **Then** each service shows a green check with response time
4. **Given** a user runs `sorcha config validate`, **When** a service is unreachable, **Then** that service shows a red cross with the error, and the exit code is non-zero

---

### User Story 10 — Stale Model and Dead Code Cleanup (Priority: P1)

A developer updating the CLI finds that Refit client interfaces have stale endpoints, DTOs don't match current service models, and dead code clutters the codebase. After cleanup, all interfaces match current APIs, all models are current, pagination works on all list commands, and commented-out code blocks are removed.

**Why this priority**: Stale code causes runtime errors and developer confusion. This is hygiene work that must happen before new features are built on top.

**Independent Test**: Run `sorcha register list` against a live service and verify all returned fields are correctly parsed. Verify `--page` and `--page-size` work on all list commands.

**Acceptance Scenarios**:

1. **Given** the CLI calls any service endpoint, **When** the response is received, **Then** all fields are correctly deserialized (no unknown field warnings or missing data)
2. **Given** any list command supports `--page` and `--page-size`, **When** pagination parameters are provided, **Then** the correct page of results is returned
3. **Given** the codebase is audited, **When** checking for commented-out code, **Then** no blocks larger than 5 lines of commented code exist
4. **Given** all Refit client interfaces, **When** compared to current service endpoints, **Then** endpoint paths and HTTP methods match exactly

---

### Edge Cases

- What happens when the user's token expires during a long-running watch command? Auto-refresh the token if possible, disconnect gracefully if not.
- What happens when `--output csv` is used for data with commas in values? Properly quote and escape per RFC 4180.
- What happens when a bulk import CSV has malformed rows? Skip the row, log the error, continue processing, report in summary.
- What happens when a service is unreachable during `sorcha health`? Report that specific service as unreachable without failing the entire command.
- What happens when `sorcha events watch` is run and no events arrive for 5 minutes? The command stays connected silently (no timeout); the keepalive handles the connection.
- What happens when a user runs a command without authentication? A clear error message directs them to `sorcha auth login`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: CLI MUST display a branded ASCII art banner on `--help` and `version` commands
- **FR-002**: CLI MUST show current profile name and authentication status in the help header
- **FR-003**: All commands MUST include at least one usage example in their `--help` output
- **FR-004**: All data output commands MUST support `--output` with table, json, csv, and yaml formats
- **FR-005**: All error messages MUST follow the format "Error: {description}. Run 'sorcha {command} --help' for help."
- **FR-006**: `sorcha events watch` MUST connect to real-time event streams and display events as they occur
- **FR-007**: Event watch MUST support `--output json` producing one JSON object per line for machine consumption
- **FR-008**: Event watch MUST reconnect automatically on disconnection with exponential backoff
- **FR-009**: Event watch MUST support role-based filtering (consumer, admin, sysadmin visibility levels)
- **FR-010**: CLI MUST provide commands for all major platform operations identified in the coverage gap audit
- **FR-011**: `sorcha health` MUST aggregate health status from all services into a single report
- **FR-012**: Bulk operation commands MUST show progress feedback and produce a summary on completion
- **FR-013**: Bulk operations MUST continue on individual item failure and report failures in the summary
- **FR-014**: Export commands MUST produce portable, self-contained files (JSON, CSV) suitable for backup or migration
- **FR-015**: All HTTP operations MUST retry up to 3 times on transient failures with exponential backoff
- **FR-016**: Ctrl+C MUST gracefully cancel any in-progress operation within 2 seconds
- **FR-017**: `--machine-readable` MUST produce structured JSON with consistent schema across all commands
- **FR-018**: `sorcha completion` MUST generate shell completion scripts for bash, zsh, PowerShell, and fish
- **FR-019**: All Refit client interfaces MUST match current service API endpoints exactly
- **FR-020**: All list commands MUST support `--page` and `--page-size` options
- **FR-021**: `sorcha config view` MUST display the active profile, API URL, authentication status, and token expiry
- **FR-022**: `sorcha config validate` MUST check connectivity to each service and report health status
- **FR-023**: Commented-out code blocks larger than 5 lines MUST be removed from all CLI source files

### Key Entities

- **OutputFormat**: Enum representing supported output formats (Table, Json, Csv, Yaml)
- **EventStream**: Real-time connection to service SignalR hubs for event delivery
- **BulkOperationResult**: Summary of a bulk operation including success count, failure count, and error details per failed item
- **HealthReport**: Aggregated health status across all services with per-service detail

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 120+ existing commands display at least one usage example in `--help` output
- **SC-002**: 100% of data output commands support all four formats (table, json, csv, yaml) with valid, parseable output
- **SC-003**: `sorcha events watch` displays events within 3 seconds of occurrence and reconnects within 30 seconds of disconnection
- **SC-004**: CLI API coverage reaches 80%+ across all service areas (up from current 30-70% range)
- **SC-005**: Bulk operations process 100 items with progress feedback completing in under 60 seconds (network permitting)
- **SC-006**: All Refit client interfaces pass a parity check against current service endpoint definitions (zero mismatches)
- **SC-007**: Zero commented-out code blocks larger than 5 lines remain in CLI source files
- **SC-008**: `sorcha config validate` verifies connectivity to all services within 10 seconds
- **SC-009**: `--machine-readable` output passes JSON schema validation on every command
- **SC-010**: Ctrl+C cancellation completes within 2 seconds on any command

## Assumptions

- The existing System.CommandLine 2.0.2 framework remains the CLI foundation (no framework migration)
- Spectre.Console is already available or can be added for table formatting
- SignalR client libraries for .NET are available for event streaming
- The Refit library continues to be used for HTTP service clients
- The MCP server command (`sorcha mcp serve`) defers to the existing Sorcha.McpServer project — the CLI just launches it
- Shell completion scripts follow the patterns established by dotnet CLI and other .NET tools
- The YAML output format uses a standard YAML serialization library
- Bulk operations are sequential (not parallel) by default to avoid overwhelming services
- Export formats are designed for human readability and tooling compatibility, not as a formal backup/restore mechanism
