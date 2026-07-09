# Feature Specification: Ethereum Address-Form Issuer DID Verification (Offline ecrecover)

**Feature Branch**: `178-ethereum-ecrecover`

**Created**: 2026-07-09

**Status**: Draft

**Input**: User description: "Phase 2 Ethereum address-form issuer DID verification (offline ecrecover). Extend Sorcha's ES256K VC verification to issuer DIDs that resolve offline to an ADDRESS ONLY: did:pkh (CAIP-10) and default-document (no-rotation) address-form did:ethr (ERC-1056)."

## User Scenarios & Testing *(mandatory)*

Phase 1 (Feature 177) let Sorcha verify Ethereum-signed credentials whose issuer identifier already embeds a public key. Phase 2 closes the larger interoperability gap: the two most widely deployed Ethereum issuer identifiers — `did:pkh` and address-form `did:ethr` — name the issuer only by its **blockchain address**. A verifier that cannot handle an address-only issuer identity must reject every credential from a Web3-native issuer that uses these forms, even when the signature is perfectly valid. This feature lets Sorcha accept those credentials, entirely offline, with no blockchain connection.

### User Story 1 - Accept a credential from a trusted address-form Ethereum issuer (Priority: P1)

A blueprint author configures a credential requirement that trusts a specific Ethereum issuer named only by its address (a `did:pkh` or address-form `did:ethr` in the requirement's allow-list). A holder presents a credential signed by that issuer. Sorcha confirms the signature genuinely came from that issuer's address and accepts the credential.

**Why this priority**: This is the core interoperability win — without it, credentials from the most common Ethereum issuer identity forms cannot be trusted at all. It delivers standalone value: a single allowlisted issuer verifying to "Pass" is a complete, demonstrable capability.

**Independent Test**: Configure a requirement allow-listing one address-form issuer DID; present a genuine credential from that issuer; confirm the outcome is **Pass** (accepted) and that a credential whose signature does not correspond to that address is **Rejected**.

**Acceptance Scenarios**:

1. **Given** a credential requirement that allow-lists issuer `did:pkh:eip155:1:0x…`, **When** a holder presents a credential validly signed by that address, **Then** verification succeeds and the credential is accepted.
2. **Given** the same requirement, **When** a holder presents a credential whose signature corresponds to a **different** address, **Then** verification fails and the credential is rejected.
3. **Given** a requirement that allow-lists an address-form `did:ethr` issuer (mainnet default, named-network, and hex-chain-id forms), **When** a genuinely-signed credential is presented, **Then** verification succeeds.
4. **Given** an allow-list entry for issuer on chain `eip155:1`, **When** a credential from the same address but a **different chain** (`eip155:137`) is presented, **Then** it is treated as a different issuer and is not automatically trusted by that entry.

### User Story 2 - Governed handling of a valid signature from an un-allowlisted issuer (Priority: P2)

A holder presents a credential that is genuinely signed by some Ethereum address, but that issuer is not in the requirement's allow-list. The blueprint author decides — per requirement — whether such a "signature-valid but untrusted" credential is rejected (the safe default) or accepted at reduced assurance with a warning.

**Why this priority**: It reuses the Phase 1 trust governance unchanged and preserves fail-closed behaviour by default, while giving authors an explicit, auditable opt-in for reduced-assurance acceptance. It is P2 because P1 already delivers the trusted-issuer path; this refines the untrusted path.

**Independent Test**: Present a validly-signed credential from an issuer not in the allow-list, once with the reduced-assurance flag unset (expect **Reject**) and once with it set (expect **Warn**).

**Acceptance Scenarios**:

1. **Given** a requirement whose reduced-assurance flag is **unset** (default), **When** a validly-signed credential from an un-allowlisted address-form issuer is presented, **Then** it is **rejected**.
2. **Given** a requirement whose reduced-assurance flag is **set**, **When** the same credential is presented, **Then** it is accepted with a **Warn** (reduced assurance) outcome and the reduced assurance is recorded for audit.

### User Story 3 - Reject malformed or unsupported issuer identifiers (Priority: P3)

A holder presents a credential whose issuer identifier is malformed, or is an Ethereum identifier form that requires blockchain interaction to resolve (e.g. a `did:ethr` relying on on-chain key rotation). Sorcha rejects it rather than guessing.

**Why this priority**: Correct fail-closed behaviour on unsupported/malformed inputs is essential for safety but is a refinement of the happy path, hence P3.

**Independent Test**: Present credentials with a malformed `did:pkh`/`did:ethr` and with a `did:ethr` that would need on-chain resolution; confirm each is rejected without any network call.

**Acceptance Scenarios**:

1. **Given** a credential whose issuer DID has a malformed address or unsupported namespace, **When** it is verified, **Then** it is rejected and no network call is attempted.
2. **Given** a credential whose issuer is a `did:ethr` that depends on on-chain rotation/delegates, **When** it is verified, **Then** it is rejected as unsupported in this phase (deferred to a later phase), with no network call.

### Edge Cases

- The issuer DID address is expressed in checksummed (mixed-case) form in one place and lowercase in another — the two MUST be treated as the same address (case-insensitive match).
- A signature could correspond to two candidate signers (the recovery is ambiguous) — verification MUST accept only if **one of** the candidates matches the issuer's address, and reject otherwise.
- A credential presents a valid signature but the issuer document commits to neither a key nor an address — it MUST be rejected (unresolved issuer).
- The same capability MUST behave identically whether verification runs in the internal workflow engine or in the standalone verifier surface.
- Behaviour for existing key-form Ethereum issuers (Phase 1) and non-Ethereum issuers MUST be unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST verify a credential signed with an Ethereum secp256k1 signature (JOSE ES256K) in the issuer position where the issuer DID resolves to a blockchain **address only** — specifically `did:pkh` (CAIP-10) and address-form `did:ethr` (ERC-1056 default document).
- **FR-002**: Verification MUST recover the signer's public key from the signature, derive its Ethereum address, and confirm it matches the address named by the issuer DID. A confirmed match constitutes a verified issuer signature.
- **FR-003**: Address matching MUST be case-insensitive (equivalent lowercase and checksummed forms are the same address).
- **FR-004**: Verification MUST be fully **offline** — no blockchain node, RPC provider, or on-chain registry read is permitted in this phase.
- **FR-005**: The system MUST resolve `did:pkh:eip155:{chainId}:0x{address}` to the issuer's address, rejecting malformed inputs.
- **FR-006**: The system MUST resolve the address-form `did:ethr` variants — bare address (mainnet default), named-network, and hex-chain-id — to the issuer's address and its chain identifier, using only the **default** DID document (no on-chain rotation/delegates/service resolution).
- **FR-007**: The chain identifier MUST be treated as part of the issuer's identity for trust matching; the same address on a different chain is a different issuer. The chain identifier MUST NOT affect signature recovery.
- **FR-008**: An allow-listed address-form issuer DID MUST verify to **Pass** using the existing trust allow-list mechanism, with no new trust configuration surface.
- **FR-009**: A signature-valid credential from an issuer that is **not** allow-listed MUST be **Rejected** by default, and MUST be accepted at **Warn** (reduced assurance) only when the requirement's existing reduced-assurance opt-in is set — identical governance to Phase 1.
- **FR-010**: A signature failure, an address mismatch, an ambiguous recovery with no matching candidate, or a malformed/unsupported issuer identifier MUST always result in **Reject** (never Warn).
- **FR-011**: The capability MUST be available on both verification surfaces (internal workflow engine and standalone verifier) with identical outcomes.
- **FR-012**: Existing verification behaviour MUST be unchanged for Phase 1 key-form Ethereum issuers (`did:key`/`did:jwk` secp256k1), for non-Ethereum issuers, and for holder key-binding.
- **FR-013**: The feature MUST introduce no new third-party dependency and MUST remain usable in the browser-hosted (WebAssembly) verifier — no native or platform-specific components.
- **FR-014**: The address-recovery capability MUST NOT expose any signing capability; it is verification-only.
- **FR-015**: A later phase's on-chain resolution (ERC-1056 registry read) MUST be able to extend the `did:ethr` handling without changing this phase's offline default-document behaviour or the verification contract.

### Key Entities

- **Address-form issuer DID**: An issuer identifier (`did:pkh` or address-form `did:ethr`) that names the issuer by blockchain address and chain, without embedding a public key.
- **Recovered signer address**: The Ethereum address derived from the public key recovered from a credential's signature; the value compared against the issuer DID's address.
- **Issuer trust decision**: The existing outcome (Pass / Warn / Reject) produced by the trust evaluation, reused unchanged for address-form issuers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A credential from an allow-listed `did:pkh` or address-form `did:ethr` issuer, validly signed, verifies to **Pass** with no network access.
- **SC-002**: A credential whose signature does not correspond to the issuer's address verifies to **Reject** in 100% of cases.
- **SC-003**: A signature-valid credential from an un-allowlisted address-form issuer verifies to **Reject** by default and to **Warn** only when the reduced-assurance opt-in is set — matching Phase 1 governance in 100% of cases.
- **SC-004**: All three address-form `did:ethr` variants and the `did:pkh` form resolve to the correct issuer address, verified against known reference vectors.
- **SC-005**: Verification produces identical outcomes on both the internal engine and standalone verifier surfaces for the same inputs.
- **SC-006**: 100% of the existing Phase 1 and prior verification test suites continue to pass (no regression), and the default fail-closed behaviour is preserved for an un-allowlisted issuer with the opt-in unset.
- **SC-007**: New verification and recovery logic is covered by tests to at least the project's required threshold (>85% of new code).

## Assumptions

- The credential is an SD-JWT or JWT verifiable credential signed with JOSE **ES256K** (Ethereum secp256k1 over SHA-256). Other proof formats (EIP-712 JSON-LD, EAS attestations) are out of scope.
- Only the **issuer** position is in scope; holder key-binding continues to require an embedded key, as in Phase 1.
- The reference recovery vectors (private key → signature → address) used for testing are well-known Ethereum test vectors, so correctness can be anchored independently.
- The existing trust allow-list matches issuer DID strings exactly, so chain-aware trust for address-form DIDs requires no new matching logic.
- `did:pkh` is inherently rotation-free; only `did:ethr` has an on-chain rotation story, which is deferred to a later phase behind an optional resolution seam.
- The reduced-assurance opt-in (`WarnOnUnlistedVerifiedIssuer`), reduced-assurance signalling, and Pass/Warn/Reject mapping introduced in Phase 1 are reused unchanged.
