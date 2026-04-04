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
| TENANT-2 | Azure AD integration | P3 | 12h | ✅ Done | Feature 054: Full OIDC with discovery, 5 provider shortcuts (Entra, Google, Okta, Apple, Cognito) |
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
| PART-1 | External identity provider (OIDC) integration for participant authentication | P3 | 24h | ✅ Done | Feature 054/055: OIDC token exchange, social login (Microsoft, Google, Apple, GitHub), auto-provisioning |
| PART-2 | API key management for machine participants | P3 | 16h | 📋 Deferred | Service clients and AI agents authenticating via API keys |
| PART-3 | Blueprint participant resolution by address | P2 | 20h | ✅ Done | Feature 065: Participant resolution from published records + instance bindings, starting action binding |
| PART-4 | Field-level encryption using published public keys | P2 | 24h | ✅ Done | Feature 045/065/075: Envelope encryption with disclosure groups, per-recipient key wrapping, batch public key resolution |
| PART-5 | DID document generation and resolution endpoints | P3 | 16h | 📋 Deferred | Generate W3C DID documents from published participant records |
| PART-6 | Peer-to-peer participant record replication and synchronization | P3 | 20h | 📋 Deferred | Replicate participant indexes across peer nodes |
| PART-7 | UI components for participant management | P2 | 16h | ✅ Done | Feature 054: Participant admin pages, publishing, wallet linking UI |
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

## Wallet Key Derivation & Threshold Signing — Research & Investigation

> **Source:** Brainstorming session on extending Sorcha's wallet service with HD key derivation, corporate key recovery, and threshold signing (2026-04-04). These features explore organisation-level key management, eliminating individual recovery phrases, and split-custody signing via FROST (RFC 9591). Each item represents a discrete capability that should be researched, scoped, and classified as pre-release or post-1.0.

| ID | Area | Priority | Impact | Status | Description |
|----|------|----------|--------|--------|-------------|
| WALLET-R1 | HD Derivation Path Schema | P2 | High | 🔬 Research | Define and register Sorcha-specific BIP-32 derivation purposes: `m / sorcha_purpose' / org_id' / dept_id' / user_id' / key_usage / index`. Purpose values: 100' DID identity, 101' VC issuance, 102' governance, 103' encrypted comms, 104' service auth. Hardened at purpose/org/dept/user levels; non-hardened at usage/index. Path encodes audit context (org, department, user, purpose). |
| WALLET-R2 | Org Master Seed Management | P2 | High | 🔬 Research | Organisation holds a master seed in HSM/KMS. Admin recovery via Shamir's Secret Sharing (K-of-N threshold to reconstruct) — prevents single-admin unilateral recovery. Integrates with Azure KMS work (feature 082): master seed in Key Vault/HSM, derivation is server-side. No individual recovery phrases needed. |
| WALLET-R3 | User Key Derivation Service | P2 | High | 🔬 Research | Derive per-user keys deterministically from org master seed at defined paths. Users never see or hold a seed phrase. Recovery = re-derivation from master seed at known path. Enables org-controlled key lifecycle without individual secret management burden. |
| WALLET-R4 | Key Rotation & Revocation | P2 | High | 🔬 Research | Index-based key rotation (increment index in derivation path). Revocation = stop deriving from that path + publish DID revocation. Must integrate with existing DID document management and credential lifecycle. Related to TRUST-10 (key rotation & re-encryption). |
| WALLET-R5 | ISigningProvider Threshold Extension | P2 | High | 🔬 Research | Extend `ISigningProvider` abstraction to support threshold signing alongside existing KMS and local signing modes. Must maintain backward compatibility with current single-party signing. Verifiers cannot distinguish threshold-signed from single-signed Ed25519 signatures. |
| WALLET-R6 | FROST Sidecar Service | P2 | High | 🔬 Research | Rust gRPC sidecar wrapping Zcash Foundation `frost-ed25519` crate (NCC Group-audited). Called via `ISigningProvider`. Avoids writing crypto from scratch. Fits existing Aspire orchestration and gRPC architecture. Interop options: Rust gRPC sidecar (recommended) or FFI via CsBindgen/UniFFI. |
| WALLET-R7 | Distributed Key Generation (DKG) | P3 | High | 🔬 Research | FROST DKG provisioning flow for co-signed key pairs. Shares generated without a trusted dealer. Each party (wallet service + user device) holds a share; full private key never exists whole anywhere. On device loss, server share (from org master seed) retained, new device share provisioned via key resharing without changing public key. |
| WALLET-R8 | Device Share Management | P3 | Medium | 🔬 Research | Secure storage of user's signing share on phone/local HSM. Re-provisioning flow on device loss via key resharing protocol. Must handle offline/lost device scenarios gracefully. Consider platform-specific secure enclaves (iOS Secure Enclave, Android StrongBox). |
| WALLET-R9 | Custody Level Model | P2 | High | 🔬 Research | Three first-class wallet modes: **Custodial** (full key in KMS, no device share — service accounts/automation), **Co-signed** (server share + device share — standard user operations), **Self-custody** (full key on device, optional recovery escrow — external/citizen wallets). Must be a core wallet property, not bolted on. |
| WALLET-R10 | Policy Enforcement Layer | P3 | Medium | 🔬 Research | Server-side partial signing gated by policy: approval workflows, rate limits, time-of-day restrictions, geo-fencing. The server share only participates after policy checks pass. Provides genuine non-repudiation — neither party can sign alone. |
| WALLET-R11 | Delegation Model | P3 | Medium | 🔬 Research | Department-level extended keys for hierarchical admin access. Derived from org master at `dept_id'` level. Enables department admins to manage user keys within their branch without access to sibling departments or parent org key. |
| WALLET-R12 | Proof of Derivation | P3 | Medium | 🔬 Research | Prove a key was derived from org master without revealing the master seed. For audit/compliance: cryptographic proof of organisational key provenance. Could use BIP-32 chain code properties or ZKP-based approaches. |
| WALLET-R13 | Governance Role Derivation | P3 | Medium | 🔬 Research | Derive keys per governance role from the same wallet seed using purpose 102'. A single user's wallet can hold multiple role-specific keys (e.g., register owner vs validator vs participant) with clear separation and auditable derivation paths. |

### Priority Rationale

| Tier | IDs | Rationale |
|------|-----|-----------|
| **Tier 1 — Core key management** | WALLET-R1, R2, R3, R4, R5, R9 | Foundation for org-level key lifecycle. R9 (custody model) shapes all other decisions. R5 is the integration seam for threshold signing. |
| **Tier 2 — Threshold signing** | WALLET-R6, R7 | FROST sidecar and DKG are the cryptographic core. Depend on Tier 1 abstractions. Rust sidecar is the recommended path — no native .NET FROST implementation exists. |
| **Tier 3 — Operational maturity** | WALLET-R8, R10, R11, R12, R13 | Device management, policy enforcement, delegation, proof-of-derivation, and governance roles. Important for enterprise adoption but not blocking core functionality. |

### Implementation Notes

- **Post-quantum (ML-DSA):** Threshold schemes are immature for PQ algorithms; keep PQ keys as single-party KMS for now.
- **Offline scenarios:** Co-signed mode requires connectivity; consider pre-signed authorisations or time-limited single-party keys as fallback.
- **Latency:** Threshold signing adds a round-trip; batch operations may need a server-only key branch (custodial mode).
- **FROST protocol:** RFC 9591, production-ready in Rust (Zcash Foundation frost-ed25519).

---

## CLI Modernisation — Deferred Items (Feature 080)

| ID | Task | Priority | Effort | Status | Notes |
|----|------|----------|--------|--------|-------|
| CLI-D1 | Register sync-status/watch commands | P2 | 8h | 📋 Deferred | Depends on Feature 078 (P2P sync) |
| CLI-D2 | User bulk-import command | P3 | 6h | 📋 Deferred | Batch user creation from CSV/JSON |
| CLI-D3 | Register bulk-subscribe command | P3 | 6h | 📋 Deferred | Batch register subscriptions |
| CLI-D4 | Docket export command | P3 | 4h | 📋 Deferred | Export docket data to file |
| CLI-D5 | Formatter unit tests (Yaml, MachineReadable) | P2 | 4h | 📋 Deferred | Smoke-level coverage for non-trivial formatters |
| CLI-D6 | EventStreamService unit tests | P2 | 4h | 📋 Deferred | Channel-based streaming coverage |
| CLI-D7 | README/CLAUDE.md documentation updates | P2 | 2h | 📋 Deferred | CLI section in main docs |
| CLI-D8 | Completion script generation from RootCommand tree | P3 | 8h | 📋 Deferred | Replace hardcoded shell completion scripts |
| CLI-D9 | WalletCreateBatch bounded parallelism | P3 | 4h | 📋 Deferred | SemaphoreSlim/Parallel.ForEachAsync for 100+ wallets |
| CLI-D10 | --since filter server-side for events watch | P3 | 4h | 📋 Deferred | Currently client-side filtering; note in --help |

---

## Summary

**Total Deferred Tasks:** 66 (5 now completed)
**Total Deferred Effort:** 588+ hours (~15 weeks, excluding research items)

These tasks represent features that enhance the platform but are not critical for the Minimum Viable Deliverable (MVD). They can be prioritized for post-MVD development based on user feedback and business requirements.

The **Transaction Architecture Research** section (TRUST-1 through TRUST-10) represents structural improvements to the decentralised trust model identified through critical analysis. These are investigation items — effort estimates will be determined after research phase.

---

**Back to:** [MASTER-TASKS.md](../MASTER-TASKS.md)
