# Feature Specification: Identity & Credentials Admin

**Feature Branch**: `050-identity-credentials-admin`
**Created**: 2026-03-05
**Status**: Draft
**Input**: User description: "Feature 050: Identity & Credentials Admin — UI components and CLI commands for credential lifecycle management (suspend/reinstate/refresh), participant publishing to registers, participant suspend/reactivate, W3C Bitstring status list viewer, and verifiable presentations (OID4VP) for both holder and verifier."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Credential Lifecycle Management (Priority: P1)

An issuer administrator needs to manage the lifecycle of credentials they have issued. When a credential holder's access should be temporarily restricted, the issuer suspends the credential. When the situation is resolved, the issuer reinstates it. If a credential is compromised or the holder's rights are permanently revoked, the issuer revokes it. When a credential expires and the holder is still in good standing, the issuer refreshes it with a new expiry.

**Why this priority**: Credential lifecycle is the most frequently needed admin operation. Without it, issuers must use raw API calls to manage credential states, which is error-prone and inaccessible to non-technical administrators.

**Independent Test**: Can be fully tested by issuing a credential, then performing suspend, reinstate, revoke, and refresh operations through the UI and CLI. Delivers immediate value for credential governance.

**Acceptance Scenarios**:

1. **Given** an Active credential, **When** the issuer selects their wallet and clicks "Suspend" with a reason, **Then** the credential status changes to Suspended and the status list is updated
2. **Given** a Suspended credential, **When** the issuer selects their wallet and clicks "Reinstate", **Then** the credential status returns to Active and the status list bit is cleared
3. **Given** an Active or Suspended credential, **When** the issuer selects their wallet and clicks "Revoke", **Then** the credential status changes to Revoked permanently, with an irreversibility warning shown before confirmation
4. **Given** an Expired credential, **When** the issuer selects their wallet and clicks "Refresh", **Then** the old credential is consumed and a new credential is issued with a fresh expiry
5. **Given** a credential detail view, **When** the credential is in Revoked status, **Then** no lifecycle action buttons are displayed
6. **Given** a user with no linked wallets, **When** they view a credential they issued, **Then** they see a message explaining that a linked wallet is required to perform lifecycle actions
7. **Given** a CLI user, **When** they run `credential suspend --id <id> --wallet <addr>`, **Then** the credential is suspended and confirmation is displayed

---

### User Story 2 - Participant Suspend & Reactivate (Priority: P1)

An organization administrator needs to temporarily suspend a participant's access without permanently removing them. This is needed when a participant is under review, on leave, or has a compliance issue. Once resolved, the administrator reactivates the participant to restore their access.

**Why this priority**: Participant lifecycle management is a core identity operation. Currently, the only option is permanent deactivation (delete), which is too destructive for temporary situations.

**Independent Test**: Can be tested by creating a participant, suspending them, verifying their status changes, then reactivating them. Delivers the ability to manage participant access without data loss.

**Acceptance Scenarios**:

1. **Given** an Active participant, **When** the administrator clicks "Suspend" and confirms, **Then** the participant status changes to Suspended and their access is restricted
2. **Given** a Suspended participant, **When** the administrator clicks "Reactivate" and confirms, **Then** the participant status returns to Active
3. **Given** an Inactive (deactivated) participant, **When** the administrator views their detail page, **Then** no suspend or reactivate buttons are shown
4. **Given** the participant list view, **When** participants are displayed, **Then** each participant shows a color-coded status indicator (green for Active, amber for Suspended, grey for Inactive)
5. **Given** a CLI user, **When** they run `participant suspend --org-id <id> --id <id>`, **Then** the participant is suspended and confirmation is displayed

---

### User Story 3 - Participant Publishing to Register (Priority: P2)

An organization administrator needs to publish a participant's identity record to a register so that the participant can be discovered and verified by other network participants. The administrator selects which linked wallet addresses to include and signs the transaction with an authorized wallet.

**Why this priority**: Publishing participants to registers is required for cross-organization identity verification, but it is not as urgently needed as basic lifecycle management. The operation is complex (multi-field form with wallet selection) but serves a smaller set of power users.

**Independent Test**: Can be tested by publishing a participant record to a register, verifying it appears in the register's participant list, then revoking the published record. Delivers on-register identity publishing capability.

**Acceptance Scenarios**:

1. **Given** an Active participant with linked wallets, **When** the administrator clicks "Publish to Register" on the detail page, **Then** a form opens pre-filled with participant name, organization name, and available wallets
2. **Given** the publish form, **When** the administrator selects a register, wallet addresses, and signer wallet then submits, **Then** the participant record is published to the register and a success confirmation is shown
3. **Given** a published participant, **When** the administrator views the detail page, **Then** the page shows which registers the participant is published to, with options to update or revoke
4. **Given** a published participant, **When** the administrator clicks "Revoke" on a published record, **Then** the on-register record is revoked and the status updates
5. **Given** a CLI user, **When** they run `participant publish --org-id <id> --register-id <id> --name <n> --org-name <n> --wallet <addr> --signer <addr>`, **Then** the participant is published and confirmation is displayed

---

### User Story 4 - Verifiable Presentations (Priority: P2)

Two user journeys exist for verifiable presentations:

**Holder flow**: A credential holder receives a presentation request from a verifier. They review what information is being requested, which credentials match, and can approve (with selective disclosure of specific claims) or deny the request.

**Verifier flow**: An administrator acting as a verifier creates a presentation request targeting a specific wallet. They specify the credential type, required claims, accepted issuers, and callback URL. After creation, they receive a shareable link/QR code and can poll for the result.

**Why this priority**: Presentations are a core credential exchange mechanism, but they require both holder and verifier sides to be useful. Currently, the UI shows only a placeholder. The CLI already has partial support via `credential present`.

**Independent Test**: The holder flow can be tested by creating a presentation request (via CLI or verifier UI), then reviewing and approving it in the holder UI. The verifier flow can be tested by creating a request, sharing the link, and viewing the result.

**Acceptance Scenarios**:

1. **Given** a holder with pending presentation requests, **When** they view their credentials page, **Then** they see a list of pending requests with verifier identity and expiry
2. **Given** a presentation request detail, **When** the holder reviews it, **Then** they see the verifier's identity, required claims, matching credentials from their wallet, and the expiry time
3. **Given** a presentation request, **When** the holder selects a credential and specific claims to disclose then confirms, **Then** the presentation is submitted and the verifier receives the result
4. **Given** a presentation request, **When** the holder clicks "Deny", **Then** the request status changes to Denied
5. **Given** the verifier admin page, **When** the verifier fills in credential type, required claims, target wallet, and callback URL then submits, **Then** a presentation request is created and a QR code / shareable link is displayed
6. **Given** a created presentation request, **When** the verifier views the request status, **Then** they see the current status (Pending, Completed, Denied, Expired) and verification result when available
7. **Given** an expired presentation request, **When** either party attempts to interact with it, **Then** they see an "expired" indicator and no actions are available

---

### User Story 5 - Status List Viewer (Priority: P3)

An administrator needs to inspect W3C Bitstring Status Lists to audit credential revocation and suspension status at scale. The status list viewer provides a read-only view of status lists with their metadata and the raw credential document.

**Why this priority**: Status lists are an internal mechanism managed automatically by credential lifecycle operations. The viewer serves a diagnostic/audit purpose and is needed less frequently than active management operations.

**Independent Test**: Can be tested by navigating to the status list admin page, viewing a list's metadata, and inspecting its raw document. Delivers audit visibility into the revocation infrastructure.

**Acceptance Scenarios**:

1. **Given** the admin navigation, **When** the administrator navigates to Admin > Credentials > Status Lists, **Then** they see a lookup field for entering a status list ID, plus a list of recently-viewed status lists (stored in browser)
2. **Given** a valid status list ID, **When** the administrator enters it and clicks "Lookup", **Then** they see the list metadata (ID, purpose, issuer, valid from) and can expand the full W3C BitstringStatusListCredential document
3. **Given** no recently-viewed status lists, **When** the administrator first visits the page, **Then** they see an empty state message explaining that status lists are created automatically when credentials are issued
4. **Given** a CLI user, **When** they run `credential status-list get --id <listId>`, **Then** the status list document is displayed in table or JSON format

---

### Edge Cases

- What happens when a user tries to suspend an already-suspended credential? The system returns a validation error: "Credential must be in Active state to suspend"
- What happens when a user tries to reinstate a non-suspended credential? The system returns a validation error: "Credential must be in Suspended state to reinstate"
- What happens when a user tries to refresh a non-expired credential? The system returns a validation error: "Credential must be in Expired state to refresh"
- What happens when the issuer wallet specified does not match the credential's original issuer? The system returns a forbidden error
- What happens when a participant with no linked wallets attempts to be published? The publish button is disabled with a tooltip explaining that linked wallets are required
- What happens when a presentation request expires while the holder is reviewing it? The system shows an "expired" status and disables the approve/deny actions
- What happens when the verifier's callback URL is not HTTPS? The system rejects the request with a validation error
- What happens when a participant is already published to a register and the user tries to publish again? The system shows the existing published record with an "Update" option instead

## Requirements *(mandatory)*

### Functional Requirements

**Credential Lifecycle**

- **FR-001**: System MUST display context-sensitive action buttons on credential detail views based on the current credential status (Active, Suspended, Expired, Revoked)
- **FR-002**: System MUST present an explicit wallet selection dialog for all credential lifecycle operations, showing the user's linked wallets
- **FR-003**: System MUST display an irreversibility warning before credential revocation, requiring explicit confirmation
- **FR-004**: System MUST support optional reason text for suspend, reinstate, and revoke operations
- **FR-005**: System MUST display a message when the user has no linked wallets, preventing lifecycle operations
- **FR-006**: System MUST provide CLI commands for credential suspend, reinstate, and refresh operations

**Participant Lifecycle**

- **FR-007**: System MUST display suspend and reactivate buttons on the participant detail page, with visibility based on current participant status
- **FR-008**: System MUST show color-coded status indicators on participant list items (Active, Suspended, Inactive)
- **FR-009**: System MUST require confirmation before suspending or reactivating a participant
- **FR-010**: System MUST provide CLI commands for participant suspend and reactivate operations

**Participant Publishing**

- **FR-011**: System MUST provide a "Publish to Register" action on the participant detail page, visible only for Active participants with Administrator role
- **FR-012**: System MUST pre-fill the publish form with participant name and organization name from the loaded context
- **FR-013**: System MUST allow the administrator to select which linked wallet addresses to include in the published record
- **FR-014**: System MUST display which registers a participant is currently published to, with options to update or revoke
- **FR-015**: System MUST provide CLI commands for participant publish and unpublish operations

**Status List Viewer**

- **FR-016**: System MUST provide a read-only admin page with a lookup-by-ID field and recently-viewed history (browser localStorage, max 10), displaying status list metadata (ID, purpose, issuer, valid from) when found
- **FR-017**: System MUST allow administrators to view the full W3C BitstringStatusListCredential document for any list
- **FR-018**: System MUST display an empty state when no status lists exist
- **FR-019**: System MUST provide a CLI command to retrieve a status list by ID

**Verifiable Presentations**

- **FR-020**: System MUST replace the presentation placeholder in the credentials section with a working holder flow (view requests, approve with selective disclosure, deny)
- **FR-021**: System MUST provide an admin page for verifiers to create presentation requests with credential type, required claims, accepted issuers, target wallet, callback URL, and TTL
- **FR-022**: System MUST display a QR code or shareable link for created presentation requests
- **FR-023**: System MUST show presentation request status (Pending, Completed, Denied, Expired) with verification results
- **FR-024**: System MUST prevent interaction with expired presentation requests

### Key Entities

- **Credential**: A verifiable credential with lifecycle states (Active, Suspended, Expired, Revoked, Consumed). Belongs to an issuer wallet and a subject. Has optional status list membership.
- **Participant Identity**: A user's identity within an organization, with states (Active, Suspended, Inactive). Can be linked to wallet addresses and published to registers.
- **Published Participant Record**: An on-register identity record with participant name, organization name, wallet addresses, and version. Published via signed transaction.
- **Status List**: A W3C BitstringStatusList tracking revocation or suspension status of credentials. Contains a compressed bitstring and metadata.
- **Presentation Request**: An OID4VP request from a verifier to a holder, specifying required credential type, claims, accepted issuers, and expiry. Has states (Pending, Completed, Denied, Expired).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can suspend, reinstate, revoke, or refresh a credential through the UI in under 30 seconds, including wallet selection and confirmation
- **SC-002**: Administrators can suspend or reactivate a participant through the UI in under 10 seconds (two clicks: action button + confirmation)
- **SC-003**: Administrators can publish a participant to a register through the UI in under 60 seconds, including wallet selection and form submission
- **SC-004**: All 8 new CLI commands complete successfully and display results in both table and JSON output formats
- **SC-005**: Credential holders can review and respond to a presentation request (approve with selective disclosure or deny) in under 45 seconds
- **SC-006**: Verifiers can create a presentation request and receive a shareable link/QR code in under 30 seconds
- **SC-007**: The status list viewer loads and displays list metadata within 2 seconds of navigation
- **SC-008**: All credential lifecycle, participant lifecycle, and presentation operations are covered by automated tests with at least 85% code coverage
- **SC-009**: The participant list displays accurate, color-coded status indicators for all participant states
- **SC-010**: All new UI pages and actions are accessible through the existing navigation structure without requiring documentation

## Assumptions

- All backend API endpoints for the 5 feature areas are fully implemented and stable
- Most UI service interfaces already exist; only 5 new service methods and 1 new service are needed
- The YARP API Gateway already routes credential and presentation endpoints to the correct backend services
- Users performing credential lifecycle operations have at least one linked wallet address (the UI handles the case where they don't)
- The QR code for presentation requests can be generated client-side without a dedicated backend service
- Status list data may not exist in development environments; the UI handles empty states gracefully
- CLI commands follow the established pattern: Refit service client, global `--output` option for table/JSON, auth token handling
