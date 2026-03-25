# Research: Unified Organisation Management UI

**Branch**: `069-unified-org-management` | **Date**: 2026-03-25

## R1: EmailVerified Field — Join vs Denormalize

**Decision**: Query-time join from UserIdentity → PlatformUser (no denormalization)

**Rationale**:
- `PlatformUser.EmailVerified` and `PlatformUser.EmailVerifiedAt` already exist in the public schema
- `UserIdentity.PlatformUserId` provides the FK for the join
- Denormalizing would require sync logic (events/triggers) for a field that changes at most once per user
- The user list query is already org-scoped (small result sets) — join cost is negligible
- EF Core can eager-load via navigation property or explicit join in the repository query

**Alternatives considered**:
- Denormalize `EmailVerified` to `UserIdentity` table — rejected due to sync complexity for a write-once field
- Add a computed column/view — rejected as over-engineering for this use case

## R2: Invitation Status Determination

**Decision**: Derive invitation status from existing `OrgInvitation` table + `UserIdentity.ProvisionedVia` field

**Rationale**:
- `OrgInvitation` already tracks `Status` (Pending/Accepted/Expired/Revoked) per email per org
- `UserIdentity.InvitedByUserId` is non-null for invited users
- `UserIdentity.ProvisionedVia == Invitation` identifies invitation-sourced users
- For the "Invited" filter: query OrgInvitations where Status=Pending for the org (these users may not yet have a UserIdentity)
- For the "Unverified" filter: join PlatformUser where EmailVerified=false and UserIdentity.Status=Active

**Alternatives considered**:
- Add `InvitationStatus` enum to `IdentityStatus` — rejected as it conflates account lifecycle with invitation workflow
- Add separate `VerificationStatus` enum — rejected as unnecessary when a boolean + join suffices

## R3: Admin Email Verification Override

**Decision**: New endpoint `POST /api/organizations/{orgId}/users/{userId}/verify-email` that sets `PlatformUser.EmailVerified = true`

**Rationale**:
- Dedicated endpoint is more RESTful and auditable than overloading the existing PUT
- Requires Administrator role + org membership validation
- Sets `PlatformUser.EmailVerified = true` and `PlatformUser.EmailVerifiedAt = DateTimeOffset.UtcNow`
- Clears `PlatformUser.VerificationToken` and `PlatformUser.VerificationTokenExpiresAt`
- Records audit event `EmailVerifiedByAdmin` (new audit event type)
- Returns 204 NoContent on success

**Alternatives considered**:
- Add `EmailVerified` to existing `UpdateUserRequest` on PUT — rejected as it mixes concerns (user profile vs verification state)
- Platform-level endpoint — rejected as verification override should be org-admin scoped

## R4: User List Filtering API Design

**Decision**: Add query parameters to existing `GET /api/organizations/{orgId}/users` endpoint

**Rationale**:
- Existing endpoint already has `includeInactive` bool parameter
- Add: `?emailVerified=true|false` — filter by PlatformUser.EmailVerified
- Add: `?provisionedVia=Invitation|Local|...` — filter by provisioning method
- Add: `?pendingInvitations=true` — include pending OrgInvitation records (users not yet in UserIdentity)
- Backwards compatible — existing callers without new params get same behaviour
- Return enhanced `UserResponse` with new fields regardless of filter

**Alternatives considered**:
- Separate `/users/invited` and `/users/unverified` endpoints — rejected as proliferating endpoints for what is filter logic
- POST-based search endpoint — rejected as over-engineering for simple filters

## R5: Org Selector Panel UX Pattern

**Decision**: Vertically-stacked collapsible `MudExpansionPanel` containing the existing `OrganizationList` component in compact mode

**Rationale**:
- MudBlazor's `MudExpansionPanel` provides native collapse/expand with animation
- Collapsed state shows "Managing: [Org Name]" text + expand icon
- Expanded state shows the org list with search and status filter
- Vertically stacked avoids width constraints that a right-drawer would face
- Compact mode on `OrganizationList`: hide action buttons, show only name/status/user-count, single-click selects

**Alternatives considered**:
- Right-anchored `MudDrawer` — rejected by stakeholder due to insufficient horizontal space
- `MudDialog` overlay — rejected as it blocks the page and doesn't support "pick and stay" UX
- Full-page org list with route change — rejected as too many clicks for frequent org switching

## R6: Merging Participants and Published Tabs

**Decision**: Enhance `ParticipantList` component with an additional "Published" status column and inline revoke action

**Rationale**:
- Current `ParticipantList` already shows participants with a "Publish" action button
- Current `PublishedParticipantsList` queries register endpoints for published records
- Merge by: loading both data sources, matching on ParticipantId, displaying publish status as a chip (None/Published/Revoked)
- Revoke action added inline (trash icon, confirmation dialog)
- Published details (register name, version, TX ID) shown in expandable row or tooltip
- `PublishedParticipantsList.razor` can be deleted after merge

**Alternatives considered**:
- Keep two tabs but visually link them — rejected as it doesn't simplify the mental model
- Sub-tabs within Participants — rejected as unnecessary nesting

## R7: Deep-Link Routing Strategy

**Decision**: Use Blazor `@page` directives with route parameters + `OnParametersSetAsync` for context loading

**Rationale**:
- `/admin/organizations` — base route, role-based default view
- `/admin/organizations/{orgId:guid}` — deep-link to org dashboard
- `/admin/organizations/{orgId:guid}/users/{userId:guid}` — separate page component for user detail
- Route parameters typed as `Guid` for compile-time safety
- `OnParametersSetAsync` loads org context and validates access
- System admin: collapses org selector, pre-selects org
- Org admin: validates orgId matches their org, returns 403 if not

**Alternatives considered**:
- Query string parameters (`?orgId=...`) — rejected as less RESTful and harder to bookmark
- Single page with dialog for user detail — rejected as deep-links need a real route
