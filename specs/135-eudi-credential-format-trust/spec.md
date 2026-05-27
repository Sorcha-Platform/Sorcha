# Feature Specification: EUDI Credential Format & Unified Trust

**Feature Branch**: `135-eudi-credential-format-trust`
**Created**: 2026-05-20
**Status**: Draft
**Input**: User description: "EUDI Digital Identity Wallet interoperability — a credential-format abstraction (adding ISO mdoc beside SD-JWT VC), a unified trust model consulted by all verification paths, and a selectable issuer trust-anchor."

## Overview

Sorcha can today issue and verify exactly one credential format (SD-JWT VC), and it does so through **two verification paths that disagree**. The externally-facing wallet path (HAIP) performs real cryptographic verification — issuer-key resolution, signature checks, certificate-chain validation against trusted roots, and status-list revocation. The internal workflow-engine path performs none of this: it matches the issuer by a flat string list, never verifies the signature (it records the credential as cryptographically unverified and defers to "the service layer"), and has no concept of a trust root. Whether a credential is genuinely trusted therefore depends on which door it came through.

This feature closes that gap and prepares Sorcha to interoperate with the EU Digital Identity (EUDI) wallet ecosystem by delivering three coupled capabilities:

1. **A credential-format seam** so the platform can issue, present, and verify more than one credential format behind one abstraction — adding ISO mdoc (`mso_mdoc`) alongside the existing SD-JWT VC.
2. **A unified trust model** — a single trust evaluator consulted by *every* verification path, expressing trust as a configurable policy over pluggable trust sources, replacing both the flat issuer allowlist and the ad-hoc trusted-root list.
3. **A selectable issuer trust-anchor** so an issuer can choose the credential format and the trust anchor (Sorcha register, tenant X.509 CA, or an external trust list) when minting a credential to an external wallet.

This is **prerelease** work: old shapes (the flat `AcceptedIssuers` issuer list, the ad-hoc trusted-root list, the unverified-signature shortcut) are removed outright and replaced. There are no backward-compatibility shims or deprecation paths.

## Clarifications

### Session 2026-05-20

- Q: How should the external EU trust list (LOTL) roots be sourced into the certificate trust store? → A: Pluggable trust-list provider seam; ship one implementation that loads an operator-supplied, versioned snapshot (file/config) with a freshness timestamp. Live LOTL XML fetch/parse is a future provider behind the same seam.
- Q: How is a credential's assurance level (low/substantial/high) determined? → A: Source-tier mapping — each trust source confers an operator-configurable assurance level — combined with an *upward-only* override from an explicit credential assurance claim where the source supports it. Absent signal → low.
- Q: What revocation mechanism applies to `mso_mdoc` credentials? → A: An IETF Token Status List reference carried in the MSO, resolved through the unified status-checking abstraction, under the same fail-closed default as SD-JWT VC.
- Q: Clean break removes the flat accepted-issuer list — what is the default trust policy when a requirement declares none? → A: Any pre-existing accepted-issuer identifiers migrate to a `did-allowlist` trust source; requirements with nothing declared default to the register/DID source at assurance "low". No compatibility shim.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One consistent trust decision for every verification path (Priority: P1)

A relying party (a Sorcha workflow action, or an external verifier desk) receives a presented credential and needs to know whether to trust it. Today the answer differs depending on which verification path runs. After this story, **both** paths route through one trust evaluator that performs real signature verification, resolves the issuer's key, evaluates the credential against a declared **trust policy** (which trust sources are acceptable, combined with anyOf/allOf, at or above a minimum assurance level), checks revocation, and returns a single trust decision plus an audit record of *why* the credential was trusted (which source vouched, at which register height / CRL version / trust-list snapshot).

The blueprint author no longer pins issuers with a flat list of identifiers; they declare a trust policy. The platform fails closed by default: if trust cannot be positively established, the credential is rejected.

**Why this priority**: This delivers value with zero credential-format work and fixes a real correctness defect that exists today — the internal engine path accepts credentials it has never cryptographically verified. It is the foundation both later stories build on.

**Independent Test**: With only SD-JWT VC credentials, present a credential through the internal workflow-engine path and confirm it is now rejected when the signature is invalid, when the issuer is untrusted under the policy, or when the assurance level is below the required minimum — and accepted, with a populated trust-evidence record, when all hold. Repeat the identical credential and policy through the external (HAIP) path and confirm the *same* decision and the *same* evidence shape.

**Acceptance Scenarios**:

1. **Given** a workflow action whose requirement declares a trust policy accepting the tenant X.509 root, **When** a credential signed by a leaf under that root with a valid chain and unrevoked status is presented through the internal engine path, **Then** the credential is accepted, its signature is reported as verified, and a trust-evidence record names the vouching source and the CRL version consulted.
2. **Given** the same requirement, **When** a credential with a structurally valid but cryptographically invalid signature is presented, **Then** it is rejected with a signature-failure reason on **both** verification paths.
3. **Given** a requirement whose policy requires assurance level "substantial" or higher, **When** a credential establishing only "low" assurance is presented, **Then** it is rejected with an assurance-level reason.
4. **Given** a requirement whose policy combines two sources with `allOf`, **When** a presented credential satisfies only one source, **Then** it is rejected; **When** it satisfies both, **Then** it is accepted.
5. **Given** a trust source is temporarily unreachable (e.g. a revocation endpoint), **When** a credential is presented under the default fail-closed posture, **Then** it is rejected with an "unavailable" reason rather than silently accepted.
6. **Given** an accepted credential, **When** the verification result is recorded, **Then** the trust-evidence record can be pinned and re-evaluated offline to reproduce the same decision.

---

### User Story 2 - Accept an mdoc credential from an EUDI wallet (Priority: P2)

An external EUDI wallet holder presents an ISO mdoc credential (for example, a Person Identification Data credential or a mobile driving licence) to a Sorcha verifier over the online presentation flow. The verifier accepts the `mso_mdoc` format through the same format seam and the same trust evaluator used for SD-JWT VC, validates the issuer signature over the mobile security object, verifies the holder's device-binding over the presentation session, checks the disclosed data elements against the credential requirement, and resolves revocation. The relying party experiences no difference in outcome shape between an mdoc and an SD-JWT VC presentation.

**Why this priority**: This is the headline interoperability capability — accepting credentials from the EUDI wallet ecosystem — and depends on the unified trust model from US1.

**Independent Test**: Submit an `mso_mdoc` presentation built for the online flow against a credential requirement and confirm: the issuer signature over the security object verifies, the device-binding over the session handover verifies, disclosed data elements map to the requirement's expected claims, an untrusted issuer is rejected under the policy, and a revoked credential is rejected. Confirm an SD-JWT VC presentation against an equivalent requirement still passes unchanged.

**Acceptance Scenarios**:

1. **Given** a credential requirement that accepts the `mso_mdoc` format with a trust policy naming an external trust list, **When** a valid mdoc PID is presented online with a verifiable issuer signature and device-binding, **Then** it is accepted and its disclosed data elements are surfaced to the workflow as claims.
2. **Given** the same requirement, **When** an mdoc whose issuer is not vouched for by any policy source is presented, **Then** it is rejected with an untrusted-issuer reason.
3. **Given** an mdoc presentation whose device-binding does not match the presentation session, **When** it is verified, **Then** it is rejected with a holder-binding-failure reason.
4. **Given** an mdoc whose digests do not match the disclosed data elements, **When** it is verified, **Then** it is rejected with an integrity-failure reason.
5. **Given** an mdoc carrying a revocation status reference that resolves to "revoked", **When** it is verified under fail-closed policy, **Then** it is rejected.

---

### User Story 3 - Issue an mdoc credential with a chosen trust anchor (Priority: P3)

A credential issuer (configured by a blueprint author) mints a non-qualified electronic attestation of attributes (EAA) to an external wallet and chooses both the **format** (SD-JWT VC or `mso_mdoc`) and the **trust anchor** the credential will be trusted under (the Sorcha register, the tenant X.509 CA, or an external trust list). When an X.509-backed anchor is chosen, the issued credential carries the correct certificate chain so a downstream verifier can validate it to the chosen root. When the register anchor is chosen, the credential remains verifiable via Sorcha's decentralised identifier resolution.

**Why this priority**: Issuance completes the round-trip and is the most contained slice — it builds on the format seam (US2) and trust model (US1) and resolves the open question of when and how the certificate chain attaches to issued credentials.

**Independent Test**: Configure a credential-issuance step to mint an `mso_mdoc` EAA with the tenant-X.509 trust anchor; mint it to an external wallet; confirm the issued credential is a well-formed mdoc carrying the expected certificate chain, and that presenting it back through US2 verification succeeds against a policy trusting that anchor. Repeat with the SD-JWT VC format and with the register anchor.

**Acceptance Scenarios**:

1. **Given** an issuance configuration specifying `mso_mdoc` format and the tenant-X.509 trust anchor, **When** a credential is minted to an external wallet, **Then** the issued credential is a valid mdoc whose security object is signed by the org's leaf certificate and carries the leaf-to-root chain.
2. **Given** an issuance configuration specifying the SD-JWT VC format and the tenant-X.509 trust anchor, **When** a credential is minted, **Then** the issued SD-JWT VC carries the org certificate chain (closing the gap where issued credentials currently carry no chain).
3. **Given** an issuance configuration specifying the register trust anchor, **When** a credential is minted, **Then** the credential is verifiable via decentralised-identifier resolution and carries no certificate chain.
4. **Given** an issuance configuration requesting a format the issuer is not configured to produce, **When** minting is attempted, **Then** it fails with a clear configuration error rather than silently producing the other format.

---

### Edge Cases

- **Mixed presentations**: a holder presents an SD-JWT VC for one requirement and an mdoc for another in the same exchange — each is verified under its own format handler but the same trust policy semantics.
- **Format/anchor mismatch**: an issuance config names a trust anchor incompatible with the chosen format or with the issuer's available keys (e.g. an X.509 anchor when no org certificate has been provisioned) — issuance fails closed with a configuration error.
- **Assurance level absent**: a credential carries no explicit assurance-level signal — the platform treats it as the lowest level rather than assuming a higher one.
- **Trust-list staleness**: the external trust-list snapshot is older than a configured freshness bound — the decision records the snapshot age and the policy decides whether to reject.
- **Post-quantum posture**: mdoc/EUDI mandates classical elliptic-curve signatures only; introducing it must not weaken or silently downgrade the post-quantum options available to Sorcha-native credentials and signing elsewhere.
- **Offline / pinned re-evaluation**: a verifier re-evaluates a previously accepted credential offline using only pinned trust evidence (no live network) and must reach the same decision, or an explicit "cannot re-evaluate offline" outcome — never a different accept.
- **Revocation source disagreement**: a credential references a revocation mechanism the platform cannot resolve — fail-closed by default.
- **Combinator with an unavailable source**: an `anyOf` policy where one source is unreachable but another positively vouches — the credential is accepted on the available source; an `allOf` policy with one unreachable source fails closed.

## Requirements *(mandatory)*

### Functional Requirements

**Credential-format seam**

- **FR-001**: The platform MUST verify, present, and issue credentials through a format-agnostic abstraction with at least two interchangeable implementations: SD-JWT VC and `mso_mdoc`.
- **FR-002**: The platform MUST verify `mso_mdoc` credentials presented over the online presentation flow, including verifying the issuer signature over the credential's security object and the integrity of disclosed data elements against the security object's digests.
- **FR-003**: The platform MUST verify the holder's device-binding for an mdoc presentation against the presentation-session handover (the agreed session parameters), as the format-equivalent of key-binding for SD-JWT VC.
- **FR-004**: The platform MUST map blueprint claim definitions to mdoc namespaces and document types, supporting at minimum the EUDI PID and mobile driving licence document types, and MUST surface disclosed mdoc data elements to workflows as claims in the same shape as SD-JWT VC claims.
- **FR-005**: The platform MUST NOT implement ISO 18013-5 proximity presentation (device engagement over BLE/NFC) in this feature; the format seam MUST be designed so proximity can be added later without re-opening the abstraction.
- **FR-006**: Introducing `mso_mdoc` (which uses classical elliptic-curve signatures only) MUST NOT remove or downgrade the post-quantum signing options available to Sorcha-native credentials and operations. Where a key cannot be represented in the platform's preferred decentralised-identifier key encoding, the platform MUST fall back to a standard public-key representation rather than failing.

**Unified trust model**

- **FR-007**: A single trust evaluator MUST be consulted by every credential-verification path (both the external wallet path and the internal workflow-engine path). No verification path may make its own independent trust decision.
- **FR-008**: The internal workflow-engine verification path MUST perform real issuer-signature verification. The current behaviour of recording a credential as cryptographically unverified and deferring verification elsewhere MUST be removed.
- **FR-009**: Trust MUST be expressed as a **trust policy** attached to a credential requirement, replacing the flat list of accepted issuer identifiers. The flat accepted-issuer list MUST be removed outright (no compatibility shim).
- **FR-010**: A trust policy MUST support pluggable **trust sources** behind a registry, including at minimum: (a) Sorcha register / decentralised-identifier resolution including issuer-equivalence, (b) the tenant X.509 certificate authority with chain and revocation-list validation, (c) an external trust list of roots, and (d) an explicit allowlist of issuer identifiers with equivalence.
- **FR-011**: A trust policy MUST support `anyOf` and `allOf` combinators over its trust sources.
- **FR-012**: A trust policy MUST support a minimum assurance level (low, substantial, high); a credential establishing a level below the requirement MUST be rejected. The established level MUST be derived from the **assurance level conferred by the vouching trust source** (operator-configurable per source), with an **upward-only override** from an explicit credential assurance claim where the source supports it; when no signal is present the level MUST default to "low".
- **FR-013**: Every trust decision MUST default to fail-closed: when trust cannot be positively established (untrusted issuer, invalid signature, unresolved or unavailable revocation, unreachable required source, insufficient assurance), the credential MUST be rejected.
- **FR-014**: Each accepted trust decision MUST produce a **trust-evidence** audit record naming which source vouched and the basis consulted (e.g. register height, certificate-revocation-list version, trust-list snapshot identifier). This evidence MUST be carryable via the platform's existing verification-receipt mechanism.
- **FR-015**: Trust resolution MUST be **pinnable**: a verifier MUST be able to re-evaluate a credential offline from pinned evidence and reach the same accept/reject decision, or report that offline re-evaluation is not possible — never a different accept.
- **FR-016**: The platform MUST expose the two existing revocation/status-list mechanisms (W3C bitstring status list and IETF token status list) through one common status-checking abstraction, so every verification path checks revocation uniformly.
- **FR-017**: The external trust list of roots MUST be loadable into the certificate-validation trust store through a **pluggable trust-list provider seam**. This feature MUST ship one provider that loads an operator-supplied, versioned snapshot (file/config) carrying a snapshot identifier and freshness timestamp; a live EU LOTL fetch/parse provider MAY be added later behind the same seam without changing the trust evaluator. The freshness/staleness of the loaded snapshot MUST be recorded in trust evidence.
- **FR-026**: When a credential requirement declares no explicit trust policy, the platform MUST apply a default: any pre-existing accepted-issuer identifiers MUST be migrated to a `did-allowlist` trust source, and requirements with nothing declared MUST default to the register/DID trust source at assurance "low". No compatibility shim for the removed flat accepted-issuer list is provided (prerelease clean break).

**Issuer trust-anchor**

- **FR-018**: A credential-issuance configuration MUST allow the author to select the credential **format** (`sd-jwt-vc` or `mso_mdoc`).
- **FR-019**: A credential-issuance configuration MUST allow the author to select the **trust anchor** the issued credential is trusted under: the Sorcha register, the tenant X.509 certificate authority, or an external trust list.
- **FR-020**: When an X.509-backed trust anchor is selected, the issued credential MUST carry the correct certificate chain so a downstream verifier can validate it to the chosen root. Issued credentials MUST stop carrying no chain when an X.509 anchor applies (closing the current gap where issued credentials attach no chain).
- **FR-021**: The platform MUST mint `mso_mdoc` electronic attestations of attributes to external wallets, in addition to SD-JWT VC. Qualified/QTSP-certified issuance is explicitly out of scope.
- **FR-022**: When the requested format or trust anchor cannot be satisfied (e.g. no provisioned org certificate for an X.509 anchor), issuance MUST fail with a clear configuration error rather than silently substituting a different format or anchor.

**Cross-cutting**

- **FR-023**: New externally-reachable endpoints MUST carry interactive API documentation with a summary and description, per platform documentation standards.
- **FR-024**: Trust decisions MUST emit observability signals (metrics and structured logs) recording the outcome, the deciding source, the format, and the assurance level, without logging credential subject data.
- **FR-025**: The credential format identifier MUST coexist with the existing presentation-source and target-audience routing discriminators on requirements and issuance configs without conflict.

### Key Entities *(include if feature involves data)*

- **CredentialFormat**: the wire format of a credential — `sd-jwt-vc` or `mso_mdoc`. Drives which format handler issues, presents, and verifies a credential.
- **TrustPolicy**: the trust expectation attached to a credential requirement — an ordered set of trust sources, an `anyOf`/`allOf` combinator, and a minimum assurance level. Replaces the flat accepted-issuer list.
- **TrustSource**: one pluggable means of vouching for an issuer — register/decentralised-identifier, tenant X.509 CA, external trust list, or explicit DID allowlist — each able to answer "do I vouch for this issuer, and on what basis".
- **TrustDecision**: the outcome of evaluating a credential against a trust policy — accept or reject, the deciding source(s), the established assurance level, and a failure reason when rejected.
- **TrustEvidence**: the audit record of a trust decision — which source vouched and the basis consulted (register height, CRL version, trust-list snapshot, snapshot freshness). Pinnable and carried on verification receipts; sufficient to re-evaluate offline.
- **AssuranceLevel**: the level of identity assurance a credential establishes — low, substantial, or high.
- **mdoc security object (MSO)**: the issuer-signed object binding the credential's data-element digests, document type, validity window, and holder device key. The mdoc equivalent of the SD-JWT VC issuer-signed body.
- **mdoc IssuerSigned data elements**: the disclosed attribute values, grouped by namespace, each verifiable against an MSO digest. The mdoc equivalent of SD-JWT disclosures.
- **mdoc device response / device authentication**: the holder's proof of possession over the presentation session, binding the response to the agreed session parameters. The mdoc equivalent of the key-binding proof.
- **TrustList snapshot**: a loaded set of external trust-anchor roots with a snapshot identifier and freshness timestamp, consulted as a trust source and recorded in evidence.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of credential verifications, regardless of which verification path runs, produce the same accept/reject decision and the same evidence shape for the same credential and the same trust policy.
- **SC-002**: 0 credentials are accepted with an unverified issuer signature on any verification path (the current internal-path defect is eliminated).
- **SC-003**: A verifier can accept a valid mdoc credential from a conformant EUDI wallet over the online presentation flow, and reject an mdoc that is untrusted, has an invalid signature, has a broken holder-binding, or is revoked.
- **SC-004**: An issuer can mint both SD-JWT VC and mdoc credentials to an external wallet and choose among at least three trust anchors (register, tenant X.509, external trust list), with the issued credential carrying the correct trust material for the chosen anchor.
- **SC-005**: Every accepted trust decision yields a trust-evidence record that is sufficient to reproduce the same decision offline from the pinned evidence alone.
- **SC-006**: A trust policy can express "any of these sources" and "all of these sources" and a minimum assurance level, and the verifier honours all three in combination.
- **SC-007**: When any required trust input is unavailable, the default outcome is rejection (fail-closed) in 100% of cases; fail-open is only ever reached by explicit policy.
- **SC-008**: Automated test coverage for the new trust-evaluation and format-handling logic is at or above 85%.
- **SC-009**: Adding the mdoc format introduces no third-party cryptographic dependency for its core encoding and signing primitives, and does not reduce the set of post-quantum signing options available elsewhere in the platform.

## Assumptions

These are informed defaults taken to keep the spec complete; the most consequential are revisited in `/speckit.clarify`.

- **A1 — mdoc scope is online only.** `mso_mdoc` is supported over the OpenID4VP/OpenID4VCI online flow only. ISO 18013-5 proximity (BLE/NFC device engagement) is deferred to a separate future feature, but the format seam is shaped so it can be added without re-opening the abstraction.
- **A2 — No qualified issuance.** Only non-qualified electronic attestations of attributes (EAA) are issued. QTSP / qualified-issuer certification is out of scope.
- **A3 — Certificate-chain attachment reuses the existing fail-soft resolver pattern.** When an X.509 trust anchor applies, the certificate chain is resolved at the issuance call site using the platform's existing fail-soft chain-resolution pattern; if the chain cannot be resolved, issuance under an X.509 anchor fails closed (a credential that should carry a chain must not be issued without one).
- **A4 — Assurance level: source-tier mapping with upward-only claim override.** *(Confirmed in clarification.)* Each trust source confers an operator-configurable assurance level; an explicit credential assurance claim may override upward only where the source supports it; absent any signal the level is "low". See FR-012.
- **A5 — mdoc revocation uses the IETF token status list.** *(Confirmed in clarification.)* mdoc credentials carry a status reference in the MSO resolved through the same unified status-checking abstraction as SD-JWT VC, under the same fail-closed default. See FR-016.
- **A6 — External trust list via a pluggable provider, operator snapshot shipped.** *(Confirmed in clarification.)* The trust list is loaded behind a provider seam; the shipped provider reads an operator-supplied versioned snapshot with a freshness timestamp. Live LOTL fetch is deferred to a future provider behind the same seam. See FR-017.
- **A7 — Clean break.** The flat accepted-issuer list, the ad-hoc verifier trusted-root list, and the unverified-signature shortcut are removed outright. Blueprints and configs are migrated to the trust-policy shape; no compatibility shim is provided (prerelease).
- **A8 — Coverage target.** Test coverage targets >85% per project guidance, exceeding the constitution's 80% floor.

## Dependencies

- Verification-receipt and revocation infrastructure (spec 079) — carries trust evidence.
- Tenant X.509 organisation trust: per-tenant root CA, org leaf certificates, certificate-revocation lists (spec 096).
- External-wallet issuance and presentation flows over OpenID4VCI/OpenID4VP (specs 097/098).
- Genesis trust anchor / system register (spec 099).
- Decentralised-identifier equivalence and issuer key rotation (Feature 120).

## Out of Scope (Non-Goals)

- ISO 18013-5 proximity presentation (BLE/NFC device engagement).
- QTSP / qualified-issuer certification.
- JSON-LD / Data Integrity Proofs.
- Any change to the SD-JWT VC wire format.
