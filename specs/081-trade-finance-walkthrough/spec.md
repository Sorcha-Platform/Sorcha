# Feature Specification: Trade Finance Walkthrough

**Feature Branch**: `081-trade-finance-walkthrough`  
**Created**: 2026-04-02  
**Status**: Implementation Complete  
**Input**: Multi-organisation, multi-peer walkthrough

## Clarifications

### Session 2026-04-02

- Q: In multi-machine mode, who creates which registers? → A: Each machine creates the registers owned by its organisations (based on manifest ownership). Box 1 creates Register 1 (Buyer-owned), Box 2 creates Register 2 (Funder-owned). Each subscribes to the other's registers via replication.
- Q: What credit score threshold determines approve/decline routing in Invoice Finance? → A: 50/100. Golden path buyer scores 85 (approved), declined scenario buyer scores 35 (refused).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Data-Driven Platform Setup (Priority: P1)

An operator runs the setup wizard on their machine. The wizard reads a walkthrough manifest defining all organisations, participants, wallets, registers, and blueprints. The operator selects which organisations belong to this machine. The wizard bootstraps everything via the CLI and outputs credentials and MCP configuration for each participant role.

**Why this priority**: Nothing else works without platform state. Setup must be reliable, idempotent, and flexible enough to support single-machine or multi-machine deployments.

**Independent Test**: Run the setup wizard on a clean remote peer, select all 4 organisations, verify all resources are created and state file is written with valid credentials.

**Acceptance Scenarios**:

1. **Given** a running remote Sorcha instance and the walkthrough manifest, **When** the operator runs the setup wizard and selects all organisations, **Then** 4 organisations, 6 users, 6 wallets, 6 participants, 2 registers, and 2 blueprints are created, and a state file with all credentials and wallet addresses is written.
2. **Given** a previously completed setup, **When** the operator re-runs the setup wizard, **Then** existing resources are detected and skipped (idempotent), no duplicates are created.
3. **Given** a two-machine deployment, **When** the operator runs the setup wizard on each machine selecting its assigned organisations, **Then** each machine creates its own organisations and the registers they own (per manifest ownership), and subscribes to registers owned by other machines once replication delivers them.
4. **Given** a completed setup, **When** the wizard finishes, **Then** MCP server configuration files are generated for each participant role with the correct JWT tokens and gateway URLs.

---

### User Story 2 - Procurement-to-Pay Workflow (Priority: P1)

A Buyer raises a purchase order. The Supplier acknowledges it, confirms delivery, and raises an invoice. The Buyer confirms goods received and approves the invoice. A Verified Invoice Credential is issued to the Supplier upon approval. Each step is performed by a different participant using their own identity and wallet.

**Why this priority**: This is the core workflow that demonstrates the platform's multi-organisation orchestration, selective disclosure, and verifiable credential issuance. It is the foundation for the financing flow.

**Independent Test**: Run the full 6-action procurement-to-pay flow with scripted scenario data. Verify each action completes, the correct participant performs each step, and a VerifiedInvoiceCredential is issued.

**Acceptance Scenarios**:

1. **Given** a published Procurement-to-Pay blueprint and a registered buyer and supplier, **When** the Procurement Manager raises a purchase order, **Then** a workflow instance is created and the Sales Manager receives a pending action to acknowledge the PO.
2. **Given** an acknowledged PO, **When** the Sales Manager confirms delivery and the Site Manager confirms goods received, **Then** both actions complete and the Sales Manager receives a pending action to raise an invoice.
3. **Given** a raised invoice, **When** the Procurement Manager approves the invoice, **Then** a VerifiedInvoiceCredential is issued containing the invoice amount, buyer/supplier identifiers, PO reference, and payment due date.
4. **Given** a raised invoice with incorrect amounts, **When** the Procurement Manager disputes the invoice, **Then** the workflow routes back to the invoice action for the Supplier to resubmit (non-terminal rejection).
5. **Given** the workflow is running with scripted scenario data, **When** all 6 actions complete, **Then** the payload data matches the scenario file exactly and calculated values (invoiceTotal, daysSinceDelivery) are correct.

---

### User Story 3 - Invoice Finance Workflow with Cross-Register Credentials (Priority: P1)

After receiving a Verified Invoice Credential from the procurement flow, the Supplier's Finance Director requests financing on a separate register. The Credit Insurer provides a buyer creditworthiness assessment. The Funder evaluates the application — seeing only the invoice amount and credit score, not the line item detail or supplier margin — and approves or declines financing.

**Why this priority**: This demonstrates the cross-register credential chain (VC from Register 1 required on Register 2), selective disclosure under FLE (the Funder cannot see line items), and the multi-register architecture.

**Independent Test**: Complete the procurement flow first, then run the 4-action finance flow using the issued VerifiedInvoiceCredential. Verify the credential requirement is enforced, the Funder's view is restricted to disclosed fields, and a TradeFinanceCredential is issued on approval.

**Acceptance Scenarios**:

1. **Given** a valid VerifiedInvoiceCredential from Register 1, **When** the Finance Director requests financing on Register 2, **Then** the credential is validated (including revocation check) and the workflow instance is created.
2. **Given** no valid VerifiedInvoiceCredential, **When** the Finance Director attempts to request financing, **Then** the action is rejected with a clear error citing the missing credential requirement.
3. **Given** a financing request, **When** the Assessment Service provides a buyer credit assessment, **Then** the credit score, limit, and risk rating are recorded and the Credit Analyst receives a pending evaluation action.
4. **Given** a buyer credit score of 50 or above, **When** the Credit Analyst approves financing, **Then** calculated values (advanceAmount, feeAmount, netAdvance) are correct and a TradeFinanceCredential is issued.
5. **Given** a buyer credit score below 50, **When** the Credit Analyst declines financing, **Then** the workflow terminates without issuing a credential.

---

### User Story 4 - DevMode to FLE Transition (Priority: P2)

The operator runs the full procurement-to-pay and financing flow in DevMode, inspecting plaintext payloads to verify data flow. At a chosen moment, the operator disables DevMode on both registers (irreversible). The same flow is run again — now all payloads are field-level encrypted with selective disclosure enforced.

**Why this priority**: The DevMode-to-FLE transition is the key pitch moment. It proves the same workflow works identically under encryption and that selective disclosure actually restricts what each participant can see.

**Independent Test**: Run the golden path flow in DevMode and inspect payloads (plaintext visible). Disable DevMode. Run the golden path again. Verify payloads are encrypted, and querying as the Funder returns only disclosed fields.

**Acceptance Scenarios**:

1. **Given** registers in DevMode, **When** the full 10-action flow completes, **Then** all payloads are stored in plaintext and any participant can read all disclosed fields.
2. **Given** registers in DevMode, **When** the operator disables DevMode via CLI, **Then** the operation succeeds and is irreversible (re-enabling fails).
3. **Given** registers with FLE active, **When** the full 10-action flow completes, **Then** all payloads are encrypted with per-recipient key wrapping.
4. **Given** FLE is active, **When** the Funder queries an invoice transaction on Register 1, **Then** they see only payment terms and invoice amount — not line items, delivery address, or supplier margin.
5. **Given** FLE is active, **When** the Credit Insurer queries a financing transaction on Register 2, **Then** they see only the buyer credit score and risk rating — not the invoice amount or financing terms.

---

### User Story 5 - Agent-Driven Parallel Execution (Priority: P2)

Two independent Claude Code sessions on separate machines act as different participants. Each session connects to the remote Sorcha instance via MCP Server connections (one per participant role). The sessions coordinate exclusively through register replication — no direct communication. Each agent polls its inbox, generates appropriate payloads, and submits actions autonomously.

**Why this priority**: Agent-driven execution is the novel element that differentiates this walkthrough from existing ones. It proves Sorcha can be operated by AI agents through standard tooling.

**Independent Test**: Start two Claude sessions on separate machines. Box 1 plays Buyer and Credit Insurer. Box 2 plays Supplier and Funder. Initiate the flow from Box 2. Verify both sessions complete their actions without manual intervention and the full flow completes across both registers.

**Acceptance Scenarios**:

1. **Given** two Claude sessions with MCP connections configured, **When** one agent submits an action, **Then** the resulting pending action appears in the other agent's inbox via register replication.
2. **Given** a pending action in an agent's inbox, **When** the agent polls using the inbox tool, **Then** it identifies the action, loads the schema, generates a valid payload, and submits successfully.
3. **Given** both agents running in scripted mode with the same scenario data, **When** the golden path completes, **Then** all 10 actions are executed in the correct order by the correct participants and both VCs are issued.
4. **Given** both agents running in persona mode, **When** the golden path completes, **Then** all actions complete with plausible generated data that passes schema validation.
5. **Given** one agent submits an action, **When** the other agent's next action depends on it, **Then** the dependent action appears in the other agent's inbox after register replication completes.

---

### User Story 6 - Single-Machine Mode (Priority: P3)

For development and local testing, the entire walkthrough runs on one machine with all 6 MCP connections in one Claude session. The agent plays all roles sequentially. Register replication is not exercised but FLE, VCs, and the full workflow are demonstrated.

**Why this priority**: Enables development and testing without requiring multiple machines or peers. Lower priority because the multi-peer scenario is the primary demonstration.

**Independent Test**: Run the setup wizard selecting all organisations on one machine. Start one Claude session with all 6 MCP connections. Run the golden path. Verify the full 10-action flow completes.

**Acceptance Scenarios**:

1. **Given** all organisations set up on one machine, **When** the setup wizard completes, **Then** all 6 MCP configs point to the same gateway URL.
2. **Given** one Claude session with all 6 MCP connections, **When** the golden path is executed, **Then** all 10 actions complete successfully with correct VC issuance.

---

### User Story 7 - Scripted Scenario Variations (Priority: P3)

Three scripted scenarios cover the key workflow paths: golden path (full approval), disputed invoice (rejection and resubmission), and declined financing (poor credit score). Each scenario has predefined payload data for deterministic, reproducible runs.

**Why this priority**: Multiple scenarios demonstrate the platform's routing, rejection, and conditional logic capabilities. Lower priority than the core flow because they build on the same infrastructure.

**Independent Test**: Run each scenario independently and verify the action path matches expectations, calculated values are correct, and VCs are issued (or not) as expected.

**Acceptance Scenarios**:

1. **Given** golden path scenario data, **When** the flow completes, **Then** the action path is 1-2-3-4-5-6 (Register 1) then 1-2-3-4 (Register 2), both VCs are issued, and financing is approved at 90% advance.
2. **Given** disputed invoice scenario data, **When** the Buyer disputes the invoice, **Then** the flow routes back to action 5, the Supplier resubmits, and the second submission is approved.
3. **Given** declined finance scenario data with a low buyer credit score, **When** the Funder evaluates, **Then** financing is declined and no TradeFinanceCredential is issued.

---

### Edge Cases

- What happens when register replication is slow and the dependent agent polls before the action arrives? The agent retries with a configurable poll interval and timeout.
- What happens when a VerifiedInvoiceCredential has been revoked between issuance on Register 1 and presentation on Register 2? The FailClosed revocation check policy rejects the financing request.
- What happens when the setup wizard is run on a machine where some organisations already exist from a previous run? The wizard is idempotent — it skips existing resources.
- What happens when an agent generates persona-mode data that fails schema validation? The agent detects the validation error and regenerates compliant data.
- What happens when DevMode is disabled mid-flow (some transactions plaintext, some encrypted)? The system handles mixed-mode registers — only new transactions are encrypted, existing plaintext transactions remain readable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The walkthrough MUST define all organisations, participants, wallets, registers, and blueprints in a single manifest file that drives setup across any number of machines.
- **FR-002**: The setup wizard MUST read the manifest, ask the operator which organisations belong to this machine, and bootstrap all required resources via CLI commands.
- **FR-003**: The setup wizard MUST be idempotent — re-running skips already-created resources without errors.
- **FR-004**: The setup wizard MUST generate MCP server configuration files for each participant role with correct JWT tokens and gateway URLs.
- **FR-005**: The setup wizard MUST create registers owned by its local organisations (based on register ownership in the manifest) and publish their associated blueprints. For registers owned by other machines, the wizard waits for replication and subscribes.
- **FR-006**: The Procurement-to-Pay blueprint MUST define 6 actions with the correct participant assignments, schemas, disclosure rules, and routing logic.
- **FR-007**: The Invoice Finance blueprint MUST define 4 actions with a credential requirement for VerifiedInvoiceCredential on the first action.
- **FR-008**: Action 6 of the Procurement-to-Pay blueprint MUST issue a VerifiedInvoiceCredential containing invoice amount, buyer/supplier identifiers, PO reference, and payment due date.
- **FR-009**: Action 4 of the Invoice Finance blueprint MUST issue a TradeFinanceCredential on approval containing advance amount, fee, repayment date, and invoice reference.
- **FR-010**: Blueprint disclosures MUST enforce the defined disclosure matrix — each participant sees only the fields they are authorised to view.
- **FR-011**: The walkthrough MUST support a DevMode phase (plaintext payloads) followed by an irreversible transition to FLE (encrypted payloads).
- **FR-012**: Agent prompts MUST instruct the Claude session on its identity, MCP connections, behaviour rules, and execution mode (scripted or persona).
- **FR-013**: In scripted mode, agents MUST use predefined payload data from scenario files for deterministic, reproducible runs.
- **FR-014**: In persona mode, agents MUST generate plausible commercial data within the action's schema constraints using their assigned company persona.
- **FR-015**: The walkthrough MUST support at least 3 scripted scenarios: golden path, disputed invoice, and declined financing.
- **FR-016**: Each agent session MUST support multiple MCP connections (one per participant role) to operate as multiple participants simultaneously.
- **FR-017**: Agents MUST coordinate exclusively through the register (inbox polling and action submission) — no direct inter-agent communication.
- **FR-018**: The walkthrough MUST work in single-machine mode (all roles in one session) and multi-machine mode (roles distributed across sessions on separate peers).
- **FR-019**: Blueprint calculations MUST compute invoiceTotal, daysSinceDelivery (Register 1) and advanceAmount, feeAmount, netAdvance (Register 2).
- **FR-020**: The walkthrough MUST include a traditional PowerShell setup script for CI compatibility alongside the agent-driven setup wizard.

### Key Entities

- **Walkthrough Manifest**: Central definition of all organisations, participants, wallets, registers, blueprints, and scenario references. Drives the setup wizard and determines what exists in the platform.
- **Organisation**: A legal entity (SME, funder, credit bureau) with its own subdomain, users, and wallets. Four organisations participate: Cairngorm Construction (Buyer), Highland Timber Supplies (Supplier), ScotTrade Finance (Funder), UK Trade Credit Bureau (Credit Insurer).
- **Participant**: An individual within an organisation who performs workflow actions. Each has a wallet, a published identity on the register, and a corresponding MCP connection. Six participants across the four organisations.
- **Scenario**: A predefined set of payload data for each action in the workflow, plus expected action paths and outcomes. Used for deterministic/scripted runs.
- **Persona**: A company description with product details, typical order sizes, and pricing ranges. Used by agents in improvised mode to generate plausible data.
- **Agent Prompt**: Instructions for a Claude session describing which roles it plays, which MCP connections to use, and how to behave during the walkthrough.
- **MCP Configuration**: Per-participant connection settings (JWT token, gateway URL) that allow a Claude session to interact with the platform as that participant.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two independent sessions on separate machines complete the full 10-action flow across 2 registers by coordinating exclusively through register replication, with no manual intervention after initial prompt.
- **SC-002**: The DevMode-to-FLE transition works correctly — the same workflow produces plaintext payloads before the switch and encrypted payloads after, with no changes to agents or blueprints.
- **SC-003**: After FLE transition, querying the register as the Funder returns only disclosed fields (invoice amount, payment terms, credit score, financing terms) — all other fields are inaccessible.
- **SC-004**: The VerifiedInvoiceCredential issued on Register 1 is successfully validated as a requirement on Register 2, including revocation checking with FailClosed policy.
- **SC-005**: Both scripted (deterministic) and persona (improvised) modes complete the golden path scenario successfully, with persona mode generating data that passes all schema validations.
- **SC-006**: The setup wizard completes platform bootstrapping (4 orgs, 6 users, 6 wallets, 6 participants, 2 registers, 2 blueprints) in a single run, and re-running produces no errors or duplicates.
- **SC-007**: All 3 scripted scenarios (golden path, disputed invoice, declined finance) complete with the correct action paths and expected outcomes (VC issuance or terminal rejection).
- **SC-008**: The walkthrough runs successfully in both single-machine mode (development) and multi-machine mode (demonstration) using the same manifest, blueprints, and scenario data.
