# Trade Finance — User Storyboard

Three user lenses through the existing Trade Finance walkthrough state — captured 2026-04-25 against local Docker, with 41 transactions on the procurement register and 22 on the finance register from prior runs.

## Buyer — Cairngorm Construction Ltd

The buyer raises POs, confirms goods received, and approves invoices. They own the procurement register.

| Frame | What the buyer sees |
|---|---|
| `buyer-01-dashboard.png` | Cairngorm's home — same Sorcha shell every other org gets, scoped to their org context. |
| `buyer-02-procurement-register.png` | The SME Trade Register (Cairngorm-owned). 41 transactions across 6 procurement actions × 6+ instances. The buyer's source of truth for "what was ordered, what was delivered, what was invoiced". |
| `buyer-03-transaction-encrypted.png` | Drilling into Action 6 (Approve Invoice) on a transaction *they did not send and were not disclosed to* — payload reads "Unable to decode payload data". The FLE money shot from the buyer's seat: even on a register they own, they can't read the supplier's confidential cost breakdown. |

## Seller — Highland Timber Supplies

The seller acknowledges POs, raises invoices, and presents the verified invoice credential to a funder for financing.

| Frame | What the seller sees |
|---|---|
| `seller-01-dashboard.png` | Highland Timber's home — same shell, different context. |
| `seller-02-credentials-pending.png` | Finance Director's wallet — five `TradeFinanceCredential`s issued by ScotTrade Finance, all pending review with **Accept** / **Decline** actions. Sells the "credential is a transferable asset" story without a single line of explanation. |
| `seller-03-finance-register.png` | The Trade Finance Register (ScotTrade-owned, Highland subscribed). 22 transactions across the 4-step financing workflow × multiple invoices. Same chain, different role. |
| `seller-04-wallets.png` | Highland's two participants — Finance Director + Sales Manager — each with their own ED25519 HD wallet. The "every actor signs with their own key" frame, scoped to one org. |

## Invoice Market — ScotTrade Finance

The funder evaluates financing applications, runs them past credit assessment, and approves or declines with rate and fee terms.

| Frame | What the market sees |
|---|---|
| `market-01-dashboard.png` | ScotTrade's home. |
| `market-02-finance-register.png` | The Trade Finance Register from the funder's seat — same register Highland sees, but ScotTrade owns it and runs the workflow. |
| `market-03-transaction-encrypted.png` | Drilling into Action 4 (Approve/Decline Financing) on a transaction the ScotTrade *admin* didn't send — payload is "Unable to decode payload data". Reinforces FLE: even the org that owns the register can't read fields they weren't disclosed to. The Credit Analyst (the actual sender) can; the admin role can't. |
| `market-04-credit-analyst-wallet.png` | The Credit Analyst's HD wallet (ED25519, ws11qz...r8hv46). The wallet that signed every approve/decline transaction. |

## Pair-frames worth landing in the deck

Two pairs make the FLE / multi-org-isolation story without saying a word:

1. **buyer-03 ↔ market-03** — Cairngorm admin can't decrypt a procurement-register transaction; ScotTrade admin can't decrypt a finance-register transaction. Different orgs, different registers, same FLE behaviour.
2. **seller-02 ↔ market-04** — Highland's wallet **receiving** TradeFinanceCredentials and ScotTrade's wallet that **issued** them. One credential, two views, cryptographically linked.

## Gaps to fill before the DPP PR

- A **decoded** transaction view from the participant who *can* read it (e.g. Sales Manager logged in for a Highland-disclosed action) — pairs with the encrypted views above.
- **Action submission forms** for the headline actions: PO (buyer), Invoice (seller), Evaluate Application (market — shows the rate calculation visibly).
- **Credential detail drill-in** — open one of the pending TradeFinanceCredentials to show its claims and JWT structure. Closes the loop on `seller-02`.

## Forestry Certification (DPP) — landed

Same lens, new register at the start of the chain. Captured 2026-04-25 against n1 after Forestry Certification setup + golden-path run:

| Frame | What it shows |
|---|---|
| `seller-00-dpp-application.png` | Highland Timber Sales Manager on the **Submit Batch for Certification** form — Sitka Spruce, Glen Affric Compartment 24, 320 m³, FSC chain-of-custody evidence attached, signing wallet shown (`ws11qrtl…` ED25519). Front of the seller's DPP journey. |
| `certifier-01-audit.png` | **New 4th lens — Certifier**: Forestry Certification's auditor on the **Audit & Issue DPP** form. Decision = approve, audit findings transcribed, FSC scheme, FSC United Kingdom certifying body, audit date + DPP expiry filled. The act of issuing a DPP. |
| `seller-05-dpp-credential.png` | Sales Manager's wallet (`ws11qr…9fly70`) — Pending tab carries a `ForestProductDPPCredential` from Forestry Certification, *Pending Review*, SD-JWT, with **Decline / Accept Credential** actions. Demonstrates the credential-as-asset story without explanation. |

### Still to capture (deferred)

| Frame | Blocked on |
|---|---|
| `market-05-evaluate-uplift.png` | TradeFinance R2 changes haven't shipped — needs the rate-uplift calculator on ScotTrade's evaluate-application form. |

Pair the "before" frames in this folder with the three "after" frames above to make the rate-uplift story land visually once R2 ships.
