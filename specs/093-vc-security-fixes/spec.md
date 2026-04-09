# Feature Specification: Credential & Presentation Security Fixes (HAIP Prep)

**Feature Branch**: `093-vc-security-fixes`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "HAIP prep: fix presentation verifier, embed credentialStatus, correct multibase"

## Context

A Phase 1 gap analysis comparing the Sorcha credential and presentation path to the HAIP 1.0 wallet interoperability profile surfaced three pre-existing bugs on master that are exploitable or incorrect today, regardless of any future HAIP work. They need to ship first because (a) the verifier bug is a real security issue behind the existing authenticated `/api/v1/presentations/{requestId}/submit` endpoint, and (b) every subsequent HAIP spec in the 093–098 series builds on a verified baseline.

This spec is **not** a HAIP spec. It makes no wire-format changes, introduces no new endpoints, and touches no new cryptographic primitives. It is a minimum-surface correction of behaviour that the existing specs (`specs/039-verifiable-presentations`) already mandated but the implementation silently diverged from.

**Related specs.** This spec amends `specs/039-verifiable-presentations` FR-009 and FR-015. It does not supersede 039. Specs 094–098 will build on the verified baseline that this spec produces.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Presentation verifier rejects tampered or forged tokens (Priority: P1)

A verifier operator submits a credential presentation to the Sorcha presentation endpoint on behalf of a holder. The presentation carries a signed verifiable presentation token. Today the verifier stores that token and then quietly ignores it — it reads claim values from the server-side credential store, runs type and expiry checks against the stored copy, and reports "Verified" without ever validating the signature on the submitted token. Any authenticated wallet-managing principal who can name a credential ID and a request ID can mark a presentation Verified without presenting a cryptographically valid token at all.

After the fix, the verifier cryptographically validates the submitted token's signature against the issuer's public key before any claim is considered, and all claim values used in the verification outcome are sourced from the verified token rather than from the server-side credential row.

**Why this priority**: This is a real security bug sitting behind an authenticated endpoint. It is the single most important fix in this spec because the endpoint is reachable by any principal with wallet-management scope. Shipping the HAIP verifier in spec 098 on top of this broken baseline would carry the bug through to external wallets, multiplying the blast radius.

**Independent Test**: Submit a presentation to the existing endpoint with a syntactically valid but signature-invalid token and confirm the request is marked Denied with a signature verification error. Submit the same endpoint with a correctly signed token disclosing a subset of claims and confirm the verification result contains only the disclosed claims and no others.

**Acceptance Scenarios**:

1. **Given** a pending presentation request, **When** a holder submits a token whose issuer signature does not verify against the issuer's public key, **Then** the request transitions to Denied with a clear signature verification error and no claim values appear in the verification result.
2. **Given** a pending presentation request with required claims specified, **When** a holder submits a correctly signed token disclosing a subset of claims, **Then** the request transitions to Verified and the verification result contains exactly the disclosed claims — no claim values are leaked from the server-side credential row.
3. **Given** a pending presentation request with a required claim value constraint, **When** a holder submits a correctly signed token whose disclosed claim value does not satisfy the constraint, **Then** the request transitions to Denied with a claim-value-mismatch error derived from the verified token, not the server copy.
4. **Given** a pending presentation request, **When** a holder submits a token signed by a key different from the credential's recorded issuer, **Then** the request transitions to Denied with an unauthorised-issuer error.

---

### User Story 2 - External verifiers can read credential status from the token alone (Priority: P1)

A third party with no access to Sorcha's internal credential store receives a Sorcha-issued credential and wants to check whether it has been revoked. Today the signed token contains only mapped claims plus `type` and `vct`; the status list URL and index are stored on the server-side credential row and never make it into the signed payload. A verifier reading the token in isolation has no way to reach the status list. This contradicts `specs/039-verifiable-presentations` FR-009, which requires every issued VC to embed a `credentialStatus` claim pointing to the status list URL and the credential's index.

After the fix, every newly issued credential contains a `credentialStatus` claim in the signed SD-JWT payload naming the status list URL, the allocated index, and the purpose. A verifier with only the token can fetch the status list and check the bit without any out-of-band Sorcha-specific knowledge.

**Why this priority**: FR-009 is a standing mandate that is silently violated in production code. Any external verifier attempting to check a Sorcha-issued credential today must rely on out-of-band information. This is also a hard prerequisite for HAIP specs 097 and 098, where the token flows to an external wallet that has no knowledge of the Sorcha server-side store.

**Independent Test**: Issue a new credential through the existing issuance endpoint and decode the signed payload. Confirm the decoded payload contains a `credentialStatus` object with `statusListCredential`, `statusListIndex`, and `statusPurpose`. Fetch the URL, index into the bitstring, and confirm the bit reflects the credential's lifecycle state.

**Acceptance Scenarios**:

1. **Given** an issuer wallet capable of issuing credentials, **When** a new credential is issued via the existing issuance endpoint, **Then** the signed SD-JWT payload contains a `credentialStatus` object carrying the status list URL, the allocated index, and the purpose identifier.
2. **Given** a newly issued credential, **When** its `credentialStatus` URL is fetched and the bit at its index is inspected, **Then** the bit reflects the credential's current lifecycle state (Active → clear, Revoked → set).
3. **Given** a credential issued before this fix shipped (no `credentialStatus` in the signed payload), **When** that credential is presented or inspected, **Then** the verifier falls back to the server-side credential row's status list fields and still correctly reports the credential's lifecycle state. No historical credentials are invalidated by this change.
4. **Given** a newly issued credential, **When** its status list index is allocated, **Then** the allocation happens before the token is signed so the pointer in the signed payload matches the bit in the list.

---

### User Story 3 - External DID consumers can parse Sorcha org and wallet DIDs (Priority: P2)

An external party — a HAIP verifier, a `did:web` client, an interoperability test harness — resolves a `did:sorcha:org:{address}` or `did:sorcha:w:{address}` DID document and attempts to validate its verification methods. Today each verification method exposes `publicKeyMultibase` as the literal character `z` concatenated with the wallet's public key string. That is not valid W3C multibase: the `z` prefix means the remainder must be base58btc of a multicodec-prefixed key. External consumers that validate multibase against the W3C DID Core specification reject the document and refuse to proceed.

After the fix, the resolver produces correct multibase output: the raw public key bytes are prefixed with the algorithm's multicodec identifier, the result is base58btc-encoded, and the whole string is prefixed with `z`. The supported algorithms are Ed25519, NIST P-256, and RSA-4096 — matching the existing Sorcha wallet model.

**Why this priority**: This is a correctness fix rather than a security fix, but it is a hard precondition for any external wallet or verifier interop. HAIP wallets that encounter a malformed DID document will reject the credential. It is also straightforward to fix and costs nothing to ship alongside the other two items.

**Independent Test**: Create a wallet with each supported algorithm, resolve its `did:sorcha:w:{address}`, and validate the resulting DID document against the W3C DID Core multibase specification. Each `publicKeyMultibase` must parse as base58btc, yield a multicodec-prefixed key on decode, and round-trip back to the original raw public key bytes.

**Acceptance Scenarios**:

1. **Given** a wallet whose algorithm is Ed25519, **When** its `did:sorcha:w:{address}` is resolved, **Then** the returned DID document contains a verification method whose `publicKeyMultibase` is `z` followed by the base58btc encoding of the multicodec-prefixed Ed25519 public key.
2. **Given** a wallet whose algorithm is NIST P-256, **When** its `did:sorcha:w:{address}` is resolved, **Then** the verification method's `publicKeyMultibase` uses the P-256 multicodec prefix and is valid base58btc.
3. **Given** a wallet whose algorithm is RSA-4096, **When** its `did:sorcha:w:{address}` is resolved, **Then** the verification method's `publicKeyMultibase` uses the RSA multicodec prefix and is valid base58btc.
4. **Given** a wallet whose algorithm is not in the supported set, **When** its DID is resolved, **Then** the resolver either emits a defined fallback encoding (`publicKeyJwk` or equivalent non-multibase form) or fails closed with a clear error — under no circumstances does it emit malformed multibase.
5. **Given** an external DID consumer that enforces W3C DID Core multibase validation, **When** it resolves any Sorcha org or wallet DID, **Then** the DID document parses without multibase errors.

---

### Edge Cases

- What happens when a holder submits a structurally invalid token (not parseable as SD-JWT)? The request is Denied with a parse error, not marked Verified against the server copy.
- What happens when a holder submits a token whose disclosures reveal a claim the holder did not select? The verification result still reflects only the claims explicitly disclosed in the token — any extra disclosures are carried through as they are part of the signed token.
- What happens when a holder submits a token for a credential whose `IssuerDid` in the server store does not match the token's `iss` claim? The verifier fails closed with an issuer mismatch error.
- What happens when the status list URL in the signed payload becomes unreachable at verification time? The fallback behaviour matches the existing `RevocationCheckPolicy` in the status list consumer (already in spec 039). This spec does not change that policy.
- What happens to a pre-fix credential whose server-side row is lost or corrupted? Without either the embedded pointer or the server row, status cannot be determined; the verifier reports "status unknown" using the existing fallback path. This is not new behaviour — it is the existing state of affairs for any credential without status tracking.
- What happens when a wallet supports multiple algorithms (for example a classical co-key added later for HAIP issuance)? Each verification method in the DID document carries its own correctly encoded multibase value. Nothing forces a single algorithm per DID document.
- What happens when the status list index allocation succeeds but token signing subsequently fails? The allocation is rolled back or marked orphaned so the bit does not drift out of sync with a credential that was never issued. The exact rollback mechanism is an implementation choice but the observable outcome is: no issued credential points to an index that does not correspond to it, and no allocated index has no corresponding credential.

## Requirements *(mandatory)*

### Functional Requirements

**Presentation verification (Bug 1):**
- **FR-001**: The presentation verifier MUST cryptographically verify the signature of the submitted verifiable presentation token against the issuer's public key before any other verification step is considered successful.
- **FR-002**: If token signature verification fails, the request MUST transition to Denied with a verification error that names signature failure as the cause, and no claim values from the server-side credential store may appear in the verification result.
- **FR-003**: Claim values carried in the verification result MUST be derived from the verified presented token, not from the server-side credential row.
- **FR-004**: If the `iss` claim in the verified token does not match the recorded issuer DID of the referenced credential, the request MUST transition to Denied with an unauthorised-issuer error.
- **FR-005**: If any disclosure in the presented token is malformed or fails its hash digest check, the request MUST transition to Denied with a disclosure integrity error.

**Credential status embedding (Bug 2):**
- **FR-006**: Credential issuance MUST allocate a status list index before signing the credential token.
- **FR-007**: The signed credential payload MUST contain a `credentialStatus` claim carrying at minimum the status list URL, the allocated index, and a status purpose identifier. The claim structure MUST match the W3C `BitstringStatusListEntry` shape used by the existing Sorcha status list producer; later specs may add alternate claim forms alongside.
- **FR-008**: If status list allocation fails, credential issuance MUST fail with a clear error and no token is signed or returned to the caller.
- **FR-009**: If token signing fails after status list allocation has succeeded, the allocated index MUST be released or marked orphaned so the bit cannot drift out of sync with a credential that was never issued.
- **FR-010**: Credentials issued before this fix shipped — identified by the absence of a `credentialStatus` claim in their signed payload — MUST continue to verify end to end against the existing W3C Bitstring Status List by falling back to the server-side credential row's `StatusListUrl` and `StatusListIndex` fields. This fallback is permanent for the lifetime of pre-fix credentials.
- **FR-011**: The verifier MUST prefer the embedded `credentialStatus` claim over the server-side row when both are present. The server-side row is only consulted as a fallback for pre-fix credentials.

**DID multibase encoding (Bug 3):**
- **FR-012**: `did:sorcha:w` and `did:sorcha:org` resolution MUST return DID documents whose verification methods use valid W3C multibase encoding of the public key.
- **FR-013**: Multibase output MUST use the correct multicodec identifier prefix for each supported algorithm: Ed25519, NIST P-256, and RSA-4096. The encoded form MUST be `z` followed by the base58btc encoding of the multicodec-prefixed raw public key bytes.
- **FR-014**: For algorithms not in the supported set, the resolver MUST either emit a defined non-multibase encoding (for example `publicKeyJwk`) or fail closed with a clear error. The resolver MUST NOT emit malformed multibase under any circumstances.
- **FR-015**: The fix MUST be applied symmetrically to the `did:sorcha:w` and `did:sorcha:org` resolution paths. The existing `did:web` and `did:key` resolver paths are out of scope except insofar as they share multibase helper code.

**Cross-cutting:**
- **FR-016**: All three fixes MUST be covered by automated tests at both unit and integration level. The tests MUST fail against the current master implementation and pass after the fix.
- **FR-017**: The fixes MUST not change the wire-format of any existing endpoint. Request and response shapes remain identical; only internal behaviour changes.
- **FR-018**: The fixes MUST not regress any acceptance scenario defined in `specs/039-verifiable-presentations/spec.md`.

### Key Entities *(include if feature involves data)*

- **PresentationRequest** (existing): The state record for an incoming verification request. This spec does not change its shape; it changes how `VerificationResult` is populated from `VpToken`.
- **CredentialEntity** (existing): The wallet-scoped credential row. This spec does not change its shape; it changes the issuance sequence so that status list allocation happens before signing, and the embedded token carries the resulting pointer.
- **DidDocument** (existing): The DID resolution output. This spec does not change its shape; it changes the encoding of `verificationMethod[].publicKeyMultibase`.
- **credentialStatus claim** (new, inside the signed SD-JWT payload): A non-disclosable claim carrying `statusListCredential` (URL), `statusListIndex` (integer), and `statusPurpose` (string). Shape matches the W3C `BitstringStatusListEntry` format already in use by the existing status list producer.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of presentation submissions with a signature-invalid token are correctly denied, measured by a test suite that exercises the submit endpoint with tampered, forged, and absent signatures.
- **SC-002**: 0 % of presentation submissions can be marked Verified without a successfully signature-verified token, measured by an audit query against the presentation request store after a controlled soak test.
- **SC-003**: 100 % of newly issued credentials contain a `credentialStatus` claim in the signed payload, measured by a corpus sweep of every credential issued after the fix ships.
- **SC-004**: An external verifier using only the signed credential token can determine the credential's lifecycle state (Active, Suspended, Revoked) by fetching the URL named in the `credentialStatus` claim and inspecting the bit at the named index. No access to the Sorcha server-side credential store is required.
- **SC-005**: 100 % of pre-fix credentials continue to verify end to end after the fix ships, confirmed by a regression suite that replays historical issuances.
- **SC-006**: Every `publicKeyMultibase` value returned by `did:sorcha:w` and `did:sorcha:org` resolution for the supported algorithms passes W3C DID Core multibase validation in an independent third-party parser.
- **SC-007**: No acceptance scenario in `specs/039-verifiable-presentations/spec.md` regresses after this fix ships, confirmed by running the 039 regression test suite.

## Out of Scope

The following are explicitly deferred to later specs in the 093–098 series:

- HAIP 1.0 conformance work of any kind: no `cnf` holder key binding, no Key Binding JWT, no nested or array-element selective disclosure, no classical co-key for HAIP issuance — these belong to spec 094 (SD-JWT VC HAIP hardening).
- IETF Token Status List production or consumption — belongs to spec 095.
- X.509 trust anchor integration — belongs to spec 096.
- OpenID4VCI issuer endpoints, metadata, token endpoint, credential offer URIs — belong to spec 097.
- HAIP-shaped OpenID4VP Authorization Requests, `presentation_definition`, `presentation_submission`, same-device and cross-device flows — belong to spec 098.
- mdoc, ISO 18013-5, ISO 18013-7, CBOR encoding, device key handling — deferred follow-up beyond the current spec set.
- Changes to the `did:web` or `did:key` resolvers, beyond whatever shared multibase helper code they consume.
- Changes to the disclosure semantics of SD-JWT VC. Top-level name-keyed disclosure is unchanged; nested disclosure is a spec 094 concern.

## Assumptions

- The existing `ISdJwtService.VerifyPresentationAsync` is a working signature verifier for the currently supported classical algorithms (Ed25519, NIST P-256, RSA-4096). Any gaps in the verifier's coverage of these algorithms are in scope of spec 094, not this spec. This spec assumes calling it is sufficient for bug 1's fix.
- The existing W3C Bitstring Status List manager in `Sorcha.Blueprint.Service` is functional and its on-register control record storage behaves as documented in `specs/039-verifiable-presentations` FR-010. This spec does not validate those claims — it only adds a call to the existing allocation API before token signing.
- A .NET-compatible base58btc encoder is either already available in the Sorcha dependency set or can be added or vendored as a small helper.
- The multicodec identifiers for Ed25519, NIST P-256, and RSA-4096 are stable and publicly published. The fix uses the standard values from the multicodec table.
- Historical credentials in production wallets that predate this fix have their `StatusListUrl` and `StatusListIndex` correctly populated on the server-side row. The fallback in FR-010 is only correct if this holds.
- Presentation request records are currently stored in an in-memory dictionary with no TTL cleanup. This spec does not fix that; it is noted for a future operational spec.

## Dependencies

- None on prior specs in the 093–098 series (this is the first).
- Hard prerequisite for specs 094, 097, and 098 — all of them assume a verifier that actually verifies and an issuer that embeds status pointers in tokens.

## Amendment note on spec 039

This spec treats `specs/039-verifiable-presentations` as authoritative for all requirements except those explicitly amended here:

- FR-009 of 039 ("System MUST embed a `credentialStatus` claim in every issued VC") is implemented for the first time by this spec.
- FR-015 of 039 ("System MUST verify presentations by checking: signature validity (via DID resolution), status list (not revoked/suspended/consumed), required claim constraints, and nonce freshness") has its *signature validity* and *required claim constraints* branches corrected to run against the submitted token rather than the server-side store.

All other 039 requirements remain in force and this spec must not regress any of them (see SC-007).
