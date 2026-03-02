# Feature Specification: Encrypted Payload Integration

**Feature Branch**: `045-encrypted-payload-integration`
**Created**: 2026-03-01
**Status**: Draft
**Input**: Wire encryption into the action transaction pipeline with envelope encryption, disclosure grouping, async processing, algorithm completeness, and enhanced UX progress feedback.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Encrypted Action Payloads (Priority: P0)

When a participant submits an action in a blueprint workflow, the disclosed payload data for each recipient MUST be encrypted before the transaction is written to the register. Recipients can only read the fields they are entitled to see, and only by decrypting with their private key. No plaintext payload data is stored on the ledger.

**Why this priority**: This is the core of Sorcha's DAD (Disclosure, Alteration, Destruction) security model. Without payload encryption, the "Disclosure" pillar is filtering only — anyone with database access reads all data in cleartext. This is the single most critical gap in the platform.

**Independent Test**: Submit a blueprint action with disclosure rules, then query the register transaction directly. The payload data MUST NOT be readable without the recipient's private key.

**Acceptance Scenarios**:

1. **Given** a blueprint with disclosure rules assigning different fields to different participants, **When** an action is executed, **Then** each recipient's payload is encrypted with a unique symmetric key wrapped for their public key, and the register stores only ciphertext.
2. **Given** a recipient with an ED25519 wallet, **When** they retrieve their action transaction, **Then** they can decrypt their disclosed fields using their private key and see only the fields specified in the disclosure rules.
3. **Given** a recipient with an RSA-4096 wallet, **When** they retrieve their action transaction, **Then** decryption works identically using RSA key unwrapping.
4. **Given** a blueprint action with no disclosure rules defined, **When** the action is executed, **Then** the full payload is encrypted under the sender's wallet address (default full disclosure to sender).
5. **Given** a transaction stored in the register, **When** an unauthorized party queries the register database directly, **Then** all payload content is ciphertext — no plaintext field names or values are visible.

---

### User Story 2 - Disclosure Group Optimization (Priority: P0)

When multiple recipients are entitled to see the same set of fields, the system groups them and encrypts the data once per unique field set, wrapping the symmetric key individually for each group member. This minimizes payload size and encryption overhead.

**Why this priority**: Without grouping, a 10-recipient action encrypts data 10 times. With grouping, common disclosure sets (e.g., "all participants see referralReason and urgency") encrypt once and wrap a 32-byte key per recipient. This directly affects the 4 MB transaction size limit and encryption performance.

**Independent Test**: Submit an action where 5 recipients share the same disclosure fields. Verify only 1 ciphertext is produced with 5 wrapped keys, not 5 separate ciphertexts.

**Acceptance Scenarios**:

1. **Given** 3 participants with identical disclosure rules (same fields), **When** the action is executed, **Then** exactly 1 encrypted payload group is created with 3 wrapped keys.
2. **Given** 10 participants across 3 distinct disclosure field sets, **When** the action is executed, **Then** exactly 3 encrypted payload groups are created, with wrapped keys distributed across them.
3. **Given** a single participant with a unique disclosure set, **When** the action is executed, **Then** their payload group contains 1 ciphertext and 1 wrapped key.

---

### User Story 3 - Algorithm Completeness (Priority: P0)

All supported wallet algorithms MUST work for payload encryption. Participants using ED25519, NIST P-256, RSA-4096, or ML-KEM-768 wallets can all receive encrypted payloads. A single action transaction MAY contain wrapped keys for recipients using different algorithms.

**Why this priority**: Without P-256 encryption (currently unimplemented) and a corrected ML-KEM-768 decapsulation, participants using those wallet types cannot receive encrypted data. The system must support all advertised algorithms.

**Independent Test**: Create wallets with each of the 4 encryption-capable algorithms, submit an action with disclosures to all 4, verify each can decrypt their payload.

**Acceptance Scenarios**:

1. **Given** a recipient with an ED25519 wallet, **When** a symmetric key is wrapped for them, **Then** wrapping uses Curve25519 sealed box and decryption succeeds.
2. **Given** a recipient with a NIST P-256 wallet, **When** a symmetric key is wrapped for them, **Then** wrapping uses ECIES (ECDH + HKDF-SHA256 + AES-256-GCM) and decryption succeeds.
3. **Given** a recipient with an RSA-4096 wallet, **When** a symmetric key is wrapped for them, **Then** wrapping uses RSA-OAEP-SHA256 and decryption succeeds.
4. **Given** a recipient with an ML-KEM-768 wallet, **When** a symmetric key is wrapped for them, **Then** wrapping uses ML-KEM encapsulation and decryption uses proper KEM decapsulation (not generic decrypt).
5. **Given** a single action with recipients using ED25519, P-256, RSA-4096, and ML-KEM-768 wallets, **When** the transaction is built, **Then** each recipient's wrapped key uses their respective algorithm and all can decrypt independently.

---

### User Story 4 - Public Key Resolution (Priority: P1)

When encrypting payloads for recipients, the system resolves each recipient's public key automatically from the register's published participant records. Externally-provided public keys are also supported for cases where participants are not yet published.

**Why this priority**: Encryption requires the recipient's public key. The majority of recipients will have published participant records on the register, so automatic resolution is essential. External key provision covers bootstrap and cross-register scenarios.

**Independent Test**: Submit an action where recipients have published participant records. Verify public keys are resolved from the register without manual input.

**Acceptance Scenarios**:

1. **Given** a recipient with a published participant record on the register, **When** payload encryption is needed, **Then** their public key is resolved automatically via the register's participant index.
2. **Given** multiple recipients on the same register, **When** public keys are needed, **Then** resolution happens in a single batch request (not N sequential calls).
3. **Given** a recipient whose participant record has been revoked, **When** public key resolution is attempted, **Then** encryption fails gracefully with a clear error identifying the revoked participant.
4. **Given** a recipient whose public key is provided externally (e.g., in the action submission request), **When** payload encryption is needed, **Then** the externally-provided key is used without register lookup.
5. **Given** a mix of register-published and externally-provided recipients, **When** encryption proceeds, **Then** both key sources work together in the same transaction.

---

### User Story 5 - Async Encryption with Progress Feedback (Priority: P1)

For actions with many recipients or large payloads, encryption runs asynchronously in the background. The user receives immediate acknowledgement and real-time progress updates via existing notification channels. The UI displays meaningful progress (percentage, current step) rather than a blocking spinner.

**Why this priority**: Encryption for 50+ recipients with large payloads could take several seconds. Blocking the HTTP request degrades user experience. Background processing with real-time progress provides a responsive, professional experience.

**Independent Test**: Submit an action with 20+ recipients, verify immediate response, then verify progress notifications arrive via SignalR before the final confirmation.

**Acceptance Scenarios**:

1. **Given** an action submission with encrypted payloads, **When** the request is received, **Then** validation, calculation, routing, and disclosure happen synchronously and an acknowledgement is returned immediately with a tracking identifier.
2. **Given** encryption is running in the background, **When** progress changes, **Then** the submitting user receives real-time notifications indicating the current step and percentage complete.
3. **Given** encryption completes successfully, **When** the transaction is submitted to the validator, **Then** the user receives a confirmation notification with the transaction hash.
4. **Given** encryption fails (e.g., revoked key, unreachable register), **When** the failure occurs, **Then** the user receives an error notification identifying the failure reason and affected recipient.
5. **Given** the user navigates away from the submission page, **When** they return or view notifications, **Then** the encryption result (success or failure) is available as a persistent notification.

---

### User Story 6 - Transaction Size Enforcement (Priority: P1)

The validator enforces a configurable maximum transaction size (default: 4 MB). Transactions exceeding this limit are rejected with a clear error. The encryption pipeline checks estimated size before submission to avoid unnecessary work.

**Why this priority**: Without enforcement, oversized transactions waste validator and network resources. Pre-flight size estimation prevents encrypting a payload that will be rejected downstream.

**Independent Test**: Submit a transaction larger than the configured limit. Verify it is rejected at the validator with a clear size error.

**Acceptance Scenarios**:

1. **Given** the validator has a configured maximum transaction size of 4 MB, **When** a transaction exceeding 4 MB is submitted, **Then** it is rejected with a clear error code and message indicating the size limit.
2. **Given** the encryption pipeline is building a transaction, **When** the estimated encrypted size would exceed the limit, **Then** encryption halts early with a pre-flight size error before wasting computation.
3. **Given** the size limit is configurable, **When** an operator changes the limit in configuration, **Then** the new limit applies without service restart.

---

### User Story 7 - Recipient Decryption (Priority: P2)

Recipients retrieve and decrypt their action payloads through the standard transaction retrieval flow. The system identifies which encrypted payload group(s) a recipient has access to, unwraps the symmetric key using the wallet service, and returns decrypted data.

**Why this priority**: Encryption is only useful if decryption works seamlessly. This story ensures the read path is as smooth as the write path.

**Independent Test**: As a recipient, retrieve a transaction and verify the disclosed fields are returned decrypted while non-disclosed fields are absent.

**Acceptance Scenarios**:

1. **Given** a recipient retrieves a transaction containing encrypted payloads, **When** they provide their wallet address, **Then** the system identifies their payload group(s) and returns decrypted disclosed fields.
2. **Given** a recipient is not included in any disclosure group, **When** they attempt to access the transaction payload, **Then** they receive an access denied response (no ciphertext is returned).
3. **Given** a recipient's key has been rotated since the transaction was created, **When** they attempt decryption with their new key, **Then** decryption fails with a clear message that the original key is required.

---

### Edge Cases

- What happens when a recipient's public key cannot be resolved from the register and no external key is provided? The encryption pipeline skips that recipient and reports a warning.
- What happens when the register service is temporarily unavailable during public key resolution? The encryption pipeline retries with exponential backoff (up to 3 attempts) then fails the operation with a retryable error.
- What happens when a transaction has only one recipient? The system still encrypts to maintain a consistent on-ledger format.
- What happens when all recipients share the exact same disclosure (all fields)? One disclosure group is created with all recipients — the most efficient case.
- What happens when payload data is empty (e.g., a status-change action with no payload fields)? No encryption is needed. The transaction is built with an empty payload section.
- What happens when the symmetric encryption key generation fails? The operation fails immediately with a cryptographic error. No partial transaction is created.
- What happens when one recipient's key wrapping fails but others succeed? The entire operation fails atomically — no partial encryption. The error identifies the failing recipient and algorithm. (Note: this applies to *cryptographic* failures during wrapping, not key *resolution* failures — see edge case 1 above.)

## Requirements *(mandatory)*

### Functional Requirements

**Encryption Pipeline:**

- **FR-001**: System MUST encrypt all action payload data before including it in a transaction. No plaintext payload content may be stored in the register.
- **FR-002**: System MUST use envelope encryption: generate a random symmetric key (XChaCha20-Poly1305 default), encrypt data with the symmetric key, then wrap the symmetric key individually for each recipient using their public key.
- **FR-003**: System MUST group recipients by identical disclosure field sets and produce one ciphertext per unique group, wrapping the symmetric key for each group member.
- **FR-004**: System MUST include a SHA-256 hash of the plaintext data alongside each encrypted payload group for post-decryption integrity verification.
- **FR-005**: System MUST support all four encryption-capable algorithms for key wrapping: ED25519 (Curve25519 sealed box), NIST P-256 (ECIES), RSA-4096 (OAEP-SHA256), and ML-KEM-768 (KEM encapsulation).
- **FR-006**: System MUST allow mixed algorithms within a single transaction — different recipients may use different wallet algorithms.

**Public Key Resolution:**

- **FR-007**: System MUST resolve recipient public keys from the register's published participant index by wallet address.
- **FR-008**: System MUST support batch public key resolution (multiple addresses in a single request) to avoid N sequential network calls.
- **FR-009**: System MUST reject encryption for revoked participants (register returns 410 Gone) with a clear error.
- **FR-010**: System MUST accept externally-provided public keys as an override, bypassing register lookup for that recipient.

**Algorithm Completeness:**

- **FR-011**: System MUST implement NIST P-256 ECIES encryption (ECDH key agreement + HKDF-SHA256 + AES-256-GCM) for key wrapping.
- **FR-012**: System MUST fix ML-KEM-768 decapsulation to use proper KEM decapsulation (not generic asymmetric decrypt).

**Async Processing & UX:**

- **FR-013**: System MUST return an immediate acknowledgement (with operation tracking identifier) after synchronous validation/calculation/routing/disclosure completes.
- **FR-014**: System MUST perform encryption and transaction submission asynchronously in the background.
- **FR-015**: System MUST send real-time progress notifications including: step name, step number, total steps, and percentage complete.
- **FR-016**: System MUST send a completion notification with the transaction hash on success, or a failure notification with error details on failure.
- **FR-017**: System MUST make completion/failure status available as persistent notifications for users who are not connected at the time.

**Transaction Size:**

- **FR-018**: Validator MUST enforce a configurable maximum transaction size (default: 4 MB) and reject oversized transactions with a clear error code.
- **FR-019**: Encryption pipeline MUST estimate encrypted transaction size before starting encryption and fail early if the estimate exceeds the configured limit.

**Decryption:**

- **FR-020**: System MUST allow recipients to decrypt their disclosed payload by identifying their payload group(s), unwrapping the symmetric key via the wallet service, and decrypting the ciphertext.
- **FR-021**: System MUST deny access to payload groups where the requesting wallet address is not listed as a recipient.
- **FR-022**: Decryption MUST verify the SHA-256 integrity hash after decrypting to confirm data has not been tampered with.

**Atomicity:**

- **FR-023**: Encryption MUST be atomic per transaction for cryptographic failures — if any recipient's key wrapping or symmetric encryption fails, the entire encryption operation fails. No partial transactions are created. Note: key *resolution* failures (recipient not found, no external key provided) are handled separately: the recipient is skipped with a warning included in the operation result, not treated as an atomic failure.

### Key Entities

- **EncryptedPayloadGroup**: A single ciphertext produced from one disclosure field set, plus a map of recipient wallet addresses to their individually-wrapped symmetric keys, the encryption algorithm used, and a plaintext integrity hash.
- **DisclosureGroup**: An intermediate grouping of recipients who share identical disclosure field sets, used to minimize the number of encryption operations.
- **WrappedKey**: A symmetric key encrypted (wrapped) for a specific recipient using their public key and algorithm. Contains the ciphertext of the wrapped key and the algorithm identifier.
- **EncryptionOperation**: A trackable background operation with status (pending, encrypting, submitting, complete, failed), progress percentage, and result (transaction hash or error).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All action transaction payloads stored in the register contain only ciphertext — zero plaintext field names or values are recoverable without a recipient's private key.
- **SC-002**: Recipients using any of the four supported wallet algorithms (ED25519, P-256, RSA-4096, ML-KEM-768) can successfully decrypt their disclosed payloads with 100% round-trip fidelity.
- **SC-003**: For actions with N recipients sharing M distinct disclosure sets (where M < N), exactly M ciphertexts are produced (not N), reducing encrypted payload size proportionally.
- **SC-004**: Encryption of a typical action (5 recipients, 3 disclosure groups, 10 KB payload) completes in under 2 seconds.
- **SC-005**: Users receive the first progress notification within 500ms of action submission acknowledgement.
- **SC-006**: Encrypted transactions for typical actions (under 50 recipients) fit within the 4 MB transaction size limit.
- **SC-007**: All existing tests continue to pass — encryption integration does not break the current action execution flow.
- **SC-008**: The validator correctly rejects 100% of transactions exceeding the configured size limit.

## Assumptions

1. **In-process symmetric encryption**: Symmetric key generation and payload encryption happen in-process within the Blueprint Service (not via Wallet Service HTTP calls). The Wallet Service is only invoked for operations requiring stored private keys (signing, decryption). This avoids network overhead and strict rate limiting on the wallet endpoints.
2. **Existing disclosure processor unchanged**: The DisclosureProcessor (JSON Pointer filtering) continues to work as-is. Encryption is a new step AFTER disclosure filtering, not a replacement.
3. **Register participant records**: The majority of recipients will have published participant records with public keys on the register. The batch resolution endpoint is a new addition.
4. **Backward compatibility**: Existing unencrypted transactions already on registers remain readable. The system recognises both encrypted (v1) and legacy (unencrypted) payload formats.
5. **Transaction size default**: 4 MB is sufficient for typical multi-recipient encrypted actions. This is configurable per deployment.
6. **XChaCha20-Poly1305 default**: The symmetric cipher default is XChaCha20-Poly1305 (already the platform default). AES-256-GCM is also supported.

## Scope Boundaries

**In scope:**
- Encryption of action payloads in the Blueprint Service pipeline
- Disclosure group optimization
- P-256 ECIES implementation in Sorcha.Cryptography
- ML-KEM-768 decapsulation fix in Wallet Service
- Batch public key resolution endpoint in Register Service
- Async encryption pipeline with SignalR progress
- Transaction size enforcement in Validator
- Recipient decryption flow
- Encrypted transaction wire format

**Out of scope (deferred):**
- Validator-enforced disclosure verification (TRUST-2 — requires separate research)
- Key rotation and re-encryption of historical payloads (TRUST-10)
- File attachment encryption (separate transaction type)
- Homomorphic encryption
- Azure Key Vault integration for encryption keys
- UI components for decryption (API-level only for this feature)
