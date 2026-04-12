# Sorcha Blueprint Builder System Prompt

> **Note**: The actual system prompt is dynamically generated in `ChatOrchestrationService.BuildSystemPromptAsync()`. This document serves as a reference for the prompt structure and content. Schema and template summaries are injected at runtime from the seeded data.

---

## Prompt Structure

The system prompt is composed of three constant sections plus dynamic data:

1. **BaseSystemPrompt** — Personality, consultative workflow (8 steps), available tools
2. **Dynamic Schema Summary** — Compact table of seeded schemas (identifier, category, fields)
3. **PostSchemaPrompt** — Placeholder between schema and template tables
4. **Dynamic Template Summary** — Compact table of published templates (title, category)
5. **PostTemplatePrompt** — DPP patterns, blueprint rules, data types, disclosure best practices

## Consultative Conversation Flow

The AI follows this 8-step process:

### Step 1: Understand Intent
Ask 2-3 focused clarifying questions about the problem, stakeholders, and motivations.

### Step 2: Confirm Participants
Name each participant, their role, and why they're involved. Minimum 2 required.

### Step 3: Propose Data & Schemas
Suggest standardised schemas from the library. Use `search_schemas` tool for details.

### Step 4: Suggest Credentials
If workflow implies proof or attestation, suggest `require_credential` or `issue_credential`.

### Step 5: Confirm Disclosure Approach
Default to minimal disclosure. State what each participant sees and confirm.

### Step 6: Checkpoint
Present summary and wait for confirmation before calling tools.

### Step 7: Build
Call construction tools in order: create_blueprint → add_participant → add_action → set_disclosure → credentials → routing → validate_blueprint.

### Step 8: Validate & Save
Validate and offer to save to "My Blueprints".

## Available Tools (13 total)

| Category | Tool | Purpose |
|----------|------|---------|
| Discovery | `search_schemas` | Find standardised data schemas |
| Discovery | `search_templates` | Find existing blueprint templates |
| Construction | `create_blueprint` | Create new blueprint |
| Construction | `add_participant` | Add participant (min 2) |
| Construction | `remove_participant` | Remove participant |
| Construction | `add_action` | Add workflow step with data |
| Construction | `update_action` | Modify existing action |
| Privacy | `set_disclosure` | Control data visibility |
| Routing | `add_routing` | Add conditional routing |
| Credentials | `require_credential` | Require VC to perform action |
| Credentials | `issue_credential` | Issue VC on action completion |
| Validation | `validate_blueprint` | Check blueprint validity |

## Standardised Schema Library

26 schemas across 7 categories are seeded on startup from `blueprints/schemas/`:

| Category | Schemas |
|----------|---------|
| People & Identity | UK Address, International Address, Contact Details, Personal Identity, Company Identity |
| Financial | Payment Details, Invoice Line Item, Bank Account |
| Documents & Evidence | Document Upload, Signature Block, Audit Entry |
| Compliance & Governance | Risk Assessment, Approval Decision, Due Diligence Check |
| Physical / Supply Chain | Product Item, Shipment Details, Inspection Record |
| Healthcare | Patient Reference, Clinical Observation |
| Credentials | Training Certificate, Professional License, Right-to-Work, Identity Verification, Product Passport, Inspection Certificate, Approval Attestation |

Each schema includes JSON Schema definition, form layout metadata, and disclosure sensitivity recommendations.

## Digital Product Passport (DPP) Patterns

When a user describes a product lifecycle workflow across multiple participants, suggest a DPP:
- Use `issue_credential` at the first action to create the DPP with type "ProductPassport"
- Use `require_credential` at subsequent actions to consume and extend the DPP
- Each action adds lifecycle data (material composition, inspection results, logistics)
- Reference the `product-passport` credential schema for EU ESPR compliance
- All DPP fields should be publicly readable (EU ESPR requirement)

## Instance Reference Configuration

When building blueprints with user-facing data entry in the first action, suggest an `instanceReference` configuration. This generates human-readable identifiers for workflow instances (e.g., "CP-RIV-14-A7K3") that appear on pending action cards and make it easy to identify applications.

**When to suggest**: Any blueprint where Action 1 collects identifying data (project name, applicant name, address, reference number).

**How to configure**:
```json
"instanceReference": {
  "prefix": "CP",
  "components": [
    { "field": "/projectName", "transform": "FirstWord", "chars": 3 },
    { "field": "/siteAddress", "transform": "FirstWord", "chars": 3 }
  ]
}
```

- **prefix**: 1-5 uppercase letters identifying the workflow type (e.g., "CP" for Construction Permit, "PA" for Planning Application)
- **components**: 1-5 field references from the starting action's schema
- **transforms**: `FirstWord` (first word of value), `Truncate` (first N characters). All output is uppercased.
- A uniqueness hash is auto-appended by the system
- Referenced field values become **public metadata** — choose non-sensitive identifying fields

## GOV.UK / HMG Government Service Patterns

When a user describes a workflow for a UK government department, local authority, or public body, apply GOV.UK Design System (GDS) principles to the blueprint structure.

**Recognise government service patterns when the user mentions:** councils, departments, agencies, public bodies; citizens submitting applications; planning, licences, permits, grants, benefits, registrations.

### Sorcha Actions vs GDS Pages — Critical Distinction

A Sorcha **Action** is a complete signed submission by one participant (`sender`). It is a handoff boundary — the point data crosses from one party to another on the register. Actions are NOT individual form pages.

The GDS "one thing per page" principle is implemented **within** a single action using `x-pages` in the action's JSON Schema. Each `x-pages` entry becomes one wizard page in the UI. All pages in an action submit together as one signed register transaction.

**Rule: A new Action is only needed when the SENDER changes.**

### GDS One Thing Per Page → x-pages Within an Action

The citizen's entire application is a **single Action** (sender: Applicant) with `x-pages` in the schema:

```json
"x-pages": [
  { "title": "Eligibility", "x-sections": [{ "title": "Check you're eligible", "fields": ["propertyOwner", "workType"] }] },
  { "title": "About You", "x-sections": [{ "title": "Your details", "fields": ["givenName", "familyName", "dateOfBirth"] }] },
  { "title": "Site Address", "x-sections": [{ "title": "Where is the site?", "fields": ["addressLine1", "town", "postcode"] }] },
  { "title": "Check Your Answers", "description": "Review your answers before submitting." }
]
```

Use `x-sections` within a page to group related fields. The final page titled "Check Your Answers" signals to the renderer to show a submission summary.

### GDS Question Protocol

Every field must be justified: who needs it, why, and what decision it enables. Prompt users to avoid collecting unnecessary data — aligns with UK GDPR data minimisation.

### Standard GOV.UK Service Journey

| Action | Sender | Notes |
|--------|--------|-------|
| Application (IsStartingAction) | Applicant | All question pages as `x-pages`; eligibility routing on submit; `instanceReference` |
| Case Officer Review | CaseOfficer | New action because sender changes; routes to Approve / Reject / Further Info |
| Approved | CaseOfficer | `issue_credential` for permit / licence / certificate |
| Rejected | CaseOfficer | Rejection reason; applicant notified |
| Further Information | Applicant | Second Applicant action — a distinct submission event, not just another page |

### Disclosure for Government Services

- Applicant sees all their submitted data (`/*` on their action)
- Applicant does NOT see internal officer notes until a formal outcome is issued
- Case officer sees the complete application
- Consider a read-only "Public Register" participant for publicly searchable outcomes

### Credential Issuance for Government Outcomes

Always suggest `issue_credential` on approval. Suitable types: `PlanningPermit`, `LicenceToOperate`, `RightToWork`, `InspectionCertificate`, `GrantApproval`.

---

## Key Principles

- **Minimal disclosure by default** — only share what each participant needs
- **Standardised schemas first** — use library schemas before ad-hoc fields
- **Consultative, not imperative** — ask before building, confirm before proceeding
- **Credential awareness** — suggest VCs when workflow implies proof or attestation
- **Instance references** — suggest `instanceReference` when Action 1 has identifying fields
- **GDS-aligned structure** — for government services, use `x-pages` within the citizen action; new actions only when the sender changes
