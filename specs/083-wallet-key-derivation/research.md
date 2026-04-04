# Research: Wallet Key Derivation & UI Transaction Lifecycle

**Feature**: 083-wallet-key-derivation
**Date**: 2026-04-04

## Research Summary

All technical unknowns were resolved during the brainstorming phase. This document consolidates the decisions and their rationale.

---

## R1: BIP32 Derivation Path Namespace

**Decision**: Use purpose `0x534F52'` (decimal 5,456,978) — "SOR" encoded as hex.

**Rationale**: BIP43 reserves purpose numbers for specific standards (44' for BIP44, 49' for BIP49, 84' for BIP84, 86' for BIP86). Using a high-value private-use purpose avoids any collision with registered or future-registered purposes. The hex encoding of "SOR" is self-documenting — any wallet software encountering this path immediately identifies it as Sorcha-specific.

**Alternatives considered**:
- `100'-104'` range: Currently unregistered but could be claimed by future BIPs. Risk of silent collision.
- Register Sorcha as a SLIP-0044 coin type: Coin types are for currencies; Sorcha is a register platform, not a cryptocurrency. Semantic mismatch.
- BIP44-compatible path `m/44'/registered_coin'/...`: Constrains the path structure to BIP44's account/change/index model, which doesn't map to org/dept/user/usage hierarchy.

---

## R2: GUID to Derivation Index Mapping

**Decision**: SHA-256 hash of GUID bytes, first 4 bytes masked to 31 bits (`& 0x7FFFFFFF`).

**Rationale**: BIP32 hardened derivation requires uint31 indices (0 to 2^31-1). GUIDs are 128-bit, so a deterministic mapping is needed. SHA-256 provides uniform distribution. The 31-bit mask ensures the value fits in the hardened child key space. Collision probability is ~1 in 2 billion per pair — negligible for practical org/user counts (thousands, not billions).

**Alternatives considered**:
- Sequential integer assignment: Requires a mapping table and breaks determinism — you can't re-derive from path alone.
- Truncate GUID to 4 bytes: Non-uniform distribution since GUID versions concentrate entropy in specific bytes.
- CRC32 of GUID: Weaker distribution than SHA-256; known collision weaknesses.

---

## R3: Org Master Seed Protection

**Decision**: Pluggable `IOrgKeyProtectionProvider` interface. Ship with `SoftwareKeyProtectionProvider` (AES-256-GCM, key from configuration).

**Rationale**: Feature 082 (Cloud KMS) laid the foundation for `IKeyProtectionProvider` but phases 2-6 are incomplete. Blocking on KMS would delay org key derivation indefinitely. The pluggable interface lets us ship with software encryption (secure for development/staging), then swap to Azure Key Vault when 082 completes — no code changes needed in the derivation service.

**Alternatives considered**:
- Require KMS from the start: Blocks on Feature 082 completion. Unacceptable delay.
- Direct AES encryption without abstraction: Works but creates migration pain when KMS arrives. Hard-wiring encryption strategy is a one-way door.
- Store seed in environment variable: No encryption at rest. Violates Constitution Principle II (Security First).

---

## R4: Custody Model

**Decision**: Implement custodial mode only. Schema supports co-signed and self-custody for future use.

**Rationale**: Co-signed mode requires device share infrastructure (secure enclaves, re-provisioning flows, mobile SDK) which is a large surface area (WALLET-R8). Self-custody requires the full on-device wallet experience. Both are future work. Adding the `CustodyMode` enum field now means no schema migration when these modes are built.

**Alternatives considered**:
- Implement co-signed in this tranche: Requires FROST threshold signing (R6-R7) or a simpler 2-of-2 scheme, plus device management. Significant scope increase.
- No custody mode field: Would require an ALTER TABLE migration when co-signed is added. Defeats the purpose of forward-compatible schema.

---

## R5: Department Level in Derivation Path

**Decision**: Always present in path, defaults to `0'` for flat organisations.

**Rationale**: BIP32 derivation paths have fixed depth — you can't insert a level retroactively without re-deriving all keys under that branch. Including the department level at `0'` by default means flat orgs work naturally, and hierarchical orgs can use non-zero department IDs without any key re-derivation.

**Alternatives considered**:
- Omit department level: Simpler path but one-way door — can't add it later without re-deriving all existing keys.
- Optional depth (variable path length): Complicates path parsing and validation. Tooling must handle multiple path formats.

---

## R6: Transaction Tick UI Pattern

**Decision**: Tick icons in existing transaction table + MudDrawer slide-out detail panel.

**Rationale**: The backend (`TransactionLifecycleService`, `TransactionLifecycleEventBridge`) already fires SignalR events for state transitions. The UI just needs to render state. A MudDrawer slide-out follows the existing Sorcha UI pattern (used for register details) and avoids full-page navigation for a detail view. Three new components (`TransactionTickIcon`, `TransactionDetailDrawer`, `ReceiptProofCard`) are composable and reusable.

**Alternatives considered**:
- Dedicated outbound transactions page: More work, deferred to future. The table + drawer covers the moderate scope agreed in brainstorming.
- Modal dialog instead of drawer: Drawers are better for detail views that may need scrolling (timeline, receipt proof). Modals feel constraining for this amount of content.
- Toast notifications for state changes: Not a replacement — users need persistent state in the table, not ephemeral notifications.

---

## R7: Threshold Signing Schema Design

**Decision**: Three tables (ThresholdKeyGroup, SigningKeyShare, SigningSession) with relationships and constraints. No service code.

**Rationale**: FROST (Flexible Round-Optimized Schnorr Threshold) signatures require: (1) a group key identity with K-of-N parameters, (2) individual encrypted shares per participant, and (3) a multi-round signing ceremony with state tracking. These three entities are well-established in threshold cryptography literature (RFC 9591, Zcash Foundation frost-ed25519). Creating the tables now avoids schema migration disruption when FROST is implemented.

**Alternatives considered**:
- Include a SigningPolicy table: Policy enforcement (R10) is Tier 3 research. The rules engine design will evolve during research — premature schema risks the very migration pain we're avoiding.
- JSON blob in a single table: Loses referential integrity and query capability. Threshold operations need to query by group, by participant, by session state.
- No schema at all: Defeats the stated goal of reducing future migration pain.
