# Feature Specification: Unified Register Policy Model & System Register

**Feature Branch**: `048-register-policy-model`
**Created**: 2026-03-04
**Status**: Draft
**Input**: User description: "Unified RegisterPolicy model that consolidates scattered per-register policy (validator config, consensus params, leader election, governance rules) into a single on-chain model embedded in the Control record. Includes: System Register bootstrap with .env flags, approved validator list on-chain with Redis TTL for operational presence, and backward compatibility for existing registers."

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register Creator Sets Policy at Genesis (Priority: P1)

A register owner creates a new register and establishes its operational policy — who can validate, what consensus rules apply, which governance model is used — all in a single genesis transaction. Today these settings are scattered across hardcoded defaults that the creator has no control over. With a unified policy model, the creator can make explicit choices (or accept sensible defaults) that are permanently recorded on-chain.

**Why this priority**: Without an explicit policy in the genesis block, every register runs on hardcoded defaults with no way to customise. This is the foundation for all other stories — nothing else works without a well-defined policy at genesis.

**Independent Test**: Can be fully tested by creating a register with a custom policy (e.g., consent-mode validators, supermajority quorum) and verifying the genesis transaction payload contains those settings.

**Acceptance Scenarios**:

1. **Given** a register creation request with no explicit policy settings, **When** the genesis transaction is finalized, **Then** the Control record contains a `RegisterPolicy` section with all default values populated (public validator mode, strict-majority quorum, rotating leader election).
2. **Given** a register creation request specifying `registrationMode: consent` and `maxValidators: 5`, **When** the genesis transaction is finalized, **Then** those values appear in the on-chain policy and the Validator Service reads them instead of hardcoded defaults.
3. **Given** an existing register created before this feature, **When** the Validator Service reads its genesis transaction, **Then** the system falls back to default policy values (backward compatibility) and continues operating normally.

---

### User Story 2 - Platform Operator Bootstraps the System Register (Priority: P1)

A platform operator starting a fresh Sorcha instance needs a way to seed the System Register — a special register that holds system blueprints (register-governance-v1, register-creation-v1) and blueprint templates. In development environments, this happens automatically on first startup. In production, it is a deliberate one-time provisioning step.

**Why this priority**: The System Register is the distribution mechanism for governance blueprints and templates. Without it, every register has to hardcode references to governance workflows. Tied with P1 because the genesis policy references blueprints that live on the System Register.

**Independent Test**: Can be fully tested by starting a fresh Sorcha instance with `SORCHA_SEED_SYSTEM_REGISTER=true`, verifying the System Register exists with a deterministic ID, and confirming system blueprints are published as transactions on it.

**Acceptance Scenarios**:

1. **Given** a fresh Sorcha instance with `SORCHA_SEED_SYSTEM_REGISTER=true`, **When** the Register Service starts, **Then** a System Register is created with a deterministic well-known ID, a bootstrap "system-setup" wallet as Owner (tied to the admin user, transferable via governance), and all system blueprints published as transactions.
2. **Given** a Sorcha instance where the System Register already exists, **When** the Register Service starts with `SORCHA_SEED_SYSTEM_REGISTER=true`, **Then** no duplicate register is created (idempotent).
3. **Given** a production Sorcha instance with `SORCHA_SEED_SYSTEM_REGISTER=false` (default), **When** the Register Service starts, **Then** no automatic System Register seeding occurs.
4. **Given** a government deployment requiring a custom governance blueprint, **When** the operator sets `SORCHA_SYSTEM_REGISTER_BLUEPRINT=custom-governance-v1`, **Then** the System Register seeds using that blueprint instead of the default `register-governance-v1`.

---

### User Story 3 - Register Admin Manages Approved Validator List (Priority: P2)

A register owner operating in consent mode needs to maintain a list of approved validators — organisations or services that are authorised to participate in consensus for their register. Being on the approved list means a validator is allowed to join, not that it is currently online. The real-time operational state remains in Redis with a heartbeat-driven TTL.

**Why this priority**: Depends on the policy model (Story 1) being in place. This story delivers the core value of consent-mode validation: register sovereignty over who validates their data.

**Independent Test**: Can be fully tested by creating a consent-mode register, approving a validator via governance proposal, verifying it appears in the on-chain approved list, and confirming it can register in Redis only if approved.

**Acceptance Scenarios**:

1. **Given** a register with `registrationMode: consent` and an empty approved list, **When** a validator attempts to register, **Then** the registration is rejected because the validator is not on the approved list.
2. **Given** a register admin who proposes adding a validator to the approved list, **When** quorum is reached and the Control transaction is committed, **Then** the validator's DID and public key appear in the on-chain `approvedValidators` list.
3. **Given** an approved validator that comes online, **When** it registers with the Validator Service, **Then** its DID is checked against the on-chain approved list, and if present, a Redis entry is created with a TTL (refreshed by heartbeat).
4. **Given** an approved validator that goes offline, **When** its Redis TTL expires, **Then** it disappears from the operational validator list but remains on the on-chain approved list (can reconnect later).
5. **Given** a register admin who proposes removing a validator from the approved list, **When** quorum is reached, **Then** the validator is removed from the on-chain list and cannot re-register in Redis after its current TTL expires.

---

### User Story 4 - Register Admin Updates Policy via Governance (Priority: P2)

A register admin needs to change operational policy after genesis — for example, switching from public to consent-mode validation, adjusting the consensus threshold, or changing the quorum formula. Policy changes follow the same governance workflow as admin roster changes: propose, vote, commit.

**Why this priority**: Depends on Story 1 (policy model) and the existing governance workflow. Delivers the ability to evolve register configuration over time without creating a new register.

**Independent Test**: Can be fully tested by creating a register with default policy, proposing a policy change (e.g., `registrationMode: public` to `consent`), achieving quorum, and verifying the new policy is active.

**Acceptance Scenarios**:

1. **Given** a register with `registrationMode: public`, **When** an admin proposes changing to `consent` and quorum is reached, **Then** a new Control transaction is committed containing the updated `RegisterPolicy` and the Validator Service begins enforcing consent-mode registration.
2. **Given** a policy update proposal, **When** the proposed policy fails validation (e.g., `maxValidators: 0`), **Then** the proposal is rejected with a clear validation error before the vote begins.
3. **Given** a policy update that changes the quorum formula from strict-majority to supermajority, **When** the Control transaction is committed, **Then** all subsequent governance proposals use the new quorum formula.
4. **Given** a register with active validators operating under the old policy, **When** the policy is updated, **Then** the transition mode specified in the policy update payload determines behavior: `immediate` ejects unapproved validators at commit time (cannot refresh TTL), `grace-period` allows unapproved validators to continue for one TTL cycle before enforcement.
5. **Given** a policy update switching from `public` to `consent` with `transitionMode: immediate`, **When** the Control transaction is committed, **Then** validators not on the approved list are immediately prevented from refreshing their Redis TTL.
6. **Given** a policy update switching from `public` to `consent` with `transitionMode: grace-period`, **When** the Control transaction is committed, **Then** unapproved validators continue operating until their current Redis TTL expires, after which they cannot re-register.

---

### User Story 5 - System Register Disseminates Blueprint Updates (Priority: P3)

A platform administrator publishes an updated system blueprint (e.g., `register-governance-v2`) to the System Register. All peers in the network receive the update via normal peer-to-peer replication. Individual registers can then propose upgrading their governance blueprint version through a governance vote.

**Why this priority**: Depends on the System Register existing (Story 2) and the policy model (Story 1). This completes the lifecycle: publish new versions centrally, let registers adopt them at their own pace.

**Independent Test**: Can be fully tested by publishing a new blueprint version to the System Register, verifying it replicates to a second peer, and then having a register propose a governance blueprint upgrade via quorum vote.

**Acceptance Scenarios**:

1. **Given** a new blueprint version published to the System Register, **When** peer sync occurs, **Then** all connected peers receive the new blueprint version.
2. **Given** a register currently using `register-governance-v1`, **When** an admin proposes upgrading to `v2` and quorum is reached, **Then** the register's policy `governance.blueprintVersion` is updated to `v2` and the new governance workflow takes effect.
3. **Given** a register that has not voted to upgrade, **When** `register-governance-v2` is published to the System Register, **Then** the register continues operating with `v1` — no forced upgrades.
4. **Given** a register that attempts to reference a blueprint version that doesn't exist on the System Register, **When** the governance proposal is submitted, **Then** it is rejected with a "blueprint version not found" error.

---

### User Story 6 - Validator Operational Presence via Heartbeat (Priority: P3)

The real-time operational validator list uses Redis with TTL-based entries refreshed by the existing peer heartbeat mechanism. This ensures the consensus engine always has an accurate view of which approved validators are actually online, without conflating authorisation (on-chain) with availability (ephemeral).

**Why this priority**: The heartbeat mechanism already exists (30-second intervals). This story refines how the operational list interacts with the new on-chain approved list.

**Independent Test**: Can be fully tested by starting a validator, verifying it creates a Redis entry, stopping it, waiting for TTL expiry, and confirming the entry disappears while the on-chain approval remains.

**Acceptance Scenarios**:

1. **Given** an approved validator that starts and registers in Redis, **When** it sends a heartbeat within the TTL window, **Then** the Redis entry's TTL is refreshed and the validator remains in the operational list.
2. **Given** a validator whose Redis TTL has expired, **When** the consensus engine queries the operational list, **Then** the validator is not included in the active validator set for docket building.
3. **Given** a register with `minValidators: 3` and only 2 validators with active Redis entries, **When** the consensus engine attempts to build a docket, **Then** docket building is deferred until the minimum active validator count is met.

---

### Edge Cases

- What happens when the System Register itself needs a governance blueprint upgrade? It must use its own governance workflow — the bootstrap version is the floor, upgradeable via its own admin roster.
- What happens when a validator is removed from the approved list while it has transactions in the memory pool? In-flight transactions continue through the current docket cycle; the validator is excluded from the next leader election round.
- What happens when the platform operator changes `SORCHA_SYSTEM_REGISTER_BLUEPRINT` after the System Register already exists? The flag only affects initial seeding — an existing System Register is not re-seeded (idempotent). To change the governance blueprint, use the standard governance upgrade workflow.
- What happens when all voting members of a register are offline? Policy changes cannot achieve quorum. The register continues operating under its last committed policy until voting members come back online.
- What happens to a register created before this feature (no `RegisterPolicy` in genesis)? The system applies default policy values at read time (backward compatibility). The register can adopt the new policy model by committing a `control.policy.update` transaction.
- What happens if Redis is unavailable? Operational validator list is empty (no validators appear online). On-chain approved list is unaffected. Consensus pauses until Redis recovers and validators re-register.

---

## Requirements *(mandatory)*

### Functional Requirements

#### RegisterPolicy Model

- **FR-001**: The system MUST define a unified `RegisterPolicy` structure containing four sections: governance rules, validator configuration, consensus parameters, and leader election configuration.
- **FR-002**: The `RegisterPolicy` MUST be embedded in the `RegisterControlRecord` alongside the existing `CryptoPolicy` and admin roster.
- **FR-003**: Each section of `RegisterPolicy` MUST have documented default values that are applied when the section is omitted or when reading pre-existing registers (backward compatibility).
- **FR-004**: The `RegisterPolicy` MUST include a monotonically increasing version number, incremented with each policy update.

#### Governance Section

- **FR-005**: The governance section MUST support configurable quorum formulas: `strict-majority` (floor(m/2)+1, current default), `supermajority` (floor(2m/3)+1), and `unanimous` (all voting members).
- **FR-006**: The governance section MUST include a configurable proposal time-to-live (default: 7 days).
- **FR-007**: The governance section MUST include a flag controlling whether the Owner can bypass quorum (default: true).
- **FR-008**: The governance section MUST reference the governance blueprint version in use (e.g., `register-governance-v1`), sourced from the System Register.

#### Validator Section

- **FR-009**: The validator section MUST specify a registration mode: `public` (any validator can join) or `consent` (only approved validators can join).
- **FR-010**: The validator section MUST maintain an `approvedValidators` list containing DID, public key, and approval timestamp for each approved validator. The list MUST be capped at 100 entries.
- **FR-011**: The validator section MUST specify `minValidators` and `maxValidators` bounds for the register.
- **FR-012**: The validator section MUST include a `requireStake` flag and optional `stakeAmount` for future stake-based validation.

#### Consensus Section

- **FR-013**: The consensus section MUST specify signature threshold bounds (minimum and maximum number of validator signatures required for docket approval).
- **FR-014**: The consensus section MUST specify docket construction parameters: maximum transactions per docket, docket build interval, and docket timeout.

#### Leader Election Section

- **FR-015**: The leader election section MUST specify the election mechanism: `rotating` (round-robin, default), `raft` (Raft consensus), or `stake-weighted`.
- **FR-016**: The leader election section MUST specify heartbeat interval, leader timeout, and optional term duration.

#### Genesis Integration

- **FR-017**: The register creation workflow MUST accept optional `RegisterPolicy` settings in the creation request.
- **FR-018**: When no explicit policy settings are provided, the genesis transaction MUST embed a `RegisterPolicy` with all default values.
- **FR-019**: The genesis transaction MUST write the full `RegisterPolicy` into the Control record payload so any peer can reconstruct the register's policy by reading docket 0.

#### System Register

- **FR-020**: The system MUST support a well-known System Register with a deterministic identifier (default: SHA-256 of `"sorcha-system-register"`).
- **FR-020a**: System Register bootstrap MUST create a "system-setup" wallet for the admin user to serve as the initial Owner. This wallet MUST be transferable to another wallet or user via the standard governance ownership transfer mechanism.
- **FR-021**: System Register bootstrap MUST be controlled by an environment variable `SORCHA_SEED_SYSTEM_REGISTER` (default: `false`).
- **FR-022**: The governance blueprint used by the System Register MUST be configurable via `SORCHA_SYSTEM_REGISTER_BLUEPRINT` (default: `register-governance-v1`).
- **FR-023**: System Register seeding MUST be idempotent — if the register already exists, no action is taken.
- **FR-024**: The System Register MUST contain all system blueprints (register-governance-v1, register-creation-v1) published as transactions.
- **FR-025**: System blueprints on the System Register MUST be versioned and replicable to all peers via the existing peer sync mechanism.

#### Approved Validator List (On-Chain) + Operational Presence (Redis)

- **FR-026**: In consent mode, the Validator Service MUST check a validator's DID against the on-chain `approvedValidators` list before allowing Redis registration.
- **FR-027**: In public mode, the approved list check MUST be skipped — any validator can register in Redis.
- **FR-028**: Operational validator entries in Redis MUST have a configurable TTL (default: 60 seconds), refreshed by the existing heartbeat mechanism.
- **FR-029**: The consensus engine MUST use the Redis operational list (not the on-chain approved list) to determine which validators are available for docket building and signing.
- **FR-030**: Validator revocation (removal from the on-chain approved list) MUST prevent re-registration in Redis after the current TTL expires.

#### Policy Updates via Governance

- **FR-031**: Policy updates MUST be submitted as `control.policy.update` Control transactions, subject to the register's current governance quorum rules.
- **FR-032**: Policy update payloads MUST contain the full updated `RegisterPolicy` (snapshot, not delta), with the version incremented.
- **FR-033**: Policy updates MUST be validated before the governance vote begins — invalid policies (e.g., `maxValidators < minValidators`) are rejected immediately.
- **FR-034**: The system MUST support updating individual policy sections (governance, validators, consensus, leader election) without requiring changes to all sections.
- **FR-034a**: Policy updates that change `registrationMode` from `public` to `consent` MUST include a `transitionMode` field specifying either `immediate` (unapproved validators ejected at commit) or `grace-period` (unapproved validators continue for one TTL cycle). The default if omitted MUST be `grace-period`.

#### Backward Compatibility

- **FR-035**: Registers created before this feature (with no `RegisterPolicy` in their genesis transaction) MUST continue to function using default policy values at read time.
- **FR-036**: The `GenesisConfigService` MUST read from the new `RegisterPolicy` structure when present, and fall back to the existing parsing logic (then to defaults) for legacy registers.

### Key Entities

- **RegisterPolicy**: Unified per-register operational policy containing governance, validator, consensus, and leader election configuration. Versioned, on-chain, updated via governance.
- **GovernanceConfig**: Quorum formula, proposal TTL, owner bypass flag, and governance blueprint version reference.
- **ValidatorConfig**: Registration mode (public/consent), approved validator list, min/max bounds, stake requirements.
- **ConsensusConfig**: Signature thresholds, docket size limits, build interval, timeout.
- **LeaderElectionConfig**: Election mechanism, heartbeat interval, leader timeout, term duration.
- **System Register**: A well-known register that holds system blueprints and templates. Deterministic ID, replicated to all peers.
- **ApprovedValidator**: An entry in the on-chain approved list — DID, public key, approval timestamp. Represents authorisation, not operational presence.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Register creators can specify all policy settings (validator mode, quorum formula, consensus thresholds) at creation time and verify them on-chain within the genesis transaction.
- **SC-002**: Registers created before this feature continue to operate without any manual intervention or data migration — 100% backward compatibility.
- **SC-003**: A fresh Sorcha instance with the bootstrap flag enabled starts with a functional System Register containing all system blueprints, ready for use within the normal startup sequence.
- **SC-004**: In consent mode, only validators present on the on-chain approved list can register in the operational Redis list — unauthorised validators are rejected 100% of the time.
- **SC-005**: Validator operational presence accurately reflects reality: a validator that stops heartbeating disappears from the operational list within 2x the configured TTL window.
- **SC-006**: Policy updates proposed through governance take effect after quorum approval and are visible to all services (Validator, Peer, Register) within 30 seconds of the Control transaction being committed.
- **SC-007**: The System Register replicates to new peers via the existing peer sync mechanism with no special handling — treated as a normal register.
- **SC-008**: Register admins can upgrade their governance blueprint version from v1 to v2 (or any future version) through a standard governance vote, without register downtime.

---

## Assumptions

- The existing `CryptoPolicy` model remains a separate field on `RegisterControlRecord` rather than being folded into `RegisterPolicy`. This avoids a breaking change to the well-established crypto policy structure and keeps concerns separated (operational policy vs. cryptographic policy).
- The System Register uses the same storage, replication, and governance mechanisms as any other register — no special-case code paths beyond the initial bootstrap seeding.
- Governance blueprint versions referenced in `RegisterPolicy.governance.blueprintVersion` follow the existing blueprint versioning scheme (e.g., `register-governance-v1`).
- The existing `ValidatorRegistry` Redis storage pattern is preserved for operational presence. The on-chain approved list is an authorisation gate, not a replacement for Redis.
- The `ControlDocketProcessor` already supports the action IDs (`control.validator.approve`, `control.validator.remove`, `control.config.update`) needed for policy management — this feature formalises and extends them.
- Default TTL for validator Redis entries (60 seconds) is based on the existing 30-second heartbeat interval (2x heartbeat = reasonable detection window).

## Clarifications

### Session 2026-03-04

- Q: What is the maximum size of the approvedValidators list? → A: Capped at 100 entries (same as default maxValidators).
- Q: How is the System Register owner identity determined at bootstrap? → A: A "system-setup" wallet is created for the admin user at bootstrap. This wallet serves as the initial Owner and can be transferred to another wallet or user via governance.
- Q: What happens to existing validators when switching from public to consent mode? → A: Admin chooses a transition mode in the policy update payload — `immediate` (eject unapproved validators at commit) or `grace-period` (one TTL cycle before enforcement). Default is `grace-period`.
