# Feature Specification: Designer & Blueprint Instructions Upgrade

**Feature Branch**: `059-designer-blueprint-upgrade`
**Created**: 2026-03-16
**Status**: Draft
**Input**: Comprehensive upgrade to the Sorcha blueprint designer ecosystem covering unified display, instructions/help text, semantic versioning, catalogue improvements, and a self-hosted Blueprint Publishing Blueprint.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Blueprint Author Creates and Publishes with Instructions (Priority: P1)

A blueprint author designs a workflow using either the visual designer or AI chat designer. They add step-by-step instructions for each action, field-level help text where the data schema doesn't already provide descriptions, and an overview explaining the workflow's purpose. They choose a primary language and link translations for other locales. When ready, they submit the blueprint through a publishing workflow where a reviewer validates it, and a publisher signs and commits it to the system register with version 1.0.

**Why this priority**: This is the core value proposition — blueprints become self-documenting, versioned, and governed. Without this, all other features lack their primary content.

**Independent Test**: Can be tested by creating a blueprint with instructions in the visual designer, submitting it through the publishing workflow, and verifying it appears in the system register with correct version, instructions, and signed provenance.

**Acceptance Scenarios**:

1. **Given** a blueprint in the visual designer, **When** the author opens the Instructions tab and adds overview text and per-action instructions, **Then** the instructions are saved as part of the blueprint and visible in preview mode.
2. **Given** a blueprint with data schemas that include property descriptions, **When** a field has no explicit instruction, **Then** the schema property description is shown as fallback help text in the form UI.
3. **Given** a completed blueprint with instructions, **When** the author clicks "Publish", **Then** the Blueprint Publishing workflow is initiated with the author as the submitter.
4. **Given** an in-progress publishing workflow, **When** the reviewer approves and the publisher signs, **Then** the blueprint is published to the system register as version 1.0 with a signed transaction.
5. **Given** a published blueprint at version 3.2, **When** the author updates only instruction text and submits for publishing, **Then** the system detects a documentation-only change and proposes version 3.3 (minor bump).
6. **Given** a published blueprint at version 3.2, **When** the author modifies an action's routing condition and submits for publishing, **Then** the system detects a structural change and proposes version 4.0 (major bump with minor reset).

---

### User Story 2 - Unified Blueprint Visualisation Across All Contexts (Priority: P1)

A user views a blueprint in any context — visual designer editing, AI chat preview, catalogue browsing, or a read-only viewer dialog — and sees a consistent, well-laid-out flow diagram. Actions are spaced clearly with directional arrows showing process flow, divergent paths (decision points) and convergent paths (merge points) are visually distinct, and cycle paths are marked with curved back-edge arcs. Participant swimlanes indicate who performs each action.

**Why this priority**: The blueprint diagram is the primary communication tool for explaining workflows to new users. Inconsistent or poor visualisation undermines the entire platform's value.

**Independent Test**: Can be tested by loading the same blueprint in the visual designer, AI chat preview, catalogue detail view, and viewer dialog, and verifying all four render the same diagram layout with consistent styling.

**Acceptance Scenarios**:

1. **Given** a blueprint with branching routes, **When** displayed in any context, **Then** divergent paths show a decision diamond at the branch point with labelled condition arrows, and convergent paths show a merge indicator where routes rejoin.
2. **Given** a blueprint with a cycle (back-edge), **When** displayed in any context, **Then** the cycle is shown with a curved arc and the target action is marked with a cycle badge.
3. **Given** a blueprint displayed in the AI chat preview panel, **When** comparing to the same blueprint in the visual designer, **Then** both show the same auto-layout diagram with the same node positions, edge styles, and participant colouring.
4. **Given** the visual designer in Edit mode, **When** the user drags an action node, **Then** the node repositions and connecting edges re-route. Preview mode and Compact mode do not allow dragging.
5. **Given** a blueprint with 5+ participants, **When** displayed in any context, **Then** each participant's actions appear in a visually grouped swimlane with a participant legend.

---

### User Story 3 - Blueprint Publishing Governance Workflow (Priority: P1)

An organisation uses the Blueprint Publishing Blueprint — a Sorcha workflow that governs how blueprints get published to the system register. The author submits a blueprint draft, a reviewer examines the blueprint structure and instructions for correctness, and a publisher cryptographically signs and publishes the approved version. For documentation-only updates, the system automatically detects the change type and routes through a lighter review.

**Why this priority**: Self-hosted governance is the platform's proof-of-concept for its own capabilities. It validates that Sorcha can govern itself and provides the mechanism for all future blueprint publishing.

**Independent Test**: Can be tested by running the Blueprint Publishing workflow end-to-end: submit a draft blueprint, have it reviewed and approved, then signed and published to the system register.

**Acceptance Scenarios**:

1. **Given** the Blueprint Publishing Blueprint template is installed, **When** an author initiates "Publish Blueprint" from the designer or catalogue, **Then** a new workflow instance is created with the author as the submitting participant.
2. **Given** a blueprint submitted for publishing, **When** the reviewer opens their pending action, **Then** they see the full blueprint preview with instructions, a diff against the previous version (if updating), and approve/reject buttons with a comments field.
3. **Given** the reviewer rejects the submission with comments, **When** the author views their pending action, **Then** they see the rejection reason and can revise and resubmit (cycle back to review).
4. **Given** the reviewer approves, **When** the publisher opens their pending action, **Then** they see the approved blueprint and can sign it with their wallet. The signed blueprint is published to the system register.
5. **Given** a documentation-only update is submitted, **When** the system compares the structural hash of the old and new versions, **Then** it confirms no structural changes exist, auto-tags the submission as a minor version bump, and routes to a documentation review action (lighter review).
6. **Given** a structural change is submitted, **When** the system compares versions, **Then** it detects structural differences, tags the submission as a major version bump, and routes to the full review action.

---

### User Story 4 - Seamless Designer Context Handoff (Priority: P2)

A user generates a blueprint using the AI chat designer, refines it in conversation, then clicks "Open in Visual Designer" to make precise edits. The visual designer loads the exact blueprint from the AI session. After editing, the user clicks "Open in AI Chat" to continue conversational refinement. The blueprint state is preserved in both directions.

**Why this priority**: Currently the handoff is broken — "Open in Visual Designer" loses the blueprint. Fixing this unblocks the intended dual-mode design workflow.

**Independent Test**: Can be tested by creating a blueprint in AI chat, clicking "Open in Visual Designer", verifying the blueprint loads, making an edit, then clicking "Open in AI Chat" and verifying the edit is present.

**Acceptance Scenarios**:

1. **Given** a blueprint generated in AI chat, **When** the user clicks "Open in Visual Designer", **Then** the visual designer opens with the blueprint fully loaded (all actions, participants, routes, schemas, instructions).
2. **Given** a blueprint being edited in the visual designer, **When** the user clicks "Open in AI Chat", **Then** the AI chat designer opens with the blueprint loaded and the user can continue conversational editing.
3. **Given** a blueprint loaded from one designer into the other, **When** the user saves, **Then** the save targets the same blueprint ID (no duplicate blueprints created).

---

### User Story 5 - Catalogue Browsing with Templates and Published Blueprints (Priority: P2)

A user opens the Catalogue and sees two sections: static templates (parameterised workflow patterns) and published blueprints (governance-approved workflows from the system register). They can browse, search, and filter both. For templates, they configure parameters and generate a new blueprint. For published blueprints, they can view version history, see who signed each version, and create a new instance.

**Why this priority**: The catalogue is currently empty on startup because templates aren't auto-seeded and published blueprints aren't queried. Making it useful drives template and blueprint adoption.

**Independent Test**: Can be tested by starting the platform, opening the Catalogue, and verifying templates appear (auto-seeded) and published blueprints appear (from system register).

**Acceptance Scenarios**:

1. **Given** the platform starts fresh, **When** the user opens the Catalogue, **Then** the Templates section shows the pre-built templates from the `blueprints/templates/` directory (auto-seeded on startup).
2. **Given** blueprints have been published to the system register, **When** the user views the Published Blueprints section, **Then** each blueprint shows its title, version, author, publish date, and a provenance badge indicating it was signed.
3. **Given** a published blueprint with versions 1.0, 1.1, 2.0, **When** the user views its detail, **Then** the version history shows all three versions with change type labels (structural vs documentation).
4. **Given** a template with configurable parameters, **When** the user clicks "Use", **Then** the parameter configuration dialog appears and generates a blueprint from the template.
5. **Given** a published blueprint, **When** the user clicks "Use", **Then** a new blueprint instance is created from the published version and opened in the designer.

---

### User Story 6 - Instructions Editing and Translation Management (Priority: P2)

A blueprint author edits instructions using a dedicated Instructions tab in the visual designer. They see inline text editors for each action and field, with a live preview showing what participants will see. For multilingual support, they export instruction strings as a key-value file, send it to translators, and re-import the translated version linked to a locale.

**Why this priority**: Instructions are living documents that change more often than blueprint structure. A dedicated editing workflow prevents instruction maintenance from being neglected.

**Independent Test**: Can be tested by opening a blueprint's Instructions tab, editing action-level and field-level instructions, toggling the preview, and verifying the instructions appear in the participant form UI.

**Acceptance Scenarios**:

1. **Given** a blueprint open in the visual designer, **When** the user selects the Instructions tab, **Then** they see editable text fields for: blueprint overview, each action's instructions, and each form field's instructions (pre-populated from schema descriptions where available).
2. **Given** instructions have been edited, **When** the user toggles "Preview Instructions", **Then** the blueprint viewer shows the instructions as participants would see them (help icons, expandable panels, or inline text).
3. **Given** a blueprint with instructions, **When** the user clicks "Export Strings", **Then** a JSON or CSV file is downloaded containing all instruction keys and their text values.
4. **Given** a translated instruction file, **When** the user imports it with a locale tag (e.g., "fr-FR"), **Then** the translations are stored as a linked instruction set and the locale appears in the blueprint's instruction metadata.
5. **Given** a blueprint where a field was removed after instructions were written, **When** the Instructions tab loads, **Then** stale instructions (referencing non-existent fields) are highlighted with a warning.

---

### User Story 7 - Fix Existing Designer Stubs (Priority: P3)

A user working in the designer encounters fully functional features where previously there were stubs: export from AI chat downloads a file, clipboard copy works, condition editors suggest real schema fields, routes are editable in the visual designer, and disclosures can be modified.

**Why this priority**: These are quality-of-life fixes that improve trust in the designer but don't enable new capabilities.

**Independent Test**: Can be tested individually — each stub fix has a specific before/after behaviour that can be verified.

**Acceptance Scenarios**:

1. **Given** a blueprint in the AI chat designer, **When** the user clicks Export JSON or Export YAML, **Then** the browser downloads a file (not a modal display).
2. **Given** the JSON view of a blueprint, **When** the user clicks Copy, **Then** the JSON text is copied to the system clipboard.
3. **Given** an action with a bound data schema containing 5 properties, **When** the user opens the condition editor, **Then** the field dropdown shows the 5 schema property names (not hardcoded defaults).
4. **Given** an action with routes, **When** viewing the action in the visual designer properties panel, **Then** routes are displayed and editable (add/edit/remove routes with conditions).
5. **Given** an action with disclosures, **When** viewing the action in the visual designer properties panel, **Then** disclosures are editable (add/edit/remove participant-field mappings).

---

### Edge Cases

- What happens when a blueprint has instructions in 5 locales but the user's locale doesn't match any? Falls back to primary locale (the inline text).
- What happens when a schema property description is very long (500+ chars)? Truncate with "show more" expansion in the form UI.
- What happens when a publishing workflow is in progress and someone edits the blueprint? The submitted version is immutable once submitted; edits create a new draft that requires a separate publishing submission.
- What happens when the reviewer and publisher are the same person? The workflow allows it but logs a warning for audit purposes (single-person governance is valid for development but not recommended for production).
- What happens when the system register is unavailable during publishing? The publish action fails with a clear error and the workflow stays at the publish step for retry.
- What happens when a blueprint has no actions (empty draft)? Publishing validation rejects it with a clear error listing what's missing.
- What happens when the structural diff detection encounters a blueprint with only metadata changes (e.g., title rename)? All changes outside the `instructions` section are classified as structural (major bump) — including title, description, form layout, disclosures, and metadata. Only changes confined entirely to the `instructions` section qualify as documentation-only (minor bump).

## Requirements *(mandatory)*

### Functional Requirements

**Unified Display**
- **FR-001**: The system MUST provide a single blueprint diagram component that renders in three modes: interactive editing, read-only preview with auto-layout, and compact embeddable view.
- **FR-002**: The diagram MUST show directional arrows between actions, visually distinct divergent paths (decision points) and convergent paths (merge points), start/end markers, and curved back-edge arcs for cycles.
- **FR-003**: The diagram MUST group actions by participant using visual swimlanes or colour-coded grouping with a participant legend.
- **FR-004**: The diagram MUST use consistent layout, styling, and edge colouring across all rendering contexts (designer, chat preview, catalogue, viewer dialog).

**Context Handoff**
- **FR-005**: The system MUST transfer blueprint state between the visual designer and AI chat designer via a shared identifier, preserving all blueprint data including instructions.
- **FR-006**: Saving a blueprint from either designer MUST target the same persistent record (no duplicate blueprints on handoff).

**Catalogue**
- **FR-007**: The catalogue MUST display both static templates (from the template service) and published blueprints (from the system register) as distinct browsable sections.
- **FR-008**: The platform MUST auto-seed templates from the `blueprints/templates/` directory on startup so the catalogue is populated without manual intervention.
- **FR-009**: Published blueprints in the catalogue MUST show version history with change type labels (structural vs documentation), signer identity, and publish date.

**Instructions Model**
- **FR-010**: The blueprint data model MUST support instructions at three levels: blueprint overview, per-action guidance, and per-form-field help text. Instruction text MUST use Markdown format, rendered in the UI and preserved as human-readable Markdown in export files.
- **FR-011**: When a form field has no explicit instruction but its bound data schema property has a `description`, the system MUST display the schema description as fallback help text.
- **FR-012**: The system MUST support linked instruction sets for multilingual content, each identified by locale code and an external URI (DID or URL).
- **FR-013**: Per-participant instructions MUST be supported, providing role-specific guidance visible only to the relevant participant.

**Semantic Versioning**
- **FR-014**: Blueprint versions MUST follow a `major.minor` scheme where major increments indicate structural changes (actions, routes, schemas, participants) and minor increments indicate documentation/instructions-only changes.
- **FR-015**: Minor version numbers MUST reset to zero on a major version increment (e.g., v3.5 becomes v4.0).
- **FR-016**: The system MUST automatically detect whether a change is structural or documentation-only by comparing all blueprint content except the `instructions` section. Only changes confined to the `instructions` section qualify as documentation-only (minor bump); all other changes — including form layout, disclosures, metadata, control types, and field ordering — are structural (major bump).
- **FR-017**: Both major and minor version changes MUST be published to the system register as signed transactions with full audit trail.

**Instructions Editor**
- **FR-018**: The visual designer MUST include an Instructions tab where authors can edit blueprint-level, action-level, and field-level instructions alongside the workflow structure.
- **FR-019**: The system MUST provide a preview mode that displays instructions as participants would see them in the form UI.
- **FR-020**: The system MUST support export of all instruction strings as a key-value file (JSON or CSV) for translation workflows.
- **FR-021**: The system MUST support import of translated instruction files tagged with a locale code.
- **FR-022**: The Instructions tab MUST highlight stale instructions that reference fields or actions that no longer exist in the blueprint.

**Blueprint Publishing Blueprint**
- **FR-023**: The system MUST include a Blueprint Publishing Blueprint — a governance workflow template that manages the review and publication of blueprints to the system register.
- **FR-024**: The publishing workflow MUST include at minimum three participant roles: Author (submits), Reviewer (approves/rejects), and Publisher (signs and publishes). Any authenticated user may be an Author; Reviewer and Publisher roles are assigned by organisation administrators.
- **FR-024a**: The blueprint data model MUST support per-blueprint governance role definitions (participant DIDs or identifiers for Reviewer and Publisher). When a blueprint defines its own governance roles, those definitions take precedence over organisation-level role assignments.
- **FR-024b**: If an organisation admin attempts to override blueprint-defined governance roles, the system MUST display the blueprint's governance model and report the conflict rather than silently overriding.
- **FR-025**: The publishing workflow MUST support rejection with comments, cycling the submission back to the author for revision.
- **FR-026**: For documentation-only updates (minor version), the system MUST validate that no structural changes exist before allowing the minor version path.
- **FR-027**: The publishing workflow MUST be invocable from both the visual designer and the catalogue via a "Publish" action.

**Stub Fixes**
- **FR-028**: Export from the AI chat designer MUST download a file to the browser (JSON or YAML format).
- **FR-029**: The JSON view clipboard copy MUST copy the blueprint JSON to the system clipboard.
- **FR-030**: Condition and calculation editors MUST suggest field names parsed from the action's bound data schemas rather than hardcoded defaults.
- **FR-031**: Routes MUST be viewable and editable in the visual designer properties panel.
- **FR-032**: Disclosures MUST be editable in the visual designer properties panel.

### Key Entities

- **BlueprintInstructions**: A structured section of the blueprint containing overview text, locale, per-action instructions (keyed by action ID), per-participant instructions (keyed by participant name), and linked instruction sets for translations.
- **InstructionSet**: A linked external instruction document identified by locale code, source URI (DID or URL), and version.
- **BlueprintVersion**: A `major.minor` version identifier attached to each blueprint, where major tracks structural changes and minor tracks documentation changes. Associated with a change type (structural or documentation) and a signed transaction in the register.
- **PublishingWorkflowInstance**: An instance of the Blueprint Publishing Blueprint, tracking the current state (draft submitted, under review, approved, published, or rejected) and the participants involved.
- **PublishedBlueprint**: A blueprint record in the system register with version history, signer identity, publish timestamp, and change type per version.

## Clarifications

### Session 2026-03-16

- Q: Who is authorized to fill Author/Reviewer/Publisher roles in the publishing workflow? → A: Organisation admins assign Reviewer and Publisher roles; any authenticated user can be an Author. Future: per-blueprint role assignment where the blueprint itself defines its governance participants (possibly as DIDs). Blueprint-defined roles take precedence over org-admin-assigned roles. If an org admin attempts to override blueprint-defined governance, the system displays the blueprint's governance model and reports the conflict.
- Q: What text format should instruction content use? → A: Markdown — supports lightweight formatting (bold, links, lists), remains human-readable in export files for translation workflows, and renders natively in the UI.
- Q: What is the boundary between structural and documentation-only changes for version classification? → A: Only changes within the `instructions` section are documentation-only (minor bump). All other changes — including form layout, disclosures, metadata, control types, field ordering — are structural (major bump).

## Assumptions

- The existing Sugiyama layout algorithm in `BlueprintLayoutService` provides a sufficient foundation for the unified diagram component and can be extended for swimlanes and improved edge rendering.
- The Blueprint Publishing Blueprint will use the existing Sorcha workflow execution engine — no new execution infrastructure is needed.
- Schema property descriptions follow standard JSON Schema conventions (`description` field at the property level) and are present in well-authored schemas (e.g., DPP schemas from Battery Pass, Catena-X, UNTP).
- The system register's existing publish endpoint supports the additional version metadata (major.minor, change type) without breaking existing published blueprints.
- Auto-seeding templates on startup is idempotent — re-seeding does not create duplicate templates.
- DID URIs for linked instruction sets follow the existing `did:sorcha:` format or standard W3C DID syntax.
- The reviewer and publisher roles in the publishing workflow can be the same person during development/testing but should be separate in production governance configurations. Role assignment is managed at the organisation level by admins, with blueprint-level governance definitions taking precedence when present.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A blueprint displayed in any of the four contexts (visual designer, AI chat preview, catalogue detail, viewer dialog) renders an identical layout within 2 seconds of loading.
- **SC-002**: Users can transfer a blueprint between the visual designer and AI chat designer and back without data loss — 100% of blueprint fields (including instructions) are preserved across handoffs.
- **SC-003**: The catalogue displays both templates and published blueprints within 3 seconds of page load, with templates auto-seeded from files without manual API calls.
- **SC-004**: A new user reading blueprint instructions can understand each workflow step's purpose without external documentation — measured by the presence of instructions at blueprint, action, and field levels for all published blueprints.
- **SC-005**: Documentation-only updates (instruction changes) can be published as minor versions in under 5 minutes end-to-end (edit, submit, review, sign, publish).
- **SC-006**: Structural changes are correctly detected and classified as major version bumps 100% of the time — no false classification of structural changes as documentation-only.
- **SC-007**: The Blueprint Publishing workflow completes end-to-end: author submits, reviewer approves or rejects (with cycle-back on rejection), publisher signs, and the blueprint appears in the system register with correct version and provenance.
- **SC-008**: All previously stubbed features (export download, clipboard copy, schema-aware field suggestions, route editing, disclosure editing) function correctly without fallback to placeholders or modal workarounds.
- **SC-009**: Instruction strings can be exported, translated externally, and re-imported with a new locale tag, with the translated version accessible when viewing the blueprint in that locale.
- **SC-010**: 90% of form fields in blueprints that use data schemas with property descriptions display automatic help text without requiring explicit instruction authoring.
