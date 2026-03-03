# Feature Specification: Unified Transaction Submission & System Wallet Signing Service

**Feature Branch**: `036-unified-transaction-submission`
**Created**: 2026-02-18
**Status**: Draft
**Input**: User description: "Unified Transaction Submission & System Wallet Signing Service — requirements are in docs/reference/transaction-submission-flow.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - System Wallet Signing Service (Priority: P1)

Platform operators need a centralised, secure mechanism for system-level transaction signing. Currently, system wallet signing logic is duplicated across multiple components (the validator genesis endpoint, the register creation orchestrator) with inconsistent retry/recovery behaviour. A single signing service ensures consistent security controls, audit trails, and wallet lifecycle management.

**Why this priority**: Without a secure, shared signing service, every caller that needs system-level signing must independently manage wallet acquisition, signing, retries, and error handling. This duplication is the root cause of the current architectural split between the genesis endpoint and the generic endpoint. Solving this first unblocks all other stories.

**Independent Test**: Can be tested by creating the signing service, injecting it into one caller (e.g. the register creation orchestrator), and verifying that a system-signed transaction is produced with correct audit logs and security controls.

**Acceptance Scenarios**:

1. **Given** a service with system signing capability, **When** a caller requests a system wallet signature for a valid transaction, **Then** the service acquires the system wallet, signs the transaction data, and returns the signature with public key and algorithm.
2. **Given** the system wallet does not yet exist, **When** a signing request is made, **Then** the service automatically creates the system wallet and completes the signing request.
3. **Given** the system wallet has become unavailable (deleted/expired), **When** a signing request is made, **Then** the service recreates the wallet and retries the signing operation, logging a warning.
4. **Given** a caller requests signing with an unrecognised derivation path (not in the operation whitelist), **When** the sign request is processed, **Then** the service rejects the request with a clear error.
5. **Given** a caller has exceeded the rate limit for system signing on a specific register, **When** another sign request arrives for the same register, **Then** the service rejects it with a rate-limit error.
6. **Given** any system wallet signing operation (success or failure), **When** the operation completes, **Then** an audit log entry is recorded containing: caller service identity, register ID, transaction type, resulting TxId, derivation path, and timestamp.

---

### User Story 2 - Unified Transaction Submission (Priority: P1)

All transaction types (genesis, blueprint publish, governance, action) must be submitted through a single generic validation endpoint. Currently, genesis and control transactions use a separate endpoint with a different request model, violating the principle that the validator validates but does not sign. Callers must sign transactions before submission, and the validator must accept all transaction types through one uniform interface.

**Why this priority**: This is the core architectural change. The current split creates confusion, duplicated models, and a validator that inappropriately performs signing. Unifying submission is essential for a clean, maintainable transaction pipeline.

**Independent Test**: Can be tested by submitting a pre-signed genesis transaction to the generic endpoint and verifying it is accepted, validated, promoted to the verified queue, and included in a docket.

**Acceptance Scenarios**:

1. **Given** a fully signed genesis transaction (signed by the system wallet via the signing service), **When** submitted to the generic validation endpoint, **Then** the transaction is accepted, validated through all pipeline stages, and placed in the unverified pool.
2. **Given** a fully signed blueprint publish (control) transaction, **When** submitted to the generic validation endpoint, **Then** the transaction is accepted and processed identically to any other transaction.
3. **Given** a fully signed governance transaction, **When** submitted to the generic validation endpoint, **Then** the transaction is accepted and processed through the validation pipeline.
4. **Given** a transaction with missing or invalid signatures, **When** submitted to the generic endpoint, **Then** the endpoint rejects it with a clear validation error, regardless of transaction type.
5. **Given** a genesis transaction submitted to the generic endpoint, **When** the validation pipeline processes it, **Then** blueprint conformance and schema validation stages are skipped (genesis has no associated blueprint schema), but structure, hash, signature, chain, and timing validation run normally.
6. **Given** a transaction of any type submitted to the generic endpoint, **When** the register has no prior transactions, **Then** the docket builder creates a genesis docket (number 0) for the first transaction.

---

### User Story 3 - Register Creation Uses Unified Submission (Priority: P2)

The register creation orchestrator must sign genesis transactions locally (using the system wallet signing service) and submit them through the generic validation endpoint, rather than relying on a special genesis endpoint that signs on their behalf.

**Why this priority**: Register creation is the most critical system-level flow. Migrating it to the unified path validates that the signing service and generic endpoint work correctly for the most important transaction type.

**Independent Test**: Can be tested end-to-end by creating a new register and verifying the genesis transaction flows through the generic endpoint, receives a docket, and the register comes online.

**Acceptance Scenarios**:

1. **Given** a register creation request with signed attestations, **When** the orchestrator finalises the register, **Then** it uses the system wallet signing service to sign the genesis transaction, and submits the fully-signed transaction to the generic validation endpoint.
2. **Given** the validator's generic endpoint receives the genesis transaction, **When** it processes through the validation pipeline, **Then** the transaction passes all applicable validation stages and is promoted to the verified queue.
3. **Given** the genesis transaction is in the verified queue, **When** the docket builder processes it, **Then** a genesis docket (number 0) is created and written to the register.

---

### User Story 4 - Blueprint Publish Uses Unified Submission (Priority: P2)

The blueprint publish endpoint must sign control transactions locally and submit through the generic validation endpoint, instead of routing through the genesis endpoint.

**Why this priority**: Blueprint publish was the immediate trigger for this refactoring — it was incorrectly using the genesis endpoint. Fixing this validates that non-genesis control transactions work through the unified path.

**Independent Test**: Can be tested by publishing a blueprint to a register and verifying the control transaction flows through the generic endpoint, receives a docket, and appears in the register's transaction history.

**Acceptance Scenarios**:

1. **Given** a blueprint publish request for a register, **When** the publish endpoint processes it, **Then** it uses the system wallet signing service to sign a control transaction, and submits it to the generic validation endpoint.
2. **Given** the control transaction is submitted with the actual blueprint ID (not "genesis"), **When** the validation pipeline processes it, **Then** it passes all applicable stages (blueprint conformance is skipped for control transactions).
3. **Given** a successfully validated blueprint publish transaction, **When** the docket builder processes it, **Then** it is included in the next docket and the transaction appears in the register's ledger with the correct docket number.

---

### User Story 5 - Legacy Genesis Endpoint Deprecation (Priority: P3)

Once all callers use the unified submission path, the legacy genesis endpoint must be deprecated and eventually removed, along with its associated request models and client methods.

**Why this priority**: Cleanup work that depends on all other stories being complete. Removing dead code reduces maintenance burden and eliminates the risk of callers accidentally using the old path.

**Independent Test**: Can be tested by verifying all callers use the generic endpoint, then removing the genesis endpoint and confirming all system flows still work.

**Acceptance Scenarios**:

1. **Given** all callers (register creation, blueprint publish, governance) have been migrated to the generic endpoint, **When** the legacy genesis endpoint is removed, **Then** all system operations continue to function correctly.
2. **Given** the genesis endpoint is removed, **When** a caller attempts to submit to the old endpoint, **Then** they receive a clear error indicating the endpoint no longer exists.
3. **Given** the legacy endpoint removal, **When** the service client models are cleaned up, **Then** only one submission method and one request model remain on the validator service client.

---

### Edge Cases

- What happens when the system wallet is unavailable during register creation? The signing service retries wallet creation; if it fails after retries, the register creation fails cleanly with a descriptive error and no partial state is left behind.
- What happens if a genesis transaction is submitted with an empty signatures list? The generic endpoint rejects it at the structure validation stage — signatures are required for all transaction types.
- What happens if two blueprint publish requests for the same blueprint and register are submitted simultaneously? The deterministic TxId generation ensures idempotency — the second submission is rejected as a duplicate by the memory pool.
- What happens if the rate limiter blocks a legitimate system signing request during a burst of register creations? The caller receives a rate-limit error and can retry after the configured window. Rate limits are configurable per deployment.
- What happens if a Control transaction is submitted with a BlueprintId that matches an existing blueprint? The validation pipeline detects the transaction type from metadata (`Type=Control`) and skips blueprint conformance, regardless of the BlueprintId value.
- What happens if the validator pipeline has hard-coded checks that reject genesis BlueprintId values? The pipeline must be audited and updated to be type-agnostic — no transaction type should receive special rejection logic at the submission layer.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST provide a single, uniform transaction submission endpoint that accepts all transaction types (genesis, control, action, governance) using one request model.
- **FR-002**: The platform MUST provide a system wallet signing service that encapsulates wallet acquisition, signing, and lifecycle management (creation, recovery, retry).
- **FR-003**: The system wallet signing service MUST only be available to services that explicitly opt in to system signing capability. It MUST NOT be automatically available to all services.
- **FR-004**: The system wallet signing service MUST enforce an operation whitelist — only recognised derivation paths / signing purposes are permitted. Unrecognised operations MUST be rejected.
- **FR-005**: The system wallet signing service MUST log an audit entry for every signing operation (success or failure) containing: caller identity, register ID, transaction type, TxId, derivation path, and timestamp.
- **FR-006**: The system wallet signing service MUST enforce configurable rate limits on system signing operations per register per time window.
- **FR-007**: The register creation orchestrator MUST sign genesis transactions locally using the system wallet signing service and submit them through the generic validation endpoint.
- **FR-008**: The blueprint publish endpoint MUST sign control transactions locally using the system wallet signing service and submit them through the generic validation endpoint.
- **FR-009**: The validation pipeline MUST process all transaction types uniformly — no transaction type should receive special treatment at the submission or initial validation layer.
- **FR-010**: The validation pipeline MUST skip blueprint conformance and schema validation for genesis and control transactions (identified by metadata), while still performing structure, hash, signature, chain, and timing validation.
- **FR-011**: The legacy genesis endpoint MUST be deprecated once all callers are migrated, and eventually removed along with its associated request models and client methods.
- **FR-012**: All existing transaction submission flows (register creation, blueprint publish, action execution) MUST continue to function correctly after the migration to the unified endpoint.
- **FR-013**: The system wallet signing service MUST handle wallet unavailability gracefully — automatically recreating the wallet if it has been deleted or become inaccessible, with appropriate warning logs.

### Key Entities

- **System Wallet Signing Service**: A controlled, audited service that manages system wallet lifecycle and produces cryptographic signatures for system-level transactions. Available only to authorised services via explicit opt-in.
- **Transaction Submission**: A uniform request model carrying transaction ID, register ID, blueprint ID, action ID, payload, payload hash, pre-computed signatures, metadata, and optional chain linkage. Used for all transaction types.
- **Audit Log Entry**: A record of every system signing operation containing caller identity, register ID, transaction type, TxId, derivation path, timestamp, and outcome (success/failure).
- **Operation Whitelist**: A configurable set of permitted derivation paths that the signing service will accept. Any request outside this set is rejected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four transaction types (genesis, blueprint publish, action, governance) are successfully submitted and processed through a single validation endpoint with zero type-specific submission logic.
- **SC-002**: Register creation completes end-to-end (initiate, finalise, genesis docket created, register online) using the unified submission path, with no regression in success rate.
- **SC-003**: Blueprint publish completes end-to-end (publish request, control transaction validated, docket created, transaction visible in register) using the unified submission path.
- **SC-004**: Every system wallet signing operation produces an audit log entry — 100% coverage with zero gaps.
- **SC-005**: Signing requests with unrecognised derivation paths are rejected 100% of the time.
- **SC-006**: Rate-limited signing requests are correctly throttled — no more than the configured maximum signs per register per time window.
- **SC-007**: The legacy genesis endpoint and all associated models are removed from the codebase, reducing the validator service client to a single submission method.
- **SC-008**: All existing tests for transaction submission, register creation, and blueprint publish continue to pass after migration, with additional tests covering the new signing service and unified submission path.
- **SC-009**: System wallet recovery (auto-recreate on unavailability) succeeds within 2 retry attempts, with no caller-visible downtime beyond the retry delay.

### Assumptions

- The existing generic validation endpoint and its request model are already functionally sufficient for all transaction types — no new fields are needed.
- The Register Service already has access to the Wallet Service client for signing (confirmed: RegisterCreationOrchestrator already injects the wallet client).
- The validation pipeline's genesis-specific bypasses (signature skip for genesis transactions) can be safely removed once callers provide real signatures.
- Rate limit configuration uses standard deployment configuration patterns (environment variables / configuration files).
- The signing service's operation whitelist is a compile-time or configuration-time constant — it does not need to be dynamically modifiable at runtime.
