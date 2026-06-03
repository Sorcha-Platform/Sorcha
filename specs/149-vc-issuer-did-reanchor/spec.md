# Feature Specification: Re-anchor org VC-issuer DID to the operational wallet (+ fail-closed issuance)

**Feature Branch**: `149-vc-issuer-did-reanchor`
**Created**: 2026-06-03
**Status**: Draft
**Input**: Re-anchor the organisation VC-issuer DID to the canonical operational wallet, plus fail-closed credential issuance. Full design: `docs/superpowers/specs/2026-06-03-org-vc-issuer-did-reanchor-design.md`.

## Overview

When a Sorcha organisation issues a native verifiable credential (an SD-JWT VC delivered to a Sorcha-held wallet), the credential names its issuer with a decentralised identifier (DID). Today that issuer DID is anchored on a **per-issuance-key child wallet** that the organisation derives internally, whereas every other part of the platform identifies the organisation by its **canonical operational wallet**. The two identifiers never match, so a relying party that pins trust on the organisation's known identity rejects the credential, and an organisation that has not completed key setup silently produces a credential no conformant verifier can check.

This feature re-anchors the issuer DID onto the organisation's **canonical operational identity**, publishes the issuance key as a verification method under that single identity, makes the verifier resolve the organisation's **published** identity document, and makes issuance **fail closed** (with an actionable error) instead of producing an unverifiable credential.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A relying party trusts a credential issued by a known organisation (Priority: P1)

A relying-party organisation (e.g. an insurer) requires a credential from a specific, named issuer organisation (e.g. an assessor) and pins trust on that issuer's canonical identity. The issuer issues the credential; the relying party verifies it and accepts it.

**Why this priority**: This is the core promise — a credential from a known organisation must be trustable by another organisation that already knows that organisation's identity. Without it, native cross-organisation credentials cannot be used at all.

**Independent Test**: Configure a workflow where one organisation issues a credential and another requires it with trust pinned to the issuer's canonical identity. Submit the flow end-to-end and confirm the verifying step accepts the credential (issuer signature verified, issuer identity trusted).

**Acceptance Scenarios**:

1. **Given** an issuer organisation with completed key setup and a relying party that pins trust on that organisation's canonical identity, **When** the issuer issues a credential and the relying party verifies it, **Then** the issuer signature is verified and the issuer identity matches the pinned trust (the credential is accepted).
2. **Given** the issued credential, **When** its issuer identifier is inspected, **Then** it is the organisation's canonical identity (the same identity used for register ownership, invitations, and trust pins), not an internal per-key identifier.
3. **Given** a relying party resolving the issuer's identity, **When** it fetches the issuer's published identity document, **Then** the document advertises the issuance key under the organisation's canonical identity and the credential's signature checks against it.

---

### User Story 2 - Issuance fails closed with an actionable error when the organisation cannot produce a verifiable credential (Priority: P2)

An organisation that has not completed the key setup required to issue verifiable credentials attempts to issue one. Instead of silently producing an unverifiable credential, issuance is refused with a clear, actionable message naming the missing setup step.

**Why this priority**: The silent-degradation path is what makes the failure expensive to diagnose (it surfaces later as an opaque trust rejection at the verifier). Failing at the point of cause, with guidance, removes the entire class of "looks fine, fails downstream" confusion.

**Independent Test**: Attempt to issue a credential for an organisation that has not completed issuance-key setup, and confirm the attempt is refused with an error that names the required setup step (and that no credential is delivered).

**Acceptance Scenarios**:

1. **Given** an organisation with no issuance-key setup, **When** a credential issuance is attempted, **Then** the attempt fails with an actionable error identifying the missing setup, and no credential is produced or delivered.
2. **Given** the same organisation after completing issuance-key setup, **When** issuance is retried, **Then** it succeeds and the resulting credential is verifiable per User Story 1.

---

### User Story 3 - A rotated issuance key still resolves correctly (Priority: P3)

An organisation rotates its issuance key. Credentials signed under the new key continue to verify, because the verifier resolves the organisation's published identity document, which advertises all active issuance keys.

**Why this priority**: Rotation is a real lifecycle event; a verifier that only understood the first key would reject everything after a rotation. Lower priority because it is exercised less often than first issuance, but it must not regress.

**Independent Test**: Issue under an issuance key, rotate the key, issue again, and confirm both credentials verify.

**Acceptance Scenarios**:

1. **Given** an organisation that has rotated its issuance key, **When** a credential signed under the rotated key is verified, **Then** the verifier resolves the matching published key and the signature verifies.

---

### Edge Cases

- **Organisation has no canonical operational wallet recorded**: if the canonical identity that the issuer DID must anchor on is not available, issuance MUST fail closed (User Story 2) rather than fall back to an unverifiable identifier.
- **Published identity document is unreachable at verification time**: the verifier cannot resolve the issuer key and MUST reject (fail closed), consistent with existing fail-closed trust behaviour.
- **Issuer signature valid but issuer identity not on the relying party's trust allowlist**: the credential is rejected as untrusted (signature verification and trust pinning are distinct gates; both must pass).
- **Pre-existing credentials issued under the old per-key identifier**: out of scope — clean break, dev data is wiped and regenerated; such credentials are not expected to verify after the change.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A native credential's issuer identifier (the credential's `iss`) MUST be the issuing organisation's **canonical operational identity** (`did:sorcha:org:{operational-wallet}`), not an internal per-issuance-key identifier.
- **FR-002**: The credential's signing-key reference (`kid`) MUST be a verification method **under** the organisation's canonical identity (`did:sorcha:org:{operational-wallet}#vc-issuance-{n}`); the actual signing key material remains the organisation's derived VC-issuance key.
- **FR-003**: The organisation's **published identity document** MUST use the canonical operational identity as its subject and MUST advertise each active issuance key as a verification method referenced from `assertionMethod`, with each verification method's identifier matching the `kid` emitted at signing time.
- **FR-004**: The verifier MUST resolve the issuer's key from the organisation's **published identity document** (not from a locally reconstructed document), so that the signing key — which differs from the canonical-identity wallet — is correctly located.
- **FR-005**: Credential issuance MUST **fail closed** with an actionable error when it cannot produce a resolvable issuer identity (e.g. the organisation has not completed issuance-key setup). It MUST NOT fall back to emitting a bare, non-resolvable issuer identifier.
- **FR-006**: When trust is pinned to an issuer organisation's canonical identity (a DID allowlist), a credential issued by that organisation MUST satisfy the pin (the `iss` matches the pinned identity).
- **FR-007**: A credential signed under a **rotated** issuance key MUST verify, because the published identity document advertises all currently-active issuance keys.
- **FR-008**: The change is a **clean break** — no backward-compatibility bridge (no `alsoKnownAs` mapping from the old per-key identity) and no migration of already-issued credentials. Existing development data is wiped and regenerated.
- **FR-009**: Out of scope for this feature (explicitly): the credential media-type migration (`vc+sd-jwt` → `dc+sd-jwt`), the X.509/EUDI external trust rail, and the walkthrough setup change that provisions an organisation master key. These are tracked separately.

### Key Entities *(include if feature involves data)*

- **Issuing organisation**: an organisation that issues native credentials. Has a single canonical operational identity (its operational wallet) and a separately-derived issuance key used to sign credentials.
- **Canonical operational identity (A)**: the organisation's platform-wide identity (`did:sorcha:org:{operational-wallet}`), already used for register ownership, governance roster, register invitations, certificate subject names, and trust pins.
- **Issuance key (C)**: the organisation's derived VC-issuance signing key. Its public key is advertised as a verification method under the canonical identity; it is **not** itself a separate organisation identity.
- **Published identity document**: the organisation's resolvable DID document; the single authoritative mapping from a verification-method reference to the issuance public key.
- **Credential**: the native SD-JWT VC; carries the issuer identity (`iss`), the signing-key reference (`kid`), and is bound to a holder.
- **Trust pin (DID allowlist)**: a relying party's declared set of trusted issuer identities; compared against the credential's `iss`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A credential issued by an organisation with completed key setup, required by another organisation that pins trust on the issuer's canonical identity, is **accepted end-to-end** (issuer signature verified and issuer trusted) — demonstrated by the CyberEssentialsUac scenario reaching the relying party's accept step.
- **SC-002**: 100% of native credentials carry an issuer identity equal to the issuing organisation's canonical operational identity (no credential carries an internal per-key or bare-wallet issuer identifier).
- **SC-003**: An issuance attempt by an organisation without issuance-key setup is **refused with an actionable error** in 100% of cases, and produces **zero** delivered credentials.
- **SC-004**: A credential signed under a rotated issuance key verifies successfully (rotation does not regress verification).
- **SC-005**: No regression in existing credential, trust, and DID-resolution test suites; the trust clean-break gate remains green.

## Assumptions

- The organisation's canonical operational identity is recoverable at issuance time from the issuing organisation's identifier; if it is not recorded, that is surfaced via FR-005 (fail closed) rather than worked around.
- "Completed key setup" means the organisation has provisioned the master key from which the VC-issuance key is derived.
- The platform is pre-production: wiping and regenerating development data is acceptable, so no data migration is required (FR-008).
