# SME Invoice Finance Blueprint

A comprehensive invoice finance (supply chain finance / reverse factoring) workflow for managing procurement of goods and services from SMEs.

## The Problem

An anchor buyer procures goods and services from small and medium enterprises (SMEs) who often cannot afford to wait 60-90 days for payment on standard commercial terms. Cash flow pressure forces SMEs to decline contracts, take on expensive overdrafts, or accept unfavourable terms. Meanwhile, the buyer wants to maintain or extend payment terms to optimise their own working capital.

**Invoice finance bridges this gap.** A financier pays the SME early (minus a small discount fee), then collects the full invoice amount from the buyer at the original maturity date.

## Participants

| Participant | Role | Description |
|---|---|---|
| **Buyer** (Anchor) | Procurement Manager | Issues purchase orders, confirms deliveries, approves invoices, pays at maturity |
| **Supplier** (SME) | SME Supplier | Delivers goods/services, submits invoices, requests early payment |
| **Financier** | Trade Finance Division | Assesses risk, issues funding offers, disburses early payments, collects at maturity |
| **Platform Operator** | Sorcha Platform | Manages onboarding, compliance, facilitates workflow execution |
| **Auditor** | Compliance & Audit | Read-only oversight of the full transaction chain |

## Workflow

```
                              PROCUREMENT PHASE
                    ┌──────────────────────────────────┐
                    │                                  │
   ┌────────────┐   │   ┌─────────────┐   ┌──────────┐│
   │ 0. Issue   │───┼──▶│ 1. Confirm  │──▶│ 2. Submit││
   │    PO      │   │   │   Delivery  │   │  Invoice ││
   │  (Buyer)   │   │   │ (Supplier)  │   │(Supplier)││
   └────────────┘   │   └─────────────┘   └────┬─────┘│
                    │                          │      │
                    └──────────────────────────┼──────┘
                                               │
                              ┌─────────────────┤
                              │                 │
                       Match FAILS         Match PASSES
                        (return to              │
                        supplier)               ▼
                                        ┌──────────────┐
                                        │ 3. Approve   │
                                        │   Invoice    │
                                        │   (Buyer)    │
                                        └──────┬───────┘
                                               │
                              ┌─────────────────┤
                              │                 │
                           Disputed          Approved
                              │                 │
                              ▼                 ▼
                    ┌──────────────┐   FINANCING PHASE (optional)
                    │ 11. Raise    │   ┌──────────────────────────┐
                    │   Dispute    │   │                          │
                    │  (Buyer)     │   │  ┌──────────────┐       │
                    └──────────────┘   │  │ 4. Request   │       │
                                       │  │  Early Pay   │       │
                                       │  │ (Supplier)   │       │
                                       │  └──────┬───────┘       │
                                       │         │               │
                                       │         ▼               │
                                       │  ┌──────────────┐       │
                                       │  │ 5. Issue     │       │
                                       │  │ Funding Offer│       │
                                       │  │ (Financier)  │       │
                                       │  └──────┬───────┘       │
                                       │         │               │
                                       │    ┌────┴────┐          │
                                       │ Declined   Accepted     │
                                       │ (back to     │          │
                                       │  supplier)   ▼          │
                                       │  ┌──────────────┐       │
                                       │  │ 6. Accept    │       │
                                       │  │ Funding Offer│       │
                                       │  │ (Supplier)   │       │
                                       │  └──────┬───────┘       │
                                       │         │               │
                                       │         ▼               │
                                       │  ┌──────────────┐       │
                                       │  │ 7. Disburse  │       │
                                       │  │ Early Payment│       │
                                       │  │ (Financier)  │       │
                                       │  └──────┬───────┘       │
                                       │         │               │
                                       │         ▼               │
                                       │  ┌──────────────┐       │
                                       │  │ 8. Confirm   │       │
                                       │  │ Receipt      │       │
                                       │  │ (Supplier)   │       │
                                       │  └──────────────┘       │
                                       │                          │
                                       └──────────────────────────┘
                                               │
                              SETTLEMENT PHASE │
                    ┌──────────────────────────┼──────┐
                    │                          ▼      │
                    │  ┌──────────────┐  ┌──────────┐ │
                    │  │ 9. Settle at │─▶│10.Confirm│ │
                    │  │   Maturity   │  │Settlement│ │
                    │  │  (Buyer)     │  │(Financier│ │
                    │  └──────────────┘  └──────────┘ │
                    │                                  │
                    └──────────────────────────────────┘
```

## Phases

### Phase 1 - Procurement (Actions 0-2)

The standard procurement cycle: buyer issues a purchase order, supplier delivers goods or services with proof of delivery, then submits an invoice. The system performs an automated **three-way match** - validating PO terms against delivery proof against invoice amounts. Invoices that fail the match are returned to the supplier for correction.

### Phase 2 - Approval (Action 3)

The buyer reviews the matched invoice and either approves it for payment or raises a dispute. Approved invoices become eligible for early financing. The approval is cryptographically signed and recorded on the ledger.

### Phase 3 - Financing (Actions 4-8, optional)

This is the core value-add for SMEs. The supplier can **choose** to request early payment on any approved invoice. The process:

1. **Request** - Supplier requests early payment, declaring the invoice amount and their current facility utilisation
2. **Offer** - Financier assesses risk and presents terms: advance rate (typically 90-98%), discount fee, and net disbursement
3. **Accept** - Supplier reviews and accepts the offer (cryptographically signed, creating a binding agreement)
4. **Disburse** - Financier pays the supplier via Faster Payments / BACS / CHAPS
5. **Confirm** - Supplier confirms receipt of funds

The financing phase is **entirely optional** - the supplier can skip it and wait for normal payment at maturity.

### Phase 4 - Settlement (Actions 9-10)

At the original maturity date, the buyer pays the full invoice amount to the financier (or directly to the supplier if the invoice was not financed). The financier confirms receipt and releases the supplier's credit limit for future invoices.

### Exception Handling (Action 11)

If the buyer disputes an invoice at any point, they raise a formal dispute with type classification (quality, quantity, price mismatch, etc.), supporting evidence, and a requested resolution (credit note, replacement, rework). Disputes are visible to the financier to prevent financing of contested invoices.

## Key Features

### Three-Way Matching
The invoice submission (Action 2) includes JSON Logic calculations that automatically verify:
- Line item totals match the declared net amount
- VAT calculations are correct
- Gross amount equals net + VAT
- The invoice references a valid PO and delivery note

### Selective Disclosure (DAD Model)
Each participant sees only what they need:

| Data | Buyer | Supplier | Financier | Auditor |
|------|:-----:|:--------:|:---------:|:-------:|
| PO line items & prices | Full | Full | Summary | Summary |
| Delivery proof | Full | Full | - | Summary |
| Invoice bank details | Full | Full | - | - |
| Invoice amounts | Full | Full | Full | Full |
| Funding offer terms | Due date only | Full | Full | Full |
| Disbursement details | Due amount | Payment details | Full | Full |
| Dispute details | Full | Full | Summary | Full |
| Settlement status | Full | Status only | Full | Full |

### Credit Limit Management
The financier tracks facility utilisation per supplier. Each funding offer shows the remaining facility limit. Settlement releases credit for future invoices.

### Late Payment Handling
The settlement action (Action 9) captures late payment days and interest charges, creating an auditable record of payment performance.

## SME Benefits

- **Faster cash flow** - Get paid in days instead of 60-90 days
- **Low friction** - Submit invoice, request early payment, receive funds
- **Optional** - Finance only the invoices you need to, when you need to
- **Portable credit history** - On-chain record of timely deliveries and clean invoices builds a verifiable trade record that improves access to future finance
- **No debt on balance sheet** - Invoice finance is a sale of receivables, not borrowing

## Compliance

- **AML/KYC** - Supplier onboarding includes company registration and bank account verification
- **Late Payment Directive** - Settlement records capture payment timeliness, supporting compliance with EU/UK late payment legislation
- **Audit Trail** - The auditor participant has read-only access to the complete transaction chain
- **Non-repudiation** - Every approval, acceptance, and payment confirmation is cryptographically signed

## Data Schemas

| Action | Key Fields |
|--------|-----------|
| Purchase Order | PO number, line items (qty, unit price, VAT), delivery date, payment terms, currency |
| Delivery Confirmation | PO reference, items delivered vs ordered, delivery note number |
| Invoice | Invoice number, PO/delivery references, line items, net/VAT/gross, bank details |
| Buyer Approval | Approved flag, approved amount, dispute reason (if rejected) |
| Early Payment Request | Invoice reference, amount, original due date, facility utilisation |
| Funding Offer | Advance rate, discount fee, net disbursement, maturity date, facility remaining |
| Offer Acceptance | Accepted flag, confirmed bank details, authorised signatory |
| Disbursement | Amount disbursed, payment method, bank reference, amount due from buyer |
| Receipt Confirmation | Funds received flag, amount, date |
| Maturity Settlement | Amount paid, payment status, late days/interest (if applicable) |
| Settlement Confirmation | Status, credit limit released, updated facility limit |
| Dispute | Type, description, disputed amount, requested resolution, deadline |

## Usage

Load this blueprint into the Sorcha Blueprint Designer or publish via the Blueprint Service API:

```bash
# Publish via CLI
sorcha blueprint publish --file complex-sme-invoice-finance.json

# Or via API
curl -X POST http://localhost/api/blueprints \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d @complex-sme-invoice-finance.json
```

## Extending This Blueprint

Common extensions for production use:

- **Multi-buyer programmes** - One SME supplying multiple anchor buyers, each with separate facility limits
- **Partial financing** - Finance a portion of an invoice rather than the full amount
- **Dynamic discounting** - Buyer offers their own early payment discount (no financier needed)
- **Batch processing** - Multiple invoices financed in a single funding cycle
- **Credit scoring integration** - Automated risk assessment based on on-chain payment history
- **Multi-currency** - Cross-border procurement with FX conversion at funding or settlement
