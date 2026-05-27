# Feature Specification: CLI API Surface Catch-Up

**Feature Branch**: `133-cli-api-coverage`
**Created**: 2026-05-20
**Status**: Draft
**Input**: User description: "Bring the Sorcha CLI up to date with the platform's current API surface. The CLI's command surface was effectively frozen after Feature 080/099; roughly features 100–128 shipped with no CLI coverage. Close the operator/automation gaps."

## Overview

The Sorcha CLI is the primary tool operators and automation use to drive the platform from a terminal or script. Its command surface was effectively frozen after the last dedicated CLI work (Features 080 and 099). Roughly two dozen platform features have shipped since with no corresponding CLI command, so a growing set of operator and automation tasks can only be performed through the web UI or by hand-crafting HTTP calls.

This feature closes the operator- and automation-relevant gaps in two phases. It does **not** attempt to mirror every endpoint — UI-driven, runtime, and service-to-service surfaces are deliberately excluded.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify and revoke transactions from the terminal (Priority: P1)

An operator or auditor needs to prove a transaction is included in the immutable ledger, verify that proof offline, check whether a transaction is still active or has been revoked/superseded, and revoke a transaction with a recorded reason — all without leaving the command line.

**Why this priority**: Trust-hardening is half-shipped in the CLI today (receipts and verification bundles exist, but inclusion proofs, revocation, and lifecycle status do not). The existing `transaction status` command reports the wrong information because it interprets the lifecycle response with a stale data shape. This is both a capability gap and a correctness bug affecting auditors who already rely on the command.

**Independent Test**: Submit a transaction, generate its inclusion proof, verify the proof succeeds; revoke the transaction with a reason; confirm `transaction status` then reports it as revoked. Fully exercisable against a running platform with no other story implemented.

**Acceptance Scenarios**:

1. **Given** a sealed transaction, **When** the operator requests its inclusion proof, **Then** the CLI returns a proof artifact that can be saved to a file.
2. **Given** a previously generated inclusion proof, **When** the operator verifies it, **Then** the CLI reports whether the proof is valid.
3. **Given** an active transaction, **When** the operator revokes it with a stated reason, **Then** the CLI confirms the revocation was submitted.
4. **Given** a transaction that has been revoked, **When** the operator queries its status, **Then** the CLI reports the lifecycle state (active / revoked / superseded) accurately — not the submission-acknowledgement shape it currently returns.

---

### User Story 2 - Diagnose register synchronisation state (Priority: P1)

An operator running or debugging a node needs to know, for a given register, whether this node is the owner / a validator / a subscriber, and whether the node is caught up, still syncing, or in an error state — to triage replication and consensus problems on environments like n1.

**Why this priority**: Sync/relationship state is the single most common live-operations question and is currently answerable only by reading service logs or the database. Read-only, low-risk, high-frequency diagnostic value.

**Independent Test**: Against a node subscribed to a register, query its derived relationship and sync state; confirm the reported role and sync status match the node's actual configuration and ledger height. Exercisable with no other story implemented.

**Acceptance Scenarios**:

1. **Given** a register this node participates in, **When** the operator queries the local relationship, **Then** the CLI reports the node's derived role set for that register.
2. **Given** a register that is mid-replication, **When** the operator queries sync state, **Then** the CLI reports whether the register is indeterminate, syncing, caught up, or in error.
3. **Given** a node hosting multiple registers, **When** the operator requests overall sync health, **Then** the CLI reports recovery sync status across all registers.

---

### User Story 3 - Manage the validator roster (Priority: P1)

A validator-node operator needs to enrol a validator, see the roster and pending count, and suspend / reactivate / revoke roster entries, plus read the validator audit trail and a wallet's sequence number — beyond the approve/reject the CLI already supports.

**Why this priority**: Running a validator node is a core platform role. The CLI can currently only approve or reject pending validators; the rest of the lifecycle (self-registration, suspension, revocation, audit) has no terminal path, forcing operators into the UI or direct API calls during incident response.

**Independent Test**: Register a validator for a register, list the roster and confirm the new entry, suspend it and confirm it leaves the active set, reactivate it, then read the audit trail showing those transitions. Exercisable independently.

**Acceptance Scenarios**:

1. **Given** a register, **When** the operator self-registers as a validator, **Then** the CLI confirms the validator was enrolled (or queued for approval).
2. **Given** an active validator, **When** the operator suspends it, **Then** the active validator count decreases and the validator shows as suspended.
3. **Given** a suspended validator, **When** the operator reactivates it, **Then** it returns to the active set.
4. **Given** a validator that must be permanently removed, **When** the operator revokes it, **Then** it no longer appears in the roster.
5. **Given** a register with validator activity, **When** the operator requests the audit trail, **Then** the CLI lists the roster lifecycle events.

---

### User Story 4 - Provision and rotate organisation keys (Priority: P2)

An organisation administrator needs to provision an org master key, derive per-user keys, and rotate or revoke derived keys from a script — for example as part of onboarding or key-hygiene automation.

**Why this priority**: Org key derivation is an admin-automation task with no CLI path today. Lower frequency than the P1 diagnostics, but high value for repeatable provisioning. The underlying capability already exists in the shared client library, so the cost to expose it is small.

**Independent Test**: Provision an org master key (capturing the one-time secret), derive a user key, rotate it, then revoke it — confirming each step's response. Exercisable independently.

**Acceptance Scenarios**:

1. **Given** an organisation with no master key, **When** the admin provisions one, **Then** the CLI returns the provisioning result including the one-time recovery material.
2. **Given** an org master key, **When** the admin derives a user key, **Then** the CLI returns the derived key record (idempotent on repeat).
3. **Given** a derived key, **When** the admin rotates it, **Then** the CLI confirms a new key at the next index with the old key marked decrypt-only.
4. **Given** a derived key, **When** the admin revokes it, **Then** the CLI confirms the key is revoked.

---

### User Story 5 - Inspect wallet internals (Priority: P3)

An operator needs to read a wallet's DID document, HD gap status, accounts, addresses, and delegations to diagnose wallet and credential issues.

**Why this priority**: Diagnostic breadth. Useful during support but not on the critical path of running the platform.

**Independent Test**: For a known wallet, retrieve its DID document, gap status, accounts, addresses, and delegations; confirm each returns the expected shape.

**Acceptance Scenarios**:

1. **Given** a wallet address, **When** the operator requests its DID document, **Then** the CLI returns the document.
2. **Given** a wallet, **When** the operator requests gap status / accounts / addresses / delegations, **Then** the CLI returns each respective listing.

---

### User Story 6 - Govern the system register (Priority: P3)

A platform administrator needs to initialise the system register, publish a blueprint to it, classify a blueprint change, and review blueprint version history from the command line.

**Why this priority**: Extends the existing genesis-ceremony surface. Important for platform setup but performed rarely.

**Independent Test**: Publish a blueprint to the system register and confirm it appears in the blueprint list with version history; classify a subsequent change.

**Acceptance Scenarios**:

1. **Given** an initialised platform, **When** the admin publishes a blueprint to the system register, **Then** the CLI confirms publication and the blueprint appears in the system-register blueprint list.
2. **Given** a published blueprint, **When** the admin requests its version history, **Then** the CLI lists prior versions.
3. **Given** a proposed blueprint change, **When** the admin requests change classification, **Then** the CLI reports the change type.

---

### User Story 7 - Administer citizen wallet devices (Priority: P3)

A support operator acting for the signed-in account needs to list enrolled wallet devices and revoke a lost or compromised device.

**Why this priority**: Support value (revoke a lost device); the remainder of the citizen-wallet surface is owned by the PWA and out of scope.

**Independent Test**: List the account's devices, revoke one, confirm it shows as revoked on a subsequent list.

**Acceptance Scenarios**:

1. **Given** an account with enrolled devices, **When** the operator lists devices, **Then** the CLI returns active and revoked devices.
2. **Given** an enrolled device, **When** the operator revokes it, **Then** the CLI confirms revocation and the device shows as revoked thereafter.

---

### User Story 8 - Automate token and organisation context (Priority: P3)

An automation author needs to introspect the current token, switch the active organisation, and list the signed-in user's organisation memberships from a script.

**Why this priority**: Quality-of-life for CI/automation. Login/logout already exist; token refresh is already handled transparently and is explicitly not part of this story.

**Independent Test**: Authenticate, list org memberships, switch active org, introspect the resulting token and confirm the active org changed.

**Acceptance Scenarios**:

1. **Given** an authenticated session, **When** the user lists their organisations, **Then** the CLI returns the memberships.
2. **Given** membership in multiple organisations, **When** the user switches active organisation, **Then** the CLI confirms the switch and subsequent calls use the new context.
3. **Given** an active token, **When** the user introspects it, **Then** the CLI reports the token's claims.

---

### User Story 9 - Administer organisation trust anchors (Priority: P3)

A platform/tenant administrator needs to provision a tenant trust anchor, enrol an organisation into the certificate chain, retrieve the trust anchor and an org's cert chain, revoke an org's enrolment, and read the certificate revocation list — from the command line.

**Why this priority**: Self-contained PKI administration capability (trust-hardening, Feature 079) with no current CLI path. (Note: the originally assumed `/api/trust/{address}` relationship CRUD does not exist; the real surface is tenant/org trust-anchor administration — see research R-003.)

**Independent Test**: Provision a tenant trust anchor, enrol an org, fetch its cert chain, revoke it, and confirm it appears in the CRL.

**Acceptance Scenarios**:

1. **Given** a tenant, **When** the admin provisions its trust anchor, **Then** the CLI confirms provisioning and `trust-anchor get` returns it.
2. **Given** a provisioned tenant, **When** the admin enrols an org by wallet address, **Then** the CLI confirms enrolment and the org's cert chain becomes retrievable.
3. **Given** an enrolled org, **When** the admin revokes it, **Then** the org appears in the tenant's CRL.

---

### Edge Cases

- **Authentication expiry**: Any command run with an expired or missing token reports an authentication error and points the operator to re-authenticate, rather than failing opaquely.
- **Authorisation**: Commands invoking admin-only operations against an account without the required role report a clear authorisation failure.
- **Unknown identifiers**: Requests for a non-existent register, transaction, validator, wallet, device, or key report a not-found result distinguishable from other errors.
- **Output formats**: Every new command honours the CLI's existing global output options (table / json / csv / yaml) and the machine-readable envelope, consistent with existing commands.
- **Destructive operations**: Revoke/delete commands (transaction revoke, validator revoke, key revoke, device revoke, trust delete) make the irreversible nature clear and require an explicit target; they do not act on ambiguous or wildcard inputs.
- **One-time secrets**: Org master-key provisioning surfaces the one-time recovery material exactly once and never persists it.
- **Out-of-scope routes**: Service-to-service (`/api/internal/*`), consumer persona, file-chunk upload, presentation lifecycle, enrolment/pairing, and HAIP protocol endpoints are intentionally not exposed; attempting them is not a CLI concern.

## Requirements *(mandatory)*

### Functional Requirements

#### Phase 1 — Operator / automation core

- **FR-001**: The CLI MUST allow an operator to generate a Merkle inclusion proof for a transaction and save it to a file.
- **FR-002**: The CLI MUST allow an operator to verify a previously generated inclusion proof and report validity.
- **FR-003**: The CLI MUST allow an operator to revoke a transaction with a stated reason and confirm submission.
- **FR-004**: The CLI MUST report a transaction's lifecycle status (active / revoked / superseded) correctly, replacing the current behaviour where the status command interprets the lifecycle response with a stale, incorrect data shape.
- **FR-005**: The CLI MUST report a node's derived local relationship (role set) for a given register.
- **FR-006**: The CLI MUST report a register's sync state (indeterminate / syncing / caught up / error).
- **FR-007**: The CLI MUST report recovery sync health across all registers hosted by the node.
- **FR-008**: The CLI MUST allow an operator to self-register a validator for a register.
- **FR-009**: The CLI MUST allow an operator to suspend, reactivate, and revoke a validator roster entry.
- **FR-010**: The CLI MUST report the active validator count for a register.
- **FR-011**: The CLI MUST report the validator audit trail for a register.
- **FR-012**: The CLI MUST report a wallet's sequence number for a register.
- **FR-013**: The CLI MUST allow an administrator to provision an organisation master key, returning one-time recovery material exactly once.
- **FR-014**: The CLI MUST allow an administrator to derive a per-user key from an organisation master key.
- **FR-015**: The CLI MUST allow an administrator to rotate and revoke a derived organisation key.

#### Phase 2 — Diagnostics & admin breadth

- **FR-016**: The CLI MUST allow an operator to retrieve a wallet's DID document.
- **FR-017**: The CLI MUST allow an operator to retrieve a wallet's gap status, accounts, addresses, and delegations.
- **FR-018**: The CLI MUST allow an administrator to publish a blueprint to the system register and confirm publication.
- **FR-019**: The CLI MUST allow an administrator to initialise the system register and classify a blueprint change.
- **FR-020**: The CLI MUST report system-register blueprint version history.
- **FR-021**: The CLI MUST allow an operator to list the signed-in account's enrolled wallet devices and revoke a device.
- **FR-022**: The CLI MUST allow a user to introspect the current token, list their organisation memberships, and switch the active organisation. The CLI MUST NOT add an explicit token-refresh command (refresh is handled transparently).
- **FR-023**: The CLI MUST allow a tenant administrator to provision a tenant trust anchor, retrieve it, enrol an organisation into the certificate chain, retrieve an org's cert chain, revoke an org's enrolment, and read the certificate revocation list. (Corrected from the original "trust relationships" wording — the relationship-CRUD endpoints assumed there do not exist; see research R-003.)

#### Cross-cutting requirements

- **FR-024**: Every new command MUST honour the CLI's existing global options: profile selection, output format (table / json / csv / yaml), quiet, verbose, and machine-readable envelope.
- **FR-025**: Every new command MUST use consistent, documented exit codes distinguishing success, general error, authentication error, authorisation error, not-found, and validation error.
- **FR-026**: Every new command MUST be documented in the CLI command reference, and the relevant skill documentation MUST be updated so the command surface is discoverable.
- **FR-027**: Every new command MUST have automated tests following the established CLI testing pattern.
- **FR-028**: Where a capability already exists in the shared service-client library (e.g. organisation key derivation, file download), the CLI MUST reuse it rather than introducing a duplicate client and data shapes. Where the shared library lacks the required operator/admin surface, the CLI MAY define its own client; a documented selective-reuse rule MUST govern the choice so future commands do not silently re-introduce drift.
- **FR-029**: The set of intentionally excluded surfaces (consumer persona, file-chunk upload, presentation lifecycle, enrolment/pairing, HAIP protocol endpoints, and all service-to-service `/api/internal/*` routes) MUST be recorded so the exclusion is a documented decision rather than an oversight.

### Key Entities *(include if feature involves data)*

- **Transaction lifecycle status**: The current state of a ledger transaction — active, revoked, or superseded — plus revocation reason where applicable.
- **Inclusion proof**: A portable artifact proving a transaction's membership in a sealed docket, verifiable offline.
- **Register relationship**: A node's derived role set for a register (owner / validator / subscriber), computed rather than stored.
- **Register sync state**: A register's replication status on this node — indeterminate, syncing, caught up, or error.
- **Validator roster entry**: An authorised docket-signing identity for a register, with a lifecycle (pending / active / suspended / revoked) and an audit trail.
- **Organisation key**: A master key per organisation and the per-user keys derived from it, each with a usage, index, and status.
- **Wallet device**: An enrolled citizen-wallet device for an account, active or revoked.
- **Trust anchor / cert chain**: A tenant's root of trust and the per-organisation certificate chains enrolled under it, plus the certificate revocation list (CRL) of revoked org enrolments.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can complete the full transaction trust-hardening lifecycle (generate proof → verify proof → revoke → confirm revoked status) entirely from the CLI, with no UI or hand-crafted HTTP calls.
- **SC-002**: The transaction status command reports the correct lifecycle state for active, revoked, and superseded transactions in 100% of cases (the current incorrect-shape behaviour is eliminated).
- **SC-003**: An operator can determine any register's role and sync state on a node in a single command, replacing the current need to read service logs or query the database.
- **SC-004**: A validator-node operator can perform the complete roster lifecycle (register → suspend → reactivate → revoke → audit) from the CLI.
- **SC-005**: An administrator can provision, derive, rotate, and revoke organisation keys from a script with no manual steps.
- **SC-006**: Every Phase 1 and Phase 2 command appears in the CLI command reference and is covered by automated tests; the reference and skill documentation contain no command that lacks a test and no tested command that lacks documentation.
- **SC-007**: No new command introduces a duplicate of a capability already provided by the shared service-client library; the selective-reuse rule is documented and followed.
- **SC-008**: The intentionally excluded surfaces are listed in the feature's documentation, so a future reviewer can confirm the omission was deliberate.

## Assumptions

- The CLI's existing authentication model (bearer token via stored profile, with transparent refresh) and its global option set (profile, output, quiet, verbose, machine-readable) remain unchanged and apply to all new commands.
- The platform endpoints backing every command in scope already exist and are stable; this feature adds CLI surface only and does not require backend changes.
- Phase 1 (P1/P2 stories) delivers independently and is the priority; Phase 2 (P3 stories) can follow without blocking Phase 1.
- "Operator", "administrator", and "automation author" are roles distinguished by the permissions on their authenticated token, not separate CLI installs.
- Citizen-wallet device administration is scoped to the signed-in account's own devices; cross-account device management is out of scope.
