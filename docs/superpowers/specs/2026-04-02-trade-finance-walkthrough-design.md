# Trade Finance Walkthrough Design

A multi-organisation, multi-peer walkthrough demonstrating Sorcha's procurement-to-pay capabilities with field-level encryption, cross-register credential chains, and AI agent-driven participants. Designed to address the Digital Trust Centre of Excellence Innovation Challenge 2: Digitising SME Trade Finance.

## Context

Existing walkthroughs (ConstructionPermit, SelfBuildHouse) run as single-threaded PowerShell scripts from one machine. This walkthrough advances the model:

- **Multi-peer**: two independent Claude Code sessions on separate machines coordinate through the register's own replication layer
- **Agent-driven**: Claude agents act as participants, using MCP Server tools to poll inboxes, submit actions, and monitor workflows
- **Data-driven setup**: a manifest defines the full cast; a setup wizard asks which orgs belong to this machine and bootstraps via CLI
- **DevMode → FLE transition**: the register starts in DevMode for inspection, then irreversibly transitions to field-level encryption mid-demo
- **Dual execution modes**: scripted scenarios for CI/golden-path testing, persona-driven improvisation for live demos

## Organisations & Participants

### Cairngorm Construction Ltd (Buyer SME)

Highland construction firm. Raises purchase orders, confirms delivery, approves invoices.

| Participant | Role in Workflow |
|-------------|-----------------|
| Procurement Manager | Raises POs, approves/disputes invoices |
| Site Manager | Confirms goods received (GRN) |

### Highland Timber Supplies (Supplier SME)

Joinery materials supplier in Inverness. Fulfils orders, raises invoices, requests financing.

| Participant | Role in Workflow |
|-------------|-----------------|
| Sales Manager | Acknowledges POs, confirms delivery, raises invoices |
| Finance Director | Requests invoice financing on Register 2 |

### ScotTrade Finance (Funder)

Fintech lender specialising in SME trade finance. Evaluates and advances funds against verified invoices.

| Participant | Role in Workflow |
|-------------|-----------------|
| Credit Analyst | Evaluates financing applications, approves/declines |

### UK Trade Credit Bureau (Credit Insurer)

Provides buyer creditworthiness assessments. In production this would be an API lookup; for the demo, credit limits and risk scores are scripted per buyer.

| Participant | Role in Workflow |
|-------------|-----------------|
| Assessment Service | Issues creditworthiness VCs with scripted data |

**Total: 4 organisations, 6 participants, 6 wallets.**

## Registers & Blueprints

### Register 1: SME Trade Register

- **Owner**: Cairngorm Construction (Buyer)
- **Subscribers**: All 4 organisations
- **Blueprint**: Procurement-to-Pay (6 actions)

| # | Action | Actor | Description |
|---|--------|-------|-------------|
| 1 | Raise Purchase Order | Procurement Manager (Buyer) | Line items, quantities, delivery address, payment terms |
| 2 | Acknowledge PO | Sales Manager (Supplier) | Confirm acceptance, estimated delivery date |
| 3 | Confirm Delivery | Sales Manager (Supplier) | Delivery note reference, actual delivery date, quantities delivered |
| 4 | Confirm Goods Received | Site Manager (Buyer) | GRN reference, condition notes, discrepancy flag |
| 5 | Raise Invoice | Sales Manager (Supplier) | Invoice number, line items, amounts, payment terms, supplier margin/cost (supplier-only field) |
| 6 | Approve/Dispute Invoice | Procurement Manager (Buyer) | Approval or dispute with reason. Dispute routes back to action 5 (resubmit). Approval issues `VerifiedInvoiceCredential` |

**Rejection**: Action 6 dispute is non-terminal, routes back to action 5 for the Supplier to resubmit.

**VC Issued**: `VerifiedInvoiceCredential` on action 6 approval, containing invoice amount, buyer/supplier identifiers, PO reference, and payment due date.

**Calculations**: `invoiceTotal` (sum of line item amounts), `daysSinceDelivery` (delivery date to invoice date).

### Register 2: Trade Finance Register

- **Owner**: ScotTrade Finance (Funder)
- **Subscribers**: Supplier, Funder, Credit Insurer (Buyer is NOT a subscriber — they don't need visibility into the financing arrangement)
- **Blueprint**: Invoice Finance (4 actions)

| # | Action | Actor | Description |
|---|--------|-------|-------------|
| 1 | Request Financing | Finance Director (Supplier) | Presents `VerifiedInvoiceCredential`, requested advance amount, invoice reference |
| 2 | Provide Buyer Assessment | Assessment Service (Credit Insurer) | Buyer credit score, credit limit, risk rating, assessment date. Scripted from `credit-scores.json` |
| 3 | Evaluate Application | Credit Analyst (Funder) | Reviews invoice amount + credit score (NOT line items). Calculates advance %, fee. Routes: approve or decline |
| 4 | Approve/Decline Financing | Credit Analyst (Funder) | Advance amount, fee amount, repayment terms. Decline routes to terminal. Approval issues `TradeFinanceCredential` |

**Credential Requirement**: Action 1 requires a valid `VerifiedInvoiceCredential` with `revocationCheckPolicy: "FailClosed"`.

**VC Issued**: `TradeFinanceCredential` on action 4 approval, containing advance amount, fee, repayment date, and invoice reference.

**Calculations**: `advanceAmount` (invoice amount * advance percentage), `feeAmount` (advance amount * fee rate), `netAdvance` (advance - fee).

## FLE Disclosure Matrix

When FLE is active, each participant only sees fields disclosed to them. This is the core trust demonstration.

### Register 1 (Procurement-to-Pay) Disclosures

| Field | Buyer | Supplier | Funder | Credit Insurer |
|-------|-------|----------|--------|----------------|
| PO line items & quantities | Yes | Yes | No | No |
| Delivery address | Yes | Yes | No | No |
| Payment terms | Yes | Yes | Yes | No |
| Invoice amount (total) | Yes | Yes | Yes | No |
| Invoice line item detail | Yes | Yes | No | No |
| Supplier margin / cost breakdown | No | Yes | No | No |
| GRN / delivery confirmation | Yes | Yes | No | No |

### Register 2 (Invoice Finance) Disclosures

| Field | Supplier | Funder | Credit Insurer |
|-------|----------|--------|----------------|
| Invoice reference & amount | Yes | Yes | No |
| Requested advance amount | Yes | Yes | No |
| Buyer credit score & limit | No | Yes | Yes |
| Risk rating | No | Yes | Yes |
| Financing terms (rate, advance %) | Yes | Yes | No |
| Fee calculation breakdown | Yes | Yes | No |

**Key pitch point**: The Funder can verify the invoice is genuine (via the VC) and see the total amount, but cannot see what was actually purchased or the Supplier's margin. The Credit Insurer provides creditworthiness data but has no visibility into the commercial transaction.

## DevMode → FLE Transition

The walkthrough demonstrates Sorcha's DevMode feature with a three-phase approach:

### Phase 1: DevMode (plaintext)
- Both registers start with `DevMode = true`
- Run the full PO → Invoice → Finance flow
- Inspect payloads in the clear to verify data flow
- Show all participants can read all disclosed fields

### Phase 2: "Go Live" Moment
- Run `sorcha register update --devmode disable` on both registers
- This is irreversible — simulates a real production cutover
- Dramatic demo moment: "This register is now live"

### Phase 3: Encrypted Operation
- Run the same flow again (new instance)
- All payloads are now field-level encrypted (XChaCha20-Poly1305 symmetric, X25519 key wrapping)
- Demonstrate selective disclosure: Funder queries the register and only sees disclosed fields
- Non-disclosed fields return encrypted/inaccessible
- Show verification bundle export for offline audit

## Agent Orchestration

### Architecture

Each physical machine runs one Claude Code session. The session connects to a remote Sorcha instance (not local Docker). Communication between machines happens exclusively through register replication between peers.

```
Box 1 (e.g., n1.sorcha.dev)              Box 2 (e.g., Azure peer)
┌─────────────────────────┐              ┌─────────────────────────┐
│ Claude Code Session     │              │ Claude Code Session     │
│                         │              │                         │
│ MCP: procurement-mgr ──┐│              │ MCP: sales-mgr ───────┐│
│ MCP: site-mgr ─────────┤│              │ MCP: finance-director ─┤│
│ MCP: assessment-svc ───┘│              │ MCP: credit-analyst ──┘│
│                         │              │                         │
│ Roles: Buyer,           │              │ Roles: Supplier,        │
│        Credit Insurer   │              │        Funder           │
└───────────┬─────────────┘              └───────────┬─────────────┘
            │                                         │
            ▼                                         ▼
   Sorcha Peer (n1)  ◄──── Register Replication ────► Sorcha Peer (Azure)
```

### Setup Wizard

A prompt file (`prompts/setup-wizard.md`) instructs Claude to bootstrap the platform state. The wizard:

1. Reads `manifest.json` to discover the full cast
2. Asks the operator: "Which organisations should I set up on this machine?"
3. Authenticates as system admin via CLI: `sorcha auth login --profile admin`
4. Creates organisations: `sorcha org create --name "Cairngorm Construction Ltd" --subdomain cairngorm --output json`
5. Creates users, wallets, and participants for selected orgs
6. If this is the first machine: creates registers, publishes blueprints
7. If this is a subsequent machine: waits for register replication, then subscribes
8. Outputs `state.json` with all credentials and wallet addresses
9. Generates MCP config snippets for each participant role

The wizard is idempotent — re-running skips already-created resources (409 handling).

### Agent Prompts

Each agent prompt (`prompts/buyer-agent.md`, `prompts/supplier-agent.md`) contains:

- **Identity**: which orgs and participants this session controls
- **MCP connections**: which named connections map to which participant
- **Behaviour rules**: poll inbox, respond to pending actions, follow the workflow
- **Mode**: scripted (use scenario data) or persona (improvise within schema)
- **Coordination**: no direct communication with the other box — all coordination happens through the register

Example prompt structure:
```
You are operating as Cairngorm Construction (Buyer) and UK Trade Credit Bureau
(Credit Insurer) in the SME Trade Finance demo.

## Your MCP Connections
- `procurement-mgr`: Use for raising POs and approving invoices
- `site-mgr`: Use for confirming goods received
- `assessment-svc`: Use for responding to credit check requests

## Workflow
1. Use `procurement-mgr` → `sorcha_action_submit` to raise a Purchase Order
2. Monitor `site-mgr` → `sorcha_inbox_list` for goods received actions
3. Monitor `procurement-mgr` → `sorcha_inbox_list` for invoice approval actions
4. When credit assessment requests appear on `assessment-svc`, respond with
   data from credit-scores.json for the relevant buyer

## Mode: [scripted|persona]
...
```

### Execution Modes

**Scripted Mode** (CI / golden path): Agents use predefined payloads from `data/scenario-*.json`. Deterministic and reproducible. Three scenarios:

| Scenario | PO→Invoice Path | Finance Path | Outcome |
|----------|----------------|--------------|---------|
| Golden Path | 1→2→3→4→5→6 (approved) | 1→2→3→4 (approved) | Full flow, VC chain, financing granted |
| Disputed Invoice | 1→2→3→4→5→6 (disputed)→5→6 (approved) | 1→2→3→4 (approved) | Invoice resubmission loop |
| Declined Finance | 1→2→3→4→5→6 (approved) | 1→2→3→4 (declined) | Low credit score, financing refused |

**Persona Mode** (live demo): Agents have company personas (`prompts/personas/*.md`) and generate plausible commercial data within schema constraints. Non-deterministic but realistic. Persona files describe the company, its products, typical order sizes, and pricing ranges.

## Walkthrough File Structure

```
walkthroughs/TradeFinance/
├── manifest.json                        # Full cast definition
├── config.json                          # Walkthrough metadata (category, secrets key)
├── setup.ps1                            # PowerShell bootstrap (legacy/CI compatibility)
├── blueprints/
│   ├── procurement-to-pay.json          # Blueprint 1: PO → Invoice (6 actions)
│   └── invoice-finance.json             # Blueprint 2: Finance request → Approval (4 actions)
├── data/
│   ├── scenario-golden-path.json        # Scripted: full happy path
│   ├── scenario-disputed.json           # Scripted: invoice disputed then approved
│   ├── scenario-declined.json           # Scripted: financing declined
│   └── credit-scores.json              # Scripted buyer credit data
├── prompts/
│   ├── setup-wizard.md                  # Setup wizard prompt
│   ├── buyer-agent.md                   # Box 1 prompt (Buyer + Credit Insurer)
│   ├── supplier-agent.md               # Box 2 prompt (Supplier + Funder)
│   └── personas/
│       ├── cairngorm.md                 # Buyer persona (improvised mode)
│       ├── highland-timber.md           # Supplier persona
│       ├── scottrade.md                # Funder persona
│       └── trade-credit.md             # Credit Insurer persona
├── mcp-configs/
│   ├── procurement-mgr.json            # MCP server config template
│   ├── site-mgr.json
│   ├── sales-mgr.json
│   ├── finance-director.json
│   ├── credit-analyst.json
│   └── assessment-svc.json
└── docs/
    └── Trade-Finance-Walkthrough.md     # Full narrative documentation
```

## Scenarios

### Scenario A: Golden Path

A straightforward procurement-to-pay with approved financing.

**Story**: Cairngorm Construction orders 500 linear metres of treated timber for a housing development in Aviemore. Highland Timber delivers on schedule. Invoice is approved. Highland Timber's Finance Director requests early payment via ScotTrade Finance. Cairngorm's credit score is strong (85/100). Financing is approved at 90% advance, 2.5% fee.

**Demonstrates**: Full 10-action flow across 2 registers, cross-register VC chain, all 6 participants active, FLE disclosure matrix.

### Scenario B: Disputed Invoice

Invoice amount doesn't match PO — the rejection/resubmission loop.

**Story**: Same order, but Highland Timber's invoice includes an undiscussed surcharge for delivery to a remote site. Cairngorm disputes. Highland Timber resubmits with the correct amount. Second submission is approved.

**Demonstrates**: Non-terminal rejection routing, action replay, amended data on resubmission.

### Scenario C: Declined Finance

Good procurement flow but financing refused due to poor buyer credit.

**Story**: A different buyer (or Cairngorm with degraded credit in the scripted data) has a credit score of 35/100. ScotTrade declines financing citing insufficient creditworthiness.

**Demonstrates**: Terminal rejection in Blueprint 2, credit score threshold routing, workflow termination without VC issuance.

## Single-Machine Mode

For development and testing, the entire walkthrough can run on one machine. The setup wizard detects this when all orgs are assigned to one box. In this mode:

- All 6 MCP connections attach to one Claude session
- The agent plays all roles sequentially (or uses sub-agents for parallelism)
- Register replication is not exercised (all actions hit the same peer)
- Still demonstrates FLE, VCs, and the full workflow

## Remote Access

All Sorcha access is via remote endpoints — no local Docker assumption. The setup wizard asks for the gateway URL (e.g., `https://n1.sorcha.dev`, `https://peer2.eastus.cloudapp.azure.com`). CLI profiles are configured per remote:

```bash
sorcha config init --name n1 --gateway https://n1.sorcha.dev
sorcha config init --name azure-peer --gateway https://peer2.eastus.cloudapp.azure.com
```

## Success Criteria

1. **Multi-peer coordination**: Two Claude sessions on separate machines complete the full 10-action flow by communicating exclusively through register replication
2. **FLE transition**: The "go live" moment works — DevMode run produces plaintext, encrypted run produces field-level encrypted payloads with correct selective disclosure
3. **Cross-register VCs**: `VerifiedInvoiceCredential` issued on Register 1 is successfully validated as a requirement on Register 2
4. **Agent autonomy**: Each Claude session independently polls its inbox, generates appropriate payloads, and submits actions without manual intervention
5. **Dual modes**: Both scripted (deterministic) and persona (improvised) modes complete the golden path successfully
6. **Disclosure verification**: After FLE transition, querying the register as the Funder returns only disclosed fields; non-disclosed fields are inaccessible
