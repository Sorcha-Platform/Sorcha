# Feature Specification: EUDI Conformance — Protocol Alignment & External Trust Rail

**Feature Branch**: `181-eudi-conformance`

**Created**: 2026-07-10

**Status**: Draft

**Input**: User description: "EUDI conformance — protocol drift sweep (dc+sd-jwt, DCQL, multibase status decode, x509_san_dns client_id) + trust rail (ETSI TS 119 612 LOTL consumption, external X.509 anchors, org cert enrolment)"

## Overview

Sorcha's credential-presentation and credential-trust surfaces were built against draft versions of the
OpenID4VC specifications and an internal-only X.509 trust model. The final EUDI-aligned profiles (HAIP 1.0
final, OpenID4VP 1.0 final, SD-JWT VC final) have moved underneath us, and the external trust rail
(`x509-lotl`) exists as a named anchor kind with no working implementation behind it. This feature closes
both gaps — the **protocol dialect** and the **trust rail** — so that:

1. A standards-conformant external wallet (an EUDI wallet, or any wallet built against the final profiles)
   can receive, understand, and answer a Sorcha presentation request.
2. Sorcha can verify credentials issued by external EUDI-ecosystem issuers, anchored on the official
   European trusted-list infrastructure.
3. A Sorcha organisation can carry an externally-issued certificate so that credentials it issues are
   verifiable by parties outside the Sorcha ecosystem.

Two things this feature is explicitly **not**: it is not the issuance-flow hardening layer (wallet
attestation, pushed authorization, sender-constrained tokens — a follow-on feature), and it is not the
EUDI PID credential profile (doctype/namespace/mandatory-claims shape — a follow-on feature). Those tiers
build on this one.

### Decisions locked during specification (with the platform owner)

| # | Decision | Choice |
|---|----------|--------|
| D1 | Query-language migration strategy | **Clean break** to DCQL across every producer and consumer in one move. No dual Presentation-Exchange/DCQL support. **All existing OpenID4VP/OpenID4VCI routes are preserved unchanged** — only the request/response body dialect migrates. |
| D2 | Multi-credential interactions | **In scope**: a single presentation request may ask for multiple credentials at once, and may express alternatives ("credential A, or credential B if the holder lacks A"). |
| D3 | Trusted-list consumption mode | **Operator snapshot import**: an administrator imports a trusted-list document; the platform validates it, extracts the certificate-authority anchors, and stores a versioned snapshot with freshness metadata. Live scheduled refresh is a documented follow-up, not v1. |
| D4 | External issuance identity | **Operator-imported org certificates**: an org admin generates a certificate signing request from the org's existing issuing key, obtains a certificate from an external authority out-of-band, and uploads the certificate + chain. Sorcha never mints externally-trusted certificates itself. |
| D5 | Enrolment ergonomics | **Auto-enrol + admin surface**: every eligible (P-256) organisation automatically receives its internal tenant-root certificate at creation; an admin surface lists, re-issues, and imports certificates. |
| D6 | Ed25519 organisations | **Typed exclusion**: organisations whose issuing key is Ed25519 are cleanly reported as not eligible for the X.509 rail (EUDI mandates P-256; the current behaviour is an unhandled server error). No Ed25519 certificate support is built. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Standards-conformant presentation dialect (Priority: P1)

A holder using a standards-conformant external wallet (e.g. an EUDI reference wallet) scans a Sorcha
verifier's QR code. The wallet fetches the signed request, understands the credential query (expressed in
the final profile's query language, DCQL), selects a matching credential, and submits a presentation that
Sorcha verifies successfully. Simultaneously, a citizen using the Sorcha wallet PWA completes the same
flows they can complete today — council-page credential gates, the open verifier, workflow presentation
gates — with no user-visible change, because the whole platform speaks the new dialect end-to-end.

**Why this priority**: Every other conformance goal is unreachable while our requests use a query language
final-profile wallets reject. This is also the largest coordinated change (three request producers, two
wallet consumers, three verification paths), so it must land first and cleanly.

**Independent Test**: Run the existing presentation walkthroughs (council credential gate, open verifier,
HAIP workflow gate) end-to-end against the migrated stack — all pass. Separately, capture a generated
presentation request and validate its body against the final OpenID4VP profile's request schema — no
legacy query-language fields present.

**Acceptance Scenarios**:

1. **Given** a verifier creates a presentation request for one credential type with three required claims,
   **When** the signed request object is fetched via its request URI, **Then** the request body contains a
   DCQL credential query (and no Presentation Exchange `presentation_definition`), and the media/type
   identifiers use the final SD-JWT VC form (`dc+sd-jwt`).
2. **Given** a citizen holds a matching credential in the Sorcha wallet PWA, **When** they scan the QR and
   consent, **Then** the wallet submits a response whose token envelope is keyed by the request's
   credential query identifier, the verifier validates it, and the outcome records success.
3. **Given** an in-flight workflow uses the F111 presentation gate, **When** the presentation completes,
   **Then** the workflow advances exactly as before — callback routes, status routes, and lifecycle events
   are byte-for-byte unchanged in their URLs and semantics.
4. **Given** newly issued SD-JWT credentials, **When** their type header is inspected, **Then** it carries
   the final profile's media type (`dc+sd-jwt`), and Sorcha's own verifiers accept **both** the new and
   previously-issued (`vc+sd-jwt`) credentials it already delivered to wallets.
5. **Given** the codebase after migration, **When** the CI conformance gate runs, **Then** no producer or
   parser of the legacy Presentation Exchange dialect remains outside an explicitly allowlisted
   compatibility note.

---

### User Story 2 - Multi-credential and alternative asks (Priority: P2)

A council service needs two credentials in one interaction ("prove your identity AND your address"), or
accepts alternatives ("an EUDI PID, or a Sorcha Assured Identity credential"). The verifier expresses this
in a single presentation request; the citizen sees one consent flow listing each ask; the wallet resolves
alternatives against what the citizen actually holds; the response carries one presentation per satisfied
query.

**Why this priority**: This is the concrete payoff of the query-language migration — accepting an external
EUDI credential *or* a Sorcha credential for the same gate is the working definition of EUDI interop for
Sorcha's council use cases. It depends on US1's dialect but is separately testable and shippable.

**Independent Test**: Author a presentation request with two credential queries and a two-option
alternative set; walk a wallet holding only one of the alternatives through it; confirm the consent
surface shows both asks, the response satisfies the request, and verification reports per-query outcomes.

**Acceptance Scenarios**:

1. **Given** a request asking for credentials A and B, **When** the holder consents to both, **Then** the
   response contains two presentations, each independently verified, and the overall outcome is success
   only if every required query is satisfied.
2. **Given** a request expressing "A or B" alternatives, **When** the holder has only B, **Then** the
   wallet matches B, the consent surface shows the B option, and verification succeeds against the B
   branch of the alternative set.
3. **Given** a request asking for A and B, **When** the holder lacks B, **Then** the wallet clearly shows
   which ask cannot be satisfied and does not submit a partial response by default.
4. **Given** a response containing a presentation keyed to an unknown query identifier, **When** the
   verifier processes it, **Then** verification fails with a specific, diagnosable reason.

---

### User Story 3 - Verify external EUDI credentials via a trusted-list snapshot (Priority: P2)

A platform operator imports the official European trusted-list document (or a member-state trusted list).
The platform validates the document's signature, extracts the certificate-authority anchors, and stores
them as a versioned snapshot with freshness metadata. From then on, when a holder presents a credential
issued by an external issuer whose certificate chains to one of those anchors, Sorcha's verifiers accept
it under a trust policy that names the trusted-list source — and the verification evidence records which
list version vouched.

**Why this priority**: This makes the `x509-lotl` trust anchor real. It is the verify-side half of the
external trust rail and does not depend on US1 (a credential can be verified regardless of which query
dialect requested it), so it can proceed in parallel.

**Independent Test**: Import a known trusted-list fixture containing a test CA; present a credential
issued under that CA; verification succeeds with evidence naming the list snapshot. Present the same
credential with no snapshot imported; verification fails closed.

**Acceptance Scenarios**:

1. **Given** an administrator with a trusted-list XML document, **When** they import it, **Then** the
   platform verifies the document's own signature, rejects tampered or unsigned documents, extracts only
   the anchors of the relevant service types, and records a versioned snapshot with the list's sequence
   number, issue date, and next-update date.
2. **Given** a stored snapshot, **When** a presented credential's certificate chain terminates at an
   anchor in that snapshot, **Then** trust evaluation succeeds via the trusted-list source and the
   verification evidence carries the snapshot identity and freshness.
3. **Given** a snapshot whose next-update date has passed, **When** a credential is evaluated against it,
   **Then** the platform still evaluates but flags the staleness in evidence and surfaces an
   operator-visible warning (metric + log); a configurable strict mode fails closed instead.
4. **Given** a new version of the same list is imported and a previously-anchored CA is absent from it,
   **When** a credential chaining to the removed CA is next evaluated, **Then** trust evaluation fails —
   the newest snapshot version is authoritative.
5. **Given** no snapshot has ever been imported, **When** a trust policy references the trusted-list
   source, **Then** evaluation fails closed with a specific "no trusted list available" reason (never an
   unhandled error, never a silent pass).

---

### User Story 4 - Externally-verifiable issuance identity (Priority: P3)

An organisation administrator wants credentials issued by their org to be verifiable by parties outside
Sorcha. They generate a certificate signing request bound to the org's existing issuing key, take it to an
external certificate authority (out-of-band), and upload the resulting certificate + chain. From then on,
credentials the org issues on the external anchor setting carry a certificate chain that terminates at the
external authority's root — so any third party that trusts that root (e.g. via the same European
trusted-list infrastructure) can verify the credential with no knowledge of Sorcha.

**Why this priority**: This is the issue-side half of the external rail. Valuable, but only after the
verify-side (US3) exists to test against, and only meaningful to orgs that have completed an external
registration — a smaller initial population.

**Independent Test**: Generate a CSR for a test org; sign it with a test CA; upload the cert + chain;
issue a credential with the external anchor setting; verify the credential using only the test CA root
(no Sorcha tenant root in the trust store).

**Acceptance Scenarios**:

1. **Given** an org whose issuing key is P-256, **When** the admin requests a CSR, **Then** the platform
   produces a standard CSR bound to that key, and the private key never leaves the platform's custody.
2. **Given** a certificate + chain uploaded by the admin, **When** the platform validates it, **Then** it
   confirms (a) the certificate's public key matches the org's issuing key, (b) the chain is internally
   consistent and within validity, and (c) the certificate is suitable for credential signing — rejecting
   mismatches with specific reasons.
3. **Given** an org with an imported external certificate, **When** it issues a credential configured for
   the external trust anchor, **Then** the credential's embedded chain terminates at the external root
   (not the Sorcha tenant root), and issuance fails closed with a clear error if the external anchor is
   requested but no imported certificate exists.
4. **Given** an imported certificate approaching expiry, **When** an admin views the org's certificate
   surface, **Then** expiry is visible, and issuance under an expired imported certificate fails closed
   with a specific reason.
5. **Given** orgs with tenant-root certificates only, **When** they issue on the internal anchor setting,
   **Then** nothing about their behaviour changes.

---

### User Story 5 - Certificate lifecycle without footguns (Priority: P3)

Every new eligible organisation automatically receives its internal (tenant-root) certificate at creation
— no manual API call, no walkthrough-only setup path. An administrator surface lists each org's
certificates (internal and imported), shows validity and chain summary, and offers re-issue (internal) and
import (external). An organisation whose issuing key is Ed25519 sees a clear "not eligible for the X.509
rail" state everywhere a certificate would appear — instead of today's unhandled server error.

**Why this priority**: Ergonomics and error-quality work that turns US4's capability into something a
normal org actually reaches. Depends on the certificate surfaces existing but not on external interop.

**Independent Test**: Create a new P-256 org; observe a tenant-root certificate exists without any manual
step. Create an Ed25519 org; observe the certificate surface reports ineligibility with a typed reason and
no server error is logged.

**Acceptance Scenarios**:

1. **Given** a new organisation with a P-256 issuing key, **When** creation completes, **Then** a
   tenant-root certificate for the org exists and is visible on the admin surface, and a failure to enrol
   never fails org creation itself (enrolment is retried/reparable, and its failure is operator-visible).
2. **Given** an existing org created before this feature, **When** the admin opens the certificate
   surface, **Then** they can enrol it with one action (backfill).
3. **Given** an organisation with an Ed25519 issuing key, **When** any certificate operation is attempted,
   **Then** the response is a typed, documented "key type not eligible for X.509" outcome — never an
   unhandled ASN.1 error.
4. **Given** the admin surface, **When** a certificate is re-issued, **Then** the previous certificate's
   status is recorded and the new one becomes active — the history remains auditable.

---

### User Story 6 - Verifier authentication for wallets (Priority: P4)

A wallet (Sorcha's own PWA, or an external one) that receives a presentation request can authenticate the
verifier: the request's client identifier uses the DNS-based certificate scheme (`x509_san_dns`), the
signed request object's signature verifies against the certificate presented with it, and — where trust
anchors are available (US3) — the certificate chains to a trusted root. The citizen's consent surface can
then display a verified verifier identity rather than a bare URL.

**Why this priority**: Completes the loop: US1 fixes what we say, US3/US4 fix whom to trust, US6 lets the
*wallet* apply that trust to the *verifier*. It is last because it composes the others — meaningless
without real anchors — and because the current unsigned deep-link path our own components use must migrate
to the fetched signed-request form first.

**Independent Test**: Configure a verifier with a certificate whose DNS name matches its public host;
create a request; confirm the wallet verifies the request signature and displays the verifier identity.
Tamper with the signature; confirm the wallet refuses the request.

**Acceptance Scenarios**:

1. **Given** a verifier with a suitable certificate, **When** it creates a presentation request, **Then**
   the client identifier uses the DNS-based certificate scheme and the signed request object carries the
   certificate chain needed to validate it.
2. **Given** the Sorcha wallet PWA receiving such a request, **When** it processes the request, **Then**
   it fetches the signed request object, verifies the signature against the presented certificate, checks
   the DNS-name binding, and refuses tampered or mismatched requests with a citizen-comprehensible error.
3. **Given** trust anchors from an imported trusted list (US3), **When** the verifier's chain terminates
   at one, **Then** the consent surface shows a verified identity; when it does not, the surface clearly
   distinguishes "request is authentic but verifier is not on a trusted list".
4. **Given** Sorcha's internal flows (council gate, open verifier, workflow gate), **When** they run after
   this story, **Then** they use the same signed-request path — the unsigned inline-parameter deep-link
   form is retired.

---

### Edge Cases

- **Legacy dialect received after the clean break**: a wallet or verifier submits the old Presentation
  Exchange shape → rejected with a specific, versioned error message (not a parse crash), so external
  integrators can self-diagnose.
- **Previously-issued credentials with the old media type**: credentials already delivered to citizen
  wallets carry `vc+sd-jwt`. Sorcha's own verifiers accept both media types on the verify side
  (pre-release data is not migrated); only *newly issued* credentials carry the final form.
- **Trusted-list document that is validly signed but semantically odd**: empty service list, unknown
  service types, duplicate CA entries → import succeeds where safe, skips irrelevant entries, and reports
  exactly what was extracted vs skipped.
- **Snapshot freshness at the boundary**: evaluation exactly at the next-update instant; clock skew
  between the operator's import time and the list's own dates → freshness comparisons use the platform's
  clock with the list's stated dates and behave deterministically.
- **Imported certificate whose chain includes the anchor itself vs chain-without-root**: both common CA
  delivery formats must import correctly.
- **Org key rotation after certificate import**: the imported certificate no longer matches the issuing
  key → issuance on the external anchor fails closed with a "certificate/key mismatch" reason and the
  admin surface flags it.
- **Multi-credential consent where the citizen deselects an optional claim** on one credential but not the
  other → per-query claim approval is independent.
- **Alternative sets where the citizen holds both options** → the wallet presents a choice, not an
  arbitrary auto-pick.
- **Concurrent snapshot import**: two admins import list versions near-simultaneously → last-write-wins on
  version ordering rules (the list's own sequence number governs), never a corrupted merged snapshot.
- **Status-list decode strictness**: a third-party status list encoded with multibase (`u`-prefixed
  base64url) must decode; Sorcha's own lists remain valid.

## Requirements *(mandatory)*

### Functional Requirements

**Protocol dialect (US1, US2)**

- **FR-001**: Every presentation request the platform produces (workflow gate, council-page gate, open
  verifier, HAIP verifier API) MUST express its credential ask in DCQL (the final OpenID4VP profile's
  query language). No producer may emit a Presentation Exchange `presentation_definition`.
- **FR-002**: All existing OpenID4VP/OpenID4VCI **routes, URL shapes, and endpoint semantics MUST be
  preserved unchanged** — request creation, request-object fetch, direct-post response, result polling,
  presentation-lifecycle callbacks and status. Only request/response body dialects change.
- **FR-003**: Presentation responses MUST use the final profile's token envelope: one entry per credential
  query identifier. The legacy `presentation_submission` descriptor-mapping structure MUST be neither
  produced nor required. A response entry keyed to an unknown query identifier MUST fail verification with
  a specific reason.
- **FR-004**: Newly issued SD-JWT credentials MUST carry the final profile media type (`dc+sd-jwt`) in
  their type header, and all request/query format identifiers MUST use the final form. Sorcha's verifiers
  MUST continue to accept previously-issued credentials carrying the prior media type (`vc+sd-jwt`).
- **FR-005**: A single presentation request MUST be able to carry multiple credential queries, and MUST be
  able to express alternative sets ("satisfy with A, or with B"). Verification MUST report per-query
  outcomes and succeed overall only when every required query (or a complete alternative branch) is
  satisfied.
- **FR-006**: The Sorcha wallet PWA MUST parse DCQL requests, match multiple queries (including
  alternatives) against the citizen's held credentials, and present a consent surface that (a) lists each
  ask separately, (b) supports per-query claim approval, (c) lets the citizen choose among alternatives
  when more than one is satisfiable, and (d) clearly identifies unsatisfiable asks without submitting a
  partial response by default.
- **FR-007**: Requests or responses in the legacy dialect received after migration MUST be rejected with a
  specific, versioned error identifying the expected dialect — never an unhandled parse failure.
- **FR-008**: The request-building and request-parsing logic for the DCQL dialect MUST exist exactly once,
  shared by every producer and consumer inside the platform (single-obvious-path: the current
  independently hand-rolled builder/parser pairs are retired).
- **FR-009**: A CI gate MUST fail the build if the legacy Presentation Exchange dialect (producer or
  parser) reappears outside an explicitly allowlisted location.
- **FR-010**: Status-list decoding MUST accept multibase (`u`-prefixed base64url) encoded status lists in
  addition to the currently-accepted plain base64 form.

**Trusted-list snapshot rail (US3)**

- **FR-011**: An administrator MUST be able to import a trusted-list document conforming to the European
  trusted-list format (ETSI TS 119 612 — the LOTL or a member-state trusted list) by upload or by URL
  fetch-once. Import MUST verify the document's enveloped signature and reject unsigned, tampered, or
  malformed documents with specific reasons.
- **FR-012**: Import MUST extract certificate-authority anchors from service entries relevant to
  credential issuance (qualified/recognised certificate-issuance service types), skipping irrelevant
  service types, and MUST report a summary of what was extracted vs skipped.
- **FR-013**: Each import MUST be stored as a **versioned snapshot** carrying the list's own sequence
  number, issue date, next-update date, territory/scheme identity, the anchor set, and who imported it
  when. The newest valid snapshot per list identity is authoritative; superseded snapshots remain
  auditable.
- **FR-014**: The trust-evaluation source for trusted lists MUST resolve anchors from the authoritative
  snapshot(s). With no snapshot present, evaluation against a trusted-list trust policy MUST fail closed
  with a specific "no trusted list available" reason.
- **FR-015**: Verification evidence for a trusted-list-vouched decision MUST record the snapshot identity
  (list + sequence number) and its freshness state, so a past decision can be audited against the list
  version that vouched for it.
- **FR-016**: Snapshot staleness (past next-update) MUST be evaluated on every use: default behaviour is
  evaluate-with-warning (evidence flag + operator-visible metric/log); a configuration switch MUST allow
  strict fail-closed behaviour per installation.
- **FR-017**: Administrators MUST be able to list snapshots (with freshness state), inspect an anchor set,
  and delete a snapshot. All snapshot mutations MUST be restricted to platform administrators and audited.

**External issuance identity (US4)**

- **FR-018**: An org administrator MUST be able to generate a certificate signing request bound to the
  org's existing P-256 issuing key. The private key MUST never be exported or exposed by this flow.
- **FR-019**: An org administrator MUST be able to upload an externally-issued certificate + chain. Import
  MUST validate: public-key match against the org's issuing key; chain internal consistency and validity
  window; suitability for credential signing. Each failure mode MUST produce a distinct, actionable
  reason. Both chain-with-root and chain-without-root delivery formats MUST import.
- **FR-020**: When an org issues a credential configured for the external trust anchor, the embedded
  certificate chain MUST be the imported external chain. If no valid imported certificate exists (absent,
  expired, or key-mismatched after rotation), issuance MUST fail closed with a specific reason — never
  fall back silently to the internal tenant root.
- **FR-021**: Issuance configured for the internal anchor MUST be entirely unaffected by this feature.

**Certificate lifecycle & eligibility (US5)**

- **FR-022**: Every newly created organisation whose issuing key is P-256 MUST automatically receive its
  internal tenant-root certificate. Enrolment failure MUST NOT fail organisation creation; it MUST be
  operator-visible and re-triggerable.
- **FR-023**: An administrator surface MUST list an org's certificates (internal and imported) with
  status, validity, and chain summary; support one-action enrolment backfill for pre-existing orgs;
  support internal re-issue with auditable history; and host the CSR/import flows of US4.
- **FR-024**: Certificate operations against an organisation whose issuing key is not P-256 MUST return a
  typed, documented "key type not eligible for the X.509 rail" outcome. The current unhandled ASN.1
  server error MUST be eliminated.

**Verifier authentication (US6)**

- **FR-025**: Platform verifiers MUST identify themselves in presentation requests using the DNS-based
  certificate client-identifier scheme (`x509_san_dns`), with the signed request object carrying the
  certificate material a wallet needs to validate it. The verifier's DNS name MUST match its public host.
- **FR-026**: The Sorcha wallet PWA MUST migrate from the unsigned inline-parameter deep-link form to
  fetching and validating the signed request object: signature verification against the presented
  certificate, DNS-name binding check, and refusal of tampered or mismatched requests with a
  citizen-comprehensible error. The unsigned form MUST be retired from all internal producers.
- **FR-027**: Where trusted-list anchors are available, the wallet's consent surface MUST distinguish
  three verifier states: verified against a trusted list; authentic (signature valid) but not on a trusted
  list; and unverifiable. It MUST never present an unverifiable request as verified.

**Observability & documentation (cross-cutting)**

- **FR-028**: The platform MUST expose operational metrics for: trusted-list snapshot freshness and
  staleness events; trust decisions by source (existing metric gains the trusted-list source in practice);
  external-anchor issuance successes/failures by reason; legacy-dialect rejections; and verifier
  request-authentication outcomes.
- **FR-029**: STANDARDS.md rows for OpenID4VP, OpenID4VCI, HAIP, and SD-JWT VC MUST be updated to the
  final-profile versions with honest partial/full status; the discoverability surfaces that cite them
  follow.

### Key Entities

- **Credential Query (DCQL)**: the unit of ask inside a presentation request — an identifier, a credential
  format, format-specific matching metadata (e.g. credential type values), and claim paths with
  required/optional character. Grouped into a query set with optional alternative-set combinators.
- **Trusted-List Snapshot**: a versioned, immutable record of one imported trusted list — list identity
  (scheme/territory), sequence number, issue and next-update dates, extracted anchor set, import
  provenance (who/when/how), and computed freshness state. Supersedes earlier snapshots of the same list
  identity.
- **Trust Anchor (external)**: one certificate-authority certificate extracted from a snapshot, with its
  originating service entry's type and status. Referenced by trust evaluation and by verifier
  authentication.
- **Org Issuing Certificate**: a certificate bound to an organisation's issuing key. Two provenances:
  *internal* (tenant-root, auto-enrolled) and *imported* (externally issued, uploaded with chain). Carries
  status (active/superseded/expired/mismatched), validity window, chain summary, and audit history.
- **Certificate Signing Request**: a one-shot artefact generated from the org's issuing key for out-of-band
  external certification; records when/by whom it was generated.
- **Presentation Response Entry**: one presented credential keyed by the credential-query identifier it
  answers; the response envelope holds one entry per satisfied query.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A presentation request generated by each of the three platform request producers validates
  against the final OpenID4VP profile's request schema (independent schema check), with zero legacy
  query-language fields.
- **SC-002**: 100% of the existing presentation walkthroughs (council credential gate, open verifier
  demo, HAIP workflow gate, AIAS rehearse) pass unchanged in their user-visible behaviour after the
  migration.
- **SC-003**: A wallet holding only the second option of a two-option alternative ask completes the
  presentation successfully; a wallet lacking a required query is told exactly which ask is unsatisfiable.
- **SC-004**: A credential issued under a test CA whose root is present only via an imported trusted-list
  snapshot verifies successfully, and the verification evidence names the snapshot; with the snapshot
  deleted, the same credential fails verification closed.
- **SC-005**: A credential issued by an org with an imported external certificate verifies successfully
  using only the external root (Sorcha's tenant root absent from the trust set).
- **SC-006**: 100% of newly created P-256 organisations hold a tenant-root certificate with zero manual
  steps; certificate operations on an Ed25519 org produce the typed ineligibility outcome in 100% of
  cases and zero unhandled server errors.
- **SC-007**: The Sorcha wallet PWA refuses 100% of presentation requests with tampered signatures or
  mismatched DNS-name bindings, with a citizen-comprehensible message; authentic requests display the
  correct verifier state (trusted / authentic-untrusted).
- **SC-008**: The CI conformance gate blocks any PR reintroducing the legacy dialect; the gate itself is
  demonstrated by a deliberate red-test during development.
- **SC-009**: An operator can import a trusted list and see its anchors in use (first vouched
  verification) in under 10 minutes without reading source code — using only the admin surface and its
  inline guidance.

## Assumptions

- **Pre-release clean break is acceptable** (established repo policy): no live external integrators depend
  on the Presentation Exchange dialect today; Sorcha's own wallets/verifiers migrate in lockstep within
  this feature. The only backwards-compatibility kept is verify-side acceptance of already-issued
  `vc+sd-jwt` credentials (FR-004), because those live in citizen wallets we don't control.
- **Trusted-list refresh is manual in v1** (D3): operators re-import when lists roll. The freshness
  metadata + staleness warnings (FR-016) make the manual cadence safe; scheduled refresh is a named
  follow-up.
- **External certification happens out-of-band** (D4): obtaining a certificate from a member-state
  registrar or commercial CA is an organisational process outside the platform; the platform's job is CSR
  out, cert in, chain on credentials.
- **EUDI's cryptographic profile is P-256**, so restricting the X.509 rail to P-256 issuing keys (D6)
  costs no EUDI capability. Ed25519 remains fully supported on the register/DID-native rail.
- **Out of scope, deliberately** (later tiers): wallet attestation / pushed authorization / DPoP
  (issuance-flow hardening); the EUDI PID credential profile (doctype, namespaces, mandatory claims);
  mdoc proximity transport and MAC device authentication; live LOTL pointer-following; signed request
  objects for the *issuance* (OpenID4VCI) leg; claim-value matching constraints in DCQL beyond
  required/optional claim selection; relying-party access/registration certificates.
- **Dependencies**: builds on the unified trust evaluator and format-handler seam (Feature 135), the
  presentation lifecycle (F111/F119/F127), the citizen wallet PWA presentation engine (F114/F159), and the
  existing per-tenant internal certificate authority. The mdoc verify path already consumes the
  DCQL-shaped response envelope; this feature aligns SD-JWT with it.
- **Interop validation target**: conformance is asserted against the published final profiles and schema
  checks plus our own cross-format tests; live interop against an external EUDI reference wallet is a
  stretch validation activity, not a gate for this feature (it requires the later-tier issuance
  hardening to be meaningful end-to-end).
