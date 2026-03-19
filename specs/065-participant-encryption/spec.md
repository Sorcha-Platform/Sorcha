# Feature Specification: Participant Resolution, Starting Action Binding & Field-Level Encryption

**Feature Branch**: `065-participant-encryption`
**Created**: 2026-03-19
**Status**: Draft
**Input**: Blueprint participant resolution (role-based not wallet-bound), anonymous/any-wallet starting actions, and field-level encryption integration with DevMode per-register setting.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Any Member of the Public Can Start a Workflow (Priority: P1)

A citizen (or any member of the public) can start a council workflow without having to be pre-registered as a named participant in the blueprint. They visit the council's service catalogue, select a workflow (e.g. "Apply for Council ID"), and submit the starting action using their own wallet. Their wallet address becomes bound to the "citizen" participant role for the lifetime of that workflow instance.

**Why this priority**: Without this, no external user can initiate a workflow. The platform currently requires wallet addresses to be hardcoded into blueprints at publish time, which is impossible for unknown future users. This is the minimum viable interaction.

**Independent Test**: Can be tested by publishing a blueprint with a "citizen" participant (no wallet address), starting an instance with any wallet, and verifying the action is accepted and the sender is bound to the participant role.

**Acceptance Scenarios**:

1. **Given** a published blueprint where the starting action's sender participant has no wallet address, **When** any authenticated user submits the starting action with their wallet, **Then** the action is accepted and the sender's wallet is recorded as the bound address for that participant in this instance.
2. **Given** an active instance where a citizen was bound in action 0, **When** a later action requires the same "citizen" participant, **Then** only the originally-bound wallet can submit that action.
3. **Given** a published blueprint, **When** a user submits a non-starting action without having been bound as a participant, **Then** the system rejects the action with a clear error indicating the sender is not an authorised participant.

---

### User Story 2 - Organisational Participants Resolved from Published Records (Priority: P1)

Council staff (e.g. ID Department, Service Desk) are defined in the blueprint as organisational roles, not individual wallet addresses. When a council department needs to act on a workflow step, the system looks up the department's published participant record on the register to determine which wallet addresses are authorised to sign. Multiple staff members may have signing authority for one department through delegated wallet access.

**Why this priority**: Tightly coupled with Story 1 — together they replace the hardcoded wallet model. Without this, organisational participants can't act on workflows unless their personal wallet is baked into every blueprint.

**Independent Test**: Can be tested by publishing a participant record for "ID Department" with two authorised wallet addresses, then submitting an action from each — both should be accepted.

**Acceptance Scenarios**:

1. **Given** a blueprint action assigned to participant "id-dept" (organisation: "Ashwick Council"), **When** the validator processes the action, **Then** it resolves the participant by looking up published participant records on the register for that organisation and participant name.
2. **Given** a published participant record with multiple wallet addresses (primary + delegates), **When** any of those wallets submits the action, **Then** the action is accepted.
3. **Given** a published participant record that has been revoked, **When** a wallet from that record submits an action, **Then** the action is rejected with a clear error.
4. **Given** a blueprint action assigned to an organisational participant with no published record on the register, **Then** the action is rejected with an error indicating the participant is not registered.

---

### User Story 3 - DevMode Registers Store Plaintext Payloads (Priority: P2)

A platform administrator creating a register for development or testing purposes can enable "DevMode" on the register. In DevMode, action payloads are stored as plaintext JSON with disclosure rules still evaluated (so access control is enforced at read time), but no cryptographic encryption is performed. This allows developers to inspect payloads directly in the database, debug disclosure logic, and iterate without the overhead of key management.

**Why this priority**: Enables incremental development and testing. Without DevMode, every action requires full envelope encryption which depends on resolved public keys — creating a circular dependency during development.

**Independent Test**: Can be tested by creating a DevMode register, executing an action, and verifying the payload is stored as readable JSON in the database while disclosure filtering still controls what each participant sees when querying.

**Acceptance Scenarios**:

1. **Given** a register with DevMode enabled, **When** an action payload is submitted, **Then** the payload is stored as plaintext JSON (not encrypted) on the register.
2. **Given** a register with DevMode enabled, **When** a participant queries their actions, **Then** the disclosure rules are applied to filter which fields they can see, even though the data is not encrypted.
3. **Given** a register with DevMode disabled (normal mode), **When** an action payload is submitted, **Then** the payload is encrypted using envelope encryption before storage.
4. **Given** a register initially in DevMode, **When** an administrator disables DevMode, **Then** new payloads are encrypted from that point forward (existing plaintext payloads remain readable but are flagged as legacy).

---

### User Story 4 - Field-Level Encryption Based on Disclosure Rules (Priority: P3)

When DevMode is disabled on a register, action payloads are encrypted at the field level based on the blueprint's disclosure rules. Each unique set of disclosed fields produces one encrypted group, with the symmetric key wrapped individually for each authorised recipient. Recipients can only decrypt the fields they are authorised to see.

**Why this priority**: This is the full security model — the "D" in DAD. Depends on Stories 1-3 being functional first (participant resolution provides public keys, DevMode allows testing without encryption).

**Independent Test**: Can be tested by submitting an action with two participants having different disclosure rules, then verifying each participant can only decrypt their authorised fields.

**Acceptance Scenarios**:

1. **Given** a non-DevMode register and an action where citizen sees `["/decision"]` and id-dept sees `["/*"]`, **When** the action is executed, **Then** two encrypted groups are created: one containing only `decision` (wrapped for citizen's key) and one containing all fields (wrapped for id-dept's key).
2. **Given** an encrypted action payload, **When** the citizen queries their data, **Then** they receive the decrypted `decision` field only.
3. **Given** an encrypted action payload, **When** an unauthorised wallet queries the data, **Then** they receive no payload content.
4. **Given** two participants with identical disclosure rules, **When** the action is encrypted, **Then** only one ciphertext group is created with two wrapped keys (disclosure group optimisation).
5. **Given** a payload exceeding the register's size limit after encryption, **When** the action is submitted, **Then** the system rejects it with a clear size error before attempting encryption.

---

### Edge Cases

- What happens when a starting action is submitted by a wallet that is already bound to a different participant role in the same instance? The system rejects it — one wallet can only be bound to one participant role per instance.
- What happens when a published participant record is updated (e.g. new delegate added) mid-workflow? Actions submitted after the update respect the new record; already-completed actions are unaffected.
- What happens when the only authorised wallet for a participant is revoked mid-workflow? The workflow becomes blocked at that action. The system surfaces this as a clear status ("awaiting participant — authorised signers unavailable") rather than silently waiting.
- What happens when a register transitions from DevMode to normal mode? New actions are encrypted. Existing plaintext payloads remain readable. The transition point is recorded in register metadata.
- What happens when the encryption pipeline fails mid-action (e.g. one recipient's public key is unreachable)? The entire action fails atomically — no partial encryption is stored.
- What happens when a participant has wallets using different cryptographic algorithms (e.g. one ED25519, one ML-KEM-768)? Each wrapped key uses the algorithm of the recipient's wallet. The symmetric ciphertext is shared; only the key wrapping differs per recipient.

## Requirements *(mandatory)*

### Functional Requirements

**Participant Resolution**

- **FR-001**: Blueprints MUST define participants by role identifier and organisation name, without requiring wallet addresses at publish time.
- **FR-002**: The system MUST resolve organisational participants at action execution time by looking up published participant records on the register, matching by participant role and organisation.
- **FR-003**: Published participant records MUST support multiple wallet addresses per participant (primary + delegates), all of which are authorised to sign actions for that role.
- **FR-004**: The system MUST reject actions from wallets that are not authorised for the target participant role (either by instance binding or published record lookup).

**Starting Action Binding**

- **FR-005**: Starting actions (`isStartingAction: true`) MUST accept submissions from any authenticated wallet, regardless of whether that wallet appears in the blueprint or any published participant record.
- **FR-006**: When a starting action is executed, the system MUST bind the sender's wallet address to the sender's participant role for the lifetime of that workflow instance.
- **FR-007**: For non-starting actions, the system MUST enforce that the sender's wallet matches either an instance-bound participant or an authorised address in a published participant record.
- **FR-008**: Instance participant bindings MUST be immutable once created — a participant role cannot be re-bound to a different wallet mid-workflow.

**DevMode**

- **FR-009**: Registers MUST have a configurable DevMode setting that controls whether payloads are encrypted before storage.
- **FR-010**: In DevMode, action payloads MUST be stored as plaintext JSON with disclosure metadata preserved alongside the payload.
- **FR-011**: In DevMode, disclosure rules MUST still be enforced when participants query data — the system filters fields at read time based on the participant's disclosure paths.
- **FR-012**: DevMode can be disabled at any time; from that point, new payloads are encrypted. Existing plaintext payloads remain accessible and are identified as unencrypted.

**Field-Level Encryption**

- **FR-013**: When DevMode is disabled, the system MUST encrypt action payloads using envelope encryption (symmetric content encryption + per-recipient asymmetric key wrapping) before register storage.
- **FR-014**: Encryption MUST respect disclosure rules — each unique set of disclosed fields produces one encrypted payload group.
- **FR-015**: Recipients with identical disclosure rules MUST share a single ciphertext with individual key wraps (disclosure group optimisation to minimise storage).
- **FR-016**: The system MUST resolve recipient public keys from published participant records (for organisational roles) and from instance participant bindings (for dynamically-bound participants like citizens).
- **FR-017**: Encryption failures MUST be atomic — no partial or inconsistent payloads are stored if encryption fails for any recipient.
- **FR-018**: The system MUST enforce a per-register payload size limit and reject oversized payloads before attempting encryption, providing a clear error with the size estimate.

### Key Entities

- **Participant Role**: A named role within a blueprint (e.g. "citizen", "id-dept") associated with an organisation. Does not carry a wallet address — resolved at execution time.
- **Instance Participant Binding**: A runtime mapping from participant role to wallet address, created when a starting action is executed or when a known participant first acts. Immutable for the instance lifetime.
- **Published Participant Record**: An on-register identity record mapping an organisational function to one or more wallet addresses with public keys. Supports primary and delegated signers.
- **Disclosure Group**: An intermediate encryption unit grouping recipients who share identical field visibility. One ciphertext per group, with per-recipient wrapped keys.
- **Register DevMode Setting**: A per-register flag on the register's configuration. When enabled, payloads are stored as plaintext with disclosure metadata. When disabled, payloads are envelope-encrypted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Any authenticated user can start a published workflow in under 5 seconds, without requiring pre-registration in the blueprint.
- **SC-002**: Organisational participants can act on their workflow steps within 10 seconds, with the system resolving their authority from published records automatically.
- **SC-003**: In DevMode, the end-to-end action execution time (submit to confirmed on register) adds no more than 20% overhead compared to the current path, from disclosure evaluation.
- **SC-004**: With encryption enabled, the system handles payloads with up to 20 recipients without user-perceptible delay (under 15 seconds total including encryption and register confirmation).
- **SC-005**: Disclosure group optimisation reduces ciphertext count — 5 recipients with 2 unique disclosure sets produce exactly 2 encrypted groups, not 5.
- **SC-006**: The council credential E2E test (citizen applies for ID, department verifies, credential issued, service requested, fulfilled) completes end-to-end in DevMode within 3 minutes.
- **SC-007**: Field-level read access is enforced — a participant querying data they are not disclosed for receives zero fields, in both DevMode (read-time filtering) and encrypted mode (no decryption key available).

### Assumptions

- The existing `EncryptionPipelineService` in `Sorcha.TransactionHandler` is functionally correct for envelope encryption and disclosure grouping. This feature wires it into the action execution path rather than reimplementing it.
- Published participant records already exist as a concept in the Tenant Service and Register Service. This feature extends the Validator Service to resolve them during blueprint conformance validation.
- The symmetric encryption (XChaCha20-Poly1305) and multi-algorithm asymmetric key wrapping (ED25519, P-256, RSA-4096, ML-KEM-768) are already implemented in `Sorcha.Cryptography`.
- DevMode is a development and testing convenience. Production registers should always have DevMode disabled. No migration path from DevMode plaintext to retroactively encrypting existing payloads is required in this phase.
- The current E2E test infrastructure (council credential flow) serves as the primary integration test vehicle for this feature.
