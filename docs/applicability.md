---
title: Where Sorcha Applies — Regulatory Pull and Worked Examples
description: The four near-term domains where programmable proof infrastructure replaces platform-asserted trust — Digital Product Passports, SME trade finance, IPC-1782 manufacturing data exchange, and municipal governance — each with a worked example from the codebase.
standards:
  - W3C Verifiable Credentials Data Model 2.0
  - OpenID4VCI
  - OpenID4VP
  - HAIP 1.0
last_updated: 2026-08-22
---

# Where Sorcha Applies — Regulatory Pull and Worked Examples

The strongest near-term pull for Sorcha is regulatory, not commercial-curiosity. Four domains have hard regulatory deadlines, hard cryptographic-evidence requirements, or both. This document names them, names the regulatory instrument, and points at the worked example in the Sorcha codebase that demonstrates the platform handling that domain end-to-end.

The frame in every case is the same: **the regulator demanded cryptographically anchored, multi-party, selectively disclosed evidence; Sorcha is what that evidence layer looks like in code.**

## 1. EU Digital Product Passports (ESPR)

**Regulatory instrument:** EU Ecodesign for Sustainable Products Regulation (ESPR), 2024/1781. Enters into force progressively across product categories. Hardest near-term deadlines: Battery Passport (February 2027) and the Iron and Steel category (during 2026). Textiles, electronics, and construction products follow.

**What ESPR mandates:** every regulated product carries a Digital Product Passport. The passport must be tamper-evident. It must aggregate data from every party in the supply chain — raw material extractor, component manufacturer, integrator, recycler. Different parties must be able to disclose different fields to different audiences (regulator, customer, insurer, recycler). Post-quantum cryptography is genuinely relevant given product lifetimes of 30 years for steel and 15+ for batteries.

**Why Sorcha fits:** the architecture maps one-to-one. The blueprint defines the workflow (raw material → component → integration → end-of-life). Each upstream party signs the data they contributed with their own wallet — no platform assertion required. The register Merkle-chains every contribution. The selective disclosure mechanism is architectural — the recycler sees the material composition, the regulator sees the compliance fields, the customer sees the carbon score, and no party sees what they were not given the key for. The post-quantum primary signing path means the cryptographic evidence is durable across product lifetime.

**Worked example in repo:** the trade finance walkthrough (`walkthroughs/TradeFinance/`) demonstrates the DPP credential gate end-to-end. A buyer presents a DPP credential at the financing stage; the workflow gate checks the credential, the supplier earns a sustainability uplift, and a financier downstream prices their offer differently. PR #475 added the DPP gate; PR #476 unified credential ownership across registers; PR #477 published n1 captures of the full uplift flow.

## 2. SME Trade Finance — Cryptographic Proof Replacing Platform-Asserted Trust

**Regulatory pull:** less direct than ESPR — but indirect pull is strong. EU Late Payments Directive (revised 2024) and the UK Procurement Act 2023 both push payment terms into supply chains. Lenders pricing receivables-financing instruments need provenance for the underlying invoice. ICC Digital Standards Initiative (DSI) is converging on verifiable-credential trade finance documents.

**The integration problem:** today an SME borrowing against a customer's accepted invoice asks a lender to trust the platform that hosts the invoice. The lender prices that platform risk into the rate. If the invoice acceptance is a *cryptographically signed* artefact, with a verifiable chain back to the buyer's wallet, the platform risk is collapsed to the cryptographic risk — which is much smaller. Rates fall.

**Why Sorcha fits:** every action in a Sorcha workflow is wallet-signed. A `VerifiedInvoiceCredential` issued at acceptance carries the buyer's wallet signature. A `KYBCredential` issued by a tenant onboarding flow carries the tenant's signature. A `DPPCredential` issued from the upstream supply chain carries every relevant supplier's. A lender consuming this stack can independently verify every signature without trusting any platform.

**Worked example in repo:** `walkthroughs/TradeFinance/` is the working demo. Feature 113 (Credential-Priced RFQ — currently parked) is the design specification for the productised version, where suppliers' credential stacks measurably lower their financing rates. The walkthrough soaks on n1 (`soak.ps1`) and runs through invoice issuance, acceptance, financing solicitation, and lender pricing — every step wallet-signed and register-recorded.

## 3. IPC-1782 Traceability and Manufacturing Data Exchange

**Standard:** IPC-1782 (Standard for Manufacturing and Supply Chain Traceability of Electronic Products). Adopted across electronics manufacturing for component-level provenance. Increasingly cited in defence procurement supply-chain requirements (US CMMC, EU defence procurement) and in critical-infrastructure components (semiconductors, telecoms equipment).

**The problem IPC-1782 surfaces:** electronic components flow through five-to-seven-tier supply chains. Each tier wants to disclose only what the next tier needs (cost data is sensitive; provenance and authenticity are not). Counterfeit-component fraud at lower tiers is a chronic problem. A regulator or end-customer chasing the provenance of a single component needs a verifiable trail back to the foundry.

**Why Sorcha fits:** the disclosure shape matches Sorcha's selective-disclosure architecture exactly. Each tier issues a credential to the next; each credential discloses only the fields the next tier needs; the chain is cryptographically continuous from foundry to end-customer. The Merkle-docket structure means a regulator can audit a slice of the chain without touching the whole register.

**Worked example in repo:** the assured-identity walkthrough (`walkthroughs/AssuredIdentity/`) demonstrates the credential-chain pattern that IPC-1782 demands. The construction permit walkthrough (`walkthroughs/ConstructionPermit/`) demonstrates the multi-tier review pattern that maps onto inspection-and-test reports. A dedicated electronics-manufacturing walkthrough is on the roadmap; the architectural pieces are all in place.

## 4. Municipal Governance and Automated Decision Audit Trails

**Regulatory instruments:** EU AI Act (high-risk systems must document data provenance for automated decisions, applicable from August 2026). UK Algorithmic Transparency Recording Standard. US state-level municipal-AI ordinances (New York, San Francisco). All converge on the same demand: when an automated system makes a consequential decision about a person, the data inputs to that decision must be traceable to a verified source.

**Why Sorcha fits:** Sorcha is by construction an audit-grade ledger of signed, timestamped, schema-validated actions. A council automated-decision system fed Sorcha-sourced data has, by construction, a complete audit trail — every input has a wallet signature, every state transition has a docket, every disclosure has a key. A regulator can pick any decision and walk the chain back to source without trusting the council, the platform, or anybody else.

**The architectural fit is sharper for municipal use cases than commercial ones** because the stakes are higher (a benefits decision, a planning approval) and the auditing party (a tribunal, an ombudsman) is structurally adversarial to the platform operator. Sorcha's evidence model is designed for adversarial verification.

**Worked example in repo:** the construction permit walkthrough (`walkthroughs/ConstructionPermit/`) demonstrates a multi-stakeholder municipal workflow — applicant, planning officer, statutory consultee, decision-maker — with every step signed and every disclosure scoped. It exercises the same patterns a benefits-allocation or licensing flow would.

## What Sorcha Is Not Optimised For

Naming the negative space honestly:

- **High-throughput stream processing.** The register is an immutable ledger, not a queue. Throughput is hundreds-to-low-thousands of transactions per second per validator quorum. This is appropriate for governance and credential workflows; it is not appropriate for clickstream telemetry.
- **Open public-blockchain analogues.** Sorcha is permissioned. Validators are known. There is no native token and no public-anyone-can-write surface. This is a feature, not a limitation — but it means Sorcha is not the right shape for use cases that require a permissionless writer set.
- **Identity provider replacement.** Sorcha integrates with identity providers via OAuth 2.0 / OIDC. It does not replace them. Citizens authenticate to GOV.UK Wallet, to EUDIW, or to a customer's own IdP — Sorcha consumes the identity assertion, it does not issue the underlying identity.

## Where to Start a Pilot

For an integrator picking up Sorcha for the first time, the recommended pilot path depends on the domain:

| Domain | Start with | Why |
|---|---|---|
| DPP / ESPR | `walkthroughs/TradeFinance/` (DPP credential flow) | The DPP gate is already wired; substitute a real material schema |
| Trade finance | `walkthroughs/TradeFinance/` end-to-end | Working soak; n1 deployment ready |
| Manufacturing traceability | `walkthroughs/AssuredIdentity/` | The credential-chain pattern is identical; substitute domain schemas |
| Municipal governance | `walkthroughs/ConstructionPermit/` | Multi-stakeholder pattern with statutory consultees |

Each walkthrough has a `README.md`, a deterministic `run.ps1`, and (where applicable) a soak script that demonstrates the workflow under load. Substitute schemas, substitute participants, keep the architecture.

## Pointers

| Source | Purpose |
|---|---|
| [`STANDARDS.md`](../STANDARDS.md) | The verifiable-credentials and HAIP standards underlying every example here |
| [`docs/architecture.md`](architecture.md) | How the platform produces the evidence each domain consumes |
| [`docs/openid4vc-haip-integration.md`](openid4vc-haip-integration.md) | The wallet boundary every consumer-facing flow uses |
| `walkthroughs/README.md` | Inventory of working demos with run scripts |
