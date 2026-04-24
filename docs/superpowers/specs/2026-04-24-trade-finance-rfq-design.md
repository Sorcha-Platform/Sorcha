# TradeFinance RFQ — Risk-Priced Credentials Marketplace

**Date:** 2026-04-24
**Status:** Design proposed; pending user review and downstream speckit specification.
**Type:** Walkthrough / capability demo extending the existing TradeFinance walkthrough.

---

## 1. Intent

Extend the existing `walkthroughs/TradeFinance/` scenario into a **competitive sealed-bid RFQ for invoice financing**, where a supplier's stack of attested credentials measurably lowers the rate two competing lenders quote. The walkthrough demonstrates Sorcha's unique ability to support a **market for risk-priced proof**: pseudonymous bidding, per-recipient bid encryption, selective credential disclosure, and reveal-on-win — all enforced at the protocol layer, with no trusted intermediary.

The 30-second pitch:

> A supplier with five attested credentials runs a 10-minute sealed-bid auction from a burner wallet. Two lenders with different risk curves bid competitively. The supplier picks the better rate and reveals their identity only to the winner. Nothing in this flow required a trusted intermediary — the protocol enforces every privacy and fairness property.

This is an **implementation-capability demo**, not a production-ready trade finance system. Realism gaps are called out in §5.

---

## 2. Narrative arc

### Act 1 — Supply + evidence (mostly existing flow)

The existing procurement-to-pay flow runs unchanged: Cairngorm Construction raises a PO with Highland Timber Supplies, Highland Timber delivers and invoices, Cairngorm's procurement manager approves, and a `VerifiedInvoiceCredential` is issued to Highland Timber's wallet.

The only Act 1 change is **credential pre-staging**: Highland Timber's wallet also already holds four pre-issued credentials from the new **Credentials Registry** — DPP for the timber batch, KYB-verified-business attestation, Trade Credit Insurance covering Cairngorm as buyer, and a Carbon-Tier "A" attestation. In the demo these are issued during `setup.ps1`; in production they would have accumulated over months from independent issuers.

By end of Act 1: **Highland Timber holds 5 credentials.** Cairngorm is identified throughout. No lenders involved yet.

### Act 2 — Pseudonymous RFQ

Highland Timber's finance director derives a **one-time pseudonym wallet** (e.g. `rfq-2026-0491`) from Highland Timber's HD master via the existing Org Key Derivation (Feature 083) — a fresh derivation path, no new crypto.

The pseudonym wallet opens an RFQ on the Trade Finance Register, presenting the 5 credentials with **selective disclosure** via SD-JWT — only claims the lenders' rate cards consume are revealed. The RFQ carries a **10-minute sealed-bid window**, enforced via the existing Timebound Presentation Lifecycle (Feature 111).

Both lenders — **ScotTrade Finance** (traditionalist) and **Highland Green Capital** (new, green-focused fund) — see the same RFQ and the same disclosed claims from the pseudonym. Neither sees the supplier's real organisation identity. Each lender's pricing service applies its own curve, builds a quote, **encrypts the quote to the pseudonym wallet via per-recipient FLE**, and submits via a `SubmitBid` action.

By end of Act 2: **the supplier holds two sealed bids.** The lenders cannot see each other's bids. Nobody outside the register sees commercially-sensitive payloads.

### Act 3 — Accept + reveal

The supplier decrypts both bids using the pseudonym key, picks the better one (in worked example: Highland Green at 2.70% vs. ScotTrade at 3.10%), and submits an `AcceptQuote` transaction binding to the chosen bid.

The supplier then submits a `RevealIdentity` transaction **whose payload is FLE-encrypted to the winning lender only**. The payload contains a signed proof linking `rfq-2026-0491` to `did:sorcha:org:highland-timber-supplies`. Highland Green decrypts, performs final KYC tie-out against the now-revealed organisation, and proceeds with financing through the existing `invoice-finance` blueprint.

ScotTrade learns only that the RFQ closed and it did not win — no terms, no supplier identity, no buyer disclosure beyond what was visible in Act 2.

---

## 3. Architecture

### 3.1 Registers (3 total)

| Register | Owner | Status | Contents |
|---|---|---|---|
| **Credentials Registry** | Highland Timber Federation *(new org)* | New | DPP, Carbon-Tier credential issuances. KYB and Credit Insurance credential issuances issued by UK Trade Credit Bureau here too. |
| **SME Trade Register** | Cairngorm Construction Ltd | Existing | Procurement-to-pay; `VerifiedInvoiceCredential` issuance. |
| **Trade Finance Register** | ScotTrade Finance | Existing, extended | RFQ lifecycle, bids, acceptance, reveal, then existing invoice-finance flow. |

Trade Finance Register stays in DevMode (public payloads) for demo clarity. Actor and bid privacy are achieved via pseudonym + per-recipient FLE, not register-level access control. Flipping to private/invitation-only is a clean future upgrade.

### 3.2 Organisations (6 total — 2 new, 1 role-expanded)

| Org | Subdomain | Role | Theme |
|---|---|---|---|
| Cairngorm Construction Ltd | `cairngorm` | Buyer | Highlands |
| Highland Timber Supplies | `highland-timber` | Supplier | Highlands |
| **Highland Timber Federation** *(new)* | `highland-federation` | Industry-credential issuer (DPP, Carbon-Tier) | Highlands |
| UK Trade Credit Bureau *(role expanded)* | `trade-credit` | Credit assessment **+ KYB and Credit Insurance credential issuance** | UK |
| ScotTrade Finance | `scottrade` | Lender (traditional curve) | Scottish |
| **Highland Green Capital** *(new)* | `highland-green` | Lender (green-focused curve) | Highlands |

**Realism note:** in production, KYB and Credit Insurance would come from separate bodies (Companies House, Atradius). UK Trade Credit Bureau is bundling both for walkthrough simplicity — flagged here and in setup script docs.

### 3.3 New participants (org-internal)

| Org | Participants |
|---|---|
| Highland Timber Federation | `issuer-admin@highland-federation.sorcha.dev` (issues DPP + Carbon-Tier) |
| UK Trade Credit Bureau | adds `kyb-issuer@trade-credit.sorcha.dev` (issues KYB + Credit Insurance) on top of existing `assessment-svc` |
| Highland Green Capital | `credit-analyst@highland-green.sorcha.dev` |

### 3.4 New blueprints (5)

On **Credentials Registry** (4 single-action issuance blueprints, each ~1 action):

| Blueprint | Issuer | Issued credential |
|---|---|---|
| `issue-dpp` | Highland Timber Federation | DPP for a timber batch (uses existing `product-passport.json` credential schema) |
| `issue-carbon-tier` | Highland Timber Federation | Carbon-Tier credential (`carbonTier`: A/B/C/D, `greenLoanEligible`: bool) |
| `issue-kyb` | UK Trade Credit Bureau | Verified-Business credential (Companies House registration, VAT status, directors KYC tier) |
| `issue-credit-insurance` | UK Trade Credit Bureau | Trade Credit Insurance cover (named buyer, cover amount, expiry) |

On **Trade Finance Register** (1 new RFQ blueprint, ~5 actions):

| Action | Sender | Purpose |
|---|---|---|
| 1. `OpenRFQ` | Pseudonym wallet (open participant — late-bound) | Posts RFQ; presents 5 selectively-disclosed credentials; opens 10-minute bid window |
| 2. `SubmitBid` | ScotTrade credit-analyst | FLE-encrypted quote to pseudonym wallet |
| 3. `SubmitBid` | Highland Green credit-analyst | FLE-encrypted quote to pseudonym wallet |
| 4. `AcceptQuote` | Pseudonym wallet | Binds to chosen bid; emits `PresentationOutcome(success)` |
| 5. `RevealIdentity` | Pseudonym wallet | Signed link to real org, FLE-encrypted to winning lender only |

Existing `invoice-finance` blueprint runs after `RevealIdentity`, with the winning lender as the financier.

The `OpenRFQ` action uses the **open participant** pattern (Feature 103): `IsStartingAction = true`, `Sender.WalletAddress = null`, late-bound at runtime. This is what lets the pseudonym wallet — which is not pre-baked into the blueprint — become the bound applicant on first submission.

### 3.5 Bid lifecycle wiring (Feature 111 reuse)

The RFQ blueprint is the first non-HAIP consumer of `IPresentationConsumer`:

- `OpenRFQ` invokes `IPresentationLifecycleService.InitiateAsync` → writes `PresentationInitiated` with 10-minute TTL.
- Each `SubmitBid` is a `PresentationOutcome(kind=success)` carrying the encrypted bid payload.
- `AcceptQuote` writes a final outcome record selecting the winning bid.
- If no bids submitted within 10 minutes → `PresentationAbandoned` (requires `presentationConfig.recordAbandonment=true`).

A new consumer, `RfqBidConsumer` (`ConsumerName = "rfq-bid"`), implements `IPresentationConsumer` in a new `Sorcha.Blueprint.Service.Rfq` namespace. It validates incoming bids (well-formed, within window, signed by a roster lender) and returns the appropriate outcome. **No new lifecycle machinery** — the consumer-agnostic guard (Feature 111 US5) confirms this primitive is ready for exactly this kind of use.

### 3.6 Pricing engine — lender-side services

Each lender runs a small pricing service (a script alongside `run.ps1` in the demo, a real service in production):

1. Subscribes to Trade Finance Register events.
2. On `PresentationInitiated` (RFQ open), reads the supplier's selectively-disclosed claims from the presentation.
3. Applies its **private curve** to produce a quote: `{rate, advancePct, fee, expiry}`.
4. FLE-encrypts the quote to the pseudonym wallet.
5. Submits via the `SubmitBid` action with the lender's credit-analyst wallet.

The curve is private to each lender. Only the **rate card** (the public discount weights) is shipped at setup — the actual computation is opaque.

### 3.7 Rate card publication

Each lender publishes a `RateCard` artifact on the Trade Finance Register at setup — a signed, readable JSON document. Indicative only: a supplier can self-compute expected pricing before opening an RFQ, but the binding number comes from the lender's service at bid time.

For the demo, rate cards are published once and remain static. In production they would rotate as lender appetite shifts.

### 3.8 Pseudonym wallet + reveal primitive

- **Pseudonym wallet:** a fresh wallet derived from Highland Timber's HD master under a one-time derivation path via Feature 083 Org Key Derivation. No new crypto, no new key material.
- **`RevealIdentity` action:** the pseudonym wallet signs a payload `{realDid: "did:sorcha:org:highland-timber-supplies", proof: <signature>}` and the action's payload is FLE-encrypted to the winning lender's wallet only. ScotTrade's recipient slot in the FLE Challenges block is omitted, so its service decrypts to nothing.

---

## 4. Privacy mapping (target level: L2 — pseudonymous bidding, reveal-on-win)

### 4.1 What each party sees at each step

| Step | Highland Timber (real org) | Pseudonym `rfq-2026-0491` | ScotTrade | Highland Green | Cairngorm | Public observer |
|---|---|---|---|---|---|---|
| Pre-RFQ: credentials issued | Holds 5 VCs | — | — | — | Identified as PO buyer | Sees credential issuances on Credentials Registry (issuer + recipient wallet, no claim values) |
| Open RFQ | Knows pseudonym is theirs | Posts RFQ + selectively-disclosed claims | Sees RFQ + claims | Sees RFQ + claims | Not subscribed | Pseudonymous tx on Trade Finance Register |
| Bid window | Watches clock | Inbox | Computes via own curve, FLE-encrypts bid, submits | Same, independently | — | — |
| Both bids submitted | — | Holds two encrypted bid txs | Knows it bid; can't read Highland Green's | Knows it bid; can't read ScotTrade's | — | Sees two `SubmitBid` txs; payloads opaque |
| Accept | Decrypts both, picks Highland Green | Submits `AcceptQuote(highland-green-bid-tx-id)` | Sees: "RFQ closed, you did not win." No rate. No identity. | Sees: "you won." Still doesn't know real supplier identity. | — | Sees `AcceptQuote` tx |
| Reveal | Pseudonym signs link to real org | Reveals link encrypted to Highland Green only | Cannot read `RevealIdentity` payload | Decrypts: now knows real supplier; performs KYC; proceeds with financing | — | Sees `RevealIdentity` tx; payload opaque |

### 4.2 Privacy invariants

- **Losing lender** never learns: supplier's real identity, winning rate, winning advance %, winning fee.
- **Winning lender** does not learn: the losing bid's terms.
- **Public observer** (anyone reading the register) can see RFQ and bid transactions exist but cannot read commercially-sensitive payloads.
- **Buyer (Cairngorm)** is identified throughout — deliberate. The lender needs to assess buyer credit, and the buyer is not the privacy subject.

### 4.3 Out of scope: ZK predicate proofs

Sorcha today does not implement zero-knowledge predicate proofs (no BBS+, no bulletproofs, no range proofs). Selective disclosure via SD-JWT is the only mechanism available, which means a credential cannot prove "carbon < threshold" without revealing the exact carbon value.

The walkthrough works around this with **pre-banded credentials**: the ESG auditor (Highland Timber Federation) computes the tier privately and issues a credential whose only carbon-related claim is `carbonTier: "A"`. The supplier discloses the tier; the exact value never exists in the credential.

Trade-offs are honest:
- ✓ Works with shipped Sorcha crypto.
- ✓ Mirrors real-world auditor behaviour — auditors *do* tier-bin in practice.
- ✗ Requires trust in the auditor to bucket honestly (same trust extended for issuing the cert at all).
- ✗ Different lenders cannot apply different thresholds without the auditor including multiple band claims.

ZK predicate proofs are a research direction tracked separately.

---

## 5. The two pricing curves

### 5.1 ScotTrade Finance — traditional risk-averse lender

```
base_rate:           4.20%
advance_pct_default: 85
discounts:
  kyb_verified:                -0.30%
  credit_insurance_in_force:   -0.50%   ← biggest single discount
  dpp_present:                 -0.20%
  carbon_tier_A:               -0.10%
  carbon_tier_B:               -0.05%
  carbon_tier_C_or_below:       0.00%
fee_pct:             0.40% of advance
```

### 5.2 Highland Green Capital — green-mandated fund

```
base_rate:           4.80%       ← higher base
advance_pct_default: 90          ← more advance
discounts:
  kyb_verified:                -0.20%
  credit_insurance_in_force:   -0.20%
  dpp_present:                 -0.80%   ← 4× ScotTrade
  carbon_tier_A:               -0.90%   ← 9× ScotTrade
  carbon_tier_B:               -0.40%
  carbon_tier_C_or_below:       0.00%   ← no bid (LP mandate prohibits below tier B)
fee_pct:             0.30% of advance
```

### 5.3 Worked example — Highland Timber's bundle (KYB ✓, insurance ✓, DPP ✓, Carbon Tier A)

| | ScotTrade | Highland Green |
|---|---|---|
| Base | 4.20% | 4.80% |
| KYB | -0.30 | -0.20 |
| Insurance | -0.50 | -0.20 |
| DPP | -0.20 | -0.80 |
| Carbon Tier A | -0.10 | -0.90 |
| **Final rate** | **3.10%** | **2.70%** |
| Advance % | 85% | 90% |
| Fee | 0.40% | 0.30% |

Highland Timber wins by going green: Highland Green pays out **higher advance at lower rate at lower fee** because the supplier presents full ESG paperwork. A weaker supplier (KYB ✓ but no DPP, Carbon Tier C) would get **3.40% from ScotTrade** and **no bid at all** from Highland Green. The market rewards proof.

---

## 6. Scope boundaries

### 6.1 In scope (this walkthrough)

- One new register: Credentials Registry.
- One new org: Highland Timber Federation.
- Existing org expanded: UK Trade Credit Bureau (issues KYB + Credit Insurance credentials in addition to existing assessment role).
- One new lender: Highland Green Capital with its own pricing service and rate card.
- 4 new credential issuance blueprints on Credentials Registry.
- 1 new RFQ blueprint on Trade Finance Register: 5 actions (`OpenRFQ` → `SubmitBid` ×2 → `AcceptQuote` → `RevealIdentity`).
- New `RfqBidConsumer` implementing `IPresentationConsumer`.
- Pseudonymous bidding wallet via existing Org Key Derivation.
- Selective disclosure via existing SD-JWT.
- FLE-encrypted bids per existing per-recipient FLE pattern.
- Bid lifecycle on Feature 111 (Timebound Presentation Lifecycle).
- Two static rate cards published once at setup.
- Demo run mode: `setup.ps1` pre-stages 5 credentials in Highland Timber's wallet; `run.ps1` executes Acts 2 + 3.
- Side-by-side bid render at end of walkthrough.

### 6.2 Deferred / out of scope

| Deferred | Why deferred |
|---|---|
| ZK predicate proofs (range / threshold) | Not in Sorcha today; pre-banded credentials are adequate for demo. Researched separately. |
| Chain-of-custody depth (forester → sawmill → distributor) | Doubles setup complexity; valuable later as a phase 2. |
| Three-or-more competing lenders | Two is the smallest number that makes "market" true; three repeats the second's story. |
| Private invitation-only Trade Finance Register | Pseudonym + FLE already gives actor privacy; flip is easy if we want it later. |
| Dynamic rate-card republishing / curve rotation | Static cards are sufficient for demo; production would rotate. |
| Credit insurance payout simulation (post-default) | Story ends at financing; payout is its own walkthrough. |
| Credential revocation events affecting active RFQs | Setup-staged credentials are evergreen; revocation is Feature 079 territory. |
| Multi-RFQ from same supplier (privacy accumulation analysis) | Worth a security note in the spec; not built. |
| Secondary markets / loan resale | Out of scope. |
| FLE on Trade Finance Register itself (DevMode → FLE flip) | Existing US4 from Feature 110 already demonstrates this; this walkthrough stays DevMode for clarity. |
| HAIP / external-wallet flow for the supplier | Pseudonym is a Sorcha-internal wallet; external-wallet is a separate concern. |

### 6.3 Realism gaps to flag in setup docs

- **Bundled issuer:** UK Trade Credit Bureau issues both KYB and Credit Insurance credentials. In production these come from Companies House and Atradius respectively.
- **Two lenders only:** real RFQ markets have 5–20 participants; collapsed for narrative clarity.
- **Credentials pre-staged via setup:** real flow would have month-scale issuance lifecycles from independent issuers.
- **Pseudonym created by the same operator as the real org:** production would have stronger separation (different device, different consent journey).
- **Pricing curves static:** production would react to portfolio composition, market rates, regulatory pressure.
- **No real ESG auditor:** pre-banded carbon tier is computed at demo setup; in production an accredited auditor would issue the credential after a measurement engagement.

These gaps don't undermine the demonstration but must be visible to anyone reviewing the spec.

---

## 7. Demo "wow moment"

The walkthrough output ends with both bids decrypted and rendered side-by-side, with a countdown timer:

```
ScotTrade                        Highland Green Capital
  Base rate              4.20%      Base rate              4.80%
  KYB verified          -0.30%      KYB verified          -0.20%
  Credit insurance      -0.50%      Credit insurance      -0.20%
  DPP present           -0.20%      DPP present           -0.80%
  Carbon Tier A         -0.10%      Carbon Tier A         -0.90%
  Advance pct             85%       Advance pct             90%
  -------------------               -------------------
  FINAL RATE           3.10%        FINAL RATE           2.70%
  Expires in            4:52        Expires in            4:52
```

Followed by the acceptance log lines:

```
[OK] Accepted: Highland Green Capital @ 2.70% / 90% advance
[i]  ScotTrade: outcome unreadable — encrypted to Highland Green Capital wallet only
[OK] Identity revealed to Highland Green Capital
     Pseudonym rfq-2026-0491 → Highland Timber Supplies Ltd
[i]  ScotTrade: pseudonym remains opaque
```

That sequence is the single landing point of the walkthrough — different lenders, different curves, sealed bids, encrypted reveal.

---

## 8. References

- Existing walkthrough: `walkthroughs/TradeFinance/`
- Feature 103 — Open Participants & Late Binding: `specs/103-verified-citizen-v2/`
- Feature 083 — Org Key Derivation: `.claude/skills/sorcha-architecture/SKILL.md` § Org Key Derivation API
- Feature 111 — Timebound Presentation Lifecycle: `specs/111-presentation-lifecycle/`, `src/Common/Sorcha.PresentationLifecycle.Abstractions/`
- Feature 110 — Persona mode + DevMode → FLE transition: `walkthroughs/TradeFinance/run.ps1` (`-DisableDevMode`, `-VerifyFLE` flags)
- Existing credential schema: `blueprints/schemas/credentials/product-passport.json` (ESPR-aligned DPP)
- ZK research thread (parked): see prompt in conversation; no Sorcha source today.

---

## 9. Open questions for downstream specification

These fall to the speckit workflow:

1. Exact JSON shape of the `RateCard` artifact and `Bid` payload.
2. Whether `RfqBidConsumer` lives in `Sorcha.Blueprint.Service` or a new `Sorcha.Rfq.Service` (probably the former for v1).
3. Whether the lender pricing services run inline in `run.ps1` or as separate processes (probably inline for demo simplicity, with a note that production would split).
4. Setup script design — how to stage 5 credentials in Highland Timber's wallet idempotently.
5. UI rendering of the side-by-side bid card (terminal output vs. simple HTML report file dropped to `logs/`).
6. Test surface — which assertions belong in xUnit integration tests vs. walkthrough script smoke checks.
7. Telemetry — what metrics / log lines should the new RFQ flow emit (Feature 111 already gives us PresentationInitiated/Outcome events).
