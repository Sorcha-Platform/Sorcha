# Feature Specification: Blueprint Service

**Feature Branch**: `blueprint-service`
**Created**: 2025-12-03
**Updated**: 2025-12-04 (Sprint 10 Orchestration Complete)
**Status**: 100% Complete (Unified Blueprint-Action Service with Full Orchestration)
**Input**: Derived from `docs/guides/blueprints/blueprint-architecture.md` and existing codebase analysis

## Architecture Overview

The Blueprint Service is a **microservice** that orchestrates workflow execution. It handles blueprint administration and action flow execution by coordinating between the Register Service (storage), Wallet Service (cryptography), and the Blueprint Engine (pure logic).

```
┌─────────────────────────────────────────────────────────────────┐
│                   BLUEPRINT SERVICE (Microservice)              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ADMINISTRATION                                                 │
│  • Blueprint CRUD, versioning, publishing                       │
│  • Blueprint discovery & templates                              │
│  • Schema management                                            │
│                                                                 │
│  FLOW EXECUTION (Orchestration)                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │ 1. Receive action submission from participant              │ │
│  │ 2. Fetch prior transactions from Register (by InstanceId)  │ │
│  │ 3. Decrypt payloads via Wallet Service (delegated access)  │ │
│  │ 4. Accumulate state from decrypted payloads                │ │
│  │ 5. Call Blueprint.Engine with state + submission           │ │
│  │    → validation, routing, calculations, disclosures        │ │
│  │ 6. Build transaction (encrypt payloads per recipient)      │ │
│  │ 7. Sign transaction via Wallet Service                     │ │
│  │ 8. Submit to Register                                      │ │
│  │ 9. Notify participants via SignalR                         │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  REJECTION HANDLING                                             │
│  • Process rejection with configurable routing per action       │
│  • Create rejection transaction with reason                     │
│  • Route to target defined in blueprint (not always sender)     │
│                                                                 │
│  REAL-TIME NOTIFICATIONS                                        │
│  • SignalR hub for action availability                          │
│  • Notify participants when actions are ready                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Clarifications

### Session 2025-12-04

- Q: What is the relationship between Blueprint Engine and Blueprint Service? → A: Engine is a pure library (no I/O), Service orchestrates workflow execution (fetches data, calls engine, builds transactions).
- Q: How is state reconstructed for routing evaluation? → A: Blueprint-defined scope - only fetch/decrypt transactions needed for current action (determinable from blueprint definition).
- Q: How does decrypt authorization work? → A: Delegated access via credential token obtained at participant authentication.
- Q: Where does rejection route to? → A: Configurable per action in blueprint (not necessarily the immediate sender).
- Q: Are parallel branches supported? → A: Yes, but blueprint must manage reductions and potential deadlocks from incomplete upstream actions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create and Manage Blueprints (Priority: P1)

As a workflow designer, I need to create, update, and manage multi-party workflow blueprints so that I can define declarative business processes.

**Why this priority**: Core functionality - without blueprints, no workflows can be executed on the platform.

**Independent Test**: Can be fully tested by creating a blueprint via API and retrieving it, delivering the foundation for workflow automation.

**Acceptance Scenarios**:

1. **Given** a valid blueprint definition with participants and actions, **When** I POST to `/api/blueprints`, **Then** the blueprint is created with a unique ID and returned with 201 Created.
2. **Given** an existing blueprint ID, **When** I GET `/api/blueprints/{id}`, **Then** the complete blueprint definition is returned including all participants, actions, schemas, and disclosures.
3. **Given** a blueprint with validation errors (missing required fields), **When** I POST to `/api/blueprints`, **Then** a 400 Bad Request is returned with detailed validation errors.
4. **Given** an existing blueprint, **When** I PUT to `/api/blueprints/{id}` with updated fields, **Then** the blueprint version is incremented and the updated blueprint is returned.

---

### User Story 2 - Execute Blueprint Actions (Priority: P1)

As a workflow participant, I need to execute actions within a blueprint instance so that I can progress the workflow and submit data.

**Why this priority**: Core functionality - blueprints must be executable to provide value.

**Independent Test**: Can be tested by instantiating a blueprint and executing its first action.

**Acceptance Scenarios**:

1. **Given** a valid blueprint, **When** I POST to `/api/blueprints/{id}/instances`, **Then** a new instance is created with state tracking initialized.
2. **Given** an active blueprint instance and I am the designated sender, **When** I POST action data to `/api/instances/{id}/actions/{actionId}`, **Then** the action is executed, data is validated against schemas, and the next participant is determined via JSON Logic routing.
3. **Given** action data that fails schema validation, **When** I submit the action, **Then** detailed validation errors are returned indicating which fields failed.
4. **Given** I am not the designated sender for the current action, **When** I attempt to execute it, **Then** a 403 Forbidden is returned.

---

### User Story 3 - JSON Schema Validation (Priority: P2)

As a workflow designer, I need to define and validate data schemas for each action so that data quality is enforced throughout the workflow.

**Why this priority**: Data integrity is essential but depends on basic blueprint functionality.

**Independent Test**: Can be tested by creating a blueprint with JSON Schema definitions and validating input data.

**Acceptance Scenarios**:

1. **Given** an action with JSON Schema definitions, **When** action data is submitted, **Then** the data is validated against the schema before processing.
2. **Given** a schema with required fields, format constraints, and enums, **When** invalid data is submitted, **Then** specific validation errors are returned for each violation.
3. **Given** external schema references (`$ref`), **When** the blueprint is loaded, **Then** schemas are resolved and cached for performance.

---

### User Story 4 - JSON Logic Routing and Calculations (Priority: P2)

As a workflow designer, I need to configure conditional routing and calculated fields using JSON Logic so that workflows can adapt dynamically based on data.

**Why this priority**: Enables dynamic workflows but requires basic action execution to be working.

**Independent Test**: Can be tested by creating a blueprint with JSON Logic conditions and submitting data that triggers different routing paths.

**Acceptance Scenarios**:

1. **Given** an action with a JSON Logic routing condition, **When** action data is submitted, **Then** the condition is evaluated and the next participant is determined accordingly.
2. **Given** an action with calculations, **When** action data is submitted, **Then** calculated fields are computed and included in the action result.
3. **Given** a complex routing condition with nested if/else, **When** various test data is submitted, **Then** the correct routing path is selected for each scenario.

---

### User Story 5 - Disclosure Management (Priority: P2)

As a workflow participant, I need fine-grained control over which data fields are visible to each participant so that sensitive information is protected.

**Why this priority**: Privacy control is important for multi-party workflows but depends on core execution.

**Independent Test**: Can be tested by creating a blueprint with disclosures and verifying data visibility per participant.

**Acceptance Scenarios**:

1. **Given** an action with disclosure rules, **When** a participant retrieves workflow data, **Then** only fields specified in their disclosures are visible.
2. **Given** JSON Pointer-based disclosures (`/field`, `/*`, `/nested/path`), **When** data is retrieved, **Then** the correct fields are filtered based on pointer paths.

---

### User Story 6 - Form Generation (Priority: P3)

As a UI developer, I need form definitions generated from action schemas so that dynamic forms can be rendered for data entry.

**Why this priority**: UI generation is a convenience feature that enhances usability.

**Independent Test**: Can be tested by creating an action with form definitions and retrieving the form schema.

**Acceptance Scenarios**:

1. **Given** an action with form definitions (Control types, layouts), **When** I GET the action form schema, **Then** a complete form definition is returned with field types, labels, and layout information.
2. **Given** form controls with conditional display rules (JSON Logic), **When** the form is rendered with data, **Then** controls are shown/hidden based on the conditions.

---

### Edge Cases

- What happens when a blueprint has circular routing conditions?
- How does the system handle missing Participants in routing?
- What happens when schema validation timeout occurs on large payloads?
- How does concurrent action execution on the same instance behave?

**Note**: Per constitution VII (DDD terminology), "Participant" is used instead of "user" throughout this specification.

## Workflow Execution Model

### Register-Blueprint Relationship

A Register runs a Blueprint. The Blueprint published to a Register determines:
- **Who can start** a workflow (starting action participants)
- **Where transactions route** (action-defined recipients based on JSON Logic)
- **What data is visible** (selective disclosure per participant)

```
┌─────────────────────────────────────────────────────────────────┐
│                    REGISTER + BLUEPRINT                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   Register (Distributed Ledger)                                 │
│   └── Blueprint (Published workflow definition)                 │
│       └── Instance (Running workflow)                           │
│           ├── Transaction (Action 1 data)                       │
│           ├── Transaction (Action 2 data)                       │
│           └── Transaction (Action N data)                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Identity Separation Model

Participants are linked externally as identities (from STS) but within the cryptographic space they are **wallet addresses only**. The identity-to-wallet binding exists **only on the participant's local node** to maintain separation and anonymity.

```
EXTERNAL WORLD              │         CRYPTOGRAPHIC SPACE
(Identity Layer)            │         (Blockchain Layer)
                            │
┌─────────────┐             │        ┌─────────────────┐
│    STS      │             │        │  Wallet Address │
│  (Identity) │◄────────────┼────────┤   (Pseudonym)   │
└─────────────┘             │        └─────────────────┘
      │                     │               │
• OAuth/OIDC claims         │        • ML-DSA public key
• Real identity             │        • Network anonymous
                            │
     ONLY LOCAL NODE KNOWS THE BINDING
```

### Dynamic Routing (JSON Logic)

Routing is evaluated at runtime against accumulated workflow state. Data from prior actions can influence routing decisions:

```
Action 1: Applicant submits { "age": 17, "postcode": "EH1 1AB" }
                    │
                    ▼
         JSON Logic Evaluation
    ┌────────────────────────────────┐
    │ if (age < 18)                  │
    │   → route to "guardian_review" │
    │ else if (postcode starts "EH") │
    │   → route to "edinburgh_office"│
    │ else                           │
    │   → route to "central_office"  │
    └────────────────────────────────┘
                    │
                    ▼
        Next Action: guardian_review
```

### State Reconstruction

To evaluate routing, the Blueprint Service must reconstruct accumulated state from prior transactions:

1. **Query Register** for transactions by `InstanceId`
2. **Determine needed transactions** from blueprint definition (not all may be needed)
3. **Decrypt payloads** via Wallet Service using delegated access token
4. **Merge into accumulated state** object
5. **Pass to Blueprint Engine** for evaluation

```
Accumulated State Structure:
{
  "action_1": { "age": 17, "postcode": "EH1 1AB" },
  "action_2": { "guardian_approved": true },
  ...
}
```

### Rejection Flow

Participants can reject inbound data (e.g., invalid values). Rejection routing is **configurable per action**:

```yaml
actions:
  review_application:
    participants: [assessor]
    routes:
      approved: process_application
      rejected: correct_application    # Could route to original submitter
      escalate: senior_review          # Or to a supervisor
```

### Parallel Branches

Blueprints support parallel branches where multiple participants receive actions simultaneously:

```
                Action 1
                    │
        ┌───────────┴───────────┐
        ▼                       ▼
    Action 2a               Action 2b
  (Participant A)         (Participant B)
        │                       │
        └───────────┬───────────┘
                    ▼
              Action 3 (Reduction)
            (waits for both branches)
```

**Deadlock Management**: Blueprints must define:
- Timeout actions for branches that don't complete
- Reduction logic that handles partial completion
- Blueprint Designer should warn about potential deadlock patterns

### Delegated Decrypt Authorization

When a participant authenticates, they receive a credential token that grants the Blueprint Service delegated access to decrypt payloads on their behalf:

```
Participant                    Blueprint Service              Wallet Service
     │                               │                              │
     │  Authenticate (STS)           │                              │
     │  ───────────────────────►     │                              │
     │  Credential Token             │                              │
     │  ◄───────────────────────     │                              │
     │                               │                              │
     │  Submit Action + Token        │                              │
     │  ───────────────────────►     │                              │
     │                               │  Decrypt (token, payload)    │
     │                               │  ───────────────────────►    │
     │                               │  Decrypted data              │
     │                               │  ◄───────────────────────    │
     │                               │                              │
```

## Requirements *(mandatory)*

### Functional Requirements

**Administration:**
- **FR-001**: System MUST allow creation of blueprints with unique IDs, titles, and descriptions
- **FR-002**: System MUST support at least 2 participants per blueprint
- **FR-003**: System MUST support at least 1 action per blueprint
- **FR-004**: System MUST support blueprint versioning
- **FR-005**: System MUST provide OpenAPI documentation for all endpoints
- **FR-006**: System MUST support JSON-LD context for semantic interoperability

**Workflow Execution:**
- **FR-007**: System MUST reconstruct accumulated state from prior transactions for routing evaluation
- **FR-008**: System MUST only fetch transactions needed for current action (blueprint-defined scope)
- **FR-009**: System MUST decrypt payloads via Wallet Service using delegated access tokens
- **FR-010**: System MUST call Blueprint Engine for validation, routing, calculations, disclosures
- **FR-011**: System MUST build transactions with encrypted payloads per recipient
- **FR-012**: System MUST sign transactions via Wallet Service
- **FR-013**: System MUST submit transactions to Register Service
- **FR-014**: System MUST track previous transaction IDs for blockchain chaining
- **FR-015**: System MUST support workflow instance lifecycle (create, progress, complete)

**Routing & Validation:**
- **FR-016**: System MUST validate action data against JSON Schema definitions (via Engine)
- **FR-017**: System MUST evaluate JSON Logic routing conditions (via Engine)
- **FR-018**: System MUST compute calculated fields using JSON Logic (via Engine)
- **FR-019**: System MUST enforce disclosure rules when building payloads (via Engine)
- **FR-020**: System MUST support parallel branch routing (multiple next actions)

**Rejection Handling:**
- **FR-021**: System MUST support action rejection with reason
- **FR-022**: System MUST route rejections per action configuration (configurable target)
- **FR-023**: System MUST create rejection transactions for audit trail

**Notifications:**
- **FR-024**: System MUST notify participants via SignalR when actions are available
- **FR-025**: System MUST provide form definitions for UI rendering

### Key Entities

- **Blueprint**: Workflow definition with participants, actions, and schemas
- **Participant**: Party involved in workflow with identity (DID/wallet) and organization
- **Action**: Workflow step with sender, data schemas, disclosures, routing conditions, and form
- **Disclosure**: Data access rule mapping participant to JSON Pointer paths
- **Instance**: Runtime execution of a blueprint with state tracking
- **Control**: UI form element with type, binding, and display conditions

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create blueprints in under 5 seconds via API
- **SC-002**: Action submission (full flow) completes in under 500ms for typical workflows
- **SC-003**: State reconstruction completes in under 200ms for workflows with up to 10 prior actions
- **SC-004**: API achieves <200ms P95 latency for CRUD operations
- **SC-005**: Test coverage exceeds 85% for orchestration logic
- **SC-006**: All public APIs documented with OpenAPI and Scalar UI
- **SC-007**: Parallel branch execution correctly handles up to 5 concurrent branches

## Dependencies

### Service Dependencies

| Service | Purpose | Criticality |
|---------|---------|-------------|
| **Register Service** | Fetch prior transactions, submit new transactions | Required |
| **Wallet Service** | Decrypt payloads (delegated), encrypt payloads, sign transactions | Required |
| **Tenant Service** | Authentication, credential token for delegated access | Required |
| **SignalR (Redis)** | Real-time participant notifications | Required |

### Library Dependencies

| Library | Purpose |
|---------|---------|
| **Sorcha.Blueprint.Engine** | Pure logic: validation, routing, calculations, disclosures |
| **Sorcha.Blueprint.Models** | Domain models: Blueprint, Action, Disclosure |
| **Sorcha.TransactionHandler** | Transaction building and serialization |

### Data Flow

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Register   │     │   Wallet     │     │   Blueprint  │
│   Service    │     │   Service    │     │   Engine     │
└──────┬───────┘     └──────┬───────┘     └──────┬───────┘
       │                    │                    │
       │ Fetch Tx           │ Decrypt            │ Evaluate
       │ by InstanceId      │ (delegated)        │ routing
       ▼                    ▼                    ▼
┌─────────────────────────────────────────────────────────────┐
│                    BLUEPRINT SERVICE                        │
│  Orchestrates: fetch → decrypt → accumulate → evaluate →    │
│                encrypt → sign → submit → notify             │
└─────────────────────────────────────────────────────────────┘
       │                    │                    │
       │ Submit Tx          │ Encrypt/Sign       │ Notify
       ▼                    ▼                    ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Register   │     │   Wallet     │     │   SignalR    │
│   Service    │     │   Service    │     │   Hub        │
└──────────────┘     └──────────────┘     └──────────────┘
```
