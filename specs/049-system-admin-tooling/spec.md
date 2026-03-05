# Feature Specification: System Administration Tooling

**Feature Branch**: `049-system-admin-tooling`
**Created**: 2026-03-04
**Status**: Draft
**Input**: System administration UI and CLI tooling for backend features that currently have no admin surface — register policy management, system register visibility, validator consent queues, validator metrics, threshold signing status, and service principal CRUD.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Service Principal Management (Priority: P1)

A platform administrator needs to create, manage, and revoke service-to-service credentials so that microservices can authenticate with each other securely. Currently the UI shows a hardcoded read-only list — administrators must use the CLI or raw API calls to manage service principals.

The administrator navigates to `/admin/principals`, sees a live DataGrid of all service principals with their status, scopes, and last-used timestamps. They can create new principals (receiving a one-time secret via copy-to-clipboard modal), edit scopes, rotate secrets, suspend/reactivate, and permanently revoke principals.

**Why this priority**: Service principals are the foundation of service-to-service auth. Without UI management, onboarding new services or rotating compromised secrets requires CLI access — a significant operational risk.

**Independent Test**: Can be fully tested by creating a service principal in the UI, copying the secret, and using it to authenticate via the token endpoint. Delivers immediate value for platform operators.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** they navigate to `/admin/principals`, **Then** they see a paginated DataGrid with all service principals showing name, client ID, status, scopes, and last used date.
2. **Given** the service principals page, **When** the administrator clicks "Create Service Principal" and fills in name, scopes, and expiration duration (30 days, 90 days, 1 year, or no expiry), **Then** a modal displays the generated client ID and secret with a copy-to-clipboard button and a warning that the secret will not be shown again.
3. **Given** an active service principal, **When** the administrator clicks "Rotate Secret", **Then** a confirmation dialog appears, and upon confirmation a new secret is displayed in the one-time modal.
4. **Given** an active service principal, **When** the administrator clicks "Suspend", **Then** the principal's status changes to Suspended and it can no longer authenticate.
5. **Given** a suspended service principal, **When** the administrator clicks "Reactivate", **Then** the principal's status returns to Active.
6. **Given** any service principal, **When** the administrator clicks "Revoke", **Then** a destructive-action confirmation appears, and upon confirmation the principal is permanently revoked (cannot be reactivated).
7. **Given** the principals list, **When** the administrator toggles "Include Inactive", **Then** suspended and revoked principals appear in the list with appropriate status badges.

---

### User Story 2 - Register Policy Management (Priority: P1)

A register owner needs to define governance policy at register creation time and view/propose policy updates on existing registers. Policy controls consensus rules: minimum/maximum validators, signature thresholds, registration mode (open vs consent), and approved validator lists.

During register creation, a new "Policy" step in the wizard lets the administrator configure policy before finalizing. On existing registers, a "Policy" tab on the register detail page shows the current effective policy, version history, and a "Propose Update" action.

**Why this priority**: Without policy management, all registers use default governance rules with no way to customize consensus requirements — a fundamental platform configuration gap.

**Independent Test**: Can be tested by creating a register with custom policy settings, then viewing the policy tab on the register detail page to confirm the policy was applied.

**Acceptance Scenarios**:

1. **Given** the Create Register wizard, **When** the administrator reaches the Policy step, **Then** they see pre-filled default policy values (min validators, registration mode, signature thresholds) that can be customized.
2. **Given** a register detail page, **When** the administrator selects the "Policy" tab, **Then** the current effective policy is displayed in a readable card layout showing all policy fields.
3. **Given** the Policy tab, **When** the administrator clicks "View History", **Then** a paginated table shows all policy versions with version number, updated-by, updated-at, and a link to view each version's full policy.
4. **Given** the Policy tab, **When** the administrator clicks "Propose Update" and modifies policy fields, **Then** a confirmation dialog explains that the update requires a governance vote and shows the proposed changes as a diff.
5. **Given** a policy update proposal, **When** the proposal is submitted, **Then** the system returns a 202 Accepted with the proposed version number and the UI displays a success notification.
6. **Given** a register with `RegistrationMode: Consent`, **When** viewing the policy, **Then** the approved validators list is displayed with each validator's ID and approval date.

---

### User Story 3 - Validator Consent Queue (Priority: P1)

A platform administrator needs to approve or reject validators that request to join consent-mode registers. Without this UI, validators queue up with no way to process them through the admin interface.

A new "Consent Queue" tab on the Validator admin page shows pending validator requests grouped by register, with Approve and Reject actions per validator.

**Why this priority**: Consent-mode registers are blocked from processing transactions if no validators are approved. This is a hard blocker for consent-mode operation.

**Independent Test**: Can be tested by registering a validator against a consent-mode register, then approving it via the Consent Queue tab and verifying it appears in the approved list.

**Acceptance Scenarios**:

1. **Given** the Validator admin page, **When** the administrator selects the "Consent Queue" tab, **Then** pending validator requests are displayed grouped by register with selectable checkboxes, showing validator ID, requested date, and register name.
2. **Given** one or more selected pending validators, **When** the administrator clicks "Approve Selected", **Then** a confirmation dialog appears listing the selected validators, and upon confirmation they move to the approved list.
3. **Given** one or more selected pending validators, **When** the administrator clicks "Reject Selected", **Then** a confirmation dialog with optional reason field appears listing the selected validators, and upon confirmation they are removed from the pending list.
4. **Given** the Consent Queue tab, **When** there are no pending requests, **Then** an empty state message is displayed: "No pending validator requests."
5. **Given** an approved validators section, **When** the administrator clicks "Refresh from Chain", **Then** the approved validator list is re-synchronized from the on-chain policy record.
6. **Given** a register in Open registration mode, **When** viewing the Consent Queue for that register, **Then** a notice explains that consent approval is not required for open-mode registers.

---

### User Story 4 - Validator Metrics Dashboard (Priority: P2)

A platform operator needs visibility into validation performance, consensus health, memory pool pressure, and cache efficiency. Six metrics endpoints exist but have no visualization.

A new "Metrics" tab on the Validator admin page shows KPI summary cards and expandable subsystem detail panels with auto-refresh.

**Why this priority**: Operational monitoring is essential for production but the platform currently runs without any visibility into validator health beyond basic mempool stats.

**Independent Test**: Can be tested by navigating to the Metrics tab and verifying that KPI cards display real-time data from each metrics subsystem.

**Acceptance Scenarios**:

1. **Given** the Validator admin page, **When** the administrator selects the "Metrics" tab, **Then** summary KPI cards display: validation success rate, consensus dockets proposed, pool queue depth, and cache hit ratio.
2. **Given** the Metrics tab, **When** the administrator expands the "Validation" section, **Then** detailed metrics appear: total validated, successful, failed, average validation time, in-progress count, and errors by category.
3. **Given** the Metrics tab, **When** the administrator expands the "Consensus" section, **Then** docket distribution, submission, failure, and recovery metrics are displayed.
4. **Given** the Metrics tab, **When** the administrator expands the "Pools" section, **Then** queue sizes, oldest/newest transaction timestamps, and enqueued/dequeued/expired counts are shown.
5. **Given** the Metrics tab, **When** the administrator expands the "Caches" section, **Then** blueprint cache hits/misses, hit ratio, and entry counts for both local and distributed cache are displayed.
6. **Given** the Metrics tab, **When** auto-refresh is enabled, **Then** all metrics update at a configurable interval (default 5 seconds).

---

### User Story 5 - System Register Visibility (Priority: P2)

A platform administrator needs to see the System Register's initialization status and browse the catalog of disseminated blueprints. The System Register is the platform's root of trust — currently invisible in the admin UI.

A new `/admin/system-register` page under the System nav group shows status information and a paginated blueprint catalog.

**Why this priority**: The System Register is a singleton governance construct. Administrators need to verify it's initialized and see what blueprints have been published to it, but this is a read-only monitoring concern rather than a configuration blocker.

**Independent Test**: Can be tested by navigating to `/admin/system-register` and verifying the initialization status and blueprint list match the actual system register state.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** they navigate to `/admin/system-register`, **Then** they see a status card showing: register ID, display name, initialization status, blueprint count, and creation date.
2. **Given** the System Register page, **When** blueprints have been disseminated, **Then** a paginated table shows each blueprint's ID, version, published date, publisher, and active status.
3. **Given** the blueprint table, **When** the administrator clicks a blueprint row, **Then** a detail dialog shows the full blueprint document.
4. **Given** the System Register page, **When** the system register has not been initialized, **Then** a warning banner explains the state and the blueprint table is hidden.
5. **Given** the System Register page, **When** a blueprint has multiple versions, **Then** the administrator can view specific versions via a version selector.

---

### User Story 6 - Threshold Signing Status (Priority: P3)

A platform administrator needs to view BLS threshold signing configuration and status per register. This is an advanced consensus feature used when multiple validators must jointly sign dockets.

A new "Threshold" tab on the Validator admin page shows per-register threshold configuration: group public key, threshold ratio (t-of-n), collected shares, and setup action.

**Why this priority**: Threshold signing is an advanced feature. Most deployments will use single-validator signing initially. This is included for completeness but is lower priority than basic consensus management.

**Independent Test**: Can be tested by setting up threshold signing for a register and verifying the status display shows the correct threshold, total validators, and group public key.

**Acceptance Scenarios**:

1. **Given** the Validator admin page, **When** the administrator selects the "Threshold" tab, **Then** registers with threshold signing configured show status cards with: group public key, threshold (t), total validators (n), and validator IDs.
2. **Given** a register without threshold signing, **When** viewing the Threshold tab, **Then** a "Setup Threshold Signing" action is available for that register.
3. **Given** the setup action, **When** the administrator configures threshold (t), total validators (n), and selects validator IDs, **Then** the system initializes BLS key shares and displays the group public key.
4. **Given** an active threshold signing configuration, **When** a docket signing is in progress, **Then** the status shows collected shares vs required threshold.

---

### User Story 7 - CLI Commands for Policy, System Register, Consent, Metrics, and Threshold (Priority: P2)

A platform operator using the CLI needs commands to manage register policies, view the system register, process the validator consent queue, view metrics, and check threshold signing status — enabling scripting and automation of admin tasks.

**Why this priority**: CLI commands enable automation and headless administration. They mirror the UI features but are secondary since the UI is the primary admin interface for interactive use.

**Independent Test**: Can be tested by running each CLI command against a running platform instance and verifying output matches expected data.

**Acceptance Scenarios**:

1. **Given** an authenticated CLI session, **When** the user runs `sorcha register policy get --register-id <id>`, **Then** the current effective policy is displayed in table or JSON format.
2. **Given** an authenticated CLI session, **When** the user runs `sorcha register policy history --register-id <id>`, **Then** a paginated list of policy versions is displayed.
3. **Given** an authenticated CLI session, **When** the user runs `sorcha register system status`, **Then** the system register's initialization status, ID, and blueprint count are displayed.
4. **Given** an authenticated CLI session, **When** the user runs `sorcha validator consent pending --register-id <id>`, **Then** pending validator requests for that register are listed.
5. **Given** an authenticated CLI session, **When** the user runs `sorcha validator consent approve --register-id <id> --validator-id <vid>`, **Then** the validator is approved and a success message is displayed.
6. **Given** an authenticated CLI session, **When** the user runs `sorcha validator metrics`, **Then** aggregated metrics are displayed in a formatted table.
7. **Given** an authenticated CLI session, **When** the user runs `sorcha validator metrics validation`, **Then** validation-specific metrics are displayed.
8. **Given** an authenticated CLI session, **When** the user runs `sorcha validator threshold status --register-id <id>`, **Then** the BLS threshold configuration for that register is displayed.

---

### Edge Cases

- What happens when the validator service is unreachable? Metrics, consent, and threshold tabs show a service-unavailable state with retry button.
- What happens when proposing a policy update with a stale version number? The API returns 409 Conflict — the UI displays an error prompting the administrator to refresh the policy before retrying.
- What happens when rotating a service principal secret while services are actively using it? A warning dialog explains that the old secret is invalidated immediately and affected services will need reconfiguration.
- What happens when approving a validator for a register that has reached its max validator count? The API rejects the approval — the UI displays the policy constraint that was violated.
- What happens when the system register is not yet initialized? The System Register page shows a warning state and hides the blueprint catalog.
- What happens when threshold setup specifies a threshold greater than total validators? Validation prevents submission with a clear error message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display a live DataGrid of service principals with name, client ID, status, scopes, last used date, and expiration.
- **FR-002**: System MUST allow creation of service principals with name, scopes, and expiration duration (preset options: 30 days, 90 days, 1 year, no expiry) — with a one-time secret displayed in a copy-to-clipboard modal.
- **FR-003**: System MUST support editing service principal scopes via inline dialog.
- **FR-004**: System MUST support suspend, reactivate, and permanent revocation of service principals with appropriate confirmation dialogs.
- **FR-005**: System MUST support secret rotation with one-time display of the new secret.
- **FR-006**: System MUST include a Policy configuration step in the register creation wizard with sensible defaults.
- **FR-007**: System MUST display the current effective register policy on the register detail page in a dedicated tab.
- **FR-008**: System MUST show paginated policy version history with version number, author, and timestamp.
- **FR-009**: System MUST allow proposing policy updates through a form that shows the diff between current and proposed values.
- **FR-010**: System MUST display pending validator requests grouped by register in a Consent Queue tab on the Validator admin page.
- **FR-011**: System MUST allow approving or rejecting pending validators individually or in bulk (multi-select checkboxes) with confirmation dialogs.
- **FR-012**: System MUST allow refreshing the approved validator list from on-chain records.
- **FR-013**: System MUST display aggregated and per-subsystem validator metrics (validation, consensus, pools, caches) with auto-refresh.
- **FR-014**: System MUST display the system register's initialization status, metadata, and disseminated blueprint catalog on a dedicated admin page.
- **FR-015**: System MUST display per-register BLS threshold signing configuration and in-progress signing status.
- **FR-016**: System MUST allow threshold signing setup through a guided form.
- **FR-017**: System MUST display read-only validator configuration (redacted sensitive values) in a Config tab.
- **FR-018**: CLI MUST provide `register policy get|history|update` subcommands.
- **FR-019**: CLI MUST provide `register system status|blueprints` subcommands.
- **FR-020**: CLI MUST provide `validator consent pending|approve|reject|refresh` subcommands.
- **FR-021**: CLI MUST provide `validator metrics [subsystem]` subcommands.
- **FR-022**: CLI MUST provide `validator threshold status|setup` subcommands.
- **FR-023**: All new admin UI pages MUST be restricted to Administrator/SystemAdmin roles.
- **FR-024**: All new CLI commands MUST require valid authentication and appropriate authorization.

### Key Entities

- **ServicePrincipal**: Service identity with client ID, encrypted secret, scopes, status (Active/Suspended/Revoked), and lifecycle timestamps.
- **RegisterPolicy**: Per-register governance configuration with min/max validators, signature thresholds, registration mode, approved validator list, and version tracking.
- **SystemRegister**: Singleton platform register holding disseminated blueprints with initialization status.
- **ValidatorRegistration**: A validator's request to join a register, with pending/approved/rejected status.
- **ThresholdConfiguration**: BLS threshold signing setup per register — group public key, threshold (t), total validators (n), and validator assignments.
- **ValidatorMetrics**: Aggregated operational statistics covering validation, consensus, memory pools, and caching subsystems.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can create a service principal and copy its credentials in under 60 seconds.
- **SC-002**: Administrators can view and propose a register policy update in under 2 minutes.
- **SC-003**: Administrators can process (approve/reject) a pending validator request in under 30 seconds.
- **SC-004**: Validator metrics dashboard displays live data with no more than 10 seconds of latency from actual system state.
- **SC-005**: All 6 admin features (service principals, register policy, consent queue, metrics, system register, threshold) are accessible from the admin navigation without requiring CLI or raw API access.
- **SC-006**: CLI commands for all 5 new command groups execute successfully and produce formatted output (table and JSON).
- **SC-007**: All new admin UI components follow existing MudBlazor patterns (loading states, error handling, empty states, polling where appropriate).
- **SC-008**: All new UI pages are restricted to Administrator/SystemAdmin roles — unauthorized users see no navigation links and receive 403 if accessing directly.

## Clarifications

### Session 2026-03-05

- Q: How is service principal expiration set? → A: Admin chooses from preset durations (30d, 90d, 1yr, no expiry) at creation time.
- Q: Should consent queue support bulk operations? → A: Yes, multi-select checkboxes with bulk approve/reject.

## Assumptions

- All backend API endpoints referenced in this spec are already implemented and functional. No backend changes are needed.
- The existing MudBlazor component patterns (loading skeletons, error alerts, confirmation dialogs, polling timers) will be reused for consistency.
- The CLI follows existing System.CommandLine + Refit + Spectre.Console patterns — no new infrastructure needed.
- Policy updates go through a governance vote mechanism (returning 202 Accepted) — the UI does not need to implement the voting workflow itself, only the proposal submission.
- The "Config" tab on the Validator page is read-only — configuration changes require service restart or separate tooling.
- Threshold signing setup is an advanced feature that may see limited initial use; the UI should support it but can have a simpler UX than other features.
