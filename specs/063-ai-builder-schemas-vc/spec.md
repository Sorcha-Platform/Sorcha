# Feature Specification: AI Blueprint Builder Enhancement — Schema Library, VC/DPP Integration, and UX Overhaul

**Feature Branch**: `063-ai-builder-schemas-vc`
**Created**: 2026-03-18
**Status**: Draft
**Input**: User description: "Comprehensive enhancement of the AI Blueprint Chat Designer with standardised schema library, Verified Credential / Digital Product Passport integration, conversation quality overhaul, and UI improvements"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — AI Uses Standardised Schemas When Building Blueprints (Priority: P1)

A workflow designer opens the AI Blueprint Chat Designer and describes a workflow: "Create a loan application process where the applicant provides their personal details and address, and the bank officer reviews and approves." The AI recognises the data requirements, suggests using the standardised "Personal Identity" and "UK Address" schemas rather than constructing ad-hoc fields, explains why ("these use validated postcode patterns and are consistently structured across blueprints"), and upon confirmation, composes them into the action's data schema with the correct form layout. The designer can also ask the AI to list or search available schemas directly in the chat conversation.

**Why this priority**: Without schema awareness, the AI builds every blueprint from scratch with inconsistent field definitions. This is the core gap the user reported — the AI didn't know templates or schemas existed.

**Independent Test**: Can be fully tested by opening the chat designer, requesting a workflow involving address/contact/financial data, and verifying the AI suggests and applies standardised schemas. Value: consistent, high-quality data definitions across all blueprints.

**Acceptance Scenarios**:

1. **Given** a user describes a workflow requiring address data, **When** the AI processes the request, **Then** it suggests the appropriate standardised address schema by name and describes its fields before applying it.
2. **Given** standardised schemas are loaded in the system, **When** the AI calls the `use_standard_schema` tool, **Then** the action's data schema includes all fields from the standardised schema with correct types, constraints, and form layout.
3. **Given** a user opens the schema browser panel, **When** they filter by category "Financial", **Then** only financial schemas (Payment Details, Invoice Line Item, Bank Account) are displayed with previews.
4. **Given** a user asks the AI "what schemas do you have for healthcare?", **When** the AI processes the query, **Then** it lists the available healthcare schemas with descriptions.

---

### User Story 2 — Professional, Inquisitive Conversation Flow (Priority: P1)

A workflow designer opens the chat and says "I need a permit approval process." Instead of immediately calling tools, the AI asks clarifying questions: "What type of permit is this for? Who are the key stakeholders — is there a regulatory body involved? What information does the applicant need to provide?" After the designer answers, the AI confirms: "So we have an Applicant who submits the permit application, an Assessor from the regulatory body who reviews it, and a Senior Officer who gives final approval — is that right?" Upon confirmation, the AI proposes the schema choices and disclosure approach: "I'd recommend minimal disclosure — the Assessor sees the application details but not the applicant's personal contact information. The Senior Officer sees the assessment outcome and recommendation. Shall I proceed with this approach?" Only after the designer confirms does the AI call the tools to build the blueprint.

**Why this priority**: The current "CALL TOOLS IMMEDIATELY" approach produces blueprints that don't match user intent. A consultative flow produces better outcomes and builds user trust.

**Independent Test**: Can be tested by describing an ambiguous workflow and verifying the AI asks at least 2 clarifying questions before building, confirms participants and their motives, proposes disclosure defaults, and checkpoints before tool execution.

**Acceptance Scenarios**:

1. **Given** a user provides a brief workflow description, **When** the AI starts processing, **Then** it asks clarifying questions about participants, their roles/motives, and data requirements before calling any blueprint construction tools.
2. **Given** the AI has gathered requirements, **When** it proposes the blueprint structure, **Then** it explicitly states the disclosure approach (defaulting to minimal) and asks for confirmation before proceeding.
3. **Given** the AI has built a valid blueprint, **When** it completes validation, **Then** it offers to save to "My Blueprints" and explains next steps (publishing to a register).
4. **Given** a user provides a detailed, unambiguous workflow description, **When** the AI processes it, **Then** it may proceed with fewer clarifying questions but still confirms the participant roster and disclosure approach before building.

---

### User Story 3 — Credential Requirements on Blueprint Actions (Priority: P2)

A workflow designer is creating a professional licensing workflow. They tell the AI: "The applicant needs to prove they have a valid training certificate before they can submit." The AI suggests: "I can add a Verified Credential requirement to the submission action — the applicant would need to present a 'TrainingCertificate' credential from an accepted issuer. Would you like to specify which issuers are trusted, or accept any?" The designer confirms, and the AI calls the `require_credential` tool to add a `CredentialRequirement` to the action. The blueprint preview shows a credential badge on the action.

**Why this priority**: Credential requirements gate who can participate in workflows, which is fundamental to trust. The models already exist in the codebase but are inaccessible through the AI builder.

**Independent Test**: Can be tested by requesting a workflow with credential requirements, verifying the AI adds `CredentialRequirement` to the action, and confirming the preview shows credential indicators.

**Acceptance Scenarios**:

1. **Given** a user describes a workflow requiring proof of qualification, **When** the AI identifies the credential need, **Then** it suggests adding a Verified Credential requirement and asks about trusted issuers.
2. **Given** the AI adds a credential requirement via the `require_credential` tool, **When** the blueprint is rendered in the preview, **Then** the action shows a visual credential badge indicating a VC is required.
3. **Given** a credential requirement has `AcceptedIssuers` configured, **When** the blueprint is validated, **Then** validation passes with the credential requirement properly attached to the action.

---

### User Story 4 — Credential Issuance from Blueprint Actions (Priority: P2)

A workflow designer is creating a training completion workflow. The final action is "Issue Certificate" performed by the Training Provider. The designer tells the AI: "When the training provider marks the course as complete, I want a verifiable credential issued to the trainee." The AI responds: "I'll configure the final action to issue a 'TrainingCompletionCertificate' credential to the trainee. I'll map the trainee's name, course title, completion date, and assessor decision from the workflow data. How long should the credential be valid?" After confirmation, the AI calls the `issue_credential` tool. The preview shows a credential issuance badge on the action.

**Why this priority**: Credential issuance turns blueprint completions into reusable, portable attestations — key for composable workflows.

**Independent Test**: Can be tested by creating a workflow where the final action issues a credential, verifying claim mappings are correct, and confirming the preview shows issuance indicators.

**Acceptance Scenarios**:

1. **Given** a user describes a workflow that produces a certification or attestation, **When** the AI identifies the issuance opportunity, **Then** it suggests issuing a Verified Credential and asks about claim mappings, recipient, and validity period.
2. **Given** the AI issues a credential via the `issue_credential` tool, **When** claim mappings reference action data fields, **Then** each mapping correctly uses JSON Pointer syntax to reference source fields.
3. **Given** a credential issuance is configured, **When** the blueprint preview renders, **Then** the issuing action shows a visual badge indicating a VC is issued, with the credential type name visible.

---

### User Story 5 — Digital Product Passport Lifecycle (Priority: P3)

A supply chain designer creates a multi-stage workflow: Manufacturer → Inspector → Shipper → Retailer. They tell the AI: "This product needs a Digital Product Passport that accumulates data from each stage." The AI suggests: "I'll create the DPP at the manufacturing action and append lifecycle events at each subsequent stage. The material composition goes in at manufacturing, inspection results at quality check, and logistics data at shipping. The retailer and end consumer can read the full passport." The AI configures credential issuance at the first action (creating the DPP) and credential requirements at subsequent actions (consuming and appending to the DPP).

**Why this priority**: DPP is a growing regulatory requirement (EU ESPR) and a natural extension of the VC model. However, it builds on Stories 3 and 4 and requires the composable chain pattern.

**Independent Test**: Can be tested by creating a supply chain workflow with DPP, verifying each action either creates or appends to the passport, and confirming the full lifecycle is visible in the preview.

**Acceptance Scenarios**:

1. **Given** a user describes a product lifecycle workflow, **When** the AI identifies a DPP use case, **Then** it suggests creating a Digital Product Passport and explains the lifecycle event accumulation pattern.
2. **Given** a DPP workflow is built with multiple actions, **When** the blueprint is rendered, **Then** each action shows whether it creates, appends to, or reads the DPP.
3. **Given** a DPP credential issuance is configured at the first action, **When** subsequent actions require the DPP, **Then** they reference the same credential type and the chain is validated as consistent.

---

### User Story 6 — Chat UI Layout Fixed (Priority: P1)

The chat input area stays pinned to the bottom of the chat panel regardless of message count. Messages scroll above the input. When new messages arrive, the messages area auto-scrolls to show the latest message. The text input has a comfortable default height and the overall layout fills the available viewport without requiring page-level scrolling.

**Why this priority**: Basic usability — if the input scrolls out of view or the layout breaks, the feature is unusable.

**Independent Test**: Can be tested by sending multiple messages until they exceed the viewport height, verifying the input stays fixed at the bottom and messages auto-scroll.

**Acceptance Scenarios**:

1. **Given** a chat session with many messages, **When** the messages exceed the viewport, **Then** the input area remains fixed at the bottom and only the messages area scrolls.
2. **Given** a new message arrives (user or AI), **When** it is appended, **Then** the messages area auto-scrolls to show the new message.
3. **Given** the chat designer is opened on different viewport sizes, **When** it renders, **Then** the layout fills the available space without page-level scrollbars.

---

### Edge Cases

- What happens when the AI suggests a standardised schema but the user wants to customise fields? The AI should apply the schema then allow individual field modifications via subsequent conversation.
- What happens when a credential requirement references an issuer that doesn't exist in the system? Validation should warn (not error) since issuers may be external entities not yet registered.
- What happens when the user describes a workflow in a domain with no matching standardised schemas? The AI falls back to building ad-hoc fields with appropriate types and constraints, as it does today.
- What happens when a DPP lifecycle chain references blueprints that haven't been published yet? The credential type reference is stored as a string identifier — resolution happens at execution time, not design time.
- What happens when the AI's connection to the Anthropic API is unavailable? The UI layout improvements remain fully functional; only the conversational AI is degraded.
- What happens when the schema library is empty (no schema files seeded)? The AI falls back to ad-hoc field construction as it does today.
- What happens when multiple standardised schemas are composed into a single action? The schemas are merged — if field names conflict, the AI warns the user and asks which to keep.
- What happens when the user manually scrolls up in the chat to review history? Auto-scroll should pause until the user scrolls back to the bottom.

## Requirements *(mandatory)*

### Functional Requirements

**Schema Library:**

- **FR-001**: System MUST provide a standardised schema library stored as structured files, each containing a JSON Schema definition, default form layout metadata, disclosure sensitivity recommendations, and searchable tags.
- **FR-002**: System MUST seed standardised schemas on Blueprint Service startup, similar to the existing template seeding pattern.
- **FR-003**: System MUST provide schemas across these categories: People & Identity (UK Address, International Address, Contact Details, Personal Identity, Company Identity), Financial (Payment Details, Invoice Line Item, Bank Account), Documents & Evidence (Document Upload, Signature Block, Audit Entry), Compliance & Governance (Risk Assessment, Approval Decision, Due Diligence Check), Physical / Supply Chain (Product Item, Shipment Details, Inspection Record), Healthcare (Patient Reference, Clinical Observation), Credentials (Training Certificate, Professional License, Right-to-Work, Identity Verification, Product Passport, Inspection Certificate, Approval Attestation).
- **FR-004**: The AI MUST have a `use_standard_schema` tool that applies a standardised schema's fields to an action's data definition, including form layout metadata.
- **FR-005**: The AI MUST have a `search_schemas` tool to query available schemas by name, category, or tag.
- **FR-006**: The AI system prompt MUST include a compact summary table of available schema names and categories (not full definitions) for ambient awareness. The AI MUST use the `search_schemas` tool to retrieve full schema details on demand.
- **FR-007**: Each standardised schema MUST include disclosure recommendations indicating which fields are sensitive (e.g., NI number, bank details) to guide the AI's minimal-disclosure defaults.
- **FR-008**: Standardised schemas MUST include embedded form layout metadata specifying field ordering, grouping, column spans, and section labels for default UI rendering.

**Verified Credentials:**

- **FR-009**: The AI MUST have a `require_credential` tool that adds a credential requirement to a blueprint action, specifying credential type, accepted issuers, required claims, and revocation check policy.
- **FR-010**: The AI MUST have an `issue_credential` tool that configures credential issuance on a blueprint action, specifying credential type, claim mappings from action data, recipient participant, expiry duration, and usage policy.
- **FR-011**: The AI system prompt MUST include awareness of credential concepts (requirements and issuance) and guide users toward credential-based patterns when appropriate.
- **FR-012**: The AI MUST proactively suggest credential requirements when workflow context implies proof is needed (e.g., "must be qualified", "needs certification", "verified identity").
- **FR-013**: The AI MUST proactively suggest credential issuance when workflow outcomes represent attestations (e.g., "issue certificate", "approve license", "complete training").

**Digital Product Passport:**

- **FR-014**: The AI MUST recognise DPP patterns (product lifecycle across multiple participants) and suggest configuring actions as DPP lifecycle events.
- **FR-015**: The AI MUST configure DPP workflows using the credential requirement/issuance models — first action creates (issues) the DPP, subsequent actions consume (require) and append lifecycle data.
- **FR-016**: The system MUST include a starter set of 5-8 credential type schemas (training certificate, professional license, right-to-work, identity verification, product passport, inspection certificate, approval attestation) stored in the standardised schema library under a "Credentials" category. DPP schemas MUST follow EU ESPR guidelines. Comprehensive credential catalogue deferred to a future iteration.

**Conversation Quality:**

- **FR-017**: The AI MUST follow a consultative conversation flow: understand intent → confirm participants and motives → propose schema choices → suggest credentials → confirm disclosure approach → checkpoint before building → validate and offer save.
- **FR-018**: The AI MUST default to minimal disclosure, explicitly asking the user which participants need to see which data rather than granting broad access.
- **FR-019**: The AI MUST confirm the participant roster (who they are, what they do, why they're involved) before constructing actions.
- **FR-020**: The AI MUST present a summary checkpoint before calling construction tools, allowing the user to adjust the plan.
- **FR-021**: The AI MUST offer to save the blueprint to "My Blueprints" after successful validation.
- **FR-022**: The AI system prompt MUST include a compact summary of available templates (names and categories) from the catalogue so it can suggest starting from a template when appropriate. Full template details retrieved via `search_templates` tool on demand.
- **FR-023**: The AI MUST have a `search_templates` tool to query the template catalogue by name, category, or keyword.

**UI:**

- **FR-024**: The chat input area MUST remain fixed at the bottom of the chat panel regardless of message count.
- **FR-025**: The messages area MUST auto-scroll to the latest message when new content arrives, pausing auto-scroll when the user manually scrolls up.
- **FR-026**: The chat designer layout MUST fill the available viewport without requiring page-level scrolling.
- **FR-027**: Blueprint preview MUST show visual indicators (badges/icons) on actions that require or issue Verified Credentials.

### Key Entities

- **StandardisedSchema**: A reusable data schema component containing a JSON Schema definition, default form layout (field ordering, grouping, column spans), disclosure sensitivity recommendations per field, category assignment, searchable tags, and version. Stored as a file and seeded on service startup.
- **SchemaCategory**: A grouping label for organising standardised schemas. Top-level categories (People & Identity, Financial, Credentials, etc.) with individual schemas as children. Credential type definitions are stored as schemas in the "Credentials" category — same format, same seeding, same discovery tools. Used by the AI's `search_schemas` tool for filtering.

## Clarifications

### Session 2026-03-18

- Q: Where should the schema browser appear in the UI? → A: No separate schema browser panel needed. Schema discovery is handled entirely through the AI chat conversation — users ask the AI to list, search, or describe schemas. The schema browser already exists elsewhere in the UI (Template Library page).
- Q: How many predefined credential types should ship initially? → A: Starter set of 5-8 common types (training cert, professional license, right-to-work, identity verification, product passport, inspection certificate, approval attestation). Credential types are stored as schemas in the standardised schema library under a "Credentials" category — same file format, same seeding, same `search_schemas` tool. Comprehensive catalogue deferred to a future iteration.
- Q: How should the AI get schema/template/credential context — full injection, summary, or tool-only? → A: Summary injection. Embed a compact table of schema names and categories in the system prompt for ambient awareness. Use the `search_schemas` tool for full details on demand. Balances token cost with discoverability.

## Assumptions

- The existing `CredentialRequirement`, `CredentialIssuanceConfig`, `ClaimConstraint`, and `ClaimMapping` models in `Sorcha.Blueprint.Models.Credentials` are sufficient for the VC/DPP integration. No new domain models need to be created — only AI tools to expose them.
- The existing fluent API (`RequiresCredential()`, `IssuesCredential()`) supports the full credential configuration needed. The AI tools will translate user intent into fluent API calls.
- Schema files will follow the same seeding pattern as blueprint templates (`TemplateSeedService`), using a `SchemaSeedService` that scans `blueprints/schemas/` on startup.
- The Anthropic API model used by the Blueprint Service supports tool use with the additional tools (currently 8, expanding to ~13). Token budget for the system prompt can accommodate the schema/template/credential summaries.
- DPP lifecycle patterns are modelled using the existing credential requirement/issuance primitives — no special "DPP mode" is needed at the model level.
- Form layout metadata in standardised schemas follows the existing `Control` model used by blueprint actions for UI rendering.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: When a user describes a workflow involving common data patterns (address, contact, financial), the AI suggests a standardised schema in at least 80% of applicable cases.
- **SC-002**: All standardised schemas include form layout metadata that renders correctly in the blueprint form preview.
- **SC-003**: Users can discover available schemas by asking the AI (e.g., "what schemas do you have?") and receive a categorised list within a single response.
- **SC-004**: The AI asks at least one clarifying question before building a blueprint from an ambiguous single-sentence description.
- **SC-005**: The AI defaults to minimal disclosure on every blueprint it creates, disclosing only the fields each participant needs for their role.
- **SC-006**: Credential requirements and issuance configured through the AI validate correctly and appear in the blueprint preview with visual indicators.
- **SC-007**: The chat input remains visible and usable after 50+ messages in a session without page-level scrolling.
- **SC-008**: The AI can suggest relevant templates from the catalogue when the user's request matches an existing template pattern.
- **SC-009**: A complete blueprint with standardised schemas, credential requirements, and disclosure rules can be designed through conversation in under 15 minutes.
- **SC-010**: All 18+ standardised schemas are seeded on service startup and queryable via the AI's `search_schemas` tool.
