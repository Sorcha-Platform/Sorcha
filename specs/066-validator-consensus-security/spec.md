# Feature Specification: Validator Consensus Security

**Feature Branch**: `066-validator-consensus-security`
**Created**: 2026-03-22
**Status**: Draft
**Input**: Security audit findings 4.5 (Validator Approval Workflow), 4.1 (Consensus Vote Verification), 4.2 (Transaction Replay Protection)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validator Approval Governance (Priority: P1)

A system administrator manages which validator nodes are permitted to participate in the consensus network. When a new validator registers, it enters a pending state and cannot influence consensus until an administrator explicitly approves it. The administrator uses a dedicated page in the Admin UI to review pending validators, inspect their public keys, and approve, suspend, or revoke them.

**Why this priority**: Without approval governance, any node can register as a validator and influence consensus — the most critical security gap identified in the audit. This is the foundation that enables vote verification (US2).

**Independent Test**: Can be fully tested by registering a validator, confirming it cannot participate in consensus, then approving it via the Admin UI and verifying it now appears in the active validator list.

**Acceptance Scenarios**:

1. **Given** a new validator registers with its public key, **When** the registration completes, **Then** the validator status is "Pending" and it does not appear in the active validator list used by consensus.
2. **Given** a pending validator exists, **When** a system administrator approves it via the Admin UI, **Then** the validator status transitions to "Active" and it begins participating in consensus.
3. **Given** an active validator exists, **When** a system administrator suspends it, **Then** the validator status transitions to "Suspended", it is immediately excluded from consensus, and other validators are notified of the change.
4. **Given** a suspended or active validator, **When** a system administrator revokes it, **Then** the validator status transitions to "Revoked" (terminal state), its consensus votes are no longer accepted, and it cannot be re-activated.
5. **Given** the Admin UI validator management page, **When** an administrator opens it, **Then** they see a list of all validators with their status, public key fingerprint, registration date, and approval history.
6. **Given** the validator registry data, **When** the system restarts, **Then** all validator statuses are preserved (durable storage) and consensus resumes with the correct set of active validators.

---

### User Story 2 - Consensus Vote Cryptographic Verification (Priority: P2)

When validators vote on proposed dockets, each vote must be cryptographically signed by the voting validator's registered key. The consensus engine verifies every vote signature before counting it. Votes with invalid, missing, or unrecognised signatures are rejected and logged.

**Why this priority**: Depends on US1 (validator registry with keys). Prevents vote impersonation attacks where a malicious peer casts fake approval votes to corrupt dockets.

**Independent Test**: Can be tested by submitting a docket for consensus, verifying that votes from registered validators are counted, and that votes with forged or missing signatures are rejected.

**Acceptance Scenarios**:

1. **Given** a docket is proposed for consensus, **When** an active validator casts a vote signed with its registered private key, **Then** the vote signature is verified against the validator's registered public key and the vote is counted.
2. **Given** a docket is proposed for consensus, **When** a vote arrives with a signature that does not match any registered validator's public key, **Then** the vote is rejected and a security warning is logged.
3. **Given** a docket is proposed for consensus, **When** a vote arrives without a signature, **Then** the vote is rejected.
4. **Given** a docket is proposed for consensus, **When** a vote arrives from a validator whose status is "Suspended" or "Revoked", **Then** the vote is rejected regardless of signature validity.
5. **Given** multiple validators voting simultaneously, **When** signature verification runs, **Then** all verifications complete within the existing consensus timeout window.

---

### User Story 3 - Transaction Replay Protection (Priority: P3)

Each participant wallet maintains a monotonically increasing sequence number per register. When submitting a transaction, the sender includes the next expected sequence number. The validator rejects transactions with out-of-sequence numbers, preventing replay attacks where previously valid transactions are resubmitted.

**Why this priority**: Independent of US1/US2 but lower urgency — the existing idempotency key in the Blueprint Service provides partial protection. This adds chain-level replay protection as defense-in-depth.

**Independent Test**: Can be tested by submitting a transaction with sequence number N, confirming it succeeds, then resubmitting the same transaction (same sequence number N) and confirming it is rejected.

**Acceptance Scenarios**:

1. **Given** a wallet has no prior transactions on a register, **When** it submits a transaction with sequence number 1, **Then** the transaction is accepted and the wallet's sequence number is recorded as 1.
2. **Given** a wallet's last sequence number on a register is N, **When** it submits a transaction with sequence number N+1, **Then** the transaction is accepted and the recorded sequence number updates to N+1.
3. **Given** a wallet's last sequence number on a register is N, **When** it submits a transaction with sequence number N (replay), **Then** the transaction is rejected with a replay protection error.
4. **Given** a wallet's last sequence number on a register is N, **When** it submits a transaction with sequence number N+2 (gap), **Then** the transaction is rejected with a sequence gap error, allowing the client to retry with the correct number.
5. **Given** a genesis or control transaction, **When** it is submitted, **Then** it uses sequence number 0 and bypasses per-wallet sequence validation.
6. **Given** the sequence number store, **When** a client needs to know its next sequence number, **Then** it can query the current sequence number for its wallet on a specific register.

---

### Edge Cases

- What happens when a validator is revoked while it has pending votes in-flight? Votes from revoked validators must be discarded even if they were signed before revocation.
- How does the system handle a validator key rotation? A validator should be able to register a new key (creating a new pending registration) while the old key remains active until the new one is approved.
- What happens when the sequence number store is unavailable? The validator should reject transactions (fail-closed) rather than accepting potentially replayed transactions.
- What happens when two transactions from the same wallet arrive simultaneously with the same sequence number? Only one should succeed; the other receives a sequence conflict error.
- What happens when a validator is the only active validator and gets suspended? The system should prevent the last active validator from being suspended (or warn the administrator).
- What happens when a validator re-registers after revocation? A new registration should be created with a new identity — the revoked record remains for audit purposes.

## Requirements *(mandatory)*

### Functional Requirements

**Validator Approval Workflow (4.5)**

- **FR-001**: System MUST support a validator lifecycle with states: Pending, Active, Suspended, Revoked
- **FR-002**: Newly registered validators MUST start in the Pending state
- **FR-003**: Only validators in the Active state MUST participate in consensus voting
- **FR-004**: System administrators MUST be able to transition validators between allowed states (Pending to Active, Active to Suspended, Suspended to Active, Active/Suspended to Revoked)
- **FR-005**: Revoked is a terminal state — revoked validators MUST NOT be re-activated
- **FR-006**: Validator registrations MUST include the validator's public key for signature verification
- **FR-007**: Validator state changes MUST be persisted durably (survive service restarts)
- **FR-008**: The system MUST prevent suspension or revocation of the last remaining active validator to avoid consensus deadlock
- **FR-009**: All validator state transitions MUST be audit-logged with the administrator identity and timestamp

**Admin UI (4.5)**

- **FR-010**: The Admin UI MUST provide a validator management page accessible only to system administrators
- **FR-011**: The page MUST display all validators with their status, public key fingerprint, registration date, and last state change
- **FR-012**: Administrators MUST be able to approve, suspend, and revoke validators from the UI
- **FR-013**: State transition actions MUST require confirmation before execution
- **FR-014**: The page MUST show real-time status updates when validator states change

**Consensus Vote Verification (4.1)**

- **FR-015**: Every consensus vote MUST include a cryptographic signature from the voting validator
- **FR-016**: The consensus engine MUST verify each vote's signature against the voter's registered public key before counting the vote
- **FR-017**: Votes with invalid, missing, or unrecognised signatures MUST be rejected and logged as security events
- **FR-018**: Votes from non-Active validators (Pending, Suspended, Revoked) MUST be rejected regardless of signature validity
- **FR-019**: The vote signing contract MUST use a deterministic, canonical format that includes the docket identifier, docket hash, vote decision, and validator identity
- **FR-020**: Vote signature verification MUST complete within the existing consensus timeout window

**Transaction Replay Protection (4.2)**

- **FR-021**: Each transaction MUST include a sender sequence number
- **FR-022**: The system MUST maintain a per-wallet, per-register sequence number counter
- **FR-023**: The validator MUST reject transactions where the sequence number does not equal the sender's last known sequence number plus one
- **FR-024**: Genesis and control transactions MUST use sequence number 0 and bypass per-wallet sequence validation
- **FR-025**: Clients MUST be able to query their current sequence number for a given register
- **FR-026**: The sequence number store MUST be durable and consistent (a rejected transaction must not increment the counter)

### Key Entities

- **ValidatorRegistration**: Represents a validator node in the network. Attributes: identity, public key, status (Pending/Active/Suspended/Revoked), registration timestamp, approval details (who approved, when), last state change
- **ConsensusVote**: A validator's signed vote on a proposed docket. Attributes: docket reference, vote decision (approve/reject), validator identity, cryptographic signature, timestamp
- **WalletSequence**: Tracks the last used sequence number for a wallet on a register. Attributes: register identity, wallet address, last sequence number, last updated timestamp

## Assumptions

- The existing multi-algorithm cryptography module (ED25519, P-256, RSA-4096) will be used for vote signing and verification — no new algorithms are needed.
- Validator nodes already generate and manage their own key pairs; this feature does not handle key generation for validators.
- The Admin UI uses the existing Blazor WASM application at `Sorcha.UI.Web.Client/Pages/Admin/` with MudBlazor components, `[Authorize(Roles = "Administrator")]`, and `InteractiveWebAssemblyRenderMode(prerender: false)`.
- Sequence numbers start at 1 for participant transactions; 0 is reserved for genesis/control transactions.
- The consensus timeout window is currently 30 seconds (configurable) and vote verification must fit within this budget.
- Validator status changes propagate to all validator instances via the existing Redis pub/sub infrastructure.

## Dependencies

- US2 (Consensus Vote Verification) depends on US1 (Validator Approval Workflow) for the validator registry with public keys.
- US3 (Transaction Replay Protection) is independent and can be implemented in parallel with US1/US2.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero unauthorized validators can influence consensus outcomes (100% of votes from non-Active validators are rejected)
- **SC-002**: 100% of consensus votes are cryptographically verified before being counted
- **SC-003**: System administrators can review and approve a pending validator in under 60 seconds via the Admin UI
- **SC-004**: Replayed transactions (duplicate sequence numbers) are rejected with a clear error in 100% of cases
- **SC-005**: All validator state transitions are audit-logged and visible in the Admin UI
- **SC-006**: Consensus continues to complete within its configured timeout window after vote verification is added (no regression in consensus latency beyond 10%)
- **SC-007**: Validator registry data survives service restarts with zero data loss
- **SC-008**: Clients can determine their next valid sequence number before submitting a transaction
