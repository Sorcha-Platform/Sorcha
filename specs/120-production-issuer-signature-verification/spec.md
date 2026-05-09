# Feature Specification: Production Issuer Signature Verification

**Feature Branch**: `120-production-issuer-signature-verification`
**Created**: 2026-05-09
**Status**: Draft
**Input**: User description: see `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md` for the authoritative design — every architectural and product decision in this spec is locked there and must not be relitigated.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Verifier rejects unverifiable credentials by default (Priority: P1)

A consuming application (the citizen verifier, the wallet's inbound credential pipeline, or any other point-of-use) receives a presented credential. Before accepting any claim from that credential, the platform resolves the issuer's published identity, locates the verification method named in the credential's signature header, and verifies the cryptographic signature. A credential whose issuer cannot be resolved, whose signature header points to no published key, or whose signature does not verify against the published key is rejected.

**Why this priority**: Without this story, a consuming application has no cryptographic basis for trusting any claim made by any credential. Every other story in this feature exists to support this one; until it ships, the platform's credential ecosystem is structurally insecure.

**Independent Test**: Submit three credentials to a presentation flow — one with a valid signature from a published issuer, one with a tampered signature, one whose issuer identity does not resolve. The valid one is accepted; the other two are rejected with distinguishable failure reasons surfaced in operational logs.

**Acceptance Scenarios**:

1. **Given** an organisation has published its identity and an issued credential whose signature is intact, **When** that credential is presented, **Then** the credential is accepted and the verifier records a successful verification outcome.
2. **Given** a credential whose signature has been altered after issuance, **When** that credential is presented, **Then** the credential is rejected and the failure is recorded as a signature mismatch.
3. **Given** a credential whose stated issuer cannot be resolved (the identity has no published document, or the published document is unreachable), **When** that credential is presented, **Then** the credential is rejected and the failure is recorded as an unresolved issuer.
4. **Given** a credential whose signature header references a key identifier not present in the issuer's published document, **When** that credential is presented, **Then** the credential is rejected and the failure is recorded as an unmatched key identifier.

---

### User Story 2 — Organisations have a published verifiable identity (Priority: P1)

When an organisation issues its first credential, the platform automatically publishes a public identity document for that organisation. The document declares the keys the organisation uses to sign credentials and is reachable at a stable, well-known location. The document is regenerated whenever the organisation's signing keys change (rotation, revocation, additional keys added). External parties — including standards-compliant wallets, verifiers, and auditors — can fetch and parse the document using ordinary web infrastructure.

**Why this priority**: User Story 1 has nothing to verify against without a published identity for each issuer. This story is the enabling counterpart to US1 — both must ship together for either to deliver value.

**Independent Test**: Trigger a first credential issuance for a freshly-created organisation. Confirm that the organisation's published identity document is now reachable at the documented well-known location, that it declares at least one signing key, and that a third-party tool (such as a generic W3C DID resolver) can fetch and parse it without Sorcha-specific knowledge.

**Acceptance Scenarios**:

1. **Given** an organisation has never issued a credential, **When** the organisation issues its first credential, **Then** a published identity document for the organisation becomes reachable at a stable URL and that document declares the signing key used for the credential.
2. **Given** an organisation already has a published identity document, **When** a credential is issued under an already-derived signing key, **Then** the document does not need to be regenerated.
3. **Given** an organisation's signing key has been changed (rotated or revoked) by an authorised governance action, **When** the change is committed, **Then** the published document reflects the new state on the next request.

---

### User Story 3 — Federation interop without Sorcha-specific tooling (Priority: P2)

A standards-compliant external wallet or verifier — one that understands the IETF/W3C credential and identity standards but has no knowledge of Sorcha's internal naming conventions — can verify a Sorcha-issued credential. The credential carries an identity reference that any standards-compliant resolver can dereference. The published document follows the W3C identity-document conventions in widespread use.

**Why this priority**: Federation is a property the platform claims to support but currently cannot demonstrate. Shipping US1 + US2 with only Sorcha-internal naming would lock the credential ecosystem inside the platform. This story ensures that external interop works on day one — even if it is not heavily exercised at v1.

**Independent Test**: Take a credential issued by Sorcha and present it to a standards-compliant external verifier with no special configuration. The external verifier resolves the issuer, fetches the public key, and verifies the credential without needing any Sorcha-specific code or trust anchor.

**Acceptance Scenarios**:

1. **Given** a credential issued by a Sorcha-hosted organisation, **When** that credential is examined by a standards-compliant external verifier, **Then** the verifier can resolve the issuer's identity and successfully verify the credential's signature using only standard tooling.
2. **Given** a Sorcha-internal verifier and an externally-issued credential whose issuer publishes a standards-compliant identity document, **When** that credential is presented, **Then** the Sorcha verifier can resolve and verify it through the same path used for Sorcha-internal credentials.

---

### User Story 4 — Cross-resolution prevents identity impersonation (Priority: P2)

When an organisation's identity is reachable through more than one identifier — for example, the platform-internal name and the public-web name — the document at each identifier names the others. A verifier that receives a credential under one identifier looks up the others as well, confirms that the same signing key appears in every linked document, and only then accepts the equivalence as established. If the documents diverge in their published key material, the verifier rejects the credential.

**Why this priority**: Without cross-resolution, anyone who could write to the public-web hosting of an identity document could claim equivalence to any other organisation's identity and issue credentials in their name. This story closes the impersonation gap that US3's federation surface would otherwise open.

**Independent Test**: Construct a fixture in which the public-web identity document falsely claims equivalence to a different organisation's platform-internal identity, while serving an attacker-controlled signing key. Present a credential signed by the attacker's key. The verifier rejects the credential because the platform-internal identity's published document does not contain the attacker's key.

**Acceptance Scenarios**:

1. **Given** an organisation has published consistent identity documents under both its identifiers, **When** a credential signed by that organisation is presented, **Then** the cross-resolution succeeds and the credential is verified normally.
2. **Given** an attacker has obtained write access to one of an organisation's published documents and altered it to claim equivalence to a different organisation, **When** a credential signed by the attacker's key is presented, **Then** the cross-resolution fails and the credential is rejected.
3. **Given** one of the documents in an equivalence chain is temporarily unreachable, **When** a credential is presented, **Then** the verifier rejects the credential rather than accepting on the resolvable side alone.

---

### User Story 5 — Per-action issuer allowlist honours equivalent identities (Priority: P3)

A blueprint action that requires a credential from a specific list of issuers should treat all equivalent identifiers for the same issuer as acceptable. A blueprint author who lists the platform-internal identifier should not need to also list the federated identifier (and vice versa) — the platform automatically follows the equivalence chain when matching.

**Why this priority**: This story removes a sharp edge in blueprint authoring. Without it, blueprint authors would need to know about both identifier forms for every issuer they accept, doubling the maintenance surface and creating an obvious source of authoring error. It is P3 because the existing per-action allowlist already enforces the strict-string match correctly; this story adds the equivalence-aware variant.

**Independent Test**: Author a blueprint action whose allowlist contains only the platform-internal identifier of an issuer. Present two credentials from that issuer: one whose signature header references the platform-internal identifier, one whose signature header references the federated identifier. Both are accepted as satisfying the allowlist.

**Acceptance Scenarios**:

1. **Given** an action allowlist names only one of an issuer's published identifiers, **When** a credential is presented under any other equivalent identifier of that issuer, **Then** the allowlist match succeeds and the action proceeds.
2. **Given** an action allowlist names a specific identifier and a credential is presented from a different issuer entirely (not equivalent), **When** the credential is matched against the allowlist, **Then** the allowlist match fails and the action does not proceed on this credential.

---

### User Story 6 — Issuance key compromise can be remediated by governance (Priority: P3)

If an organisation's signing key is compromised — leaked, stolen, or otherwise lost the property of being controlled only by the organisation — an authorised quorum of administrators can revoke it. After revocation, any credential signed by the revoked key is rejected on the next presentation. Credentials signed before the revocation by the same key are also rejected; the platform's audit log records the revocation event with a timestamp.

**Why this priority**: Key-compromise remediation is a security must-have, but its frequency is low; the v1 manual-rotation, manual-revocation pattern is enough. It is P3 because the existing governance-op machinery (Feature 086 validator roster pattern) provides the precedent and most of the infrastructure; this story is primarily about wiring it to the issuance key.

**Independent Test**: Initiate a governance-op revocation against an organisation's currently-active issuance key. Confirm that subsequent presentation attempts of credentials signed by that key are rejected, that the audit log records the revocation event, and that the organisation can continue to operate (issue future credentials) by deriving a new signing key.

**Acceptance Scenarios**:

1. **Given** an organisation has an active signing key and credentials in circulation signed by that key, **When** an authorised quorum revokes the key, **Then** subsequent presentations of credentials signed by that key are rejected and the audit log records the revocation.
2. **Given** an organisation's signing key has been revoked, **When** the organisation issues a new credential, **Then** the new credential is signed under a fresh key and the published identity document reflects the new key alongside (or in place of) the revoked one.
3. **Given** a revocation has been initiated but the quorum has not yet been reached, **When** a credential signed by the (still pending) key is presented, **Then** the credential is verified normally — revocation only takes effect after quorum approval.

---

### Edge Cases

- An organisation's published identity document is briefly unreachable due to a transient network failure: the verifier should not silently fall back to acceptance and should not panic-reject every credential.
- An organisation rotates its signing key while a credential signed under the previous key is mid-flight: previously-issued credentials remain verifiable until they expire, while new credentials are signed under the new key.
- A credential's signature header references a key identifier whose form does not match the platform's default style but matches an alternate style published in the same identity document: verification still succeeds.
- A presented credential names an issuer using an identifier method the platform does not yet support (a method registered with W3C but not implemented in the resolver registry): the credential is rejected with a distinct, actionable failure code.
- An issuer's identity document declares equivalence with a third party who has not reciprocated the declaration: cross-resolution fails the equivalence check and the credential is rejected.
- A first-credential-issuance attempt happens during a brief window when the issuance key has just been derived but the identity document has not yet been published: the issuance flow blocks until the document is reachable.
- An attacker constructs a credential with a forged signature whose corresponding key is not in any published document: the credential is rejected at the unmatched-key-identifier stage.
- A cached identity document expires mid-presentation: the verifier transparently re-resolves; presentation latency varies but the outcome is correct.

## Requirements *(mandatory)*

### Functional Requirements

#### Verification (the core flow)

- **FR-001**: System MUST verify the cryptographic signature of every presented credential against the issuer's published key material before accepting any claim from that credential.
- **FR-002**: System MUST resolve the credential's issuer identifier through the platform's identity-resolver registry and use the verification method named in the credential's signature header to obtain the verification key.
- **FR-003**: When the issuer identifier cannot be resolved, when no verification method matches the signature header's key identifier, or when the signature does not verify against the matched key, the system MUST reject the credential and MUST surface the three failure reasons as distinguishable outcomes in operational logs and metrics.

#### Issuer identity publication

- **FR-004**: System MUST publish a public identity document for an organisation no later than the moment that organisation's first credential is issued.
- **FR-005**: System MUST publish each Sorcha-hosted organisation under both its platform-native identifier and a publicly-resolvable federated identifier, with each document declaring the other as an equivalent identity.
- **FR-006**: System MUST regenerate an organisation's published identity document whenever the organisation's signing-key state changes (a key is added, rotated, or revoked).
- **FR-007**: System MUST serve published identity documents at stable, well-known URLs that conform to the relevant external standard for the identifier method, so that standards-compliant external resolvers can dereference them without Sorcha-specific knowledge.

#### Cross-identity verification

- **FR-008**: When an issuer's published identity document declares equivalence with one or more other identifiers, the system MUST resolve each declared equivalent identity, compare the verification methods across all linked documents, and accept the equivalence only when the same verification key appears in every linked document.
- **FR-009**: When a declared equivalent identity is unreachable, the system MUST reject the credential rather than accept on the basis of the resolvable side alone.
- **FR-010**: When verification keys diverge across documents in an equivalence chain, the system MUST reject the credential and MUST surface the divergence as a distinct failure reason.

#### Key identifier styles

- **FR-011**: System MUST publish each active signing key under at least two equivalent identifier styles in the issuer's identity document — one human-readable and sequential, and one cryptographically derived from the key itself.
- **FR-012**: System MUST verify credentials whose signature header references either identifier style, without requiring per-credential or per-issuer configuration to switch between them.
- **FR-013**: System MUST allow the platform-default identifier style to be expressed as a configuration value, and MUST reserve a per-organisation override slot in the organisation record so that this default can be overridden in future without a schema change.

#### Issuer allowlist matching

- **FR-014**: When a blueprint action declares an allowlist of acceptable issuer identifiers, the system MUST treat any identity declared as equivalent (via the cross-identity mechanism in FR-008) to a listed identifier as also satisfying the allowlist.
- **FR-015**: System MUST preserve the existing publish-time guardrail that flags blueprint actions with empty allowlists as overly permissive.

#### Lifecycle and governance

- **FR-016**: System MUST allow administrators to revoke an organisation's signing key via a quorum-approved governance action; subsequent presentations of credentials signed by the revoked key MUST be rejected.
- **FR-017**: System MUST allow administrators to rotate an organisation's signing key via a quorum-approved governance action; previously-issued credentials signed under the prior key MUST remain verifiable until those credentials expire.
- **FR-018**: System MUST defer the derivation of an organisation's signing key until the moment of that organisation's first credential issuance — organisations that never issue credentials MUST never accumulate unused signing keys.

#### Default enforcement and rollout

- **FR-019**: System MUST provide a platform-level configuration switch that gates whether issuer-signature verification is required, and the default state of that switch at ship MUST be that verification is required.
- **FR-020**: System MUST reserve, in the per-register policy record, a slot for per-register issuer-signature enforcement that overrides the platform-level switch. The slot MUST NOT be read at v1 but MUST be tolerant of future readers (any future addition of read logic MUST NOT require schema migration of existing records).
- **FR-021**: System MUST reserve, in the per-register policy record, a slot for a register-wide issuer allowlist. The slot MUST NOT be read at v1 but MUST follow the same forward-compatibility rule as FR-020.

#### Resolver hygiene

- **FR-022**: System MUST cache resolved identity documents for a bounded period appropriate to the identifier method (longer for inherently-immutable methods such as offline-derived keys, shorter for documents fetched over the public web).
- **FR-023**: System MUST invalidate cached entries for platform-internal identities whenever an on-platform key event for that identity is committed.
- **FR-024**: System MUST consolidate the platform's identity-resolution surface to a single canonical interface; the legacy parallel resolver MUST be retired before this feature ships.

#### Forward-compatibility

- **FR-025**: The component that resolves an issuer's identity to a verification key MUST be deployable in any process that reads credentials — its public contract MUST NOT depend on its hosting context, so that future deployments (for example in a different service that needs the same capability) are a wiring change rather than a redesign.

### Key Entities *(include if feature involves data)*

- **Issuer Identity Document**: A public, machine-readable description of an organisation's cryptographic identity. Lists the verification methods (public keys) the organisation uses to sign credentials, describes their relationships (which key signs assertions, which key authenticates), and may declare equivalence with other identity documents that represent the same underlying organisation.
- **Verification Method**: An entry within an identity document representing one specific public key, named by an identifier (the *key identifier*) that credential signatures can reference. Each active signing key may appear as multiple verification methods sharing the same key material under different identifier styles.
- **Equivalence Declaration**: A claim within one identity document that another identifier represents the same organisation. The platform verifies such claims by cross-resolving the named identifier and comparing key material.
- **Issuance Key**: The cryptographic key an organisation uses to sign credentials, derived from the organisation's master key under the platform's existing key-derivation infrastructure. Has a lifecycle (derived → active → rotated/revoked) controlled exclusively by the platform's custodial key-management facility.
- **Issuer Allowlist (per-action)**: An optional declaration on a blueprint action that names which issuer identities may satisfy the action's credential requirement.
- **Issuer Allowlist (per-register, reserved)**: A future-only declaration on a register's policy record that names which issuer identities may issue credentials referenced anywhere in that register. Reserved at v1 (slot exists, not read).
- **Issuer-Signature Enforcement Switch**: The platform-level configuration that gates whether issuer-signature verification is mandatory. Default at v1: required. May be overridden per-register in future.
- **Identity Resolver Registry**: The platform component that turns an identifier into an identity document, dispatching to method-specific resolvers (platform-internal, web-hosted, key-derived, others). Caches results, follows equivalence chains, and consolidates the contract used by every credential-consuming surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An attacker who tampers with the signature of any presented credential cannot complete a presentation flow — the tampered credential is rejected before any of its claims are read.
- **SC-002**: An attacker who controls a credential issuer's public-web hosting and rewrites the published identity document to claim equivalence to a different organisation cannot impersonate that organisation to a Sorcha verifier — the impersonation attempt is detected and the credential rejected.
- **SC-003**: 100% of the existing walkthrough suite (covering verified identity, finance, permitting, and other primary credential flows) passes end-to-end with issuer-signature verification enforced by default.
- **SC-004**: A standards-compliant external wallet — one that knows only the IETF/W3C credential and identity standards and nothing about Sorcha — successfully verifies a Sorcha-issued credential using only the published federated identity document, with no Sorcha-specific configuration.
- **SC-005**: When an issuer's signing key is compromised, the time required for an authorised quorum to revoke that key and have subsequent presentations of compromised credentials rejected is bounded by the duration of a single governance operation — typically minutes, not hours.
- **SC-006**: The platform's operational dashboard distinguishes the three signature-verification failure causes (unresolved issuer, unmatched key identifier, signature mismatch), enabling operators to triage failures by root cause rather than by symptom.
- **SC-007**: The future addition of validator-side issuer-signature verification at seal time (deferred to a later phase) requires no schema migration on existing register policy records — the slots reserved at v1 are read by the validator without changes to the record structure.
- **SC-008**: First-credential-issuance flows for new organisations complete in a single end-to-end ceremony: derive signing key, publish identity document, issue credential — with no manual intervention required between steps.
- **SC-009**: Steady-state verification latency for repeat issuers (after the first cache miss) does not regress relative to the pre-feature baseline by more than a single resolver round-trip's typical wall-clock time.

## Assumptions

- The platform is in a pre-production posture: there are no in-flight credentials issued under the prior accept-everything default that need a deprecation window. Default-on at ship is therefore safe.
- Domain control of a publicly-hosted identity document is treated as equivalent to identity ownership of the federated identifier, in line with the trust model used by the public web's TLS infrastructure.
- An organisation's signing keys are generated, stored, rotated, and revoked exclusively through the platform's existing custodial key-management infrastructure (Feature 083). Self-custody and co-signed modes are out of scope.
- The walkthrough suite is a representative test surface for production credential flows; passing the suite end-to-end with verification enforced is sufficient evidence that no in-tree flow is broken by the change.
- The W3C `alsoKnownAs` property is the canonical mechanism for declaring identity equivalence; signed equivalence assertions (a non-standard, Sorcha-specific extension) are explicitly out of scope for v1.
- The legacy parallel identity-resolver interface has exactly one consumer in the codebase and its retirement is a small, mechanical migration that ships as a precursor PR.

## Dependencies

- **Feature 083 (Org Key Derivation)**: Provides the per-organisation signing-key derivation infrastructure. The issuance key in this feature is derived under that feature's `KeyUsage.VCIssuance` slot.
- **Feature 086 (Validator Roster pattern)**: Provides the precedent for governance-op key rotation and revocation. The proto-rule code `VAL_CRED_GOV_001` (revoke issuance key) follows this pattern.
- **Feature 093 (multibase verification method emission)**: Provides the W3C-conforming key encoding used in published identity documents.
- **W3C DID Core 1.0**: External standard for the identity document structure and the `alsoKnownAs` semantics.
- **IETF SD-JWT VC**: External standard for the credential format whose signatures this feature verifies.

## Out of Scope

The following are deliberately deferred and MUST NOT be addressed by this feature. Each has a documented forward-compat path so that revisiting it later does not require rework of v1.

- **Validator-side issuer-signature verification at seal time** (the chain-authoritative posture, "Future B" in the design doc). The schema slots reserved by FR-020 and FR-021 exist precisely so this can be added later without migration.
- **Bring-your-own-domain federated identifiers**. Organisations cannot in v1 publish their identity document on a domain they control; the Sorcha-hosted form is the only federated path. The cross-resolution mechanism (FR-008) is the upgrade hinge — when BYO-domain ships, the old Sorcha-hosted identifier remains resolvable, and the two documents declare each other.
- **Automatic, schedule-driven key rotation**. v1 supports manual rotation only.
- **Additional identifier methods beyond those already supported by the platform** (`did:ethr`, `did:ion`, etc.). The resolver registry pattern means each new method is a single additive class without changes to the rest of the feature.
- **Signed equivalence assertions**. Cross-resolution is the mechanism in v1; a signed-assertion variant (in which an equivalence claim itself carries a cryptographic proof of the second party's consent) is not included.
- **Per-credential-type issuer-signature enforcement**. The per-action allowlist already covers the operational need at this granularity.

## References

- **Authoritative design**: `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md` (file paths, class names, phase breakdown, locked decisions D1–D6 with rationale).
- **Companion architectural memos** (shared memory): `Validator2/2026-05-09-programmable-validation-thesis.md`, `Validator2/2026-05-09-did-resolution-and-issuer-sig-companion.md` — captures the Future A vs Future B framing and the alignment with future validator rule families.
