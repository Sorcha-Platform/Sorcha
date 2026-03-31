# Feature Specification: Transaction Receipts, Merkle Inclusion Proofs & Revocation Transactions

**Feature Branch**: `079-trust-hardening`
**Created**: 2026-03-31
**Status**: Draft
**Tracks**: TRUST-3, TRUST-4, TRUST-5 from Transaction Architecture Review

## Overview

Three trust-hardening primitives that close active gaps in the Sorcha distributed ledger's verification model. Currently, participants must trust the system's own reporting to confirm their transactions were sealed, cannot produce lightweight proof of inclusion for third parties, and have no on-chain mechanism to revoke or supersede previously issued credentials.

All three primitives operate on the transaction **envelope** (identifiers, hashes, signatures) rather than encrypted payloads, making them fully compatible with field-level encryption (FLE). This is a deliberate design choice: trust verification and confidentiality are orthogonal concerns.

**Key insight from brainstorming**: Validator-side calculation verification (TRUST-1) and disclosure enforcement (TRUST-2) are fundamentally incompatible with FLE — the Validator cannot re-execute calculations or check disclosure content when payloads are encrypted. Those items are deferred as ZKP research. The three items in this spec operate at the envelope layer and require no plaintext access.

## User Scenarios & Testing

### User Story 1 - Transaction Receipts (Priority: P1)

After a participant submits a transaction and it is validated and sealed into a docket, they receive a cryptographically signed receipt proving finality. The receipt is an independently verifiable artefact — the participant can store it locally and present it to any third party as proof that their transaction was recorded, without the third party needing access to the register.

**Why this priority**: Without receipts, the only way to confirm a transaction was sealed is to query the register. This creates a trust dependency on the system's own reporting. Receipts transform "the system says it happened" into "here is cryptographic proof it happened." This is the foundation for offline verification (US2) and credential lifecycle management (US3).

**Independent Test**: Submit a transaction, receive a receipt after sealing, then verify the receipt's validator signature and Merkle root independently using only the receipt data and the validator's public key — no register access required.

**Acceptance Scenarios**:

1. **Given** a transaction is submitted and sealed into a docket, **When** the docket is confirmed, **Then** a signed receipt is generated containing the transaction ID, docket number, Merkle root, inclusion proof path, validator signature, and timestamp.

2. **Given** a receipt has been generated, **When** a participant retrieves the receipt by transaction ID, **Then** the receipt is returned with all fields intact and the validator signature can be independently verified.

3. **Given** a receipt exists, **When** the receipt's validator signature is verified against the validator's known public key, **Then** the verification succeeds confirming the receipt was produced by an authorised validator.

4. **Given** a docket contains multiple transactions, **When** receipts are generated, **Then** each transaction in the docket receives its own receipt with a unique inclusion proof path.

5. **Given** a participant is connected via real-time notifications, **When** their transaction is sealed, **Then** they receive a push notification containing the receipt data.

6. **Given** a participant is offline when their transaction is sealed, **When** they later query the receipt endpoint, **Then** the receipt is available for retrieval.

---

### User Story 2 - Merkle Inclusion Proofs (Priority: P1)

A participant or third party can verify that a specific transaction is included in a sealed docket using only a lightweight proof (logarithmic in the number of transactions) and the published Merkle root — without fetching the entire docket or having register access.

**Why this priority**: Equal priority with receipts because they are tightly coupled — a receipt contains an inclusion proof. The offline verification use case (e.g., presenting a Verifiable Credential to an insurer without the insurer needing register access) requires both primitives working together.

**Independent Test**: Seal a docket with N transactions, generate an inclusion proof for one transaction, then verify the proof recomputes to the correct Merkle root using only the proof data — no docket access required.

**Acceptance Scenarios**:

1. **Given** a sealed docket containing N transactions, **When** an inclusion proof is requested for a specific transaction, **Then** a proof is returned containing the transaction hash, sibling hashes along the Merkle path, leaf index, and tree size.

2. **Given** an inclusion proof and a published Merkle root, **When** the verification algorithm recomputes the root from the proof path, **Then** the computed root matches the published root, confirming inclusion.

3. **Given** a tampered transaction hash, **When** the inclusion proof is verified against the original Merkle root, **Then** verification fails, detecting the tampering.

4. **Given** a docket with 1,000 transactions, **When** an inclusion proof is generated, **Then** the proof contains at most 10 sibling hashes (log2(1000)), keeping the proof compact.

5. **Given** a portable verification bundle (VC + receipt + inclusion proof), **When** a third party runs the verification algorithm offline, **Then** they can confirm: (a) the VC signature is valid, (b) the inclusion proof recomputes to the Merkle root, (c) the receipt's Merkle root matches, (d) the validator's signature on the receipt is valid.

---

### User Story 3 - Revocation Transactions (Priority: P2)

An authorised party can revoke or supersede a previously sealed transaction by submitting a revocation transaction that is itself sealed on-chain. Once revoked, any party checking the transaction's status receives a clear indication that the credential has been revoked, along with the reason and timestamp.

**Why this priority**: Revocation is a hard requirement for any Verifiable Credential system in production, but it builds on the receipt and proof infrastructure (a revocation should itself produce a receipt). Receipts and proofs are foundational; revocation extends them.

**Independent Test**: Seal a transaction, submit a revocation transaction referencing it, then query the original transaction's status and confirm it shows as revoked with the correct reason and revocation timestamp.

**Acceptance Scenarios**:

1. **Given** a sealed transaction, **When** the original signer submits a revocation transaction referencing it, **Then** the revocation is validated, sealed in a docket, and the original transaction's status changes to "revoked."

2. **Given** a sealed transaction, **When** a governance roster member with revocation rights submits a revocation, **Then** the revocation is accepted even though they are not the original signer.

3. **Given** a revoked transaction, **When** any party queries the transaction status endpoint, **Then** the response includes: status "revoked", revocation transaction ID, reason, and timestamp.

4. **Given** a revoked transaction, **When** someone attempts to revoke the revocation itself, **Then** the system rejects the attempt — revocations are irrevocable.

5. **Given** a revocation with reason "superseded" and a pointer to a replacement transaction, **When** the status is queried, **Then** the response includes the superseding transaction ID for chain-of-custody tracing.

6. **Given** a revocation transaction is sealed, **When** the register is replicated to peer nodes, **Then** the revocation propagates through normal peer sync without special handling.

7. *(Future — bulk revocation deferred to follow-up feature)* **Given** the original signer's key has been compromised, **When** a governance roster member revokes all transactions signed by that address after a specific date, **Then** a bulk revocation is recorded covering the affected transactions.

---

### User Story 4 - Offline Verification Bundle (Priority: P2)

A participant can export a portable verification bundle containing a Verifiable Credential, its transaction receipt, and the inclusion proof. A third party receiving this bundle can verify the credential's authenticity, inclusion in the ledger, and revocation status without any network connectivity or register access.

**Why this priority**: This is the integration story that ties receipts, proofs, and revocation status together into a practical end-to-end use case. Depends on US1, US2, and US3.

**Independent Test**: Export a verification bundle for a sealed VC, transfer it to an air-gapped machine, run the portable verification library, and confirm all checks pass.

**Acceptance Scenarios**:

1. **Given** a sealed transaction with a VC payload, **When** the participant requests an export bundle, **Then** the bundle contains: the VC, the transaction receipt, the inclusion proof, and a revocation status snapshot.

2. **Given** a verification bundle, **When** verified on an air-gapped machine using the portable verification library, **Then** all four checks pass: VC signature, inclusion proof, receipt signature, and revocation status at time of export.

3. **Given** a verification bundle for a VC that was revoked after export, **When** verified offline, **Then** the bundle shows the revocation status at export time with a warning that online status check is recommended.

---

### User Story 5 - Transaction Lifecycle Indicators (Priority: P2)

A participant who submits a transaction sees real-time lifecycle status updates in their wallet UI, similar to WhatsApp's message delivery ticks. The transaction progresses through three visual states: submitted (single grey tick), sealed in docket (single blue tick), and receipt confirmed (double blue ticks). This provides immediate, intuitive feedback without the user needing to understand the underlying blockchain mechanics.

**Why this priority**: Receipts (US1) and proofs (US2) provide the cryptographic infrastructure, but without visible feedback the user has no way of knowing their transaction was received, sealed, or receipted. This story closes the UX gap between backend trust primitives and user-facing confidence.

**Independent Test**: Submit a transaction via the UI, observe the grey tick appear immediately, then the blue tick when the docket is sealed, then the double blue ticks when the receipt is confirmed — all without page refresh.

**Acceptance Scenarios**:

1. **Given** a participant submits a transaction, **When** the transaction enters the memory pool, **Then** the wallet UI shows a single grey tick next to the transaction.

2. **Given** a submitted transaction, **When** the docket containing it is confirmed, **Then** the wallet UI updates to a single blue tick via real-time notification pushed to the submitter's wallet group.

3. **Given** a sealed transaction, **When** the receipt is generated and stored, **Then** the wallet UI updates to double blue ticks via real-time notification pushed to the submitter's wallet group, and the receipt ID is stored against the transaction.

4. **Given** a participant navigates away and returns, **When** the transaction list loads, **Then** the correct tick state is displayed based on stored receipt status (not just real-time events).

5. **Given** a participant clicks on the double blue ticks, **When** the receipt detail opens, **Then** the full receipt with inclusion proof is displayed, with an option to export the verification bundle.

---

### Edge Cases

- What happens when a docket contains only one transaction? The Merkle tree is a single leaf; the inclusion proof has an empty path; the root equals the transaction hash.
- What happens when a receipt is requested for a transaction still in the memory pool (not yet sealed)? The system returns a "pending" status — no receipt exists until sealing.
- What happens when a revocation references a transaction ID that doesn't exist on the register? The Validator rejects the revocation with a "target not found" error.
- What happens when a revocation is submitted for a transaction that's already revoked? The Validator rejects it — double-revocation is not permitted.
- What happens when the validator's signing key is rotated? Receipts signed with the old key remain valid; new receipts use the new key. Key rotation metadata should be queryable.
- What happens during peer sync when a revocation arrives before its target transaction? The revocation is queued until the target transaction is available (eventual consistency).

## Requirements

### Functional Requirements

**Transaction Receipts (TRUST-3)**:

- **FR-001**: System MUST generate a signed receipt for every transaction sealed in a confirmed docket.
- **FR-002**: Receipt MUST contain: transaction ID, docket number, Merkle root, inclusion proof path, validator wallet address, validator signature, and sealing timestamp.
- **FR-003**: Receipt MUST be generated as part of the docket confirmation flow, immediately after successful persistence.
- **FR-004**: Receipt MUST be retrievable by transaction ID via a dedicated endpoint.
- **FR-005**: Receipt MUST be pushed to the register notification group via real-time channel when connectivity is available.
- **FR-006**: Receipt validator signature MUST use the same cryptographic infrastructure as transaction signing (ED25519/P-256/RSA).
- **FR-007**: Receipt format MUST accommodate future multi-validator signatures by using a signatures array rather than a single signature field.

**Merkle Inclusion Proofs (TRUST-4)**:

- **FR-008**: System MUST generate standard (non-ZK) Merkle inclusion proofs using the existing binary Merkle tree implementation.
- **FR-009**: Inclusion proof MUST contain: transaction hash, docket number, Merkle root, sibling hash path with left/right positions, leaf index, and tree size.
- **FR-010**: System MUST provide a verification function that recomputes the Merkle root from a proof and confirms it matches the published root.
- **FR-011**: Proofs MUST be generated on-demand from stored docket data, not pre-computed.
- **FR-012**: The verification function MUST be available in the portable verification library for offline use.

**Revocation Transactions (TRUST-5)**:

- **FR-013**: System MUST support a new "Revocation" transaction type that references and supersedes a previously sealed transaction.
- **FR-014**: Revocation transaction MUST contain: original transaction ID, original docket number, revocation reason, optional superseding transaction ID, optional metadata, signer signature, and signer address.
- **FR-015**: Revocation MUST be authorised by either the original transaction signer OR a governance roster member with explicit revocation rights.
- **FR-016**: Revocation reasons MUST include: Superseded, Erroneous, Compromised, Expired, Withdrawn, Regulatory.
- **FR-017**: The primary revocation scope MUST be per-transaction. Bulk revocation by signer address (for key compromise scenarios) is deferred to a follow-up feature and is NOT in scope for this implementation.
- **FR-018**: System MUST provide a transaction status endpoint returning: active/revoked/superseded status, revocation transaction ID, superseding transaction ID, and revocation timestamp.
- **FR-019**: A revocation transaction MUST NOT itself be revocable — revocations are irrevocable.
- **FR-020**: Revocation transactions MUST be sealed in dockets and replicated via normal peer sync.

**Offline Verification Bundle (US4)**:

- **FR-021**: System MUST support exporting a portable verification bundle containing: VC data, transaction receipt, inclusion proof, and point-in-time revocation status.
- **FR-022**: The portable verification library MUST verify all four components (VC signature, inclusion proof, receipt signature, revocation status) without network access.

**Transaction Lifecycle Indicators (US5)**:

- **FR-023**: Receipt notifications MUST be pushed to the submitter's wallet group (in addition to the register group) so the user's UI receives the event regardless of which page they are on.
- **FR-024**: The wallet UI MUST display three lifecycle states for sent transactions: submitted (single grey tick), sealed (single blue tick), and receipted (double blue ticks).
- **FR-025**: The wallet service MUST track both outbound (signed by this wallet) and inbound (this wallet is a recipient) transaction status (Pending/Sealed/Receipted) with an optional receipt ID reference.
- **FR-026**: When a receipt notification arrives for a sent transaction, the wallet MUST store the receipt ID and update the transaction's lifecycle state to Receipted.
- **FR-027**: The transaction list MUST render the correct tick state on load from persisted state, not only from real-time events.

### Design Decision Analysis

#### Receipt Generation Timing

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **A: Synchronous (during seal)** | **Recommended** | Receipt available immediately with no eventual consistency gap. Single atomic operation is simpler and more reliable. The latency impact is minimal — signing is a fast cryptographic operation. For multi-validator consensus, the proposer validator signs the receipt; other validators' votes are already captured in the docket's consensus record. |
| B: Asynchronous (after commit) | Not recommended | Introduces a temporal gap where a transaction is sealed but has no receipt. Requires separate receipt store, retrieval mechanism, and retry logic for failed receipt generation. Over-engineering for current single-validator architecture. |

#### Receipt Delivery

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **C: Push + Pull (both)** | **Recommended** | Push via SignalR to wallet group provides real-time notification (aligns with existing encryption progress events). Pull via GET endpoint provides resilient fallback for offline/disconnected participants. Minimal additional implementation — both mechanisms already exist in the platform. |
| A: Pull only | Not recommended | Requires polling or subscription; poor UX for real-time workflows. |
| B: Push only | Not recommended | Fragile — requires connectivity at seal time. |

#### Multi-Validator Receipts (Future-Proofing)

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **Signatures array from day one** | **Recommended** | `signatures: [{ validatorAddress, signature, algorithm, signedAt }]` — currently contains one entry. When multi-validator consensus is implemented (TRUST-6), additional entries are appended without breaking the receipt format. Zero cost now, avoids breaking change later. |
| Single signature field | Not recommended | Would require a breaking format change when multi-validator is added. |

#### Merkle Tree Construction

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **A: Binary Merkle tree** | **Recommended** | Already implemented in `MerkleTree.cs`. Simple, well-understood, efficient. Proof size is log2(n) which is excellent for typical docket sizes (10-1000 transactions = 4-10 hash steps). Non-membership proofs (sparse tree advantage) are not a current requirement. |
| B: Sparse Merkle tree | Not recommended | Unnecessary complexity. Non-membership proofs aren't needed. Larger proof sizes. |

#### Proof Persistence

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **A: On-demand generation** | **Recommended** | Dockets already store all transaction hashes. Reconstructing the tree is O(n) but proofs are requested infrequently (not on the hot path). Caching eliminates repeated reconstruction. No additional storage overhead. Always consistent with docket state — no sync concerns. |
| B: Pre-computed storage | Not recommended | Storage overhead of n * log2(n) hashes per docket. Redundant with existing docket data. Adds complexity to the sealing path. |

#### Revocation Authority

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **B: Original signer + governance roster** | **Recommended** | Organisational continuity is essential — if a signer leaves or their key is compromised, the organisation must still be able to revoke. The governance roster already models admin roles per-register. Adding a "Revoker" right to the roster is a natural extension. This avoids the key-person dependency of Option A while being less complex than Option C (blueprint-level roles). For transactions not created via blueprints (e.g., participant records), governance roster is the appropriate authority. |
| A: Original signer only | Not recommended | Key-person dependency. Key compromise leaves legitimate transactions vulnerable to malicious revocation with no organisational override. |
| C: Blueprint "Revoker" role | Not recommended for v1 | Requires blueprint schema extension. Doesn't cover non-blueprint transactions. Could be added later as an additional layer. |

#### Revocation Scope

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **A first, B later** | **Recommended** | Start with per-transaction revocation (simple, precise, easy to reason about). Add bulk revocation as a follow-up when key compromise incident response procedures are formalised. Per-transaction is sufficient for all non-compromise scenarios (erroneous data, superseded credentials, expiry, withdrawal, regulatory). Bulk revocation adds complexity (scope validation, unintended revocation risk) that should be designed carefully. |
| B immediately | Not recommended for v1 | Bulk revocation needs careful scope controls to prevent accidental mass-revocation. Better to add once per-transaction revocation is proven in production. |

#### Irrevocability of Revocations

| Approach | Recommendation | Rationale |
|----------|---------------|-----------|
| **Irrevocable** | **Recommended** | Prevents revocation ping-pong. If a credential needs to be reinstated, issue a new transaction — this creates a clean audit trail. Reinstatement transactions could be a future extension if needed, but should be a distinct transaction type (not un-revoking). The new transaction would naturally supersede the revoked one. |

### Key Entities

- **TransactionReceipt**: A cryptographically signed attestation that a transaction was sealed in a specific docket. Contains: transaction ID, docket number, Merkle root, inclusion proof path, validator signatures, sealing timestamp. Immutable once created. Stored alongside dockets.

- **MerkleInclusionProof**: A compact proof (logarithmic in docket size) that a transaction's hash is a leaf in the docket's Merkle tree. Contains: transaction hash, sibling path, leaf index, tree size. Generated on-demand from docket data.

- **RevocationTransaction**: A new transaction type that supersedes a previously sealed transaction. Contains: target transaction ID, revocation reason, optional superseding transaction pointer, authorised signer. Sealed in dockets like any other transaction.

- **TransactionStatus**: A derived view combining a transaction's current lifecycle state (active/revoked/superseded) with pointers to any revocation or superseding transactions. Queryable via endpoint, includable in verification bundles.

- **VerificationBundle**: A portable package containing a VC, its receipt, inclusion proof, and point-in-time revocation status. Designed for offline verification by third parties without register access.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A participant receives a signed receipt within 100ms of their transaction being sealed in a docket.
- **SC-002**: Receipt verification (signature check) completes in under 10ms using only the receipt data and the validator's public key.
- **SC-003**: Inclusion proof generation completes in under 100ms for dockets containing up to 10,000 transactions.
- **SC-004**: Inclusion proof verification (root recomputation) completes in under 5ms.
- **SC-005**: Transaction revocation status checks complete in under 50ms via the status endpoint.
- **SC-006**: A third party can verify a credential's authenticity, ledger inclusion, and revocation status using only a portable bundle — no network access or register access required.
- **SC-007**: All three primitives function correctly when transaction payloads are encrypted with field-level encryption.
- **SC-008**: Receipt, proof, and revocation data replicates correctly across peer nodes through the existing sync mechanism.
- **SC-009**: 100% of sealed transactions have corresponding receipts available for retrieval.

## Assumptions

- The existing `MerkleTree.cs` binary tree implementation and `DocketHasher.cs` are correct and performant — this feature extends rather than replaces them.
- The existing ZK inclusion proof infrastructure (`ZKInclusionProofProvider.cs`) serves a different use case (privacy-preserving proofs) and coexists with the standard inclusion proofs introduced here.
- Single-validator architecture is the current deployment model. Multi-validator support is designed for (signatures array) but not implemented in this feature.
- The portable verification library will be based on the existing `Sorcha.Validator.Core` which is already designed as enclave-safe/portable.
- Governance roster "Revoker" rights reuse the existing roster role infrastructure — no new governance model is needed.
- Bulk revocation (by signer address) is explicitly deferred to a follow-up feature. Per-transaction revocation is sufficient for v1.
