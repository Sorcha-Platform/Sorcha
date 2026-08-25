# Trade Finance Walkthrough

## Overview

The Trade Finance walkthrough demonstrates a realistic SME procurement-to-pay cycle with invoice financing, spanning **2 registers**, **4 organisations**, and **6 participants**. It is the most comprehensive Sorcha walkthrough, exercising multi-org orchestration, field-level encryption (FLE), selective disclosure, cross-register verifiable credentials, and AI agent-driven execution.

This walkthrough addresses the **Digital Trust Centre of Excellence Innovation Challenge 2: Digitising SME Trade Finance** by showing how a decentralised register platform can replace paper-based procurement and invoice finance processes with cryptographically secured, privacy-preserving workflows.

**Workflow summary:** A construction company (Cairngorm) purchases timber from a supplier (Highland Timber Supplies). After the goods are delivered and the invoice is approved, the supplier's finance director uses the resulting verifiable credential to request advance financing from a funder (ScotTrade Finance), who evaluates the application with help from a credit bureau (UK Trade Credit Bureau).

---

## Architecture

The walkthrough is designed for two-machine execution, with each machine running a Claude Code session connected to its assigned participants via the Sorcha MCP Server. Both machines run a full Sorcha peer node, and the registers replicate between them.

```
┌─────────────────────────────────────────────────────────────────┐
│  Box 1 (Buyer Side)              Box 2 (Supplier/Funder Side)  │
│  ┌──────────────┐                ┌──────────────┐              │
│  │ Claude Code  │                │ Claude Code  │              │
│  │ 3 MCP conns  │                │ 3 MCP conns  │              │
│  └──────┬───────┘                └──────┬───────┘              │
│         │                               │                      │
│         ▼                               ▼                      │
│  ┌──────────────┐    Replication  ┌──────────────┐             │
│  │ Sorcha Peer  │◄──────────────►│ Sorcha Peer  │             │
│  │   (n1)       │                │   (peer2)    │             │
│  └──────────────┘                └──────────────┘             │
└─────────────────────────────────────────────────────────────────┘
```

Each Claude Code session has 3 MCP server connections, one per participant it controls. The agents coordinate exclusively through register replication -- there is no direct communication channel between them. When one agent submits a transaction, the other agent discovers it by polling its participants' inboxes via MCP.

Single-machine mode is also supported: one Claude Code session runs all 6 MCP connections and plays all roles sequentially.

---

## Organisations and Participants

| Organisation | Role | Participants | Box |
|---|---|---|---|
| Cairngorm Construction Ltd | Buyer | Procurement Manager, Site Manager | Box 1 |
| Highland Timber Supplies | Supplier | Sales Manager, Finance Director | Box 2 |
| ScotTrade Finance | Funder | Credit Analyst | Box 2 |
| UK Trade Credit Bureau | Credit Insurer | Assessment Service | Box 1 |

### Participant Responsibilities

| Participant | Organisation | What They Do |
|---|---|---|
| Procurement Manager | Cairngorm Construction | Raises purchase orders, approves or disputes invoices |
| Site Manager | Cairngorm Construction | Confirms goods received on site, flags discrepancies |
| Sales Manager | Highland Timber Supplies | Acknowledges POs, confirms delivery, raises invoices |
| Finance Director | Highland Timber Supplies | Requests invoice financing using verified invoice credentials |
| Credit Analyst | ScotTrade Finance | Evaluates financing applications, approves or declines funding |
| Assessment Service | UK Trade Credit Bureau | Provides automated buyer creditworthiness reports |

All participants use ED25519 signing keys via Sorcha HD wallets.

---

## Registers

| Register | Owner | Blueprint | Purpose |
|---|---|---|---|
| SME Trade Register | Cairngorm Construction Ltd | Procurement-to-Pay (6 actions) | Procurement lifecycle from PO to invoice approval |
| Trade Finance Register | ScotTrade Finance | Invoice Finance (4 actions) | Invoice financing from application to funding decision |

The two registers are independent but linked by a cross-register verifiable credential: the `VerifiedInvoiceCredential` issued on Register 1 is required as input to Register 2.

---

## Workflow 1: Procurement-to-Pay (Register 1)

A 6-action workflow on the SME Trade Register, owned by Cairngorm Construction.

### Action Flow

```
[1] Raise Purchase Order          (Procurement Manager)
         │
         ▼
[2] Acknowledge PO                (Sales Manager)
         │
         ▼
[3] Confirm Delivery              (Sales Manager)
         │
         ▼
[4] Confirm Goods Received        (Site Manager)
         │
         ▼
[5] Raise Invoice                 (Sales Manager)
         │
         ▼
[6] Approve/Dispute Invoice       (Procurement Manager)
         │
    ┌────┴────┐
    ▼         ▼
 Approved   Disputed ──► loops back to [5]
    │
    ▼
 VerifiedInvoiceCredential issued
```

### Action Details

**1. Raise Purchase Order** -- The Procurement Manager submits a purchase order specifying line items (description, quantity, unit, unit price), delivery address, payment terms (Net 15/30/45/60), and required delivery date. The PO reference and project name feed the instance reference (e.g., `PO-AVI-PO-C-8F2A`).

**2. Acknowledge PO** -- The Sales Manager confirms acceptance of the order, provides an estimated delivery date and order confirmation reference, and optionally adds notes.

**3. Confirm Delivery** -- The Sales Manager records the delivery note reference, actual delivery date, itemised quantities delivered, and delivery condition notes.

**4. Confirm Goods Received** -- The Site Manager verifies goods received on site against the delivery note. Records a GRN reference, received date, condition notes, and flags any discrepancies.

**5. Raise Invoice** -- The Sales Manager submits an invoice with line items, subtotal, VAT rate and amount, invoice total, payment terms, and payment due date. The invoice also contains a confidential supplier cost breakdown (material cost, logistics, margin, margin percentage) that is disclosed only to supplier-side participants. Includes JsonLogic calculations for invoice total and days since delivery.

**6. Approve/Dispute Invoice** -- The Procurement Manager reviews the invoice and either:
- **Approves**: sets the approved amount and notes. A `VerifiedInvoiceCredential` is automatically issued to the Sales Manager with claims for invoice number, amount, PO reference, and payment due date. The credential expires after 90 days.
- **Disputes**: provides a dispute reason. The workflow loops back to action 5 for the supplier to resubmit a corrected invoice.

---

## Workflow 2: Invoice Finance (Register 2)

A 4-action workflow on the Trade Finance Register, owned by ScotTrade Finance. This workflow can only begin after the Procurement-to-Pay workflow completes with an approved invoice.

### Action Flow

```
[1] Request Financing             (Finance Director)
         │
         ▼
[2] Buyer Credit Assessment       (Assessment Service)
         │
         ▼
[3] Evaluate Application          (Credit Analyst)
         │
         ▼
[4] Approve/Decline Financing     (Credit Analyst)
         │
    ┌────┴────┐
    ▼         ▼
 Approved   Declined
    │
    ▼
 TradeFinanceCredential issued
```

### Action Details

**1. Request Financing** -- The Finance Director presents a `VerifiedInvoiceCredential` (from Register 1) and submits a financing request specifying the invoice reference, invoice amount, buyer name, requested advance percentage (50-100%), and urgency (standard or express). The credential requirement uses a `FailClosed` revocation check policy -- if the credential has been revoked, the action is rejected.

**2. Buyer Credit Assessment** -- The Assessment Service (UK Trade Credit Bureau) provides a creditworthiness report on the buyer, including credit score (0-100), credit limit, risk rating (low/medium/high), assessment date, payment history score, years trading, and assessment notes.

**3. Evaluate Application** -- The Credit Analyst reviews the financing application against the credit assessment data. They set the approved advance percentage and fee rate. JsonLogic calculations automatically compute:
- `advanceAmount` = invoiceAmount x (advancePercentage / 100)
- `feeAmount` = advanceAmount x (feeRate / 100)
- `netAdvance` = advanceAmount - feeAmount

**4. Approve/Decline Financing** -- The Credit Analyst makes the final decision:
- **Approves**: specifies advance amount, fee amount, net advance, repayment terms, repayment date, and financing reference. A `TradeFinanceCredential` is issued to the Finance Director with financing details. The credential expires after 180 days.
- **Declines**: provides a decline reason. No credential is issued.

---

## Disclosure Matrix

Selective disclosure controls which participants can see which fields. Under field-level encryption (FLE), undisclosed fields are encrypted and cannot be read even if the transaction envelope is intercepted.

### Register 1: Procurement-to-Pay

| Field | Procurement Mgr | Sales Mgr | Site Mgr | Finance Director | Credit Analyst |
|---|:---:|:---:|:---:|:---:|:---:|
| **Action 1: Raise PO** | | | | | |
| poReference, projectName, siteAddress | Yes | Yes | Yes | -- | -- |
| lineItems, deliveryAddress, requiredDeliveryDate | Yes | Yes | Yes | -- | -- |
| paymentTerms | Yes | Yes | -- | -- | Yes |
| **Action 2: Acknowledge PO** | | | | | |
| accepted, notes | Yes | Yes | -- | -- | -- |
| estimatedDeliveryDate, orderConfirmationRef | Yes | Yes | Yes | -- | -- |
| **Action 3: Confirm Delivery** | | | | | |
| All fields | Yes | Yes | Yes | -- | -- |
| **Action 4: Confirm Goods Received** | | | | | |
| All fields | Yes | Yes | Yes | -- | -- |
| **Action 5: Raise Invoice** | | | | | |
| invoiceNumber, invoiceDate, lineItems, subtotal, vatRate, vatAmount, paymentDueDate | Yes | Yes | -- | -- | -- |
| invoiceTotal | Yes | Yes | -- | -- | Yes |
| paymentTerms | Yes | Yes | -- | -- | Yes |
| supplierCostBreakdown | -- | Yes | -- | Yes | -- |
| **Action 6: Approve/Dispute** | | | | | |
| decision | Yes | Yes | -- | -- | Yes |
| approvedAmount | Yes | Yes | -- | -- | Yes |
| approvalNotes, disputeReason | Yes | Yes | -- | -- | -- |

### Register 2: Invoice Finance

| Field | Finance Director | Sales Mgr | Credit Analyst | Assessment Svc |
|---|:---:|:---:|:---:|:---:|
| **Action 1: Request Financing** | | | | |
| All fields | Yes | Yes | Yes | -- |
| **Action 2: Buyer Credit Assessment** | | | | |
| All fields | -- | -- | Yes | Yes |
| **Action 3: Evaluate Application** | | | | |
| advancePercentage, feeRate | Yes | Yes | Yes | -- |
| evaluationNotes | -- | -- | Yes | -- |
| **Action 4: Approve/Decline** | | | | |
| decision, advanceAmount, feeAmount, netAdvance, repaymentTerms, repaymentDate, financingReference | Yes | Yes | Yes | -- |
| declineReason | -- | -- | Yes | -- |

### Key Disclosure Rules

- **Buyer sees everything except supplier cost breakdown.** The Procurement Manager has full visibility of all procurement actions but never sees the supplier's material cost, logistics cost, or margin on the invoice.
- **Supplier sees everything on their own actions.** The Sales Manager and Finance Director have full visibility of actions they submit.
- **Funder has limited visibility on Register 1.** The Credit Analyst sees only `paymentTerms`, `invoiceTotal`, `decision`, and `approvedAmount` from the procurement register -- enough for credit assessment but nothing about line items, delivery details, or project specifics.
- **Credit insurer assessment is visible only to the funder.** The Assessment Service's buyer credit report (score, risk rating, payment history) is disclosed only to the Credit Analyst and the Assessment Service itself. Neither the supplier nor the buyer sees the credit report.
- **Supplier sees financing terms but not evaluation notes.** The Finance Director and Sales Manager see the approved advance percentage, fee rate, and final terms, but not the Credit Analyst's internal evaluation notes.

---

## DevMode and FLE Transition

Registers are created in **DevMode** by default, where all transaction payloads are stored in plaintext. This makes debugging and development easier since you can read payloads directly from the database.

### DevMode (Default)

- All fields are stored in plaintext regardless of disclosure rules
- Disclosure rules are defined in the blueprint but not enforced cryptographically
- Any participant (or database administrator) can read all fields
- Useful for verifying workflow logic, schema validation, and routing

### Disabling DevMode

DevMode is disabled by submitting a crypto-policy control transaction. This is an **irreversible**
operation — validators reject a policy update that re-enables DevMode.

```http
POST /api/registers/{registerId}/disable-dev-mode
```

> There is **no `sorcha register devmode disable` CLI command.** This document used to promise one;
> it has never existed in `RegisterCommands.cs` (#1579). Use the endpoint above, or
> `run.ps1 -DisableDevMode`, which calls it.

The promotion is **asynchronous**. A `200` means the control transaction was *submitted*; each node
flips its own `devMode` only once that transaction seals into a docket. Poll
`GET /api/registers/{registerId}` until `devMode` is `false` rather than assuming the response means
it has taken effect.

Once it has sealed:
- All **new** transactions use field-level encryption
- Each field is encrypted to only the participants listed in its disclosure rules
- Undisclosed fields appear as encrypted blobs to unauthorised participants
- The same blueprint, same workflow, same actions run identically -- the only difference is payload encryption

**Promotion is not retrospective.** Payloads already sealed while the register was in DevMode remain
plaintext on the ledger permanently, because the ledger is immutable. Promoting a register protects
what it stores *next*, not what it already holds.

### Verification

`run.ps1 -DisableDevMode` performs the promotion and **fails the step** if the register has not
actually left DevMode within 180 seconds.

`run.ps1 -VerifyFLE` demonstrates **role-based disclosure**: it reads the register as different
role-holders and prints which fields each is shown. That is not evidence about encryption at rest —
it asks the API what it is willing to show, so a filter bug or an unpromoted register would produce
the same output.

The claim that a Normal register stores field **values** as ciphertext is settled by reading the
stored bytes out of MongoDB, in `walkthroughs/EncryptionAtRest/`:

```bash
pwsh walkthroughs/EncryptionAtRest/setup.ps1 -Profile n1 -Force
pwsh walkthroughs/EncryptionAtRest/run-conformance.ps1 -Profile n1
```

That check promotes a register mid-run and requires the same probe to **find** a known value while
the register is in DevMode and **fail to find it** afterwards. The pairing is the point: an absence
on its own is equally the result of a probe looking in the wrong place.

---

## Scenarios

The walkthrough includes three scripted scenarios with deterministic data, plus support for improvised persona-mode execution.

### Scenario A: Golden Path

The happy path -- full procurement cycle followed by approved financing.

| Phase | Actions | Outcome |
|---|---|---|
| Procurement | 6 (PO, Ack, Delivery, GRN, Invoice, Approve) | Invoice approved, VerifiedInvoiceCredential issued |
| Finance | 4 (Request, Assessment, Evaluate, Approve) | Financing approved, TradeFinanceCredential issued |

**Sample data:** Cairngorm orders structural timber, OSB boards, and connectors for the Aviemore Heights Phase 2 project. Invoice total: £10,248.00. Buyer credit score: 85/100 (low risk). Advance: 90% at 2.5% fee. Net advance to supplier: £8,992.62.

### Scenario B: Disputed Invoice

Invoice dispute with resubmission, followed by approved financing.

| Phase | Actions | Outcome |
|---|---|---|
| Procurement | 8 (PO, Ack, Delivery, GRN, Invoice, Dispute, Resubmit, Approve) | Disputed then approved after correction |
| Finance | 4 (Request, Assessment, Evaluate, Approve) | Financing approved on corrected invoice |

**Sample data:** Grantown Affordable Housing project. Original invoice lists 450 linear metres of timber but PO and delivery note show 400. Procurement Manager disputes. Supplier resubmits corrected invoice (£7,452.00 vs original £7,962.00). Second invoice approved. Financing proceeds normally.

### Scenario C: Declined Finance

Full procurement cycle completes but financing is declined due to poor buyer credit.

| Phase | Actions | Outcome |
|---|---|---|
| Procurement | 6 (PO, Ack, Delivery, GRN, Invoice, Approve) | Invoice approved, VerifiedInvoiceCredential issued |
| Finance | 4 (Request, Assessment, Evaluate, Decline) | Financing declined, no TradeFinanceCredential |

**Sample data:** Loch Ness View Cottages project. Invoice total: £3,162.00. Buyer credit score: 35/100 (high risk) with recent CCJ and limited trading history. Credit Analyst declines: score below minimum threshold of 50.

### Verifiable Credentials Issued

| Scenario | VerifiedInvoiceCredential | TradeFinanceCredential |
|---|:---:|:---:|
| Golden Path | Yes (90-day expiry) | Yes (180-day expiry) |
| Disputed Invoice | Yes (after resubmission) | Yes |
| Declined Finance | Yes | No |

---

## Agent-Driven Execution

The walkthrough is designed for execution by AI agents (Claude Code sessions) rather than manual API calls.

### Two-Machine Mode

Two Claude Code sessions run on separate machines, each with 3 MCP connections:

**Box 1 -- Buyer-Side Agent** (`prompts/buyer-agent.md`):
- `sorcha-procurement-mgr` -- Raises POs, approves invoices
- `sorcha-site-mgr` -- Confirms goods received
- `sorcha-assessment-svc` -- Provides buyer credit assessments

**Box 2 -- Supplier/Funder-Side Agent** (`prompts/supplier-agent.md`):
- `sorcha-sales-mgr` -- Acknowledges POs, confirms delivery, raises invoices
- `sorcha-finance-director` -- Requests invoice financing
- `sorcha-credit-analyst` -- Evaluates and approves/declines financing

### Single-Machine Mode

One Claude Code session with all 6 MCP connections. The agent plays all roles sequentially, switching between buyer and supplier perspectives as actions become available.

### Execution Modes

**Scripted mode** -- The agent loads a scenario file (e.g., `data/scenario-golden-path.json`) and uses the exact payload values for every action. Deterministic and repeatable.

**Persona mode** -- No scenario file is provided. The agent generates plausible commercial data using the persona descriptions in `prompts/personas/`. All generated data must pass the action's JSON schema validation. Results vary between runs.

### Agent Behaviour

Both agents follow the same loop:

1. Poll all MCP connection inboxes for pending actions (every 10 seconds)
2. Identify which participant should handle the pending action
3. Load the action's JSON schema
4. Prepare the payload (from scenario file or generated)
5. Submit the action via the correct MCP connection
6. Log the result and return to polling
7. Timeout after 5 minutes of inactivity

Agents coordinate exclusively through register replication. There is no side channel.

---

## Cross-Register Credential Flow

The key integration between the two registers is the `VerifiedInvoiceCredential`:

```
Register 1 (Procurement-to-Pay)          Register 2 (Invoice Finance)
┌─────────────────────────────┐          ┌──────────────────────────────┐
│                             │          │                              │
│ Action 6: Approve Invoice   │          │ Action 1: Request Financing  │
│   decision = "approve"      │          │   credentialRequirements:    │
│        │                    │          │     - VerifiedInvoiceCredential│
│        ▼                    │          │       requiredClaims:        │
│ Issue VerifiedInvoiceCredential ──────►│       - invoiceNumber        │
│   claims:                   │          │       - invoiceAmount        │
│   - invoiceNumber           │  present │       - poReference          │
│   - invoiceAmount           │          │       - paymentDueDate       │
│   - poReference             │          │   revocationCheckPolicy:     │
│   - paymentDueDate          │          │     FailClosed               │
│   90-day expiry             │          │                              │
└─────────────────────────────┘          └──────────────────────────────┘
```

The credential is:
- **Issued** on Register 1 when the Procurement Manager approves the invoice
- **Presented** on Register 2 when the Finance Director requests financing
- **Verified** against revocation status with a `FailClosed` policy (if revocation check fails, the action is rejected)
- **Selectively disclosable** -- only `invoiceNumber`, `invoiceAmount`, `poReference`, and `paymentDueDate` need to be revealed

---

## Setup Instructions

### Prerequisites

- Docker Desktop running with all Sorcha services healthy
- Sorcha CLI configured with a valid profile
- Blueprint template files in the walkthrough directory

### Quick Start

```powershell
# 1. Run the setup script (creates orgs, wallets, registers, blueprints, participants)
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway

# 2. For two-machine mode, run setup on each box with its organisations:
# Box 1:
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway -Organizations "cairngorm,trade-credit"
# Box 2:
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway -Organizations "highland-timber,scottrade"

# 3. Configure MCP connections from generated configs in mcp-configs/

# 4. Start Claude Code sessions with agent prompts:
# Box 1: Use prompts/buyer-agent.md as the system prompt
# Box 2: Use prompts/supplier-agent.md as the system prompt

# 5. Run scenarios (scripted execution via PowerShell):
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario golden-path
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario disputed
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario declined
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario all

# 6. Run with FLE enabled:
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario golden-path -DisableDevMode -VerifyFLE
```

### Setup Details

The `setup.ps1` script is idempotent and performs these steps:

1. Loads `config.json` and secrets
2. Creates 4 organisations (or the subset specified by `-Organizations`)
3. Creates 6 wallets (one per participant, ED25519)
4. Registers participants and links them to their wallets
5. Creates 2 registers (SME Trade Register, Trade Finance Register)
6. Publishes 2 blueprints from template files
7. Generates MCP server configurations in `mcp-configs/`
8. Writes `state.json` with all created entity IDs and wallet addresses

Re-running the script skips already-created resources by checking `state.json`.

### MCP Configuration

After setup, merge the generated MCP configs into Claude Code settings:

- **Single-machine:** All 6 configs into one session
- **Two-machine Box 1:** `mcp-procurement-mgr.json`, `mcp-site-mgr.json`, `mcp-assessment-svc.json`
- **Two-machine Box 2:** `mcp-sales-mgr.json`, `mcp-finance-director.json`, `mcp-credit-analyst.json`

Each config follows the template in `mcp-configs/template.json`, connecting via `dotnet run --project src/Apps/Sorcha.McpServer` with a JWT token for authentication.

---

## File Structure

```
walkthroughs/TradeFinance/
├── config.json                         # Walkthrough configuration: orgs, participants, registers, agent assignments
├── procurement-to-pay-template.json    # Blueprint template for the 6-action procurement workflow
├── invoice-finance-template.json       # Blueprint template for the 4-action invoice finance workflow
├── setup.ps1                           # Bootstrap script: creates orgs, wallets, registers, blueprints, MCP configs
├── run.ps1                             # Scenario runner: executes golden-path, disputed, declined, or all scenarios
├── data/
│   ├── credit-scores.json              # Scripted buyer credit data for the Assessment Service
│   ├── scenario-golden-path.json       # Scenario A: full approval flow with deterministic payloads
│   ├── scenario-disputed.json          # Scenario B: invoice dispute with resubmission
│   └── scenario-declined.json          # Scenario C: financing declined due to poor buyer credit
├── docs/
│   └── Trade-Finance-Walkthrough.md    # This document
├── mcp-configs/
│   └── template.json                   # MCP server connection template (populated by setup.ps1)
└── prompts/
    ├── setup-wizard.md                 # Agent prompt for automated setup assistance
    ├── buyer-agent.md                  # Agent prompt for the buyer-side Claude Code session
    ├── supplier-agent.md               # Agent prompt for the supplier/funder-side Claude Code session
    └── personas/
        ├── cairngorm.md                # Cairngorm Construction Ltd — buyer company profile
        ├── highland-timber.md          # Highland Timber Supplies — supplier company profile
        ├── scottrade.md                # ScotTrade Finance Ltd — funder company profile
        └── trade-credit.md             # UK Trade Credit Bureau — credit insurer company profile
```

### Key Files

| File | Purpose |
|---|---|
| `config.json` | Defines the 4 organisations, 6 participants, 2 registers, agent-to-box assignments, and scenario list |
| `procurement-to-pay-template.json` | Full blueprint with schemas, disclosures, routes, forms, calculations, and VC issuance config |
| `invoice-finance-template.json` | Full blueprint with cross-register credential requirements, JsonLogic calculations, and VC issuance |
| `setup.ps1` | Idempotent setup script supporting single-machine and multi-machine modes |
| `run.ps1` | Scenario runner with `--DisableDevMode` and `--VerifyFLE` flags for FLE testing |
| `data/scenario-*.json` | Deterministic payload data for each scenario, covering both procurement and finance actions |
| `prompts/buyer-agent.md` | System prompt for the buyer-side AI agent with polling, coordination, and execution rules |
| `prompts/supplier-agent.md` | System prompt for the supplier/funder-side AI agent with cross-register flow awareness |
| `prompts/personas/*.md` | Detailed company profiles for persona-mode improvised execution |
