# Feature Specification: `did:ethr` On-Chain Resolution via Read-Only EVM RPC (ERC-1056)

**Feature Branch**: `179-ethr-rpc-resolution`

**Created**: 2026-07-09

**Status**: Draft

**Input**: User description: "Phase 2b did:ethr on-chain resolution via read-only EVM RPC (ERC-1056). Resolve the CURRENT did:ethr DID document from ERC-1056 registry state so ES256K issuer signatures verify against the currently-authorised keys (current owner, unexpired sigAuth/veriKey delegates, unexpired did/pub key attributes). Server-side only; fail-closed on configured-RPC error; no node, no on-chain write."

## User Scenarios & Testing *(mandatory)*

Phase 2 (Feature 178) verifies a `did:ethr` issuer against its **default** document — the address that *is* the DID, resolved entirely offline. But `did:ethr` is an on-chain identity: its owner can be **rotated**, additional signing **delegates** can be authorised, and **public keys** can be published, all recorded on a public registry. A verifier that only knows the default document draws two wrong conclusions: it **accepts a signature from an owner that has since been rotated away** (a revoked key), and it **rejects a credential validly signed by an authorised delegate** (the common issuer pattern). Phase 2b lets Sorcha read the issuer's *current* on-chain state — over a read-only blockchain query to a configured provider, with no node and no writes — so verification reflects who is authorised to sign **now**.

### User Story 1 - Verify a credential from a rotated or delegate-signed `did:ethr` issuer (Priority: P1)

A blueprint author trusts a `did:ethr` issuer. That issuer has rotated its owner key and/or signs credentials with an authorised delegate. A holder presents such a credential. Sorcha reads the issuer's current on-chain state and confirms the signature came from a currently-authorised key, then accepts the credential.

**Why this priority**: This is the core capability — without it, credentials from the most common real-world `did:ethr` issuers (Veramo, which rotates and delegates) cannot be verified at all. A single rotated/delegate-signed credential verifying is a complete, demonstrable slice of value.

**Independent Test**: Configure a provider endpoint for the issuer's chain; present a credential signed by the current owner and one signed by an authorised delegate — both accepted; present one signed by a rotated-away former owner — rejected.

**Acceptance Scenarios**:

1. **Given** a trusted `did:ethr` issuer whose owner has been rotated, **When** a credential signed by the **current** owner is presented, **Then** verification succeeds.
2. **Given** the same issuer, **When** a credential signed by the **rotated-away former** owner is presented, **Then** verification fails.
3. **Given** a `did:ethr` issuer that has authorised a signing delegate, **When** a credential signed by that **unexpired** delegate is presented, **Then** verification succeeds.
4. **Given** a delegate whose authorisation has **expired**, **When** a credential signed by it is presented, **Then** verification fails.
5. **Given** a `did:ethr` issuer that has published a public key on-chain, **When** a credential signed by that key is presented, **Then** verification succeeds.

### User Story 2 - Safe behaviour when the provider is unavailable (Priority: P1)

The on-chain provider is configured for a chain but temporarily unreachable (timeout, network error, bad response). Rather than silently trusting a possibly-stale offline view, Sorcha refuses to verify — because it cannot confirm the issuer has not rotated away from the key that signed.

**Why this priority**: This is a security-critical correctness rule, not an enhancement. Falling back to the stale offline document when the provider errors would accept a revoked key — the exact failure Phase 2b exists to prevent. It is P1 alongside Story 1 because the feature is unsafe without it.

**Independent Test**: Configure a provider that errors; present any `did:ethr` credential on that chain; confirm it is rejected (not accepted via a stale offline document). Then remove the provider configuration entirely for a chain and confirm the offline default document is used (the explicit offline mode).

**Acceptance Scenarios**:

1. **Given** a provider **configured** for the issuer's chain that returns an error, **When** a `did:ethr` credential on that chain is verified, **Then** it is **rejected** (no stale-document fallback).
2. **Given** **no** provider configured for a chain, **When** a `did:ethr` credential on that chain is verified, **Then** the offline default document is used (identical to prior behaviour).
3. **Given** a provider configured for a chain, **When** the on-chain state shows the DID has **no history** (never changed), **Then** the offline default document is used (the chain confirms no rotation).

### User Story 3 - The offline surfaces are unaffected (Priority: P2)

Verification that runs in an offline context (the browser-hosted wallet verifier) continues to behave exactly as before — it does not attempt any network query and is not broken by this feature.

**Why this priority**: Preserves the existing offline/no-network guarantee of the wallet verifier. P2 because it is a preservation requirement rather than new capability, but it must hold.

**Independent Test**: Run the offline verifier composition (no provider available) against a `did:ethr` credential; confirm it resolves the offline default document and never makes a network call.

**Acceptance Scenarios**:

1. **Given** the offline verifier composition, **When** it resolves a `did:ethr` issuer, **Then** it uses the offline default document and performs no network query.
2. **Given** a `did:pkh` issuer (rotation-free), **When** it is verified anywhere, **Then** behaviour is unchanged by this feature.

### Edge Cases

- A delegate or published key with an expiry in the past MUST be excluded from the current document.
- A key superseded by a later change MUST NOT reappear (newest state wins).
- A credential whose signing-key identifier does not correspond to any currently-authorised key MUST be rejected.
- A blocked or private provider address (e.g. an internal host) MUST be refused unless explicitly permitted (dev override), consistent with existing outbound-request protection.
- Behaviour for existing key-form (`did:key`/`did:jwk`) and address-form-default (Phase 2) issuers, and for non-`did:ethr` issuers, MUST be unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST resolve the **current** `did:ethr` DID document from on-chain registry state via a **read-only** blockchain query to a configured provider — no blockchain node, no wallet, and no on-chain write.
- **FR-002**: The resolved current document MUST include, as currently-authorised signing keys: the **current owner** (after any rotation), each **unexpired signing delegate**, and each **unexpired published key attribute**.
- **FR-003**: Each authorised entity MUST honour its on-chain validity/expiry; expired or superseded entries MUST be excluded.
- **FR-004**: An ES256K issuer signature MUST verify only against a currently-authorised key; a signature by a rotated-away owner, an expired delegate, or a superseded key MUST be rejected.
- **FR-005**: On-chain resolution MUST run **server-side only**; offline/browser-hosted verification MUST retain the Phase-2 offline default-document behaviour and MUST NOT perform any network query.
- **FR-006**: If a provider is **configured** for a chain but the query **fails** (timeout, network error, or malformed response), resolution MUST **fail closed** (reject) and MUST NOT fall back to the offline default document.
- **FR-007**: If **no** provider is configured for a chain, resolution MUST use the offline default document (the operator's explicit offline mode), identical to prior behaviour.
- **FR-008**: If the on-chain state shows the DID has **no history** (never changed), resolution MUST use the offline default document.
- **FR-009**: Provider queries MUST be protected against server-side request forgery consistent with existing outbound DID resolution (reject private/reserved hosts unless a documented dev override is set) and MUST time out.
- **FR-010**: The provider endpoint per chain and any registry-address override MUST be operator-configurable; the well-known registry address MUST be the default.
- **FR-011**: This feature MUST NOT change the verification decision logic, the trust evaluation, or the issuer-key matching — the current document reuses only existing verification-method shapes (key-match and address-recovery).
- **FR-012**: `did:pkh` and all Phase-1/Phase-2 behaviours MUST be unchanged.
- **FR-013**: The feature MUST NOT introduce any new third-party dependency and MUST NOT expose any signing or transacting capability (read-only).
- **FR-014**: Only signing-relevant on-chain data is read; service endpoints and non-signing attributes are out of scope for this feature.

### Key Entities

- **Current `did:ethr` document**: The DID document reconstructed from on-chain state, listing the currently-authorised signing keys (owner, delegates, published keys) with their validity.
- **Authorised signing key**: An owner address, delegate address, or published public key that is currently valid on-chain and against which an issuer signature may verify.
- **Provider configuration**: The per-chain read-only endpoint (and optional registry-address override) the operator supplies to enable on-chain resolution for that chain.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A credential from a `did:ethr` issuer whose owner has rotated verifies to **Pass** when signed by the current owner and to **Reject** when signed by the former owner, in 100% of cases.
- **SC-002**: A credential signed by an unexpired authorised delegate verifies to **Pass**; one signed by an expired delegate verifies to **Reject**, in 100% of cases.
- **SC-003**: When the provider is configured but errors, `did:ethr` verification on that chain **Rejects** in 100% of cases (no stale-document acceptance).
- **SC-004**: When no provider is configured, or the DID has no on-chain history, verification uses the offline default document — matching Phase-2 behaviour exactly.
- **SC-005**: The offline verifier makes **zero** network queries resolving `did:ethr`, verified by test.
- **SC-006**: 100% of existing Phase-1/Phase-2 and prior verification suites continue to pass; `did:pkh` behaviour is unchanged.
- **SC-007**: New on-chain-resolution logic is covered by tests to at least the project's required threshold (>85% of new code), including rotation, delegate expiry, key supersession, and the fail-closed and offline paths.

## Assumptions

- The credential is an SD-JWT or JWT verifiable credential signed with JOSE ES256K, in the issuer position (holder key-binding remains out of scope, per Phase 2).
- The on-chain registry follows the standard ERC-1056 event model (owner change, delegate change, attribute change with validity), deployed at the well-known address unless overridden per chain.
- Issuers set a signing-key identifier on the credential that names which authorised key signed it (the common issuer behaviour); a credential omitting it falls back to the first currently-authorised key (documented best-effort).
- The trust decision (allow-list / reduced-assurance opt-in) introduced in Phase 1 applies unchanged once the signature is verified against a currently-authorised key.
- Providers are public read-only endpoints reached over HTTPS; local/dev nodes require the documented private-address override.
- Document caching from existing DID resolution applies; cache freshness tuning for on-chain state is a later refinement, not part of this feature.
