# Feature Specification: Unified Organisation Management UI

**Feature Branch**: `069-unified-org-management`
**Created**: 2026-03-25
**Status**: Draft
**Input**: Consolidate two separate admin organisation pages (`/admin/organizations` and `/admin/platform-organizations`) into a single role-aware page with a vertically-stacked collapsible org selector for system admins, enhanced user management with status filtering and admin overrides, and merged participant/published functionality.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Organisation Admin Manages Their Organisation (Priority: P1)

An organisation administrator navigates to the Organisations page and is presented directly with their organisation's dashboard. The page shows the organisation name as the header with tabs below for Users, Participants, and Settings. Above the tabs, a collapsible overview panel shows key stats (user count, participant count, published participant count). The admin can collapse this panel to a compact single-line summary to maximise working space.

**Why this priority**: This is the primary use case — most admin users manage a single organisation and need fast access to its details without navigating through org selection.

**Independent Test**: Can be fully tested by logging in as an org admin, navigating to `/admin/organizations`, and verifying the dashboard loads with correct org context and all tabs are functional.

**Acceptance Scenarios**:

1. **Given** a user with the Administrator or OrganizationAdmin role, **When** they navigate to `/admin/organizations`, **Then** their organisation dashboard loads directly with the org name as header and Users/Participants/Settings tabs visible.
2. **Given** an org admin viewing their dashboard, **When** they view the overview panel, **Then** they see user count, participant count, and published participant count for their organisation.
3. **Given** an org admin viewing the overview panel, **When** they click the collapse control, **Then** the panel collapses to a single-line summary showing key counts, preserving vertical space for tab content.
4. **Given** an org admin, **When** they attempt to access `/admin/organizations/{differentOrgId}`, **Then** they are denied access and shown an appropriate error.

---

### User Story 2 - System Admin Selects and Manages Any Organisation (Priority: P1)

A system administrator navigates to the Organisations page and sees a vertically-stacked org selector panel at the top of the page. This panel shows a searchable, filterable list of all organisations with their status and user counts. The system admin can select an organisation to load its dashboard below, or create a new organisation. Once an org is selected, the selector panel can be collapsed to show "Managing: [Org Name]" with an expand button.

**Why this priority**: System admins need to manage multiple organisations across the platform — the org selector is the gateway to all org management and replaces the separate Platform Organisations page.

**Independent Test**: Can be fully tested by logging in as a system admin, navigating to `/admin/organizations`, verifying the org selector panel shows all organisations, selecting one, and confirming the dashboard loads for the selected org.

**Acceptance Scenarios**:

1. **Given** a user with the SystemAdmin role, **When** they navigate to `/admin/organizations`, **Then** the page shows the "Organisations" header with the org selector panel expanded, listing all organisations with name, status, and user count.
2. **Given** a system admin viewing the org selector, **When** they search or filter by status (Active/Suspended), **Then** the org list updates to match the filter criteria.
3. **Given** a system admin, **When** they select an organisation from the list, **Then** the org dashboard loads below with the Overview, Users, Participants, and Settings tabs for that organisation.
4. **Given** a system admin with an org selected, **When** they collapse the org selector panel, **Then** it shows "Managing: [Org Name]" on a single line with an expand button.
5. **Given** a system admin, **When** they click "Create Organisation" in the selector panel, **Then** a creation dialog opens allowing them to set up a new organisation with name, subdomain, admin email, and role.
6. **Given** a system admin, **When** they navigate to `/admin/organizations/{orgId}` via deep link, **Then** the page loads with the org selector collapsed and the specified organisation's dashboard displayed.

---

### User Story 3 - Admin Manages Users with Status Filtering and Overrides (Priority: P1)

An administrator (org or system) views the Users tab and can filter/sort the user list by status: Active, Invited, and Unverified. For walkthrough and demo scenarios, the admin can perform administrative overrides such as confirming email verification for unverified users, resending invitations, and toggling user active/suspended status — all without requiring the user to complete email verification loops.

**Why this priority**: User management with admin overrides is essential for both production administration and demo/walkthrough scenarios where the email verification loop cannot be completed.

**Independent Test**: Can be fully tested by navigating to the Users tab, verifying filter controls work for each status, and performing an admin email verification override on a test user.

**Acceptance Scenarios**:

1. **Given** an admin on the Users tab, **When** they view the user list, **Then** each user shows their current status (Active, Invited, Unverified) with visual indicators.
2. **Given** an admin on the Users tab, **When** they filter by "Invited" status, **Then** only users who have been invited but not yet accepted are displayed.
3. **Given** an admin on the Users tab, **When** they filter by "Unverified" status, **Then** only users who have not completed email verification are displayed.
4. **Given** an admin viewing an unverified user, **When** they click "Verify Email" override action, **Then** the user's email is marked as verified without requiring the email verification loop.
5. **Given** an admin viewing an invited user, **When** they click "Resend Invitation", **Then** a new invitation is sent to the user.
6. **Given** an admin, **When** they click on a user row or navigate to `/admin/organizations/{orgId}/users/{userId}`, **Then** a user detail/edit view opens showing all editable fields.
7. **Given** an admin editing a user, **When** they update display name, role, or status and save, **Then** the changes are persisted via the existing user update endpoint.

---

### User Story 4 - Admin Manages Participants with Inline Publish Status (Priority: P2)

An administrator views the Participants tab which shows all participants with their publish status (Draft, Published, Revoked) indicated inline. The admin can create, edit, publish, and revoke participants from this single tab — there is no separate "Published" tab.

**Why this priority**: Merging the Participants and Published tabs simplifies the mental model — participants and their publish status belong together.

**Independent Test**: Can be fully tested by navigating to the Participants tab, viewing publish status indicators, and publishing/revoking a participant.

**Acceptance Scenarios**:

1. **Given** an admin on the Participants tab, **When** they view the participant list, **Then** each participant shows their publish status (Draft, Published, Revoked) with visual indicators.
2. **Given** an admin viewing a Draft participant, **When** they click "Publish", **Then** the participant is published to the register and the status updates to Published.
3. **Given** an admin viewing a Published participant, **When** they click "Revoke", **Then** the participant's published record is revoked and the status updates to Revoked.
4. **Given** an admin, **When** they click "Create Participant", **Then** they can define a new participant and optionally publish immediately.

---

### User Story 5 - Admin Configures Organisation Settings (Priority: P3)

An administrator views the Settings tab to manage organisation branding (logo, colours, tagline) and security policies. Settings are saved per-organisation and reflect immediately in the organisation's UI presentation.

**Why this priority**: Settings configuration is important but less frequently accessed than user and participant management.

**Independent Test**: Can be fully tested by navigating to the Settings tab, modifying branding fields, saving, and verifying persistence.

**Acceptance Scenarios**:

1. **Given** an admin on the Settings tab, **When** they modify branding fields (logo URL, primary colour, secondary colour, tagline), **Then** changes can be saved and persist across page reloads.
2. **Given** an admin on the Settings tab, **When** they view security policies, **Then** they see configurable options for 2FA enforcement, password length, and session timeout.

---

### User Story 6 - Deep-Linked User Detail View (Priority: P2)

An administrator navigates directly to `/admin/organizations/{orgId}/users/{userId}` (via bookmark, shared link, or audit trail) and sees the full user detail/edit view in context of the correct organisation. For system admins, the org selector shows the correct org. For org admins, access is only permitted for their own organisation.

**Why this priority**: Deep links enable integration with audit logs, support workflows, and direct navigation from notifications.

**Independent Test**: Can be fully tested by navigating to a deep-linked user URL and verifying the correct org and user context loads.

**Acceptance Scenarios**:

1. **Given** a system admin, **When** they navigate to `/admin/organizations/{orgId}/users/{userId}`, **Then** the page loads with the org selector collapsed showing the correct org, and the user detail view displayed.
2. **Given** an org admin, **When** they navigate to `/admin/organizations/{theirOrgId}/users/{userId}`, **Then** the user detail view loads for that user.
3. **Given** an org admin, **When** they navigate to `/admin/organizations/{otherOrgId}/users/{userId}`, **Then** access is denied.

---

### Edge Cases

- What happens when a system admin's selected organisation is suspended or deleted while they are viewing it? The page should show a status banner and disable edit actions.
- What happens when an org admin's organisation is suspended? They should see a read-only view with a suspension notice.
- What happens when a deep-linked organisation ID does not exist? Show a "not found" message with navigation back to the org list.
- What happens when a deep-linked user ID does not exist within the organisation? Show a "user not found" message within the org context.
- What happens when the admin tries to verify an already-verified user's email? The action should be hidden or disabled.
- What happens when the admin is the last Administrator in the org and tries to change their own role? The system should prevent this to avoid orphaned organisations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display a single unified Organisations page at `/admin/organizations` accessible to Administrator, OrganizationAdmin, and SystemAdmin roles.
- **FR-002**: System MUST remove the separate `/admin/platform-organizations` page and its navigation entry under Admin > System.
- **FR-003**: System MUST move the "Organisations" navigation entry from Admin > System to directly under Admin.
- **FR-004**: System MUST show organisation admins their own organisation dashboard directly without an org selector.
- **FR-005**: System MUST show system admins a vertically-stacked, collapsible org selector panel at the top of the page with search, status filter, and create organisation capabilities.
- **FR-006**: System MUST provide an overview stats panel (user count, participant count, published count) that collapses to a compact single-line summary.
- **FR-007**: System MUST provide three tabs: Users, Participants, and Settings.
- **FR-008**: The Users tab MUST support filtering and sorting by user status: Active, Invited, and Unverified.
- **FR-009**: System MUST provide an admin override action to mark a user's email as verified without requiring the email verification loop.
- **FR-010**: System MUST provide a "Resend Invitation" action for users in Invited status.
- **FR-011**: System MUST support deep-linked URLs: `/admin/organizations/{orgId}` and `/admin/organizations/{orgId}/users/{userId}`.
- **FR-012**: The Participants tab MUST show publish status (Draft, Published, Revoked) inline for each participant, replacing the separate Published tab.
- **FR-013**: The Participants tab MUST provide publish and revoke actions inline.
- **FR-014**: System MUST enforce organisation-scoped access control — org admins can only access their own organisation's data.
- **FR-015**: System MUST expose `EmailVerified` status on user list and detail responses to enable status filtering independently of the account lifecycle status (sourced from the platform user record via query-time join — no schema change required).
- **FR-016**: System MUST provide a backend endpoint for admin email verification override that updates the `EmailVerified` field with appropriate authorisation checks.
- **FR-017**: The user list endpoint MUST support filtering by invitation and verification status.
- **FR-018**: System MUST reuse existing components (OrganizationList, OrganizationForm, OrganizationConfiguration) with refactoring rather than rebuilding from scratch.

### Key Entities

- **Organisation**: The top-level entity representing a tenant. Has name, subdomain, status (Active/Suspended/Deleted), and branding configuration. System admins can manage all; org admins manage their own.
- **User Identity**: A user within an organisation. Has email, display name, roles, account status (Active/Suspended/Deleted), email verification status (boolean), provisioning method, and invitation tracking.
- **Participant**: A workflow participant linked to an organisation. Has identity details, wallet links, and a publish status (Draft/Published/Revoked) representing its on-register state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can access all organisation management functions (users, participants, settings) from a single page without navigating between separate pages.
- **SC-002**: System admins can find and select any organisation within 3 interactions (search/filter/select) from the Organisations page.
- **SC-003**: Admins can filter the user list by any status (Active, Invited, Unverified) within a single click.
- **SC-004**: Admin email verification override completes in a single action — no multi-step workflow required.
- **SC-005**: Deep-linked URLs to specific organisations and users load the correct context without requiring additional navigation.
- **SC-006**: The collapsible overview panel reduces its vertical footprint by at least 75% when collapsed.
- **SC-007**: The collapsible org selector panel reduces its vertical footprint by at least 80% when collapsed, showing only the selected org name.
- **SC-008**: Participant publish status is visible at a glance on the Participants tab without switching to a separate view.
- **SC-009**: All existing E2E tests for organisation management continue to pass or are updated to reflect the new page structure.
- **SC-010**: Page load time for the unified Organisations page does not exceed the combined load time of the two pages it replaces.

## Assumptions

- The existing OrganizationList, OrganizationForm, and OrganizationConfiguration components are sufficiently modular to be refactored into the new layout without full rewrites.
- The existing user update endpoint can be extended to accept an EmailVerified field update.
- The ProvisioningMethod.Invitation and InvitedByUserId fields on UserIdentity are sufficient to determine "Invited" status without adding a new enum value.
- "Unverified" status is determined by the new EmailVerified boolean being false on an Active user account.
- The org selector panel uses the same data source as the current Platform Organisations list endpoint, with pagination and filtering.
- Branding and security policy configuration remain unchanged in functionality — only the tab label changes from "Configuration" to "Settings".
- The separate platform and organisation admin service clients can be consolidated or composed without breaking existing functionality.
