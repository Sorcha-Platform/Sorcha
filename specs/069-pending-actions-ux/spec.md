# Feature Specification: Pending Actions UX Overhaul & Instance Reference System

**Feature Branch**: `069-pending-actions-ux`
**Created**: 2026-03-26
**Status**: Draft
**Input**: Overhaul "My Pending Actions" page with user-oriented task cards, auto-generated instance references, API enrichment, and Execute Action form fix.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Meaningful Pending Action Cards (Priority: P1)

A participant (e.g., a structural engineer) logs in and navigates to "My Pending Actions". Instead of seeing cryptic blueprint IDs and instance UUIDs, they see task-oriented cards showing the workflow name, the specific action they need to perform, and a human-readable reference identifying the application.

For example, instead of:
> Action 2 — construction-permit-20260326141308 — Instance: 9e8062...ee39d9

They see:
> Construction Permit Approval — Structural Assessment — CP-RIV-14W-a7k3

**Why this priority**: This is the core UX problem. Without meaningful card content, the page is unusable for real workflows where a user might have dozens of pending actions across multiple applications. Everything else builds on having the right data in the cards.

**Independent Test**: Can be tested by logging in as a participant with pending actions and verifying the cards display blueprint title, action title, and instance reference instead of raw IDs.

**Acceptance Scenarios**:

1. **Given** a participant has pending actions, **When** they view the Pending Actions page, **Then** each card displays the blueprint title (e.g., "Construction Permit Approval"), the action title (e.g., "Structural Assessment"), the instance reference (e.g., "CP-RIV-14W-a7k3"), and the assigned date.
2. **Given** a blueprint does not define an instance reference template, **When** an instance is created from that blueprint, **Then** a fallback reference is generated using the blueprint prefix and a unique short hash (e.g., "BP-a7k3x").
3. **Given** the pending actions API returns data, **When** the UI maps it to card view models, **Then** no raw blueprint IDs or truncated instance UUIDs are shown as primary identifiers.

---

### User Story 2 - Auto-Generated Instance Reference (Priority: P1)

A blueprint author defines an `instanceReference` section in their blueprint that specifies how to generate a human-readable compound reference for each workflow instance. When the first action is submitted, the system auto-generates the reference from the submitted payload fields and stores it as searchable public metadata on the instance.

For the Construction Permit blueprint, the reference might be configured as:
- Prefix: "CP"
- Components: first word of projectName (3 chars) + first word of siteAddress (3 chars)
- Plus a short uniqueness hash

Producing references like `CP-RIV-14W-a7k3` for "Riverside Heights" at "14 Waterfront Lane".

**Why this priority**: Tied with P1 because the instance reference is the key data that makes cards meaningful. Without it, cards can show action titles but still can't distinguish between two "Structural Assessment" actions for different applications.

**Independent Test**: Can be tested by creating a workflow instance, submitting Action 1, and verifying the instance metadata contains the auto-generated reference matching the expected pattern.

**Acceptance Scenarios**:

1. **Given** a blueprint defines an `instanceReference` with prefix "CP" and components from `/projectName` and `/siteAddress`, **When** Action 1 is submitted with projectName "Riverside Heights" and siteAddress "14 Waterfront Lane, Riverside, RS1 4AB", **Then** the instance metadata contains a reference matching the pattern `CP-RIV-14W-{hash}` where hash is a 4-character alphanumeric suffix.
2. **Given** a blueprint defines no `instanceReference`, **When** an instance is created, **Then** the system generates a fallback reference using the first 2 characters of the blueprint title plus a unique hash.
3. **Given** two instances are created from the same blueprint with identical field values, **When** references are generated, **Then** the short hash suffix ensures they are unique.
4. **Given** Action 1 payload contains a field referenced by `instanceReference` with a value of `null` or empty string, **When** the reference is generated, **Then** that component is replaced with "UNK" (unknown) and the reference is still valid.

---

### User Story 3 - Execute Action Form Loads Schema (Priority: P2)

A participant clicks "TAKE ACTION" on a pending action card and the system opens a dialog with the correct form fields for that action. The form fields are generated from the blueprint action's data schema, allowing the participant to fill in and submit their response.

**Why this priority**: Without this fix, participants cannot submit actions through the UI at all — the form is empty. This is a critical bug but ranked P2 because the pending actions list (P1) must show meaningful data first to even identify which action to take.

**Independent Test**: Can be tested by clicking TAKE ACTION on any pending action and verifying the dialog displays the correct form fields matching the blueprint action's schema definition.

**Acceptance Scenarios**:

1. **Given** a participant has a pending Action 2 (Structural Assessment) with fields loadRating, foundationType, structuralGrade, and structuralNotes, **When** they click TAKE ACTION, **Then** the dialog displays input fields for all four schema-defined fields with correct labels, types, and validation.
2. **Given** a participant clicks TAKE ACTION, **When** the schema is being loaded, **Then** a loading indicator is shown in the dialog until the form renders.
3. **Given** the schema fetch fails (network error, service unavailable), **When** the dialog opens, **Then** an error message is displayed with a retry option.
4. **Given** a participant fills in all required fields and clicks SUBMIT, **When** the form validates successfully, **Then** the action is submitted through the existing execution pipeline.

---

### User Story 4 - Card/Row View Toggle with Persisted Preference (Priority: P3)

A participant can switch between a card grid view (visual, good for few items) and a compact table row view (efficient, good for many items). Their preference is remembered across sessions — if they choose table view, it stays on table view after logout and login.

**Why this priority**: Nice-to-have UX improvement. The card view works for small numbers but becomes unwieldy with 10+ pending actions. Table view provides density. Persisted preference avoids the annoyance of resetting every session.

**Independent Test**: Can be tested by toggling between views, verifying both render correctly, then logging out, logging back in, and confirming the last-selected view is restored.

**Acceptance Scenarios**:

1. **Given** a participant is on the Pending Actions page in card view, **When** they click the table/row toggle button, **Then** pending actions display as compact table rows with columns for action title, instance reference, blueprint name, assigned date, urgency, and an action button.
2. **Given** a participant switches to table view, **When** they log out and log back in, **Then** the Pending Actions page loads in table view.
3. **Given** a participant has no pending actions, **When** they view the page in either mode, **Then** an appropriate empty state message is displayed.

---

### User Story 5 - Grouped and Sorted Actions (Priority: P3)

Pending actions are grouped by blueprint type so participants can quickly find actions related to a specific workflow. Within each group, actions are sorted by assigned date (newest first). Badge counts show how many actions are pending per workflow type.

**Why this priority**: Organizational improvement for users managing multiple workflow types simultaneously. Less critical than getting the right data on cards.

**Independent Test**: Can be tested by creating pending actions across multiple blueprint types and verifying they appear grouped with correct counts and sort order.

**Acceptance Scenarios**:

1. **Given** a participant has 3 pending actions for "Construction Permit Approval" and 2 for "Planning Application", **When** they view Pending Actions, **Then** actions are grouped under blueprint headings with counts (e.g., "Construction Permit Approval (3)").
2. **Given** a group has multiple pending actions, **When** displayed, **Then** they are sorted by assigned date with newest first.
3. **Given** a participant has actions from only one blueprint type, **When** they view Pending Actions, **Then** the single group is displayed without unnecessary grouping chrome.

---

### User Story 6 - Documentation for Blueprint Authors and AI Assistants (Priority: P2)

A blueprint author (human or AI) wants to add an `instanceReference` to their blueprint. They consult the blueprint schema documentation and find a clear explanation of the property, its components, available transforms, and worked examples. AI assistants building blueprints via the designer or MCP server also need this information in their context (CLAUDE.md, blueprint-builder skill) to generate correct references without guessing.

**Why this priority**: Same as P2. Without documentation, the feature is discoverable only by reading source code. Blueprint authors and AI assistants will produce incorrect or missing references, defeating the purpose.

**Independent Test**: Can be tested by asking an AI assistant to "add an instance reference to a blueprint" and verifying it produces valid configuration without additional prompting.

**Acceptance Scenarios**:

1. **Given** a blueprint author reads the blueprint schema documentation, **When** they look for instance reference configuration, **Then** they find a dedicated section with the property schema, available transforms, validation rules, and at least two worked examples.
2. **Given** an AI assistant is building a blueprint via the blueprint-builder skill, **When** the blueprint includes participant data entry (Action 1), **Then** the assistant suggests an appropriate `instanceReference` configuration based on the action's data schema fields.
3. **Given** a developer reads CLAUDE.md, **When** they search for "instanceReference", **Then** they find the property documented in the Blueprint Creation Standards or a linked reference.

---

### Edge Cases

- What happens when a blueprint action has no title defined? The system should use "Action {id}" as a fallback display name.
- What happens when the instance reference generation encounters Unicode or non-ASCII characters in field values? The system should transliterate or strip non-alphanumeric characters, using only ASCII in the reference.
- What happens when a participant has 100+ pending actions? The page should paginate or virtualise, and table view should handle large lists without performance degradation.
- What happens when the same user has pending actions in both the card/row Pending Actions page AND the sidebar Pending Actions panel? Both should show the same enriched data consistently.
- What happens when a blueprint is updated after instances are already running? The instance reference and action titles should be stable — based on the blueprint version at instance creation, not the current version.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow blueprint authors to define an `instanceReference` section in the blueprint that specifies a prefix and field-based components for generating human-readable instance references.
- **FR-002**: The system MUST auto-generate an instance reference when the first action of an instance is completed, using the blueprint-defined template and submitted payload values.
- **FR-003**: The system MUST store the generated instance reference as plaintext metadata on the instance record, accessible without decryption by all participants.
- **FR-004**: The system MUST generate a unique fallback reference for blueprints that do not define an `instanceReference` template.
- **FR-005**: The system MUST ensure instance references are unique within a register by including a short hash suffix derived from the instance ID.
- **FR-006**: The pending actions endpoint MUST return the action title, blueprint title, and instance reference alongside the existing action data.
- **FR-007**: The UI MUST display blueprint title, action title, and instance reference as the primary identifiers on pending action cards, replacing raw blueprint IDs and instance UUIDs.
- **FR-008**: The system MUST fetch the blueprint action's data schema on-demand when a user clicks TAKE ACTION, and render the form fields from that schema in the Execute Action dialog.
- **FR-009**: The UI MUST provide a toggle to switch between card grid view and compact table row view on the Pending Actions page.
- **FR-010**: The system MUST persist the user's view preference (card or table) locally, surviving logout and login cycles.
- **FR-011**: The UI MUST group pending actions by blueprint type when displaying multiple actions, showing a count badge per group.
- **FR-012**: The UI MUST sort pending actions within each group by assigned date, newest first, as the default order.
- **FR-013**: Documentation MUST be updated to cover the `instanceReference` blueprint property — including the blueprint schema reference, a usage guide with examples, and updates to CLAUDE.md so that AI assistants know how to configure instance references when building blueprints.

### Key Entities

- **Instance Reference**: A human-readable compound identifier (e.g., "CP-RIV-14W-a7k3") auto-generated from blueprint-defined field mappings. Stored as public metadata on the workflow instance. Components include a prefix, field-derived segments with configurable transforms, and a uniqueness hash.
- **Instance Reference Template**: A blueprint-level definition specifying how to generate references — prefix string, ordered list of field components with JSON Pointer paths and transform rules (first-word, truncate, uppercase).
- **Enriched Pending Action**: A pending action record augmented with action title, blueprint title, and instance reference — the minimum data needed for a user-oriented task card.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Participants can identify which application a pending action belongs to within 2 seconds of viewing the Pending Actions page, without clicking into any action.
- **SC-002**: Every workflow instance has a unique, human-readable reference visible to all participants within 10 seconds of the first action being submitted.
- **SC-003**: Participants can fill in and submit action forms through the UI for all action types — the Execute Action dialog renders correct form fields 100% of the time when the blueprint defines a data schema.
- **SC-004**: Switching between card and table views takes less than 1 second, and the preference persists across at least 5 consecutive login sessions.
- **SC-005**: The Pending Actions page remains responsive (renders within 2 seconds) with up to 50 pending actions displayed.

## Assumptions

- Blueprint authors are expected to define `instanceReference` templates for production workflows. The fallback (prefix + hash) is a safety net, not the primary experience.
- The instance reference is intentionally public metadata — it is not part of the encrypted payload. Blueprint authors understand that the fields they reference (e.g., project name, address) will be visible in plaintext as part of the reference. The reference is a summary/label, not a disclosure of the full field value.
- The `instanceReference` component transforms (first-word, truncate) operate on the raw string value and produce uppercase ASCII output. Complex transforms (regex, conditional) are out of scope.
- View preference persistence uses browser-local storage, not server-side user preferences. Clearing browser data resets the preference.
- The action schema fetch for the Execute Action dialog uses the existing blueprint service API — no new schema storage is needed.
- Grouping and sorting are client-side operations on the fetched pending actions list. Server-side grouping/sorting is out of scope for this feature.
