# Feature Specification: Ethereum-key Verifiable Credential verification — Phase 1 (verify-only)

**Feature Branch**: `177-ethereum-vc-verify`

**Created**: 2026-07-09

**Status**: Draft

**Input**: User description: "Ethereum-key Verifiable Credential verification — Phase 1 (verify-only, secp256k1/ES256K)."

**Authoritative design**: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md` (approved). This spec restates that design as testable requirements; it does not re-open settled decisions.

## User Scenarios & Testing *(mandatory)*

Phase 1 is **verify-only**: Sorcha gains the ability to *accept and check* credentials that were signed with an Ethereum-style key. It never signs, never issues, and never touches a blockchain. This is the first slice of a four-phase interoperability roadmap; Phases 2–4 (address-form DIDs via RPC, prove-control/signing, transacting) are out of scope here.

### User Story 1 - Verify a credential issued with an Ethereum key (Priority: P1)

An external organisation in the wider Verifiable Credential ecosystem issues a W3C / SD-JWT credential signed with a secp256k1 key (JOSE **ES256K**), identifying itself with a DID that carries its public key directly (`did:key` secp256k1, or `did:jwk`). A person presents that credential to a Sorcha-gated action. Sorcha resolves the issuer's key **offline**, verifies the issuer signature, and reaches a trust decision — all through the same credential-verification pipeline it uses for its own credentials.

**Why this priority**: This is the core adoption value — Sorcha becoming able to *consume* credentials from the established Ethereum/SSI world. It is a viable MVP on its own: with only this story, Sorcha can verify a foreign Ethereum-issued credential end-to-end.

**Independent Test**: Present a Veramo-style ES256K SD-JWT/JWT credential from a `did:key`(secp256k1) issuer against a requirement that allowlists that issuer; confirm the credential is accepted (Pass) and that a tampered copy is rejected.

**Acceptance Scenarios**:

1. **Given** a valid ES256K-signed SD-JWT credential from an allowlisted `did:key`(secp256k1) issuer, **When** it is verified, **Then** the issuer signature validates and the outcome is **Pass** with full assurance.
2. **Given** the same credential with a single byte of the signature altered, **When** it is verified, **Then** the outcome is **Reject** (signature invalid) — never Warn.
3. **Given** a valid ES256K credential from a `did:jwk`(secp256k1) issuer, **When** it is verified, **Then** the issuer key resolves offline and the signature validates.

### User Story 2 - Verify an Ethereum holder key-binding (Priority: P2)

A holder presents a credential that is cryptographically bound to their own secp256k1 key (the credential carries a secp256k1 confirmation key, and the presentation includes an ES256K holder proof). Sorcha verifies that the presenter controls the bound key.

**Why this priority**: Completes the picture — Sorcha can verify not only who *issued* a credential with an Ethereum key, but that the *presenter* controls the Ethereum key the credential was bound to. Independently testable and independently valuable, but secondary to accepting foreign issuers.

**Independent Test**: Present a credential carrying a secp256k1 confirmation key with a matching ES256K holder proof; confirm the holder binding verifies, and that a mismatched/absent holder proof fails.

**Acceptance Scenarios**:

1. **Given** a credential with a secp256k1 confirmation key and a valid ES256K holder proof over the expected challenge, **When** the presentation is verified, **Then** the holder binding validates.
2. **Given** the same credential with a holder proof signed by a different key, **When** the presentation is verified, **Then** the binding fails and the outcome is Reject.

### User Story 3 - Govern trust of unlisted-but-verified Ethereum issuers (Priority: P3)

A blueprint author decides, per credential requirement, how much to trust an Ethereum issuer whose signature is cryptographically valid but who is **not** on that requirement's allowlist. By default such an issuer is rejected (fail-closed). The author may opt a specific requirement into accepting it at **reduced assurance** (a Warn) instead — an explicit, auditable choice at that gate.

**Why this priority**: Governs the openness/assurance trade-off that "interoperate with the wider world" demands, without weakening Sorcha's default posture. Depends on US1 being in place; refines rather than enables.

**Independent Test**: Verify a signature-valid credential from an issuer absent from the allowlist twice — once with the per-requirement flag off (expect Reject) and once with it on (expect Warn) — and confirm a non-Ethereum credential is unaffected either way.

**Acceptance Scenarios**:

1. **Given** a signature-valid credential from an unlisted Ethereum issuer and a requirement with the reduced-assurance flag **disabled**, **When** it is verified, **Then** the outcome is **Reject**.
2. **Given** the same credential and a requirement with the flag **enabled**, **When** it is verified, **Then** the outcome is **Warn** (accepted at reduced assurance) and the outcome records that the issuer was not on the allowlist.
3. **Given** an allowlisted Ethereum issuer, **When** it is verified, **Then** the flag has no effect and the outcome is **Pass** with full assurance.

### Edge Cases

- **Tampered signature** → Reject (never Warn).
- **Malformed or off-curve key material** (invalid secp256k1 JWK, bad point) → Reject.
- **Unresolvable DID** (unknown `did:key` multicodec, malformed `did:jwk`) → Reject.
- **Unlisted issuer, flag off** → Reject; **unlisted issuer, flag on** → Warn.
- **Non-Ethereum credential** (existing SD-JWT / mdoc) → verified exactly as before, fail-closed default preserved (regression).
- **`did:jwk` carrying a non-secp256k1 key** (P-256 / Ed25519) → resolves and verifies via the existing curves (general resolver), unaffected by the Ethereum work.
- **Address-form DID** (`did:pkh`, address-only `did:ethr`) → not resolvable in Phase 1; treated as an unresolvable/unsupported issuer → Reject (support arrives in Phase 2).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST verify the issuer signature of a W3C / SD-JWT Verifiable Credential signed with a secp256k1 key using JOSE **ES256K**.
- **FR-002**: The system MUST resolve an issuer or holder DID that carries a secp256k1 public key **offline** — `did:key` (secp256k1) and `did:jwk` — with no network or blockchain access.
- **FR-003**: The system MUST verify a holder key-binding where the credential carries a secp256k1 confirmation key and the presentation includes an ES256K holder proof.
- **FR-004**: Ethereum-key credentials MUST be verified through the **existing** credential-verification pipeline, producing the same outcome shape (Pass / Warn / Reject with assurance and evidence) as every other credential format — no separate or parallel verification path.
- **FR-005**: The system MUST treat an Ethereum issuer listed on a credential requirement's issuer allowlist as fully trusted, with assurance taken from configuration.
- **FR-006**: The system MUST, and only when a per-credential-requirement flag (`warnOnUnlistedVerifiedIssuer`) is enabled, accept a signature-valid but unlisted Ethereum issuer at **reduced assurance (Warn)** rather than reject it. This flag MUST default to disabled.
- **FR-007**: For any credential where the reduced-assurance flag is not set — including all existing non-Ethereum credentials — the system MUST preserve fail-closed behaviour (an issuer that is not vouched for is rejected).
- **FR-008**: The system MUST **Reject** (never Warn) any credential whose signature fails to verify, whose key material is malformed or not on the secp256k1 curve, or whose issuer/holder DID cannot be resolved.
- **FR-009**: The system MUST NOT require or perform any blockchain node or RPC access, any on-chain read or write, and MUST NOT place any personal data on-chain.
- **FR-010**: The system MUST provide keccak256 hashing and EIP-55 Ethereum-address derivation as tested internal capabilities (foundation for later phases), and MUST NOT invoke them from any Phase 1 verification path.
- **FR-011**: The same verification path MUST accept credentials from both the Web3 SSI ecosystem (e.g. a Veramo-style ES256K JWT-VC) and EU/EUDI-style `did:jwk` credentials.
- **FR-012**: Phase 1 MUST remain verify-only: the system MUST NOT gain the ability to sign with a secp256k1 key or to issue Ethereum-signed credentials (signing is a later phase), and MUST NOT alter the existing wallet signing capability.

### Key Entities *(include if feature involves data)*

- **Ethereum-key credential**: A W3C / SD-JWT Verifiable Credential whose issuer signature (and optionally whose holder key-binding) uses a secp256k1 key with JOSE ES256K.
- **Key-bearing DID**: A decentralised identifier that yields a secp256k1 public key by offline decoding — `did:key` (secp256k1 multicodec) or `did:jwk`.
- **Issuer trust policy (per requirement)**: The issuer allowlist plus the `warnOnUnlistedVerifiedIssuer` flag that governs whether a signature-valid unlisted issuer is rejected or accepted at reduced assurance.
- **Verification outcome**: The result of verification — Pass (full assurance), Warn (reduced assurance), or Reject — carrying the trust evidence used to reach it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of valid ES256K credentials from an allowlisted key-bearing issuer verify and are accepted (Pass).
- **SC-002**: 0% false accepts — every tampered, malformed, off-curve, or unresolvable-issuer credential is rejected.
- **SC-003**: A signature-valid unlisted issuer is accepted at reduced assurance (Warn) only when the per-requirement flag is enabled, and rejected otherwise — both outcomes demonstrable from the same input.
- **SC-004**: 100% of the existing credential-verification regression suite passes unchanged, confirming non-Ethereum credentials and the fail-closed default are unaffected.
- **SC-005**: The complete verification test suite runs and passes with **no** network or blockchain access (fully offline).
- **SC-006**: The same verification path accepts one Web3-style fixture and one EU/EUDI-style `did:jwk` fixture, proving shared plumbing across both ecosystems.
- **SC-007**: keccak256 and EIP-55 address derivation match published known-answer vectors, and no Phase 1 verification path invokes them.

## Assumptions

- The existing credential-verification pipeline (credential-format handling, trust evaluation, DID resolver registry, and outcome model) is reused; Phase 1 adds an isolated cryptographic primitive and offline resolvers, not a parallel pipeline.
- The cryptographic building blocks required (secp256k1 curve operations, keccak256) are already available to the platform; no new third-party dependency is introduced.
- Address-form Ethereum DIDs (`did:pkh`, address-only `did:ethr`) require a blockchain read and/or public-key recovery and are **out of scope** for Phase 1 (Phase 2).
- Blueprint authors configure issuer allowlists and the reduced-assurance flag per credential requirement; reduced assurance is never a global or implicit default.
- Holder key-binding trust is purely cryptographic (proof of control) and requires no external trust source.
- No personal data is placed on-chain at any point, consistent with Sorcha's DAD model and GDPR guidance.
