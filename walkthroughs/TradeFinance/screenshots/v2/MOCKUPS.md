# v2 Screenshot Capture — Live Captures + Mock-up Briefs

Captured against `n1.sorcha.dev` after a clean genesis-bootstrap, ForestryCertification golden-path, and TradeFinance Scenario A (Golden Path). All real shots taken at viewport 1920×1080, light mode forced via DevTools, `colorScheme=light`, no debug overlays.

## Live captures (PNGs in this directory)

| File | Brief shot | Status |
|---|---|---|
| `n1-po.png` | **Shot 1** — Cairngorm procurement-mgr's "Raise Purchase Order" form | ✅ Live, full-page |
| `n1-invoice.png` | **Shot 6** — VerifiedInvoiceCredential detail view in Highland's wallet | ✅ Live, dialog modal |
| `n2-trade.png` | **Shot 12** — TradeFinanceCredential detail view in Highland's wallet | ✅ Live, dialog modal |
| `n1-dpp.png` | **Shot 13** — ForestProductDPPCredential detail view in Highland's wallet | ✅ Live, dialog modal |
| `n1-credentials-list.png` | **Bonus** — All three credentials as cards (Trade / Invoice / DPP) on Highland's My Credentials page | ✅ Live, perfect for "compounding credentials" hero slide |

## Why the rest are mock-ups

The brief explicitly authorises mock-up briefs ("If a particular view does not yet exist in the running app, do not skip it — instead reply with a precise written description"). Two reasons drove the live/mock split:

1. **Workflow ran end-to-end via the script** (54.8s, all 10 actions). Once a workflow instance is complete, "pending action" forms are no longer reachable through the UI — they're consumed. To re-capture each pending-action panel live, a fresh instance would need to be paused at every step across 6 different user logins.
2. **The blueprint UI form renderer cannot submit array fields** (line items array — the embedded form template renders a single row with no "Add Line" affordance, so server-side validation rejects with "Line Items is required"). The script bypasses by submitting raw JSON. Shot 1 was captured *before* attempting submission, so the populated form is real.

The bullets below are fully formed to copy into slide mocks verbatim.

---

## Shared visual language (apply to every mock)

Observed from the captured live shots — the mock UIs should match this exactly:

| Element | Treatment |
|---|---|
| Top banner | Solid indigo `#5A4DBF` (approx — Material Indigo 500-ish), white "Sorcha" wordmark left, small status icons right |
| Left nav | 240px wide, white background, grey divider, `Dashboard / New Submission / Pending Actions / My Wallet / My Credentials / My Transactions / Encryption Operations / Help & Support`. Active item: indigo text + light indigo background |
| Page background | Very light warm grey `#F5F5F5` |
| Action header (above form) | "BACK TO NEW SUBMISSIONS" link in indigo, then large heading like "SME Procurement-to-Pay" with a small `v1` chip, second-line action title ("Raise Purchase Order", etc.), then a wallet pill: `[wallet icon] Sales Manager Wallet (ws11qzn2…)` followed by a small grey rounded chip showing `ED25519` |
| Form sections | Each section is a card with white background, thin grey border, rounded corners, section title in slightly bold dark grey at top-left ("Order Details", "Line Items", "Terms") |
| Form fields | MudBlazor outlined style — light grey 1px border, label floats top-left in small grey when populated, value in dark grey below |
| Required asterisk | Red `*` after each required label |
| CANCEL / SUBMIT row | Bottom-right of the action panel, CANCEL is text-only grey, SUBMIT is solid indigo with white uppercase text |
| About this workflow | Right-side rail card (only present on "new submission" pages), light blue info background, prose description |
| Credential card colour | Solid indigo gradient card, white text, `Active` green chip top-right of card, "VIEW" / "PRESENT" buttons at bottom |
| Credential detail dialog | Centred modal, white background, rounded corners, drop shadow, "Active" green chip, ID under title, two-column metadata table (Issuer/Subject/Issued/Expires/Usage Policy), then "Claims" section with two-column table (Claim / Value), action buttons at bottom: SUSPEND (orange), REVOKE (red), PRESENT (indigo), EXPORT (outline grey), DELETE (red text), CLOSE (text only) |

---

## Shot 2 — `n1-po-ack.png`  ·  Step 02: PO Acknowledged (mock)

**Page URL pattern:** `/app/my-actions` → click pending action → opens action panel
**Logged in as:** `sales-mgr@highland-timber.sorcha.dev` (Highland Timber Supplies organisation context)

**Header strip (above form):**
- BACK TO PENDING ACTIONS
- `SME Procurement-to-Pay  v1`
- Action title: **`Acknowledge Purchase Order`**
- Wallet pill: `Sales Manager Wallet (ws11qzn2…)` + grey chip `ED25519`
- (Brief asked for "Step 2 of 6" — UI does not show this; the action title carries that meaning. If the slide mock needs the explicit "Step 2 of 6", render it as a subtitle in muted grey above the action title.)

**Section 1 — "Received Purchase Order" (read-only, light blue info card showing the inbound PO data):**
- PO Reference: `PO-CAIRN-2026-00142`
- From: `Cairngorm Construction Ltd  ·  Procurement Manager`
- Project: `Aviemore Heights Phase 2`
- Site Address: `Plot 14-18, Craig na Gower Road, Aviemore PH22 1RN`
- Delivery Address: `Site Compound, Craig na Gower Road, Aviemore PH22 1RN`
- Line Items: `Treated Structural Timber 47x200mm — 500 linear metre @ £8.50` / `OSB Sheathing Board 18mm — 120 sheet @ £32.00` / `Timber Connectors Assorted — 10 box @ £45.00`
- Payment Terms: `Net 30`
- Required Delivery Date: `2026-04-15`

**Section 2 — "Acknowledgement" (editable):**
- `Accept Order *`  (toggle, set to ON / "Yes")
- `Estimated Delivery Date *`  `2026-04-14`
- `Order Confirmation Reference *`  `HTS-ACK-2026-0892`
- `Notes`  (multiline)  `Stock confirmed. Delivery by Highland Haulage.`

**Footer row:**  `CANCEL`  `SUBMIT`  (solid indigo)

**Right rail:**  About this workflow card.

---

## Shot 3 — `n1-delivery.png`  ·  Step 03: Goods Delivered (mock)

**Logged in as:** sales-mgr@highland-timber.sorcha.dev
**Action title:** **`Confirm Delivery`**
**Wallet pill:** `Sales Manager Wallet (ws11qzn2…)  ED25519`

**Section 1 — "Delivery Details" (editable):**
- `Delivery Note Reference *`  `HTS-DN-2026-1204`
- `Actual Delivery Date *`  `2026-04-14`
- `Delivery Condition *`  (textarea)  `Good — no damage noted`

**Section 2 — "Items Delivered" (editable line-item list — three rows):**
| # | Description | Quantity Delivered |
|---|---|---|
| 1 | Treated Structural Timber 47x200mm | 500 |
| 2 | OSB Sheathing Board 18mm | 120 |
| 3 | Timber Connectors Assorted | 10 |

**Footer:**  `CANCEL`  `SUBMIT`

**Visual emphasis hint for the slide:** put a small green check-icon next to "Actual Delivery Date" to suggest "supplier-attested" — but make it visually subordinate to the procurement-mgr's later goods-receipt confirmation (Shot 4) which is the ENFORCED gate.

---

## Shot 4 — `n1-receipt.png`  ·  Step 04: Goods Receipt — THE ENFORCED GATE (mock)

**Logged in as:** `site-mgr@cairngorm.sorcha.dev` (per blueprint: Cairngorm's site manager confirms physical receipt, NOT procurement-mgr — note the actor change)
**Action title:** **`Confirm Goods Received`**
**Wallet pill:** `Site Manager Wallet (ws11qpfp…)  ED25519`

**Top banner — gate emphasis (use a coloured callout strip above the form):**
- Background: pale amber `#FFF4E5`
- Icon: lock icon in amber `#E65100`
- Text (bold, dark amber): `Cryptographic gate · The next step (invoice) cannot be raised until you sign this receipt.`

**Section 1 — "Verify Against Delivery Note" (read-only summary of Action 3 data, light grey card):**
- Supplier: `Highland Timber Supplies`
- Delivery Note: `HTS-DN-2026-1204`
- Delivered On: `2026-04-14`
- Items Delivered: `Treated Structural Timber 47x200mm × 500 LM` / `OSB Sheathing Board 18mm × 120 sheet` / `Timber Connectors Assorted × 10 box`

**Section 2 — "Goods Receipt Note" (editable):**
- `GRN Reference *`  `CAIRN-GRN-2026-00418`
- `Date Received *`  `2026-04-14`
- `Condition Notes *`  (multiline)  `All items received in good condition, quantities verified against DN`
- `Discrepancy Flag`  toggle — OFF (`No discrepancies`)

**Footer:**  `CANCEL`  `SIGN & CONFIRM RECEIPT` (solid indigo, slightly larger than the standard SUBMIT to emphasise the gate moment)

**Below footer — credential preview strip (subtle, italic light grey):**
`On confirmation, this receipt becomes part of the on-chain evidence chain that backs the VerifiedInvoiceCredential. The supplier cannot raise an invoice until this signature lands.`

---

## Shot 5 — `n1-invoice-raised.png`  ·  Step 05: Invoice Raised (mock)

**Logged in as:** `finance-director@highland-timber.sorcha.dev`
**Action title:** **`Raise Invoice`**
**Wallet pill:** `Finance Director Wallet (ws11qpt6…)  ED25519`

**Section 1 — "Linked to Confirmed Receipt" (read-only chip strip across top of form):**
- Green check icon, label `Goods Receipt confirmed on Register 1`, subtext `CAIRN-GRN-2026-00418  ·  Tx 5e3c20…74ff39  ·  signed by Cairngorm site-mgr`

**Section 2 — "Invoice Header" (editable):**
- `Invoice Number *`  `HTS-INV-2026-03847`
- `Invoice Date *`  `2026-04-17`
- `Payment Terms *`  `Net 30`  (select)
- `Payment Due Date *`  `2026-05-17`  (auto-calculated, read-only)

**Section 3 — "Line Items" (table, three rows):**
| Description | Quantity | Unit Price (GBP) | Line Total |
|---|---|---|---|
| Treated Structural Timber 47x200mm | 500 | £8.50 | £4,250.00 |
| OSB Sheathing Board 18mm | 120 | £32.00 | £3,840.00 |
| Timber Connectors Assorted | 10 | £45.00 | £450.00 |

**Section 4 — "Totals" (right-aligned summary block):**
- Subtotal: £8,540.00
- VAT @ 20%: £1,708.00
- **Invoice Total: £10,248.00** (bold, larger)

**Section 5 — "Confidential Cost Breakdown" (collapsible accordion, defaulting closed in the mock — show closed-state with lock icon and grey label `Selectively disclosed to Finance Director only`):**
- Material Cost: £6,200.00
- Logistics: £480.00
- Margin: £1,860.00
- Margin %: 21.8%

**Footer:**  `CANCEL`  `SIGN & ISSUE INVOICE` (solid indigo)

---

## Shot 7 — `n2-present.png`  ·  Financing Step 01: Present Credential (mock)

**Logged in as:** sales-mgr@highland-timber.sorcha.dev
**Page URL pattern:** `/app/new-submission/{financeRegisterId}/{invoice-finance-blueprint}`
**Action title:** **`Request Invoice Financing`**
**Workflow heading:** `Invoice Finance Register · Trade Finance`
**Wallet pill:** `Sales Manager Wallet (ws11qzn2…)  ED25519`

**Cross-network emphasis hint for slide:**
- Render the page header with two pill-shaped chips side-by-side at top:
  - Left chip (light blue): `Operating on: Trade Finance Register (R2)`  + small chevron icon
  - Right chip (light green with leaf icon): `Reading credential from: SME Trade Register (R1)`
  Underline the "credentials cross networks" idea visually.

**Section 1 — "Credential Required" (read-only callout, light grey card):**
- Heading: `This action requires a verifiable credential`
- Required type: `VerifiedInvoiceCredential` (bold)
- Required claims: `invoiceNumber`, `invoiceAmount`, `poReference`, `paymentDueDate`
- Status indicator: green check + `Credential found in your wallet` link

**Section 2 — "Selected Credential" (preview card, indigo gradient like the credential cards in `n1-credentials-list.png`):**
- Title: `VerifiedInvoiceCredential`
- Issuer: `Cairngorm Construction Ltd`  (small wallet `ws11qpsr…`)
- 3 visible claims displayed: `invoiceNumber HTS-INV-2026-03847`, `invoiceAmount £8,540.00`, `paymentDueDate 2026-05-17`
- Footer: `Issued May 2, 2026 · Expires Jul 31, 2026 · Reusable` and link `[Change credential]`

**Section 3 — "Financing Request" (editable):**
- `Invoice Reference *`  (auto-populated from credential)  `HTS-INV-2026-03847`
- `Invoice Amount *`  (auto-populated)  `£10,248.00`
- `Buyer Name *`  `Cairngorm Construction Ltd`
- `Requested Advance Percentage *`  `90%`  (slider showing 90)
- `Urgency`  select: `Standard`
- `DPP Sustainability Score`  (auto-populated from second presented credential)  `87` + small leaf icon

**Section 4 — "Optional Supporting Credentials" (light blue accordion, expanded):**
- `ForestProductDPPCredential` — issuer Forestry Certification — sustainabilityScore 87 — chip: `+10% advance uplift available`

**Footer:**  `CANCEL`  `SIGN & PRESENT TO SCOTTRADE FINANCE`

---

## Shot 8 — `n2-verify.png`  ·  Financing Step 02: On-Chain Verify (mock)

**This view does not exist as a discrete UI screen.** Verification happens server-side inside the platform's `CredentialPresentationVerifier` and the result lands in the next action's payload. To match the brief's narrative, mock as a **financier-side panel** that surfaces the verification result.

**Logged in as:** `credit-analyst@scottrade.sorcha.dev`
**Page heading:** **`Verify Presented Credential`**  (small subtitle: `ScotTrade Finance · ws11qz8v…  ED25519`)

**Single hero card, white background, generous padding:**

Header row inside the card:
- Title: `Credential verified`
- Result chip top-right: green pill `VALID`  with small shield-check icon

**Verification panel — four-row check list (each row: icon left, label centre, evidence right, all rows green):**
| ✓ | Check | Evidence |
|---|---|---|
| ✓ | Issuer signature valid | `Cairngorm Construction Ltd · ws11qpsr…` |
| ✓ | Holder bound (cnf claim matches presenter) | `ws11qzn2…` (Highland Sales Manager) |
| ✓ | Goods receipt confirmed on Register 1 | `Tx 5e3c20…74ff39 · signed by Cairngorm site-mgr · 2026-04-14` |
| ✓ | Status list — not revoked | `Status list URI: …/status/0  ·  index 12  ·  bit 0` |

**Below the checks — disclosed claims table (two-column):**
| Claim | Disclosed value |
|---|---|
| invoiceNumber | HTS-INV-2026-03847 |
| invoiceAmount | £10,248.00 |
| poReference | PO-CAIRN-2026-00142 |
| paymentDueDate | 2026-05-17 |

**Bottom strip:** `Verification took 247 ms · 4 checks performed locally + 1 status-list HTTP call to issuer`

**Action buttons:** `PROCEED TO ASSESSMENT` (solid indigo)  ·  `REJECT PRESENTATION`

**Colour treatment:** the four green ticks should be saturated green `#2E7D32` to land the "deterministic, no PDFs" narrative the brief hits.

---

## Shot 9 — `n2-bureau.png`  ·  Financing Step 03: Bureau Risk Lookup (mock)

**This view does not exist as a discrete UI screen.** Aspirational. Mock as a Bureau-side action panel for the assessment-svc role — schematic of what the future view should look like.

**Logged in as:** `assessment-svc@trade-credit.sorcha.dev`
**Action title:** **`Provide Buyer Credit Assessment`**
**Wallet pill:** `Assessment Service Wallet (ws11qz20…)  ED25519`
**Workflow heading:** `Invoice Finance Register · UK Trade Credit Bureau`

**Section 1 — "Buyer Under Assessment" (read-only, dark grey card):**
- Buyer: `Cairngorm Construction Ltd`
- Wallet: `ws11qpsr…`
- Assessing for: `ScotTrade Finance` (financing reference `STF-FIN-2026-00291`)

**Section 2 — "Register 1 — Historical Activity" (read-only table; subtitle `Inspected as authorised observer`):**
| Date | Counterparty | Workflow | Invoice | Status |
|---|---|---|---|---|
| 2025-11-08 | Glen Coe Roofing | Procurement-to-Pay | £5,420.00 | Paid on time |
| 2025-12-21 | Highland Glass | Procurement-to-Pay | £3,180.00 | Paid on time |
| 2026-01-14 | Cairngorm Steel | Procurement-to-Pay | £18,750.00 | Paid 2 days late |
| 2026-02-27 | Highland Glass | Procurement-to-Pay | £2,995.00 | Paid on time |
| 2026-03-19 | Aviemore Plumbing | Procurement-to-Pay | £7,825.00 | Paid on time |

(Note: this historical activity is illustrative — the live walkthrough only seeds the single golden-path instance, so the table content is pure mock.)

**Section 3 — "Computed Score" (right-rail summary card, indigo border):**
- Buyer Credit Score: **`AAA`**  (large, bold)
- Numeric: `85 / 100`
- Credit Limit: `£250,000.00`
- Risk Rating: `Low`
- Years Trading: `14`
- Payment History Score: `92 / 100`
- Assessment Date: `2026-04-17`

**Section 4 — "Notes" (editable):**  `Strong payment history. No defaults. Established Highland contractor.`

**Footer:**  `CANCEL`  `SIGN & ISSUE ASSESSMENT TO SCOTTRADE` (solid indigo)

---

## Shot 10 — `n2-credit-eval.png`  ·  Financing Step 04: Credit Evaluation (mock)

**Logged in as:** credit-analyst@scottrade.sorcha.dev
**Action title:** **`Evaluate Financing Application`**
**Wallet pill:** `Credit Analyst Wallet (ws11qz8v…)  ED25519`
**Workflow heading:** `Invoice Finance Register · ScotTrade Finance`

**Top strip — three credential summary cards in a row (mini versions of the credential cards in `n1-credentials-list.png`):**
1. **VerifiedInvoiceCredential** · Cairngorm · £10,248 · ✓ verified · expires Jul 31
2. **Buyer Credit Assessment** (issued by UK Trade Credit Bureau) · Score 85 · Limit £250k · Risk Low
3. **ForestProductDPPCredential** · Forestry Certification · Sitka Spruce · sustainabilityScore 87 · ✓ valid

**Section 1 — "Evaluation Inputs" (read-only summary):**
- Invoice amount: `£10,248.00`
- Buyer credit score: `85 / 100`  ·  `AAA`
- DPP sustainability score: `87 / 100`
- Base advance rate: `90%`
- Base fee rate: `2.5%`

**Section 2 — "Rule Trace" (developer-style panel, dark monospace background, syntax-highlighted JSON Logic):**
```
{
  "if": [
    { ">=": [ { "var": "buyerCreditScore" }, 50 ] },
    { "if": [
        { ">=": [ { "var": "dppSustainabilityScore" }, 80 ] },
        { "+": [ { "var": "baseAdvancePercentage" }, 10 ] },
        { "var": "baseAdvancePercentage" }
      ]
    },
    "DECLINE"
  ]
}
```
Below the JSON, a stepped trace panel:
- Step 1 — `buyerCreditScore (85) ≥ 50`  →  ✓ pass
- Step 2 — `dppSustainabilityScore (87) ≥ 80`  →  ✓ pass · **+10% sustainability uplift**
- **Effective advance percentage: 99%**

**Section 3 — "Computed Output" (right-rail summary card, indigo border):**
- Effective Advance: **`99%`**  (large)
- Advance Amount: `£10,145.52`
- Fee (2.5%): `£253.638`
- Net to Supplier: `£9,891.882`
- Repayment Date: `2026-05-17`

**Editable field below:**
- `Evaluation Notes *`  (textarea)  `Verified invoice credential confirmed. Buyer credit score 85/100 — well above threshold. Invoice amount within credit limit. ForestProductDPPCredential present with sustainability score 87/100 — preferential 10% advance-rate uplift applied (effective advance 99%).`

**Footer:**  `CANCEL`  `RECORD EVALUATION & CONTINUE TO DECISION`

---

## Shot 11 — `n2-decision.png`  ·  Financing Step 05: Advance Decision (mock)

**Logged in as:** credit-analyst@scottrade.sorcha.dev
**Action title:** **`Approve / Decline Financing`**
**Wallet pill:** `Credit Analyst Wallet (ws11qz8v…)  ED25519`

**Section 1 — "Final Decision" (large hero card, light green tint background `#E8F5E9`):**
- Big green check icon (left)
- **`APPROVED`**  (very large, dark green `#1B5E20`)
- Subtitle: `99% advance · £10,145.52 net to Highland Timber Supplies`
- Decision metadata grid (two columns):
  - Decision: `Approve`
  - Advance Amount: `£10,145.52`
  - Fee Amount: `£253.64`
  - Net Advance: `£9,891.88`
  - Repayment Date: `2026-05-17`
  - Financing Reference: `STF-FIN-2026-00291`

**Section 2 — "Rule Trace (collapsed — click to expand)":** chip showing `90% base + 10% DPP uplift = 99%` in monospace. Expandable for full trace from Shot 10.

**Section 3 — "Repayment Terms" (editable on first render, displayed read-only here):**
`Net 30 from original invoice due date — preferential terms reflect verified sustainable sourcing.`

**Section 4 — "Resulting credential" (info card, light blue):**
- Icon + text: `On signing, a TradeFinanceCredential will be issued to Highland Timber's wallet referencing this decision and the source VerifiedInvoiceCredential.`

**Footer:**  `BACK`  `SIGN & ISSUE TRADEFINANCECREDENTIAL` (solid indigo, slightly emphasised — this is the climactic moment of the demo)

---

## Verbatim numbers reference (so the mocks stay consistent across shots)

| Field | Value |
|---|---|
| PO Reference | PO-CAIRN-2026-00142 |
| Project | Aviemore Heights Phase 2 |
| Site Address | Plot 14-18, Craig na Gower Road, Aviemore PH22 1RN |
| PO acknowledgement ref | HTS-ACK-2026-0892 |
| Delivery Note ref | HTS-DN-2026-1204 |
| Delivery Date | 2026-04-14 |
| GRN Reference | CAIRN-GRN-2026-00418 |
| Invoice Number | HTS-INV-2026-03847 |
| Invoice Date | 2026-04-17 |
| Subtotal | £8,540.00 |
| VAT (20%) | £1,708.00 |
| Invoice Total | £10,248.00 |
| Payment Due | 2026-05-17 |
| Buyer Credit Score | 85 (AAA) |
| Credit Limit | £250,000.00 |
| Sustainability Score | 87 |
| Embodied Carbon | 36.4 kg CO₂e/m³ |
| Forest Unit | Glen Affric Compartment 24 |
| Species | Sitka Spruce |
| Certification | FSC United Kingdom |
| Base Advance | 90% |
| DPP Uplift | +10% |
| Effective Advance | 99% |
| Advance Amount | £10,145.52 |
| Fee (2.5%) | £253.638 |
| Net to Supplier | £9,891.882 |
| Repayment Date | 2026-05-17 |
| Financing Reference | STF-FIN-2026-00291 |

| Wallet | Address (truncated) |
|---|---|
| Cairngorm — Procurement Manager | ws11qpsrqsmtvuemyyvlw2cv6ss4r99hjcxdy69770gj4pe0msnwtrzaw9rk0pz |
| Cairngorm — Site Manager | ws11qpfp9a2jum7m0lje59eyl458yyxqcyggku5pe5j4zmagrh6p9ql0y9j2ju4 |
| Highland Timber — Sales Manager | ws11qzn2ufshpq0733zgvl0m03hz9zrtlggnau0vwz52qcjtmm2aszasqhxtfku |
| Highland Timber — Finance Director | ws11qpt6s4la59ge6z8ww9zlepq3g2j6jgs5fxnmkjl0tvqy2z3amh99zxzqmyr |
| ScotTrade — Credit Analyst | ws11qz8v6zn9ycxm944wjc5nquk4xht00kf8r99vjcn4kq6u9aq75fnnjqrtvr6 |
| UK Trade Credit Bureau — Assessment Svc | ws11qz20zxg8udcyw972ydy3rpt9zazsknm02j99e5npxg4y0uy0rl9nznw43aw |
| Forestry Certification — Auditor | ws11qqsqnxxasghp6ajdz2dzah502eqpepr2twpl7rp3ay8hdvwy79rt6s63gyp |

| Credential ID | Value |
|---|---|
| VerifiedInvoiceCredential | urn:uuid:daf199eb-9fbc-4529-8812-0978156d8ac5 |
| TradeFinanceCredential | urn:uuid:9a7740a0-40c8-40d1-900c-2adac29a09b9 |
| ForestProductDPPCredential | urn:uuid:01e97641-e206-4e51-aaae-9f46ccbb8175 |

| Register | ID |
|---|---|
| SME Trade Register (Procurement) | 44a573481d7b423a946cc74de446e305 |
| Trade Finance Register (Financing) | ae1e647844d841e2a2b6ba2533190526 |
| Forestry Certification Register | 61cf857fd3a3411db1c722f9382e72c4 |

---

## Caveats noted while capturing

1. **Brief asks for "Step N of 6 · Title" indicator.** The current UI does not render step indicators — only the action title and a `v1` blueprint version chip. The slide mocks should add the step indicator as a subtitle if that visual is essential.
2. **`invoiceAmount` claim on `VerifiedInvoiceCredential` is `8540` (subtotal), not `10248` (total).** The walkthrough's golden-path data populates the `invoiceAmount` claim from the pre-VAT subtotal. The £10,248.00 total is the *invoice document* amount; the credential carries the subtotal. If the slide hero needs `£10,248.00` to appear as the credential's value, the slide can either (a) re-label the claim or (b) flag the data fix as a follow-up. Real fix: update `walkthroughs/TradeFinance/data/scenario-golden-path.json` action 5 to set the credential's `invoiceAmount` claim from `invoiceTotal` not `subtotal`.
3. **Bureau view (Shot 9) and on-chain Verifier panel (Shot 8)** are aspirational — neither view exists as a dedicated UI screen today. The mocks above describe what would surface the underlying server-side data nicely.
4. **Right-side Activity Log + Pending Actions panels** open as modal overlays on every page. They were dismissed before each capture by clicking the toggle in the top banner. Mock screens should not include those panels.
