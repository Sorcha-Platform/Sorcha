# Feature Specification: Register TenantId Removal & Security Hardening

**Feature Branch**: `067-register-security-hardening`
**Created**: 2026-03-24
**Status**: Draft
**Input**: User description: "Remove node-local TenantId from Register entity, add RegisterPurpose flag, replace with JWT-based authorization and org-subscription access control"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register Purpose Classification (Priority: P1)

Platform administrators and register owners need to classify registers by purpose so the system can distinguish between platform-internal system registers and user-created general-purpose registers. This enables differentiated access rules — system registers are visible to all authenticated users, while general registers are scoped by subscription.

**Why this priority**: Foundation for all other stories. The purpose flag determines access rules and replaces part of the role that TenantId currently plays in scoping.

**Independent Test**: Can be fully tested by creating registers with different purposes and verifying the flag persists and is queryable. Delivers value by enabling system register identification without relying on TenantId.

**Acceptance Scenarios**:

1. **Given** a new register is being created, **When** no purpose is specified, **Then** the register purpose defaults to "General"
2. **Given** the system register bootstrapper runs, **When** it creates the platform system register, **Then** the register purpose is set to "System"
3. **Given** a register exists with a purpose, **When** the register is queried, **Then** the purpose value is included in the response
4. **Given** a register with purpose "General", **When** an unauthenticated user queries it, **Then** access is denied

---

### User Story 2 - Authenticated Register Creation (Priority: P1)

An organisation administrator creates a new register on behalf of their organisation. The system verifies their identity and role from JWT claims before allowing the operation. Ownership is established through the cryptographic attestations in the RegisterControlRecord, not through a TenantId field.

**Why this priority**: Closes a critical security gap where register creation currently allows anonymous access with a user-supplied TenantId.

**Independent Test**: Can be fully tested by attempting register creation with valid admin JWT, non-admin JWT, and no JWT, verifying only admin succeeds. Delivers value by preventing unauthorised register creation.

**Acceptance Scenarios**:

1. **Given** an authenticated user with org admin role, **When** they initiate register creation, **Then** the system accepts the request and the org's identity is derived from JWT claims
2. **Given** an authenticated user without org admin role, **When** they attempt to initiate register creation, **Then** the system returns a 403 Forbidden response
3. **Given** an unauthenticated request, **When** it attempts to initiate register creation, **Then** the system returns a 401 Unauthorized response
4. **Given** an org admin creating a register, **When** the register is finalized with signed attestations, **Then** the RegisterControlRecord establishes ownership through attestations without any TenantId field

---

### User Story 3 - Subscription-Scoped Register Queries (Priority: P1)

An authenticated user browses registers available to their organisation. The system uses the caller's JWT OrgId claim to look up which registers the organisation is subscribed to (via existing subscription records in Tenant Service) and returns only those registers. System-purpose registers are visible to all authenticated users regardless of subscription.

**Why this priority**: Core security requirement — users must only see registers their organisation has access to. Replaces the broken tenantId query parameter filtering.

**Independent Test**: Can be fully tested by creating subscriptions for an org to specific registers, then querying as a user of that org and verifying only subscribed registers (plus system registers) are returned.

**Acceptance Scenarios**:

1. **Given** an org subscribed to registers A and B but not C, **When** a user from that org queries available registers, **Then** only registers A and B are returned (plus any system registers)
2. **Given** a system register exists, **When** any authenticated user queries registers, **Then** the system register is included in results regardless of subscription status
3. **Given** an org with no register subscriptions, **When** a user queries registers, **Then** only system registers are returned
4. **Given** a user switches org context, **When** they query registers in the new org context, **Then** results reflect the new org's subscriptions

---

### User Story 4 - Attestation-Based Register Deletion (Priority: P2)

A register owner or admin deletes a register. The system verifies authorization by checking the caller's wallet address or org DID against the RegisterControlRecord attestations, replacing the previous TenantId ownership check.

**Why this priority**: Important for security but lower frequency operation. The attestation data already exists; this story connects it to the authorization flow.

**Independent Test**: Can be fully tested by attempting deletion as an attested owner (succeeds), an attested admin (succeeds), and a non-attested user (fails).

**Acceptance Scenarios**:

1. **Given** a register with an Owner attestation for a specific DID, **When** a user whose wallet address matches that DID attempts deletion, **Then** the deletion succeeds
2. **Given** a register with an Admin attestation for a specific DID, **When** a user whose wallet address matches that DID attempts deletion, **Then** the deletion succeeds
3. **Given** a register, **When** a user whose wallet address does not appear in any Owner or Admin attestation attempts deletion, **Then** the system returns 403 Forbidden
4. **Given** a system register, **When** any user attempts deletion, **Then** the system prevents deletion of system registers

---

### User Story 5 - Register-Scoped Real-Time Notifications (Priority: P2)

Users receive real-time notifications (via SignalR) for registers they are actively working with. Instead of subscribing to a tenant-wide notification group, clients subscribe to specific register groups. The system verifies the caller's org has a subscription to the register before allowing the SignalR subscription.

**Why this priority**: Ensures notification security aligns with the new access model. Functionally important but not a blocker for core operations.

**Independent Test**: Can be fully tested by connecting two clients — one with access to a register and one without — and verifying only the authorized client receives notifications for that register.

**Acceptance Scenarios**:

1. **Given** a user whose org is subscribed to register X, **When** they subscribe to notifications for register X, **Then** the subscription succeeds and they receive events for register X
2. **Given** a user whose org is NOT subscribed to register X, **When** they attempt to subscribe to notifications for register X, **Then** the subscription is denied
3. **Given** a transaction is submitted to register X, **When** the transaction is processed, **Then** only clients subscribed to register X's notification group receive the event
4. **Given** a client was using tenant-based notification groups, **When** they connect after this change, **Then** the tenant-based subscription methods are no longer available

---

### User Story 6 - Register Creation UI Purpose Selection (Priority: P1)

An organisation administrator creating a register through the wizard selects the register's purpose from a dropdown in the Options step (alongside the existing Advertise and Full Replica controls). The purpose defaults to "General". The "System" option is only available to system administrators. The review step displays the selected purpose before confirmation.

**Why this priority**: The UI is the primary way registers are created. The purpose field must be selectable at creation time for the classification to be meaningful.

**Independent Test**: Can be fully tested by rendering the Create Register Wizard, verifying the purpose dropdown appears with "General" pre-selected, selecting a different value, and confirming it appears in the review step and is submitted with the creation request.

**Acceptance Scenarios**:

1. **Given** an org admin opens the Create Register Wizard, **When** they reach the Options step, **Then** a "Purpose" dropdown is displayed with "General" selected by default
2. **Given** a non-system-admin user, **When** they view the Purpose dropdown options, **Then** "System" is not available as a choice
3. **Given** a system admin user, **When** they view the Purpose dropdown options, **Then** both "General" and "System" are available
4. **Given** a user selects a purpose, **When** they advance to the Review step, **Then** the selected purpose is displayed in the summary
5. **Given** a user completes the wizard, **When** the register is created, **Then** the selected purpose is persisted on the register

---

### User Story 7 - CLI Register Purpose Option (Priority: P2)

An administrator creating a register via the CLI can specify the register's purpose using a `--purpose` option. The option defaults to "General" when omitted. The register list and get commands display the purpose in their output.

**Why this priority**: CLI is the secondary creation path and must support the same capabilities as the UI.

**Independent Test**: Can be fully tested by running `register create --name test --purpose General` and verifying the created register has the correct purpose, then running `register list` and confirming purpose appears in the output.

**Acceptance Scenarios**:

1. **Given** a CLI user creates a register without `--purpose`, **When** the register is created, **Then** the purpose defaults to "General"
2. **Given** a CLI user creates a register with `--purpose System`, **When** they are not a system admin, **Then** the command returns an authorization error
3. **Given** a CLI user runs `register list`, **When** registers are displayed, **Then** the purpose column is included in the table output
4. **Given** a CLI user runs `register get --id {id}`, **When** the register details are displayed, **Then** the purpose field is shown

---

### User Story 8 - Test Coverage for Purpose and Security Changes (Priority: P1)

All new functionality has comprehensive test coverage: unit tests for the purpose enum and authorization logic, component tests for the UI wizard purpose dropdown, CLI command tests for the new `--purpose` option, and API tests for the new authorization requirements on register creation, query, and deletion endpoints.

**Why this priority**: Without tests, the security hardening cannot be validated. Tests are a deliverable, not an afterthought.

**Independent Test**: Can be verified by running the full test suite and confirming all new tests pass, coverage is maintained above 85%, and no existing tests are broken.

**Acceptance Scenarios**:

1. **Given** the RegisterPurpose enum, **When** unit tests run, **Then** default value, serialization, and all valid values are verified
2. **Given** the Create Register Wizard component, **When** component tests run, **Then** purpose dropdown rendering, default selection, system-admin-only filtering, and review step display are verified
3. **Given** the CLI register create command, **When** CLI tests run, **Then** the `--purpose` option presence, default value, and validation are verified
4. **Given** the register creation API, **When** API tests run, **Then** authentication required (401 for anonymous), admin role required (403 for non-admin), and purpose persistence are verified
5. **Given** the register query API, **When** API tests run, **Then** subscription-scoped results are verified — user sees only subscribed registers plus system registers
6. **Given** the register delete API, **When** API tests run, **Then** attestation-based authorization is verified — only attested owners/admins can delete, system registers are protected
7. **Given** the CLI `register list` and `register get` commands, **When** CLI tests run, **Then** purpose is present in the output format

---

### User Story 9 - TenantId Removal from Domain Model (Priority: P3)

The TenantId property is removed from the Register entity, RegisterControlRecord, creation request DTOs, domain events, service clients, UI models, CLI commands, and all associated tests. All code paths that previously read, wrote, or filtered by TenantId are updated or removed.

**Why this priority**: Cleanup story that depends on all other stories being implemented first. The functional replacements must be in place before the field can be safely removed.

**Independent Test**: Can be verified by confirming no compilation errors after removal, all tests pass, and no runtime references to TenantId remain in register-related code paths.

**Acceptance Scenarios**:

1. **Given** the Register entity definition, **When** inspected, **Then** no TenantId property exists
2. **Given** the RegisterControlRecord, **When** a genesis transaction is created, **Then** no TenantId field is included in the control record
3. **Given** the register creation API, **When** a request is sent with a tenantId field, **Then** the field is ignored (not required, not stored)
4. **Given** the MongoDB register collection, **When** indexes are inspected, **Then** no TenantId index exists
5. **Given** existing registers in the database that have TenantId values, **When** they are queried after migration, **Then** they function correctly with the TenantId field ignored

---

### Edge Cases

- What happens when a register's control record has no attestations (corrupted data)? The system should reject authorization checks and return an error with diagnostic information.
- What happens when a user's JWT contains a wallet_address that was revoked from the control record after a governance operation? Access should be denied based on the current control record state.
- What happens when the Tenant Service is unreachable and subscription data cannot be resolved? The system should fail closed (deny access) rather than fail open.
- What happens when an org's subscription to a register is revoked while a user has an active SignalR connection? The existing connection should continue until disconnected; new subscription attempts should be denied.
- What happens when a system register is created without the "System" purpose flag? It should be treated as a general register and subject to subscription-based access control.
- What happens when a non-system-admin user manipulates the API request to set purpose to "System" directly? The API must reject the request with 403 — UI-only filtering is insufficient.
- What happens when the wizard is opened by a user who has no active wallets? The wallet step should block progress and display a message directing the user to create a wallet first (existing behaviour, unaffected).
- What happens when the CLI `--purpose` flag receives an invalid value (e.g., `--purpose Foo`)? The command should reject it with a clear validation error listing valid options.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a register purpose classification with at least two values: "General" (default) and "System"
- **FR-002**: System MUST default all new registers to "General" purpose unless explicitly set otherwise
- **FR-003**: System MUST require authentication for register creation — anonymous register creation is not permitted
- **FR-004**: System MUST require the caller to hold an org admin role (from JWT claims) to create a register
- **FR-005**: System MUST derive the creating organisation's identity from JWT claims, not from a user-supplied field
- **FR-006**: System MUST scope register query results to registers the caller's organisation is subscribed to (using existing subscription records)
- **FR-007**: System MUST include system-purpose registers in query results for all authenticated users regardless of subscription
- **FR-008**: System MUST authorize register deletion by verifying the caller's identity (wallet address or org DID) appears as an Owner or Admin in the register's control record attestations
- **FR-009**: System MUST prevent deletion of system-purpose registers
- **FR-010**: System MUST replace tenant-scoped SignalR notification groups with register-scoped groups
- **FR-011**: System MUST verify a caller's org has a subscription to a register before allowing SignalR subscription to that register's notification group
- **FR-012**: System MUST remove the TenantId property from the Register entity and all related domain models, DTOs, events, and service clients
- **FR-013**: System MUST remove the TenantId index from the MongoDB register collection
- **FR-014**: System MUST fail closed (deny access) when subscription data cannot be resolved due to service unavailability
- **FR-015**: System MUST handle existing registers that have TenantId values stored in the database without data loss or runtime errors
- **FR-016**: The Create Register Wizard MUST present a "Purpose" dropdown in the Options step, defaulting to "General", alongside the existing Advertise and Full Replica controls
- **FR-017**: The "System" purpose option MUST only be available to system administrators in the UI
- **FR-018**: The wizard Review step MUST display the selected purpose before the user confirms creation
- **FR-019**: The CLI `register create` command MUST accept an optional `--purpose` flag, defaulting to "General"
- **FR-020**: The CLI `register list` and `register get` commands MUST display the register's purpose in their output
- **FR-021**: System MUST have unit tests covering the RegisterPurpose enum, authorization logic, and purpose persistence
- **FR-022**: System MUST have UI component tests covering purpose dropdown rendering, default selection, admin-only filtering, and review step display
- **FR-023**: System MUST have CLI tests covering the `--purpose` option, default value, and output formatting
- **FR-024**: System MUST have API tests verifying authentication and admin role requirements on register creation, subscription-scoped query results, and attestation-based deletion authorization

### Key Entities

- **RegisterPurpose**: Classification of a register's intended use — "General" for user-created registers, "System" for platform-internal registers. Stored as a property on the Register entity. Extensible for future values.
- **Register (modified)**: The distributed ledger entity. TenantId removed; RegisterPurpose added. Ownership determined by RegisterControlRecord attestations.
- **RegisterControlRecord (modified)**: The immutable genesis record establishing register governance. TenantId removed. Attestations remain the authoritative source for ownership and admin roles.
- **Subscription (existing, Tenant Service)**: Existing org-to-register subscription records used to determine which registers an organisation can access. No modifications needed to the subscription model itself.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No register can be created without valid authentication and org admin authorization — 100% of anonymous creation attempts are rejected
- **SC-002**: Authenticated users see only registers their organisation is subscribed to (plus system registers) — zero information leakage across organisations
- **SC-003**: Register deletion is authorized exclusively through control record attestations — no dependency on user-supplied ownership claims
- **SC-004**: All existing tests continue to pass after TenantId removal (updated to use new authorization patterns)
- **SC-005**: Real-time notifications are scoped to individual registers — users receive events only for registers they have access to
- **SC-006**: System registers remain accessible to all authenticated users without requiring explicit subscriptions
- **SC-007**: When the subscription resolution service is unavailable, zero unauthorized access occurs (fail-closed behaviour verified)
- **SC-008**: The Create Register Wizard displays the purpose dropdown with correct options based on user role — verified by component tests
- **SC-009**: The CLI supports `--purpose` on register creation and displays purpose in list/get output — verified by CLI tests
- **SC-010**: All new security and purpose functionality has corresponding test coverage — no untested code paths in authorization checks

## Assumptions

- The existing subscription system in Tenant Service (SubscriptionType records linking orgs to registers) is sufficient to determine register access without modification to the subscription model
- The wallet_address JWT claim (added in PR #116) is available on all authenticated requests and can be used to match against control record attestations
- The system register bootstrapper already identifies the system register and can be updated to set the purpose flag
- Existing registers in MongoDB with TenantId values will not cause errors when the field is removed from the domain model — MongoDB's flexible schema handles unmapped fields gracefully
- The register creation two-phase flow (initiate/finalize) remains structurally the same; only the authorization and field requirements change
