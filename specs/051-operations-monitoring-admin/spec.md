# Feature Specification: Operations & Monitoring Admin

**Feature Branch**: `051-operations-monitoring-admin`
**Created**: 2026-03-06
**Status**: Draft
**Input**: User description: "Roll together remaining UI/CLI admin gaps (Feature 051 from gap analysis) with 050 PR review code quality follow-ups into a single feature"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Gateway Dashboard & Alerts (Priority: P1)

An administrator navigates to the Home dashboard and sees real system statistics (total registers, active blueprints, connected peers, transaction throughput) populated from the live API Gateway stats endpoint. An alerts panel displays active system alerts (service degradation, high error rates, resource warnings) with severity indicators. The administrator can dismiss or acknowledge individual alerts.

**Why this priority**: The dashboard currently shows hardcoded placeholder values. Wiring it to real data is the highest-impact change — it's the first thing admins see after login and gives immediate visibility into system health.

**Independent Test**: Can be fully tested by logging in as an admin user and verifying dashboard cards show live data that changes when system state changes (e.g., creating a register increments the count).

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** they view the Home dashboard, **Then** all dashboard cards display live statistics from the gateway stats endpoint
2. **Given** active system alerts exist, **When** the administrator views the dashboard, **Then** an alerts panel shows each alert with severity (info/warning/error), message, and timestamp
3. **Given** an active alert, **When** the administrator dismisses it, **Then** the alert is removed from the panel
4. **Given** the gateway stats endpoint is unavailable, **When** the dashboard loads, **Then** cards show a "data unavailable" state rather than stale or zero values

---

### User Story 2 - Wallet Access Delegation (Priority: P1)

A wallet owner navigates to their wallet detail page and sees an "Access" tab listing all users who have been granted access to the wallet. The owner can grant access to another user (by identifier), view existing grants with their permissions, and revoke access. A CLI equivalent allows the same operations for scripted/automated environments.

**Why this priority**: Multi-user wallet access is a core operational need for organizations where multiple team members need to sign transactions. Currently this is API-only with no admin surface.

**Independent Test**: Can be tested by granting access to a wallet, verifying the grant appears in the list, checking access returns true, then revoking and verifying it returns false.

**Acceptance Scenarios**:

1. **Given** a wallet owner viewing their wallet detail, **When** they click the "Access" tab, **Then** they see a list of all current access grants with subject identifiers, permission levels (read/sign/admin), and grant timestamps
2. **Given** a wallet with no grants, **When** the owner views the Access tab, **Then** an empty state message explains how to grant access
3. **Given** a wallet owner, **When** they grant access to a subject and the subject attempts a wallet operation, **Then** the system confirms the subject has access
4. **Given** a wallet owner, **When** they revoke a previously granted access, **Then** the grant is removed and the subject can no longer perform wallet operations
5. **Given** a CLI user, **When** they run `wallet access grant|list|revoke|check`, **Then** the commands produce the same results as the UI equivalents

---

### User Story 3 - Schema Provider Admin (Priority: P2)

An administrator navigates to the Schema Provider Health page (currently a placeholder) and sees a list of registered schema providers with their health status (healthy, degraded, unreachable), last refresh timestamp, and schema count. The administrator can trigger a manual refresh for any provider. A CLI equivalent exposes `schema providers list|refresh` commands.

**Why this priority**: Schema providers are the source of truth for data validation. Admins need visibility into whether providers are healthy and the ability to force a refresh when new schemas are published upstream.

**Independent Test**: Can be tested by viewing the provider list, verifying health status is displayed, and triggering a refresh that updates the last-refreshed timestamp.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** they navigate to the Schema Provider admin page, **Then** they see all registered providers with name, health status, last refresh time, and schema count
2. **Given** a healthy provider, **When** the administrator clicks "Refresh", **Then** the provider re-fetches its catalog and the last refresh timestamp updates
3. **Given** a provider that is unreachable, **When** the page loads, **Then** the provider shows a "degraded" or "unreachable" status with the last successful refresh time
4. **Given** a CLI user, **When** they run `schema providers list`, **Then** they see the same provider health information in tabular format

---

### User Story 4 - Events Admin (Priority: P2)

An administrator navigates to an Events admin page and sees a chronological log of system events (blueprint published, register created, participant actions, validation errors). The administrator can filter events by type, date range, and severity. Individual events can be deleted (e.g., to clear resolved error entries). A CLI equivalent provides `admin events list|delete` commands.

**Why this priority**: System events are generated but not surfaced to administrators in a manageable way. The activity log panel shows user-scoped events, but there is no admin-level view for system-wide event management.

**Independent Test**: Can be tested by triggering system events (e.g., publishing a blueprint), viewing them in the admin event log, filtering by type, and deleting an entry.

**Acceptance Scenarios**:

1. **Given** an authenticated administrator, **When** they navigate to the Events admin page, **Then** they see a paginated list of system events ordered by most recent first
2. **Given** events of multiple severities, **When** the administrator filters by severity, **Then** only matching events are displayed
3. **Given** a specific event, **When** the administrator deletes it, **Then** the event is removed from the log and a confirmation message is shown
4. **Given** a CLI user, **When** they run `admin events list --severity Error --since 2026-03-01`, **Then** they see filtered events in tabular or JSON format

---

### User Story 5 - Push Notification Management (Priority: P3)

A user navigates to Settings > Notifications and sees a toggle to enable or disable browser push notifications. When enabled, the system registers a push subscription with the backend. When disabled, the subscription is removed. The current subscription status is displayed.

**Why this priority**: Push notification endpoints exist in the backend but there is no UI toggle. This is a user-facing convenience feature that completes the notification story started in Feature 043.

**Independent Test**: Can be tested by toggling push notifications on, verifying the subscription status shows as active, then toggling off and verifying it shows as inactive.

**Acceptance Scenarios**:

1. **Given** a user on the Settings page, **When** they view the Notifications tab, **Then** they see a push notification toggle with the current subscription status
2. **Given** push notifications are disabled, **When** the user enables them and grants browser permission, **Then** the system registers a subscription and shows "Active" status
3. **Given** push notifications are enabled, **When** the user disables them, **Then** the system removes the subscription and shows "Inactive" status
4. **Given** the user denies browser notification permission, **When** they try to enable push notifications, **Then** a helpful message explains how to grant permission in browser settings

---

### User Story 6 - Encrypted Payload Operation Status (Priority: P3)

When a user submits an action that triggers envelope encryption (Feature 045), they see a progress indicator showing the encryption operation status (queued, encrypting per-recipient keys, encrypting payload, complete). The indicator updates in near-real-time. A CLI command `operation status <id>` allows checking operation status programmatically.

**Why this priority**: Encryption operations can take several seconds for multi-recipient payloads. Currently the user has no feedback beyond a spinner. SignalR partially covers this but the UI doesn't render progress stages.

**Independent Test**: Can be tested by submitting an action on a blueprint with encrypted disclosures and observing the progress indicator advancing through stages until completion.

**Acceptance Scenarios**:

1. **Given** a user submitting an encrypted action, **When** encryption begins, **Then** a progress indicator shows the current stage (queued, processing, complete)
2. **Given** an encryption operation in progress, **When** the operation completes, **Then** the progress indicator shows completion and the normal action result is displayed
3. **Given** a CLI user, **When** they run `operation status <operationId>`, **Then** they see the current stage and percentage complete

---

### User Story 7 - UI Service Reliability Improvements (Priority: P2)

Administrators and users receive clear, contextual error messages when credential lifecycle operations fail, rather than silent failures or generic errors. Credential status values are displayed consistently across all views. The presentation request creation form uses structured input for multi-value fields (accepted issuers, required claims) instead of error-prone comma-separated text.

**Why this priority**: These are code quality improvements identified during the Feature 050 review that directly impact user experience — silent failures confuse users, magic strings risk inconsistency, and comma-separated inputs cause data entry errors.

**Independent Test**: Can be tested by attempting a credential lifecycle operation with invalid permissions and verifying a specific error message is shown (not a generic failure or silent null).

**Acceptance Scenarios**:

1. **Given** a user attempting to suspend a credential they did not issue, **When** the operation returns a 403 error, **Then** the UI displays "Permission denied: you are not the issuer of this credential" rather than a generic error or silent failure
2. **Given** a credential in "Suspended" status, **When** the user attempts to suspend it again, **Then** the UI displays "Credential is already suspended" (409 Conflict) rather than a generic error
3. **Given** an administrator creating a presentation request, **When** they enter accepted issuers, **Then** they use a structured tag/chip input rather than free-text comma-separated entry
4. **Given** a pending presentation request, **When** the administrator views the request detail, **Then** the status refreshes automatically without requiring a manual page reload

---

### Edge Cases

- What happens when the gateway stats endpoint returns partial data (some services healthy, some unreachable)?
- How does wallet access delegation handle granting access to a non-existent user identifier?
- What happens when a schema provider refresh is triggered while a previous refresh is still in progress?
- Events admin supports single-event deletion only (bulk deletion is out of scope for this feature)
- What happens when the push notification service worker fails to register?
- How does the encryption progress indicator handle a dropped SignalR connection mid-operation?
- What happens when an event is deleted while another admin is viewing it?

## Requirements *(mandatory)*

### Functional Requirements

**Gateway Dashboard & Alerts**
- **FR-001**: System MUST display live system statistics on the Home dashboard sourced from the gateway stats endpoint
- **FR-002**: System MUST display active system alerts with severity, message, and timestamp
- **FR-003**: Administrators MUST be able to dismiss individual alerts (per-user — dismissal hides the alert for that admin only; other admins still see it)
- **FR-004**: Dashboard MUST show a graceful "unavailable" state when the stats endpoint is unreachable

**Wallet Access Delegation**
- **FR-005**: Wallet owners MUST be able to grant access to their wallet by specifying a subject identifier and a permission level (read, sign, or admin)
- **FR-006**: Wallet owners MUST be able to view all current access grants on their wallet
- **FR-007**: Wallet owners MUST be able to revoke a previously granted access
- **FR-008**: System MUST provide a way to check whether a subject has access to a specific wallet
- **FR-009**: CLI MUST provide `wallet access grant|list|revoke|check` subcommands

**Schema Provider Admin**
- **FR-010**: ~~System MUST display all registered schema providers with health status, last refresh time, and schema count~~ *PRE-SATISFIED — SchemaProviderHealth.razor already implements this*
- **FR-011**: ~~Administrators MUST be able to trigger a manual refresh for any provider~~ *PRE-SATISFIED — SchemaProviderHealth.razor already implements per-provider "Refresh Now" button*
- **FR-012**: ~~System MUST replace the existing placeholder page at `/admin/schema-providers` with the functional page~~ *PRE-SATISFIED — page is fully functional (not a placeholder); confirmed via codebase analysis*
- **FR-013**: CLI MUST provide `schema providers list|refresh` subcommands

**Events Admin**
- **FR-014**: System MUST display system events in a paginated, chronologically ordered admin view
- **FR-015**: Administrators MUST be able to filter events by severity and start date (the backend `/api/events/admin` endpoint supports `severity` and `since` parameters; type filtering is not supported by the current API)
- **FR-016**: Administrators MUST be able to delete individual events
- **FR-017**: CLI MUST provide `admin events list|delete` subcommands

**Push Notification Management**
- **FR-018**: Users MUST be able to enable and disable push notifications from Settings
- **FR-019**: System MUST show the current push subscription status (active/inactive)
- **FR-020**: System MUST handle browser permission denial gracefully with user guidance

**Encrypted Payload Status**
- **FR-021**: System MUST display encryption operation progress with distinguishable stages
- **FR-022**: Progress indicator MUST update in near-real-time as the operation advances
- **FR-023**: CLI MUST provide an `operation status` subcommand

**UI Service Reliability (Code Quality)**
- **FR-024**: Credential lifecycle operations MUST return distinguishable error information for different failure types (403, 409, 404, 500)
- **FR-025**: Credential status values MUST be referenced from a shared set of constants, not inline strings
- **FR-026**: Multi-value input fields (accepted issuers, required claims) MUST use structured input controls rather than comma-separated text
- **FR-027**: Pending presentation requests MUST auto-refresh their status every 5 seconds without manual page reload
- **FR-028**: Shared configuration (e.g., JSON serialization options) MUST be defined once and reused across services

### Key Entities

- **WalletAccessGrant**: Represents a delegation of wallet access — wallet address, subject identifier, permission level (read/sign/admin), granted timestamp, grantor
- **SystemAlert**: An active alert from the API Gateway — severity (info/warning/error), message, source service, timestamp, per-user dismissal tracking
- **DashboardStats**: Aggregated system statistics — register count, blueprint count, peer count, transaction throughput, service health summary
- **SchemaProviderStatus**: Health information for a schema source — provider name, status (healthy/degraded/unreachable), last refresh time, schema count
- **SystemEvent**: An admin-visible system event — event type, severity, message, source, timestamp, metadata
- **PushSubscription**: A browser push notification subscription — user identifier, subscription endpoint, status (active/inactive)
- **EncryptionOperation**: Status of an in-progress encryption operation — operation ID, current stage, percentage complete, recipient count

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All dashboard cards display live data within 5 seconds of page load, with automatic refresh every 30 seconds (configurable)
- **SC-002**: Wallet owners can grant, verify, and revoke access delegation through the UI in under 60 seconds per operation
- **SC-003**: Schema provider health status is visible and a manual refresh completes within 30 seconds
- **SC-004**: System events are searchable and filterable, with results appearing within 2 seconds
- **SC-005**: Push notification enable/disable toggle takes effect within 5 seconds
- **SC-006**: Encryption progress stages are visible to users within 1 second of stage transitions
- **SC-007**: Credential lifecycle errors display specific, actionable messages for all documented error codes (403, 404, 409, 500)
- **SC-008**: All new UI features have corresponding CLI commands with consistent output formatting
- **SC-009**: All new UI components and CLI commands have automated tests (>85% coverage for new code)
- **SC-010**: Zero inline magic strings for credential status values across all UI components

## Clarifications

### Session 2026-03-06

- Q: What level of access does a wallet delegate receive? → A: Configurable per-grant — owner selects permission level (read, sign, admin) at grant time.
- Q: How should dismissed alerts behave — per-user or system-wide? → A: Per-user — dismissing hides the alert for that admin only; others still see it.
- Q: What auto-refresh interval for the Home dashboard stats? → A: 30 seconds.
- Q: Should bulk event deletion be in scope? → A: No — single deletion only; bulk deletion removed from edge cases.
- Q: What polling interval for presentation request auto-refresh? → A: 5 seconds.

## Assumptions

- The gateway dashboard (`/api/dashboard`) and alerts (`/api/alerts`) endpoints already exist and return structured data — this feature wires the UI to them, not builds the backend. Note: `/api/stats` is a separate system statistics endpoint (via HealthAggregationService) and is not used by this feature.
- Wallet delegation endpoints (`/api/v1/wallets/{address}/access/*`) already exist in the Wallet Service
- Schema provider endpoints (`/api/v1/schemas/providers`, `.../refresh`) already exist in the Blueprint Service
- Events admin endpoints (`/api/events/admin`, `DELETE /api/events/{id}`) already exist in the Blueprint Service
- Push notification endpoints (`/api/push-subscriptions`) already exist in the Tenant Service
- Encryption operation polling endpoint (`GET /api/operations/{operationId}`) already exists in the Blueprint Service
- The existing SignalR infrastructure can be leveraged for near-real-time progress updates
- The HttpClient registration pattern (manual handler creation) is pre-existing technical debt across 20+ services — this feature follows the established pattern but does not refactor all existing registrations
- Auto-refresh for presentation requests will use a 5-second polling interval (not WebSocket) to keep implementation simple
