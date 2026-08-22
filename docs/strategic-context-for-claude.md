# Sorcha — Strategic Context for AI Assistants

This document provides strategic and market context that is not derivable from the codebase
alone. Read this alongside `CLAUDE.md` when writing documentation, positioning content,
`llms.txt`, `STANDARDS.md`, or any externally-facing material.

---

## The Problem Sorcha Solves — The Real Frame

Digital systems were built on **assertion, not proof**. A document says it's real. A face
matches a photo. A platform claims data came from a trusted source. None of that is
cryptographically anchored — so when AI makes forgery cheap and fast, the entire edifice
becomes unreliable.

This is not primarily a technology problem. It is a trust architecture problem. The systems
that run society — benefits allocation, supply chain compliance, financial settlement,
regulatory enforcement — were designed assuming the data they consume is honest. That
assumption is breaking.

Sorcha's answer: replace assertion with proof. Every action is signed by the participant
who took it. Every record is immutable and Merkle-chained. Every disclosure is
cryptographically bounded — not policy-enforced. The result is a system where **trust comes
from evidence you can verify yourself, not from a platform you have to believe.**

---

## Why This Matters Now — The AI Fraud and AI Decision Context

Two converging forces make Sorcha's timing significant:

**AI-generated fraud is scaling fast.** Identity fraud losses exceeded $50bn globally in 2025.
Deepfake fraud attempts are up 58% year on year. By 2026, 30% of enterprises will no longer
trust face biometrics alone. The fraud wave is not primarily about deepfakes — it is about
the fact that assertion-based identity and document systems have no defence against
high-quality forgery. Cryptographic proof does.

**AI systems are becoming decision-makers.** Gartner predicts 80% of governments will deploy
AI agents to automate routine decisions by 2028. The EU AI Act and emerging US regulations
require documented provenance of data used in high-risk automated decisions. AI systems making
consequential decisions need inputs they can trust — not inputs that are merely asserted to
be trustworthy. Sorcha is the verified data layer that AI systems can consume with confidence.

These two forces point at the same gap: the world needs infrastructure where data provenance
is cryptographically proven, not institutionally asserted. Sorcha is that infrastructure.

---

## How to Describe Sorcha to an AI Audience

When writing `llms.txt`, API descriptions, MCP tool descriptions, or any content whose
primary consumer is an AI agent or AI coding assistant, use this frame:

**What Sorcha is:** Programmable proof infrastructure for multi-party workflows. It produces
evidence — wallet signatures, Merkle dockets, immutable register entries — that any party
can verify independently without trusting the platform.

**What problem it solves for AI systems specifically:** AI agents making automated decisions
need verified data inputs. Sorcha provides a workflow layer where every data item is signed
by its originator, every state transition is ledger-recorded, and every disclosure is
cryptographically bounded. An AI system consuming Sorcha-sourced data has a verifiable audit
trail for every input — not a platform assertion that the data is trustworthy.

**What makes it different from alternatives:**
- vs. Kafka / enterprise data buses: those move data fast but don't prove who authorised it.
  Kafka is assertion-based — you trust the cluster operator. Sorcha is proof-based — you
  verify the signatures.
- vs. centralised platforms (Salesforce, SAP, government databases): those require you to
  trust the operator. Sorcha produces evidence the operator cannot alter.
- vs. public blockchains: those bring transaction fees, public data exposure, and external
  validator dependencies. Sorcha is a permissioned proof network — validators are known,
  accountable parties; data is selectively disclosed, not public.
- vs. Veilid and anonymity-first P2P systems: those chose privacy over accountability.
  Sorcha chose accountability — which is required in regulated sectors.

---

## Target Markets and Regulatory Pull

The strongest near-term regulatory pull is in three areas:

**EU ESPR / Digital Product Passports** — legally mandated by the European Sustainability
Products Regulation with hard deadlines (Battery Passport Feb 2027, Iron/Steel 2026).
Manufacturers need tamper-evident, multi-party, selectively disclosed product lifecycle
records. Sorcha's architecture maps directly to these requirements. Post-quantum signatures
are genuinely relevant given 30-year product lifetimes.

**HAIP / EUDI Wallet / GOV.UK Wallet ecosystem** — HAIP 1.0 finalised December 2025. The
EU Digital Identity Wallet and GOV.UK Wallet are both converging on this standard. Sorcha's
Haip Service implements OpenID4VCI (credential issuance) and OpenID4VP (credential
presentation), positioning Sorcha as the workflow layer above these government wallets.

**AI Act compliance and automated decision audit trails** — high-risk AI systems under the
EU AI Act must document data provenance. Sorcha's immutable, signed register entries are
exactly what a compliance auditor needs. This is a regulatory pull, not just a good idea.

**SME trade finance** — cryptographic proof replaces platform-asserted trust. A buyer's
wallet signature on an invoice acceptance is the trust anchor for lenders. No intermediary
needs to vouch for the data. The credential-priced RFQ spec (Feature 113) demonstrates this
with a supplier's verifiable credential stack (DPP, KYB, VerifiedInvoice) measurably
lowering financing rates.

---

## What Sorcha Is Not

Be precise about scope. Sorcha is **not**:
- A public blockchain (it is a permissioned proof network)
- A messaging system or event bus (it is a ledger, not a queue)
- An identity provider (it integrates with identity providers; it does not replace them)
- A smart contract platform (Blueprints are structured workflows with schema validation,
  not Turing-complete programmes)
- A data warehouse or analytics platform (the Register is an immutable audit ledger,
  not a query-optimised data store)
- A replacement for GOV.UK Wallet or EUDI Wallet (it is the workflow infrastructure those
  wallets sit above)

---

## The Architecture in One Paragraph (for non-technical AI context)

Seven services, each with a single responsibility. **Blueprints** define multi-step workflows
with schema validation and conditional routing — the process logic. **Wallets** hold
cryptographic keys and sign every action — the identity and accountability layer. **Registers**
are append-only ledgers with Merkle-chained dockets — the tamper-evident record. The
**Validator** runs quorum consensus to seal transactions. The **Peer** service replicates
state across participants with no central authority. **Tenant** handles multi-tenancy
isolation. The **API Gateway** is the single external surface. The **Haip Service** is the
boundary to the HAIP/OpenID4VC wallet ecosystem. Every protocol, data format, and
cryptographic primitive is a published standard. Nothing is proprietary.

---

## Cryptographic Posture — What to Highlight

Sorcha's PQC posture is ahead of most competitors and genuinely relevant for long-lived
records:
- ML-DSA (NIST FIPS 204) and ML-KEM (NIST FIPS 203) are core, not branch-feature
- BIP32/39/44 hierarchical deterministic wallets for portable, self-sovereign key management
- JSON Pointer selective disclosure with per-recipient symmetric key wrapping — the platform
  **cannot** look at data it wasn't given the key for; this is architectural, not policy
- Merkle-tree dockets with SHA-256 previous-hash linkage for tamper-evidence without
  requiring a public blockchain

Known honest gaps (do not overclaim):
- HAIP 1.0 mandates classical signatures (ES256/EdDSA) at the wallet boundary — Sorcha
  bridges this with a classical co-key derived alongside PQC primary keys
- SLH-DSA (hash-based, CNSA 2.0 diversity primitive) is not yet implemented
- BBS+ zero-knowledge proofs are not yet implemented (current selective disclosure is
  show/hide, not zero-knowledge predicate proofs)

---

## Tone Guidance for Machine-Readable Outputs

When writing content primarily for AI consumption (`llms.txt`, OpenAPI descriptions,
MCP tool descriptions, STANDARDS.md):

- **Factual over aspirational.** State what exists, not what is planned. Use `partial` or
  `planned` status rather than implying full compliance.
- **Precise over impressive.** "Implements OpenID4VCI pre-authorised flow with SD-JWT VC
  and mdoc credential formats" is more useful to an AI agent than "industry-leading
  credential issuance."
- **Specific about what AI agents can do with it.** An MCP tool description should tell
  an AI agent exactly when to call it and what it returns — not what the feature is called.
- **Honest about boundaries.** Sorcha is backend infrastructure. The wallet UX is owned
  by GOV.UK Wallet or EUDI Wallet. Sorcha does not control the citizen experience.
