# Deferred Tasks

**These tasks are not required for MVD and will be addressed post-launch.**

**Back to:** [MASTER-TASKS.md](../MASTER-TASKS.md)

---

## Peer Service Transaction Processing

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| PEER-1 | Transaction processing loop | P3 | 12h | 📋 Deferred | Sprint 4 originally planned |
| PEER-2 | Transaction distribution | P3 | 10h | 📋 Deferred | P2P gossip protocol |
| PEER-3 | Streaming communication | P3 | 8h | 📋 Deferred | gRPC streaming |

---

## Tenant Service Full Implementation

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| TENANT-1 | Multi-tenant data isolation | P3 | 16h | 📋 Deferred | Use simple provider for MVD |
| TENANT-2 | Azure AD integration | P3 | 12h | 📋 Deferred | Full identity federation |
| TENANT-3 | Billing and metering | P3 | 20h | 📋 Deferred | Enterprise feature |
| TENANT-4 | Activity event multi-tenant isolation | P3 | 8h | 📋 Deferred | Events currently in public schema; consider per-org schema isolation when TENANT-1 is implemented |

---

## Advanced Features

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| ADV-1 | Smart contract support | P3 | 40h | 📋 Deferred | Future roadmap |
| ADV-2 | Advanced consensus | P3 | 32h | 📋 Deferred | Beyond simple Register |
| ADV-3 | External SDK development | P3 | 24h | 📋 Deferred | Developer ecosystem |
| ADV-4 | Blueprint marketplace | P3 | 30h | 📋 Deferred | Community feature |

---

## Authentication & Session Hardening

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| AUTH-H1 | Refresh token rotation | P2 | 8h | 📋 Deferred | Issue new refresh token on each refresh — limits replay window |
| AUTH-H2 | Cross-tab token synchronization | P2 | 6h | 📋 Deferred | localStorage event listener to sync token state across browser tabs |
| AUTH-H3 | Session expiry warning UI | P3 | 4h | 📋 Deferred | Toast/dialog warning user before session expires, "Extend Session" button |
| AUTH-H4 | Sliding window refresh token extension | P3 | 6h | 📋 Deferred | Extend refresh token TTL on activity — avoids hard 24h logout for active users |

---

## Register Governance — Future Enhancements

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| GOV-1 | ZKP-based admin credentials via register DIDs | P4 | 40h | 📋 Deferred | IDIDResolver interface designed for extensibility; requires ZKP library integration |
| GOV-2 | Social recovery for lost Owner wallet access | P4 | 24h | 📋 Deferred | Multi-party recovery blueprints or ZKP-based recovery; currently register becomes unmodifiable |
| GOV-3 | Concurrent governance proposals | P3 | 16h | 📋 Deferred | Current: single proposal at a time (implicit queueing via blueprint loop); future: multi-instance or queue-based |
| GOV-4 | Enhanced DID resolution with retry & fallback | P3 | 12h | 📋 Deferred | Retry with exponential backoff, consensus-based fallback for unreachable registers |
| GOV-5 | Deadlock detection for m=2 edge case | P3 | 8h | 📋 Deferred | Automatic detection + alerting when quorum impossible; Owner bypass is current escape hatch |
| GOV-6 | Roster reconstruction caching in Validator | P3 | 6h | 📋 Deferred | Cache roster after first reconstruction per register; performance optimization for rights checks |
| GOV-7 | Governance audit trail streaming via SignalR | P3 | 12h | 📋 Deferred | Real-time audit event streaming; immutable audit trail as separate transactions |
| GOV-8 | Roster member limit increase (>25) | P4 | 4h | 📋 Deferred | Current cap: 25 members; increase based on real-world needs + performance testing |
| GOV-9 | Control TX payload versioning strategy | P3 | 8h | 📋 Deferred | ControlTransactionPayload.Version field exists but migration strategy for future versions not documented |
| GOV-10 | Multi-tenant governance policies | P4 | 16h | 📋 Deferred | Cross-tenant constraints (e.g., block admins from competing tenants); currently per-register only |

---

## Published Participant Records — Phase 2+ (Out of Scope from 001-participant-records)

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| PART-1 | External identity provider (OIDC) integration for participant authentication | P3 | 24h | 📋 Deferred | Enable orgs to link participants to Microsoft, Google, GitHub, Apple IdPs |
| PART-2 | API key management for machine participants | P3 | 16h | 📋 Deferred | Service clients and AI agents authenticating via API keys |
| PART-3 | Blueprint participant resolution by address | P2 | 20h | 📋 Deferred | Phase 2 integration — blueprint participants resolve to wallet addresses instead of text names |
| PART-4 | Field-level encryption using published public keys | P2 | 24h | 📋 Deferred | Phase 2 — encrypt action payload fields for specific participant addresses |
| PART-5 | DID document generation and resolution endpoints | P3 | 16h | 📋 Deferred | Generate W3C DID documents from published participant records |
| PART-6 | Peer-to-peer participant record replication and synchronization | P3 | 20h | 📋 Deferred | Replicate participant indexes across peer nodes |
| PART-7 | UI components for participant management | P2 | 16h | 📋 Deferred | Blazor WASM pages for publishing, updating, revoking, and browsing participants |
| PART-8 | Organization-level wallet signing for participant publication | P2 | 12h | 📋 Deferred | Currently uses individual user wallet; upgrade to org-level signing wallet |
| PART-9 | Migrate participant authorization to register governance/control system | P3 | 16h | 📋 Deferred | Move from Tenant Service enforcement to register Control TX governance roster |

---

## Transaction Architecture — Research & Investigation

> **Source:** Critical review of transaction core (2026-02-21). These are structural improvements to the decentralised trust model — not process improvements. Each item represents a genuine capability gap or trust hardening opportunity identified by examining how transactions are created, signed, validated, sealed, and disclosed.

| ID | Area | Priority | Impact | Status | Description |
|----|------|----------|--------|--------|-------------|
| TRUST-1 | Verifiable Calculations | P2 | High | 🔬 Research | Validator should re-execute JSON Logic calculations against accumulated data and reject mismatches. Currently calculations are executed by Blueprint Engine and results simply included in the payload — a compromised Blueprint Service or malicious participant could submit incorrect calculated values (e.g. `riskCategory: "routine"` for a Class III device) and the Validator would accept it. The calculation rules are on-chain (in the blueprint), the inputs are on-chain (in previous transactions), but verification is entirely off-chain. |
| TRUST-2 | Validator-Enforced Disclosure | P2 | High | 🔬 Research | Validator doesn't verify that disclosure rules were correctly applied. It validates structure, signatures, and chain — but if the Blueprint Service sends full unfiltered data to a participant who should only see specific fields, the Validator has no opinion. Disclosure rules are in the blueprint (on-chain), but enforcement is off-chain. Options: (a) Validator checks each participant's encrypted payload contains only the fields specified in their disclosure rules, or (b) ZKP proofs that disclosed subsets are faithful extractions of committed data. |
| TRUST-3 | Transaction Receipts | P2 | High | 🔬 Research | After a transaction is validated and sealed in a docket, the submitter receives no signed receipt proving finality. A receipt — signed by the validator, containing `{txId, docketNumber, merkleRoot, inclusionProof, validatorSignature}` — would be an independently verifiable artefact. This is the difference between "the system says it happened" and "here's cryptographic proof it happened." Currently requires trusting the system's own reporting. |
| TRUST-4 | Merkle Inclusion Proofs | P2 | High | 🔬 Research | Merkle root exists in dockets but there's no mechanism to generate or verify a Merkle inclusion proof for a single transaction. A participant wanting to prove their transaction is sealed must fetch the entire docket and recompute the tree. Lightweight proofs (~log2(n) hashes) would enable offline verification — e.g. a hospital presenting a Refurbishment Certificate VC to an insurer without requiring the insurer to have register access. Needs `GenerateInclusionProof(txId, docket)` and `VerifyInclusionProof(proof, merkleRoot)`. |
| TRUST-5 | Revocation & Amendment Model | P2 | High | 🔬 Research | No structural mechanism to revoke, supersede, or amend a previous transaction. If a VC contains an error (wrong serial number, wrong date), there's no on-chain way to express "transaction X is superseded by transaction Y" or "credential Z is revoked." Currently requires ad-hoc per-blueprint solutions. A first-class `RevocationTransaction` type — referencing the original TxId, signed by the original issuer, recorded on the same register — would be a structural primitive rather than an application-layer concern. Related to but distinct from the existing VC revocation endpoint (which is application-layer). |
| TRUST-6 | Consensus Finality Guarantees | P3 | High | 🔬 Research | Current consensus is simple quorum voting (>51% = accepted) with no finality guarantee. A docket accepted by 2-of-3 validators could theoretically be challenged if the third comes online and disagrees. No concept of finality depth or BFT-style commit/pre-commit phases. For high-value transactions (medical device certification, financial instruments), "probably final" isn't good enough. Options: two-phase commit (pre-commit lock then finalise) or finality threshold (final after N subsequent dockets reference it). |
| TRUST-7 | Cross-Register References | P3 | Medium | 🔬 Research | Each register is a self-contained chain with no mechanism for a transaction on Register A to cryptographically reference a transaction on Register B. In production, different organisations will have different registers. A cross-register reference (`foreignRegisterId + foreignTxId + foreignMerkleProof`) embedded in a local transaction would enable verifiable cross-chain attestation without direct register access. Essential for composability between organisations. |
| TRUST-8 | Transaction Lifecycle Audit Trail | P3 | Medium | 🔬 Research | The register stores transactions and dockets but no structured event log showing lifecycle: submitted → pooled → validated → sealed → confirmed. Memory pool fields (`AddedToPoolAt`, `Priority`, `RetryCount`) are discarded on persistence. For regulatory compliance (healthcare, finance), auditors need provable temporal ordering of each stage. A `TransactionLifecycle` record — timestamps per stage, validator ID, consensus vote tally — preserved alongside the transaction would provide non-repudiable audit provenance. |
| TRUST-9 | Timestamp Authority | P3 | Medium | 🔬 Research | Transaction timestamps are self-asserted by the submitter. Validator checks for clock skew (±5 min) and expiry, but ordering within a docket is undefined. Two transactions with identical timestamps have no deterministic order. Legal and regulatory contexts require provable temporal ordering. Options: Validator stamps transactions on receipt, or integration with RFC 3161 trusted timestamping service for independently verifiable temporal proof. |
| TRUST-10 | Key Rotation & Re-encryption | P3 | Medium | 🔬 Research | Payloads are encrypted with per-message symmetric keys wrapped for each recipient's current public key. If a key is compromised and rotated, all previously encrypted payloads remain accessible with the old key. No mechanism to re-encrypt existing payloads for a new key or revoke access to historical data. Options: envelope encryption with a rotatable master key, or proxy re-encryption where a semi-trusted proxy re-encrypts ciphertexts for new keys without seeing plaintext. |

### Priority Rationale

**Ranked by trust impact vs implementation effort:**

| Tier | IDs | Rationale |
|------|-----|-----------|
| **Tier 1 — Closes active trust gaps** | TRUST-1, TRUST-2, TRUST-3, TRUST-4 | These address cases where the system currently relies on application-layer honesty rather than cryptographic enforcement. Most actionable without architectural upheaval. |
| **Tier 2 — Essential for production credentials** | TRUST-5, TRUST-6 | Revocation is a hard requirement for any VC system in production. Finality matters for high-value use cases. |
| **Tier 3 — Platform maturity** | TRUST-7, TRUST-8, TRUST-9, TRUST-10 | Composability, auditability, temporal provability, and post-compromise recovery. Important for enterprise adoption but not blocking current workflows. |

---

## Summary

**Total Deferred Tasks:** 43
**Total Deferred Effort:** 538+ hours (~14 weeks, excluding research items)

These tasks represent features that enhance the platform but are not critical for the Minimum Viable Deliverable (MVD). They can be prioritized for post-MVD development based on user feedback and business requirements.

The **Transaction Architecture Research** section (TRUST-1 through TRUST-10) represents structural improvements to the decentralised trust model identified through critical analysis. These are investigation items — effort estimates will be determined after research phase.

---

**Back to:** [MASTER-TASKS.md](../MASTER-TASKS.md)
