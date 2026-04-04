# Feature Specification: Wallet Key Derivation & UI Transaction Lifecycle

**Feature Branch**: `083-wallet-key-derivation`
**Created**: 2026-04-04
**Status**: Draft
**Input**: User description: "Wallet Key Derivation & UI Transaction Lifecycle - Org-level HD key derivation foundations, wallet UI transaction ticks with receipt proofs, and threshold signing schema prep."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Organisation Admin Provisions Key Hierarchy (Priority: P1)

An organisation administrator sets up their organisation's cryptographic key hierarchy. They provision a master key for the organisation, which generates a one-time-visible recovery mnemonic. Once provisioned, the system automatically derives identity wallets for every existing member and for each new member added in the future. The admin never needs to manually create wallets for users.

**Why this priority**: This is the foundation for all org-level key management. Without a provisioned master key, no user keys can be derived. Every other org key feature depends on this.

**Independent Test**: Can be fully tested by an admin provisioning a master key for an org, verifying the mnemonic is returned once, confirming identity wallets are auto-created for existing org members, and verifying the mnemonic cannot be retrieved again.

**Acceptance Scenarios**:

1. **Given** an organisation with no master key, **When** an admin provisions a master key, **Then** the system generates and displays a recovery mnemonic exactly once and creates the master key record.
2. **Given** a provisioned org master key, **When** the admin attempts to provision again, **Then** the system rejects the request with an appropriate error.
3. **Given** a provisioned org master key and existing org members, **When** provisioning completes, **Then** each existing member receives an auto-derived identity wallet.
4. **Given** a provisioned org master key, **When** a new user is added to the organisation, **Then** the system automatically derives an identity wallet for that user without admin intervention.
5. **Given** an org with a provisioned master key, **When** any user queries their wallets, **Then** their org-derived wallet appears alongside any standalone wallets they may have.

---

### User Story 2 - Wallet User Views Transaction Lifecycle (Priority: P1)

A wallet user submits a transaction and wants to track its progress through the system. They see a clear visual indicator in their transaction list showing whether each transaction is pending, sealed in a docket, or has received a cryptographic receipt. They can click any transaction to see a detailed timeline of its lifecycle, including timestamps for each stage, the docket it was sealed in, and the receipt proof when available.

**Why this priority**: Transaction visibility is critical for user trust. The backend tracking is already built; users currently have no way to see the lifecycle state of their transactions.

**Independent Test**: Can be tested by submitting a transaction, observing the pending indicator, waiting for docket sealing and receipt generation, and verifying each state transition is reflected in the UI in real time.

**Acceptance Scenarios**:

1. **Given** a transaction has been submitted but not yet sealed, **When** the user views their transaction list, **Then** a grey tick indicator appears next to that transaction.
2. **Given** a transaction has been sealed in a docket, **When** the user views their transaction list, **Then** a blue tick indicator appears, replacing the grey tick.
3. **Given** a transaction has received a cryptographic receipt, **When** the user views their transaction list, **Then** a double blue tick indicator appears.
4. **Given** a transaction in any state, **When** the user clicks the transaction row, **Then** a detail panel opens showing a vertical timeline with timestamps for each completed lifecycle stage.
5. **Given** a receipted transaction with the detail panel open, **When** the user views the receipt section, **Then** they see the receipt ID, Merkle root, validator address, and signature, with options to verify the receipt or download a portable verification bundle.
6. **Given** a transaction is pending and the user has the transaction list open, **When** the transaction is sealed in a docket, **Then** the tick indicator updates in real time without requiring a page refresh.

---

### User Story 3 - Admin Derives Purpose-Specific Keys for Users (Priority: P2)

An admin derives additional purpose-specific keys for users beyond the auto-provisioned identity key. For example, they derive a VC issuance key for a user who needs to issue verifiable credentials, or a governance key for a user participating in register governance votes. Each key type serves a distinct cryptographic purpose and is tracked separately.

**Why this priority**: Extends the key hierarchy beyond the auto-provisioned identity key. Not required for basic operation but essential for organisations using advanced features like credential issuance and governance.

**Independent Test**: Can be tested by an admin deriving a VC issuance key for a user, verifying the derived wallet is created with the correct key usage, and confirming the derivation path is recorded.

**Acceptance Scenarios**:

1. **Given** a user with an existing identity key, **When** an admin derives a VC issuance key for that user, **Then** a new wallet is created with key usage "VCIssuance" and a unique derivation path.
2. **Given** a user with an existing key of a specific usage, **When** the same derivation is requested again, **Then** the system returns the existing wallet idempotently without creating a duplicate.
3. **Given** a user in a department, **When** a key is derived with a department ID, **Then** the derivation path includes the department level and the key is correctly scoped.
4. **Given** a flat organisation (no departments), **When** any key is derived, **Then** the department level defaults to zero in the derivation path.

---

### User Story 4 - Admin Rotates a Compromised or Expiring Key (Priority: P2)

An admin needs to rotate a user's key, either because it may be compromised or as part of a scheduled rotation policy. The system derives a new key at the next index in the derivation path, marks the old key as rotated (preventing new signatures but allowing decryption of historical data), and the user seamlessly begins signing with the new key.

**Why this priority**: Key rotation is essential for security hygiene and incident response. Without it, compromised keys remain active indefinitely.

**Independent Test**: Can be tested by rotating a key, verifying the old key cannot sign new transactions, confirming the new key is active, and ensuring historical data encrypted with the old key remains accessible.

**Acceptance Scenarios**:

1. **Given** a user with an active derived key at index N, **When** an admin rotates the key, **Then** a new key is derived at index N+1 and the old key's status changes to "Rotated."
2. **Given** a rotated key, **When** a signing request is made using the rotated key, **Then** the system rejects the request.
3. **Given** a rotated key, **When** a decryption request is made using the rotated key, **Then** the system allows the decryption (historical data access preserved).

---

### User Story 5 - Admin Revokes a Key (Priority: P3)

An admin permanently revokes a user's key due to a security incident, employee departure, or compliance requirement. The associated wallet is locked, and if the key was an identity key, a DID revocation event is published to the network.

**Why this priority**: Revocation is the last-resort security action. Less frequently used than rotation but required for compliance and incident response.

**Independent Test**: Can be tested by revoking a key, verifying the wallet is locked, and confirming the revocation timestamp is recorded.

**Acceptance Scenarios**:

1. **Given** an active derived key, **When** an admin revokes it, **Then** the key status becomes "Revoked," the wallet is locked, and the revocation timestamp is recorded.
2. **Given** a revoked key, **When** any signing or decryption request is made, **Then** the system rejects the request.
3. **Given** a revoked identity key (usage=0), **When** the revocation completes, **Then** a DID revocation event is published.

---

### User Story 6 - Schema Readiness for Threshold Signing (Priority: P3)

The system stores data structures for future threshold (multi-party) signing without implementing the signing ceremony. This ensures that when threshold signing is built in a future release, the data layer is already in place and no disruptive data migrations are needed.

**Why this priority**: Lowest priority as it delivers no immediate user-facing value, but prevents costly schema migrations when threshold signing is implemented. This is a technical investment in forward compatibility.

**Independent Test**: Can be tested by verifying the threshold signing tables exist in the database with correct schema, constraints, and indexes — without any service code or endpoints referencing them.

**Acceptance Scenarios**:

1. **Given** the database migration has run, **When** the threshold key group table is inspected, **Then** it exists with columns for group public key, threshold (K), total shares (N), algorithm, organisation, and status.
2. **Given** the database migration has run, **When** the signing key share table is inspected, **Then** it exists with columns for participant, share index, encrypted share data, and status, with a foreign key to the threshold key group.
3. **Given** the database migration has run, **When** the signing session table is inspected, **Then** it exists with columns for state, required signers, collected partials, and expiry, with a foreign key to the threshold key group.
4. **Given** the threshold tables exist, **When** the application starts, **Then** no service code references these tables and no endpoints expose them.

---

### Edge Cases

- What happens when two admins simultaneously attempt to provision a master key for the same org? The system must ensure only one succeeds via a unique constraint on organisation ID.
- What happens when the auto-derivation hook fires but the org has no provisioned master key? The system logs a warning and skips derivation; the user can have a wallet created manually or the hook retries once the master key is provisioned.
- What happens when a user is removed from an organisation? Their derived keys remain in "Active" status but are no longer usable because the Tenant Service revokes their JWT tokens on org removal — they cannot authenticate to the signing endpoint. The keys themselves are not revoked (admin can explicitly revoke if permanent lockout is needed).
- What happens when the transaction lifecycle event fires but the UI is not connected to SignalR? On next page load, the transaction list fetches current state from the API, showing the correct tick status.
- What happens when a receipt verification fails? The UI displays the failure reason and the receipt proof details so the user can investigate.
- What happens when a GUID-to-derivation-index mapping produces a collision for two different users? The unique constraint on the full derivation path tuple prevents duplicate key creation; the second derivation attempt would need manual resolution (extremely unlikely at practical user counts).

## Requirements *(mandatory)*

### Functional Requirements

**Org Key Derivation:**

- **FR-001**: System MUST allow an organisation administrator to provision a master key for their organisation, generating a one-time-visible recovery mnemonic.
- **FR-002**: System MUST reject duplicate master key provisioning for an organisation that already has an active master key.
- **FR-003**: System MUST derive user keys deterministically from the org master key using the path structure `m/0x534F52'/org'/dept'/user'/usage/index`, where 0x534F52 is the Sorcha-specific purpose namespace that avoids collision with all registered BIP43 purposes and SLIP-0044 coin types.
- **FR-004**: System MUST automatically derive an identity key (usage=0, index=0) for every user when they are added to an organisation with a provisioned master key.
- **FR-005**: System MUST support five key usage types: Identity (0), VC Issuance (1), Governance (2), Communications (3), and Service Auth (4).
- **FR-006**: System MUST include a department level in every derivation path, defaulting to zero for flat organisations, to allow future hierarchical key delegation without re-derivation.
- **FR-007**: System MUST support key rotation by deriving a new key at the next index and marking the previous key as "Rotated." Rotated keys allow decryption but reject signing.
- **FR-008**: System MUST support key revocation, which locks the associated wallet, records the revocation timestamp, and publishes a DID revocation event for identity keys.
- **FR-009**: System MUST return existing wallets idempotently when a derivation request matches an already-derived path.
- **FR-010**: System MUST encrypt the org master seed using a pluggable protection provider, shipping with a software encryption provider and supporting future cloud key management integration without code changes.

**Transaction Lifecycle UI:**

- **FR-011**: System MUST display a visual lifecycle indicator for each transaction in the wallet transaction list, showing three states: pending (grey tick), sealed (blue tick), and receipted (double blue tick).
- **FR-012**: System MUST update transaction lifecycle indicators in real time as state changes occur, without requiring a page refresh.
- **FR-013**: System MUST provide a transaction detail view showing a vertical timeline of lifecycle stages with timestamps and relative timing between stages.
- **FR-014**: System MUST display receipt proof details (receipt ID, Merkle root, validator address, signature) for receipted transactions.
- **FR-015**: System MUST allow users to verify a transaction receipt directly from the detail view.
- **FR-016**: System MUST allow users to download a portable verification bundle for receipted transactions.

**Threshold Signing Schema:**

- **FR-017**: System MUST create database tables for threshold key groups, signing key shares, and signing sessions as part of the schema migration.
- **FR-018**: Threshold signing tables MUST NOT have any service code, endpoints, or business logic referencing them in this release.

### Key Entities

- **Org Master Key**: Represents an organisation's root cryptographic seed. One per organisation. Encrypted at rest with a pluggable protection provider. Stores the master public key for verification and tracks lifecycle status (active, rotated, revoked).
- **Derived Key Record**: Links a user to a specific key derived from the org master. Records the full derivation path, key usage type, rotation index, and custody mode. References both the org master key and the resulting wallet.
- **Wallet** (modified): Existing wallet entity extended with an optional link to a derived key record and a custody mode field indicating how the private key is managed (custodial now; co-signed and self-custody supported in schema for future use).
- **Threshold Key Group**: Represents a group of participants sharing a threshold signing key. Stores the combined group public key, the K-of-N threshold parameters, and the algorithm. Schema-only — no implementation in this release.
- **Signing Key Share**: An individual participant's encrypted share of a threshold key. Linked to a threshold key group. Schema-only — no implementation in this release.
- **Signing Session**: A multi-round signing ceremony tracking round state, required signers, and collected partial signatures. Linked to a threshold key group. Schema-only — no implementation in this release.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Organisation administrators can provision a master key and have identity wallets auto-created for all org members within 30 seconds of provisioning.
- **SC-002**: Users can see the current lifecycle state of any transaction within 2 seconds of a state change occurring, without manual refresh.
- **SC-003**: Users can view full transaction lifecycle details (timeline, receipt proof) in under 1 second after clicking a transaction.
- **SC-004**: Key derivation for a single user completes in under 500 milliseconds.
- **SC-005**: Key rotation produces a new active key and deactivates the old key in a single operation with no window where both keys can sign simultaneously.
- **SC-006**: All org-derived wallets are fully functional for signing and verification using existing wallet workflows — users experience no difference from standalone wallets.
- **SC-007**: Receipt verification from the transaction detail panel returns a pass/fail result in under 2 seconds.
- **SC-008**: The threshold signing schema tables exist after migration with correct constraints and relationships, verifiable by inspection.
- **SC-009**: 100% of new org members receive an auto-derived identity wallet without admin intervention, verifiable through automated testing.

## Assumptions

- The existing HD wallet infrastructure (BIP32/39/44) and cryptographic modules are stable and require no changes for this feature.
- The TransactionLifecycleService and TransactionLifecycleEventBridge backends are complete and fire correct SignalR events for docket sealing and receipt generation.
- Feature 079 (transaction receipts, Merkle proofs, verification bundles) endpoints are available for the UI verify/download actions.
- The Tenant Service logs UserAddedToOrganization as an audit event but does not currently emit an external notification. A new internal event notification must be added (T027b) so the Wallet Service auto-derivation hook can subscribe.
- Only the custodial custody mode is implemented in this release; the co-signed and self-custody modes exist in the schema but have no code paths.
- Database migrations will be squashed into a single migration per context to maintain a clean migration history.
