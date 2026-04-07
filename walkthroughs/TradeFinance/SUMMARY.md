# Trade Finance Walkthrough

The Trade Finance walkthrough is the most comprehensive Sorcha demonstration — a realistic **SME procurement-to-pay cycle with invoice financing** spanning **2 registers, 4 organisations, and 6 participants**.

## Business Problem

Digitising SME trade finance by replacing paper-based procurement and invoice financing with cryptographically secured, privacy-preserving workflows.

## The Flow

### Register 1 — Procurement-to-Pay (owned by the buyer)

1. Buyer raises a purchase order (timber for construction)
2. Supplier acknowledges the PO
3. Supplier confirms delivery
4. Buyer confirms goods received
5. Supplier raises an invoice
6. Buyer approves or disputes the invoice — if approved, a **VerifiedInvoiceCredential** is issued

### Register 2 — Invoice Finance (owned by the funder)

1. Supplier presents the VerifiedInvoiceCredential to request financing
2. Credit bureau provides a buyer credit assessment
3. Funder evaluates the application (advance/fee auto-calculated via JsonLogic)
4. Funder approves or declines — if approved, a **TradeFinanceCredential** is issued

## What It Demonstrates

| Capability | How |
|---|---|
| **Multi-org orchestration** | 4 independent orgs (Cairngorm Construction, Highland Timber, ScotTrade Finance, UK Trade Credit Bureau) with separate wallets and signing keys |
| **Cross-register verifiable credentials** | Invoice credential from Register 1 is presented as a prerequisite on Register 2 |
| **Field-level encryption (FLE)** | Selective disclosure — e.g. the supplier's confidential cost breakdown is only visible to the Finance Director, not the buyer |
| **Dispute routing** | Invoice disputes loop back for resubmission without restarting the whole workflow |
| **Revocation policy (FailClosed)** | If the invoice credential is revoked, the financing request is automatically rejected |
| **AI agent-driven execution** | Two Claude Code sessions (buyer-side and supplier-side) coordinate **entirely through register replication** — no side channels |
| **P2P replication** | Two-machine mode where each side runs a full Sorcha peer node |

## Organisations and Participants

| Organisation | Role | Participants |
|---|---|---|
| **Cairngorm Construction Ltd** | Buyer | Procurement Manager, Site Manager |
| **Highland Timber Supplies** | Supplier | Sales Manager, Finance Director |
| **ScotTrade Finance** | Funder | Credit Analyst |
| **UK Trade Credit Bureau** | Credit Insurer | Assessment Service |

All participants use ED25519 signing keys via Sorcha HD wallets.

## Three Scripted Scenarios

### Scenario A: Golden Path

- Full approval flow with no disputes
- Invoice: £10,248.00 | Buyer credit score: 85/100 (low risk)
- Advance: 90% at 2.5% fee | Net to supplier: £8,992.62
- Both credentials issued (VerifiedInvoiceCredential + TradeFinanceCredential)

### Scenario B: Disputed Invoice

- Invoice disputed due to quantity mismatch (450 LM invoiced vs 400 LM delivered)
- Corrected invoice resubmitted and approved, then financed
- Corrected invoice: £7,452.00 | Net to supplier: £6,539.13
- Both credentials issued after correction

### Scenario C: Declined Finance

- Procurement completes normally (invoice approved)
- Buyer credit score: 35/100 (high risk) — below minimum threshold of 50
- Financing declined; VerifiedInvoiceCredential issued but TradeFinanceCredential is NOT issued

## Field-Level Encryption and Selective Disclosure

Registers start in **DevMode** (plaintext) and can transition to full FLE:

- Each field is encrypted only to disclosed participants
- Undisclosed fields appear as encrypted blobs to unauthorised participants
- Example: the supplier's confidential cost breakdown (material cost, logistics, margin %) is only visible to the Finance Director

## Cross-Register Verifiable Credential Flow

```
Register 1 (Procurement-to-Pay)         Register 2 (Invoice Finance)
┌────────────────────────────┐          ┌────────────────────────────┐
│ Action 6: Approve Invoice  │          │ Action 1: Request Finance  │
│   decision = "approve"     │          │ credentialRequirements:    │
│           │                │          │ - VerifiedInvoiceCredential│
│   Issue VerifiedInvoice ───────────────► requiredClaims:           │
│   Credential               │  present │   - invoiceNumber          │
│     claims:                │          │   - invoiceAmount          │
│     - invoiceNumber        │          │   - poReference            │
│     - invoiceAmount        │          │   - paymentDueDate         │
│     - poReference          │          │ revocationCheckPolicy:     │
│     - paymentDueDate       │          │   FailClosed               │
│     90-day expiry          │          │                            │
└────────────────────────────┘          └────────────────────────────┘
```

## Execution Modes

- **Two-machine**: Buyer-side and supplier-side Claude Code sessions each with 3 MCP connections, coordinating through P2P register replication
- **Single-machine**: One session playing all roles sequentially
- **Scripted**: Uses predefined scenario payloads for reproducible demos
- **Persona**: Agents generate plausible data from org persona descriptions (varies per run)

## Running the Walkthrough

```powershell
# Setup (single-machine, all orgs)
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway

# Setup (two-machine)
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway -Organizations "cairngorm,trade-credit"      # Box 1
pwsh walkthroughs/TradeFinance/setup.ps1 -Profile gateway -Organizations "highland-timber,scottrade"    # Box 2

# Run scenarios
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario golden-path
pwsh walkthroughs/TradeFinance/run.ps1 -Scenario all -DisableDevMode -VerifyFLE
```

---

*This walkthrough targets the Digital Trust Centre of Excellence Innovation Challenge 2: Digitising SME Trade Finance.*
