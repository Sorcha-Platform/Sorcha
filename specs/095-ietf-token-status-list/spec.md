# Feature Specification: IETF Token Status List (Parallel to W3C)

**Feature Branch**: `095-ietf-token-status-list`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "IETF Token Status List alongside existing W3C Bitstring Status List, shared backing bitstring, enables HAIP credential status"

## Context

HAIP 1.0 mandates the IETF Token Status List (TSL) envelope for SD-JWT VC credential status. Sorcha currently publishes credential status via the W3C Bitstring Status List envelope only (`specs/039-verifiable-presentations` FR-007 through FR-012, implemented in `Sorcha.Blueprint.Service`). Both envelopes wrap a gzipped or zlib-compressed bitstring and carry identical bit semantics — the difference is purely in the outer signed envelope and how the pointer from the credential references it.

Rather than swap one format for the other, this spec follows the Phase 2 D3 ruling to **run both formats in parallel, backed by a single bitstring and a single on-register control record**. The existing W3C endpoint is preserved for legacy and Sorcha-internal consumers. A new IETF TSL endpoint is added at a separate URL for HAIP-external consumers. The backing `StatusListManager` is extended so a single bit flip updates both envelopes.

The existing verifier is also extended to consume either envelope, so a credential issued through the HAIP path (carrying an IETF `status.status_list` claim) can be verified by the Sorcha-internal presentation verifier, and a credential issued through the internal path (carrying a W3C `credentialStatus` claim) can be verified by a HAIP external verifier that happens to resolve a Sorcha list URL.

**Related specs.**
- **Extends** `specs/039-verifiable-presentations` FR-007 through FR-012 (W3C Bitstring Status List work). Those requirements remain in force; this spec adds an alternate envelope alongside.
- **Builds on** spec 093 (`vc-security-fixes`), which makes sure status pointers are embedded in the signed credential payload.
- **Builds on** spec 094 (`sdjwt-haip-hardening`), which makes the SD-JWT payload extensible enough to carry either status claim form.
- **Required by** spec 097 (OpenID4VCI issuer) — HAIP external wallets cannot consume a credential without an IETF-shape status claim pointing at an IETF-shape list.
- **Not required by** spec 096 (X.509 org trust) or spec 098 (OpenID4VP verifier); 095 and 096 can run in parallel branches.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A HAIP external verifier checks a Sorcha-issued credential's status without any Sorcha-specific knowledge (Priority: P1)

A GOV.UK Wallet or EUDI Wallet holds a Sorcha-issued professional licence credential. The holder presents it to a third-party verifier — a booking platform, a public-sector service, a registered Digital Verification Service. The verifier takes only the signed credential token, reads the `status.status_list` claim, fetches the referenced URL, parses the returned signed JWT per the IETF Token Status List draft, indexes into the bitstring at the named position, and learns whether the credential is active, revoked, or suspended. No Sorcha-specific libraries, no W3C VC envelope handling, no out-of-band knowledge.

After this spec ships, this flow works against any credential issued via the Sorcha HAIP path (spec 097). The IETF endpoint serves a signed JWT whose payload contains `status_list: { bits, lst }`, signed by the list issuer, with correct cache headers and a stable URL structure.

**Why this priority**: This is the concrete HAIP conformance payoff for credential status. Without it, a Sorcha-issued credential reaching a HAIP wallet cannot be status-checked by a generic HAIP verifier, and the "Sorcha is the workflow layer above GOV.UK Wallet" positioning stops being demonstrable at the status check. It is a hard prerequisite for spec 097.

**Independent Test**: Issue a credential via a test HAIP issuance path, publish it, then verify status against a third-party generic IETF Token Status List client (open-source test harness) with no Sorcha-specific knowledge. Flip the revocation bit on the backing list and confirm the generic client sees the change after the cache TTL.

**Acceptance Scenarios**:

1. **Given** a credential whose signed payload carries a `status.status_list` claim with `uri` and `idx`, **When** a generic HAIP verifier fetches the URL, **Then** the response is a signed JWT with `typ` header `statuslist+jwt` whose payload contains `status_list: { bits, lst }`.
2. **Given** a fetched IETF Token Status List JWT, **When** the verifier validates its signature against the list issuer's public key, **Then** the signature verifies using the issuer's classical verification method from their DID document or X.509 chain.
3. **Given** a valid IETF TSL JWT and a credential index of N, **When** the verifier indexes into the decompressed bitstring at position N with the declared `bits` width, **Then** the returned status value matches the credential's current lifecycle state (0 = active, 1 = revoked/suspended for 1-bit lists; extended values for 2-bit lists).
4. **Given** a credential whose backing bit has been flipped to revoked, **When** a HAIP verifier checks it after the cache TTL elapses, **Then** it sees the revoked state without the issuer needing to reissue the credential.
5. **Given** a HAIP verifier with no Sorcha-specific code, **When** it is pointed at a Sorcha-issued IETF TSL URL, **Then** it successfully parses, verifies and indexes the list using only the IETF Token Status List draft specification.

---

### User Story 2 - A Sorcha-internal verifier reads credential status from either envelope (Priority: P1)

A Sorcha Blueprint action verifies a presented credential. The credential may have been issued via the Sorcha-internal path (carrying a W3C `credentialStatus` claim per spec 093) or via the HAIP path (carrying an IETF `status.status_list` claim per this spec). The verifier must succeed in both cases without any caller-side branching.

After this spec ships, the Sorcha presentation verifier reads both claim forms. If the credential carries a W3C `credentialStatus` claim, the verifier consumes the W3C endpoint as it does today. If the credential carries an IETF `status.status_list` claim, the verifier consumes the new IETF endpoint. If a credential carries both, the verifier picks one deterministically (preference: IETF for forward compatibility) and proceeds. Neither path is slower than the other by more than a trivial margin.

**Why this priority**: Without this, internal Sorcha workflows cannot consume HAIP-path credentials, which means the split between "internal issuance" and "HAIP issuance" in spec 094 becomes a hard fork of the verifier. That is not acceptable — the whole point of parallel envelopes is that consumption is seamless across both.

**Independent Test**: Issue two credentials for the same wallet, one via the internal path (W3C status), one via the HAIP path (IETF status). Present each to the same internal Sorcha verifier and confirm both are correctly checked against the correct status list endpoint, and both correctly report Active, Suspended, and Revoked states after lifecycle operations.

**Acceptance Scenarios**:

1. **Given** a credential with a W3C `credentialStatus` claim only, **When** the Sorcha-internal verifier checks status, **Then** it fetches the W3C endpoint and returns the correct lifecycle state — behaviour unchanged from spec 093.
2. **Given** a credential with an IETF `status.status_list` claim only, **When** the Sorcha-internal verifier checks status, **Then** it fetches the IETF endpoint, verifies the signed JWT, indexes the decompressed bitstring at the named position, and returns the correct lifecycle state.
3. **Given** a credential with both claim forms pointing at lists on the same register, **When** the Sorcha-internal verifier checks status, **Then** it picks the IETF form deterministically and returns the same state that the W3C form would have returned (because both envelopes back onto the same bitstring).
4. **Given** the IETF endpoint is reachable but the W3C endpoint is not, **When** a credential with both claim forms is checked, **Then** the verifier successfully uses the IETF form without attempting the W3C one.
5. **Given** a credential with neither claim form and a pre-spec-093 server-side row (legacy), **When** the internal verifier checks status, **Then** it falls back to the server-side row per spec 093 FR-010, unchanged.

---

### User Story 3 - An issuer updates a bit once and both endpoints reflect the change (Priority: P1)

An authority revokes a previously issued licence credential. Internally this is a single lifecycle operation: flip the revocation bit at the credential's allocated index. Today that operation updates the W3C Bitstring Status List. After this spec ships, the same operation updates the same backing bitstring, and both the W3C and IETF envelopes re-derive their cached wire forms from the single updated source.

**Why this priority**: The implementation cost of this is dominated by the shared-backing-bitstring design. Without this constraint, the two envelopes would drift: an operator could revoke via the W3C path and leave the IETF view stale, or vice versa. A single source of truth for the bits is a correctness requirement, not a nice-to-have.

**Independent Test**: Flip a revocation bit via a single lifecycle operation. Confirm that both the W3C endpoint and the IETF endpoint return the revoked state after their caches expire. Flip the bit back (reinstate) and confirm both endpoints return active again. Query both endpoints at the same instant and confirm the returned bit values are identical (modulo cache windows).

**Acceptance Scenarios**:

1. **Given** a credential with an allocated index N, **When** the issuer revokes it, **Then** the bit at position N is flipped to 1 in the single backing bitstring and both the W3C and IETF envelope endpoints serve the updated state after their cache TTLs elapse.
2. **Given** a credential is reinstated from Suspended back to Active, **When** the lifecycle operation commits, **Then** the backing bit is cleared and both envelopes reflect the cleared state.
3. **Given** a single on-register control transaction recording a status list update, **When** either envelope endpoint is fetched, **Then** the returned envelope references the same control transaction as its source of truth.
4. **Given** the IETF endpoint receives a request for a list that does not yet exist, **When** the first credential on that list is allocated, **Then** a single on-register control transaction is written and both endpoints can serve the list from that moment onward.
5. **Given** the W3C and IETF envelopes are both freshly regenerated from the same backing bitstring, **When** a verifier decodes the W3C `encodedList` and the IETF `lst` field, **Then** the two decompressed bitstrings are byte-for-byte identical.

---

### User Story 4 - HAIP issuance chooses the right status claim form automatically (Priority: P2)

A Blueprint author configures an action that issues a credential. They do not know or care whether the credential will ultimately be consumed by a Sorcha-internal verifier or by a HAIP external wallet. The issuance path decides which status claim form to embed in the credential payload: internal path gets W3C `credentialStatus`; HAIP path (used by spec 097) gets IETF `status.status_list`. The Blueprint author sees a single "record on status list" configuration and the plumbing happens behind it.

**Why this priority**: This is the ergonomics glue. Spec 097 will build the HAIP issuance path, and this spec provides the mechanism for 097 to embed the correct claim form without the Blueprint author thinking about it.

**Independent Test**: Run the same Blueprint action twice — once targeting an internal Sorcha participant, once via a test HAIP issuance path. Confirm the first credential's signed payload contains a W3C `credentialStatus` and the second contains an IETF `status.status_list`. Confirm both resolve to the same backing bitstring.

**Acceptance Scenarios**:

1. **Given** a Blueprint action configured to issue a credential with status tracking, **When** the action issues via the internal path, **Then** the signed payload contains a W3C `credentialStatus` claim pointing at the W3C endpoint.
2. **Given** the same Blueprint action, **When** the action issues via the HAIP path, **Then** the signed payload contains an IETF `status.status_list` claim pointing at the IETF endpoint.
3. **Given** both credentials have been issued from the same Blueprint action, **When** their allocated indices are inspected, **Then** the two credentials occupy distinct index positions on the same backing list (no collision, no reuse).
4. **Given** neither endpoint has yet published the list at the time of first allocation, **When** the first credential is issued, **Then** the list is lazily created in the backing store and both endpoints can serve it immediately.

---

### Edge Cases

- What happens when a HAIP verifier fetches the IETF endpoint but the list has never been published (no credentials have been allocated yet)? The endpoint returns a valid empty list (all bits cleared) rather than 404, so the verifier does not get a false "unknown" result.
- What happens when the backing bitstring grows beyond its declared capacity? A new list is allocated and subsequent credentials reference the new list. Existing credentials continue pointing at the old list. This matches spec 039 FR-012 behaviour and is unchanged.
- What happens when the W3C and IETF envelopes are cached with different TTLs and a verifier happens to hit a stale side? The verifier sees the stale value until its cache expires. This is the standard caching trade-off; default TTLs are short enough (5 minutes) that it is not a correctness concern for normal workflows. Time-critical applications can set shorter TTLs.
- What happens when a credential carries both claim forms and the two endpoints disagree (cache skew)? The verifier picks the IETF form deterministically and logs a warning for operator visibility. The underlying bits cannot disagree because they share a backing store; only the cached envelopes can transiently differ.
- What happens when a HAIP verifier does not trust the list issuer's DID or X.509 identity? The list JWT signature check fails. Trust resolution reuses the same machinery as credential issuer trust (see spec 096 once it lands).
- What happens if a Sorcha deployment has legacy credentials with no embedded status claim (pre-spec-093)? The spec 093 server-side-row fallback still applies. This spec does not change the pre-spec-093 behaviour.
- What happens if the IETF endpoint's signing key is rotated? The rotation replays the latest bitstring snapshot into a newly signed envelope. Verifiers that cached the previous envelope will see the new signature on their next fetch. Historical verification of the old envelope continues to work against the old key for as long as verifiers retain it, per standard JWT rotation semantics.
- What happens if the list JWT's `exp` is reached before a refresh? The endpoint always serves a fresh JWT with a rolling `exp` window; a stale cached envelope at a verifier causes that verifier to refetch. `exp` is not tied to the credential's lifetime.
- What happens when two lifecycle operations race on the same bit (one revoke, one reinstate)? The existing spec 039 concurrency rules apply — lifecycle operations are serialised through the Blueprint Service's `IStatusListManager`. This spec does not introduce a new race.

## Requirements *(mandatory)*

### Functional Requirements

**IETF Token Status List producer:**
- **FR-001**: The system MUST expose a public, anonymous, cacheable HTTP endpoint that serves an IETF Token Status List envelope for a given list identifier.
- **FR-002**: The endpoint MUST return a signed JWT whose `typ` header is `statuslist+jwt` and whose payload contains a `status_list` member with `bits` (bit width per entry) and `lst` (compressed bitstring).
- **FR-003**: The JWT MUST be signed by a key bound to the list's issuing authority. The signing key is the issuing wallet's classical verification method (see spec 094 FR-031 for HAIP issuer wallets, or the primary key for classical-primary wallets).
- **FR-004**: The JWT payload MUST contain at minimum `iss` (list issuer identifier), `iat` (issued at), `exp` (expiry for this envelope snapshot, not for the credentials), and the `status_list` member.
- **FR-005**: The compressed bitstring MUST be byte-for-byte identical (after decompression) to the W3C endpoint's `encodedList` field for the same underlying list.
- **FR-006**: The endpoint MUST return an empty, all-zero bitstring (not 404) when the list has been provisioned but no credentials have been allocated yet.
- **FR-007**: The endpoint MUST support the same minimum list size as the W3C endpoint (131,072 entries per spec 039 FR-012).
- **FR-008**: The endpoint MUST support both 1-bit (revocation-only) and 2-bit (revocation + suspension) list widths.
- **FR-009**: The endpoint MUST return cache-control headers with a configurable TTL (default 5 minutes, same as the W3C endpoint per spec 039 FR-011).

**IETF Token Status List consumer (in the presentation verifier):**
- **FR-010**: The Sorcha presentation verifier MUST accept credentials whose signed payload carries an IETF `status.status_list` claim with `uri` and `idx` members.
- **FR-011**: On encountering an IETF `status.status_list` claim, the verifier MUST fetch the URL, verify the returned JWT's signature against the list issuer's verification key, and index into the decompressed bitstring at the named position.
- **FR-012**: The verifier MUST treat a failed IETF list JWT signature check as a hard verification failure with a specific error identifying list signature failure as the cause.
- **FR-013**: The verifier MUST support both 1-bit and 2-bit list widths and correctly extract the bit(s) at the declared index.
- **FR-014**: When a credential carries both a W3C `credentialStatus` claim and an IETF `status.status_list` claim, the verifier MUST prefer the IETF claim for status resolution and log a warning if the two envelopes return disagreeing bit values.
- **FR-015**: When a credential carries neither claim form (legacy, pre-spec-093), the verifier MUST fall back to the spec 093 FR-010 server-side row path.

**Shared backing and W3C preservation:**
- **FR-016**: The backing `StatusListManager` MUST treat the bitstring as a single source of truth. A single lifecycle operation (revoke, suspend, reinstate) MUST flip exactly one bit in one backing bitstring and re-derive both envelope forms from that bitstring.
- **FR-017**: Both the W3C and IETF endpoints MUST resolve to the same on-register control transaction as their backing source for any given list identifier.
- **FR-018**: The existing W3C Bitstring Status List endpoint (`specs/039-verifiable-presentations` FR-007 through FR-012) MUST remain fully functional for Sorcha-internal consumers. This spec adds the IETF envelope alongside; it does not replace or deprecate the W3C envelope.
- **FR-019**: The existing W3C `credentialStatus` claim embedding in internal-path credentials (spec 093 FR-007) MUST remain fully functional. Internal-path credentials continue to carry the W3C claim form.

**HAIP issuance path:**
- **FR-020**: The credential issuance layer MUST select the appropriate status claim form based on the issuance path. Internal-path issuance embeds W3C `credentialStatus`. HAIP-path issuance embeds IETF `status.status_list`.
- **FR-021**: Status list index allocation MUST happen before the credential token is signed, consistent with spec 093 FR-006, for both issuance paths.
- **FR-022**: A single credential MUST carry exactly one status claim form. Dual embedding (both W3C and IETF claims in the same credential) is explicitly not required and not recommended — the two envelopes share a backing bitstring so a single claim is sufficient for cross-verification via the verifier's dual-reader capability.
- **FR-023**: The Blueprint action credential issuance configuration MUST NOT expose the choice of status claim form to the Blueprint author. The choice is driven by the issuance path.

**Cross-cutting:**
- **FR-024**: All new behaviour MUST be covered by automated tests at unit and integration level, including a round-trip that flips a bit via the lifecycle path and confirms both envelopes reflect the change.
- **FR-025**: The spec MUST not regress any acceptance scenario from specs 039, 093, or 094.
- **FR-026**: Performance: fetching the IETF envelope MUST complete in under 200 ms at P95 under cached conditions, matching the existing W3C endpoint's performance profile.

### Key Entities *(include if feature involves data)*

- **IETF Token Status List JWT** (new): A signed JWT with `typ: "statuslist+jwt"`. Payload carries `iss`, `iat`, `exp`, `status_list: { bits, lst }`. The `lst` field is a compressed bitstring whose decompressed form is byte-identical to the W3C endpoint's `encodedList` for the same list. Signed by the list issuing authority's classical verification key.
- **`status.status_list` credential claim** (new, inside the signed SD-JWT VC payload): A non-disclosable top-level claim carrying `uri` (the IETF endpoint URL) and `idx` (the allocated position). Embedded only in credentials issued via the HAIP path.
- **Backing bitstring** (extended, not new): The single source of truth for credential status. Already exists in the backing `StatusListManager`; this spec adds a second envelope derivation on top of the same bytes.
- **On-register status list control transaction** (unchanged): The canonical anchor for each list version. Both envelopes reference the same control transaction for a given list.
- **W3C `BitstringStatusListEntry` claim** (existing, from spec 093): Continues to be embedded in internal-path credentials. Unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A generic HAIP verifier with no Sorcha-specific code successfully checks credential status for a Sorcha-issued HAIP-path credential by following the `status.status_list` claim and parsing the returned IETF JWT, confirmed by an end-to-end test against an open-source HAIP verifier implementation.
- **SC-002**: A Sorcha-internal presentation verifier correctly resolves credential status for credentials carrying either claim form (W3C or IETF) in 100 % of test cases, confirmed by a parametrised regression suite.
- **SC-003**: A single lifecycle operation (revoke, suspend, reinstate) flips exactly one bit in one backing bitstring and both envelope endpoints reflect the change after their cache TTLs elapse, confirmed by a dual-endpoint observation test.
- **SC-004**: The decompressed bitstring returned by the W3C endpoint and the decompressed bitstring returned by the IETF endpoint for the same list are byte-for-byte identical at all times (modulo in-flight cache skew), confirmed by a cross-envelope byte comparison test.
- **SC-005**: Internal-path issuance embeds a W3C `credentialStatus` claim in 100 % of newly issued credentials, unchanged from spec 093.
- **SC-006**: HAIP-path issuance (via the test harness, pending spec 097) embeds an IETF `status.status_list` claim in 100 % of newly issued credentials.
- **SC-007**: IETF endpoint P95 fetch time under cached conditions is under 200 ms, matching the existing W3C endpoint's performance budget.
- **SC-008**: Running specs/039, 093 and 094 regression tests continues to pass after this spec ships.
- **SC-009**: A HAIP verifier that encounters a revoked credential after the cache TTL elapses correctly identifies it as revoked within 5 minutes of the revocation operation, matching the standard TTL contract.

## Out of Scope

The following are explicitly deferred to later specs or are unchanged from existing specs:

- Changes to bit semantics. Active = 0, revoked / suspended = 1 (or 2-bit extended values). Unchanged from spec 039.
- Changes to list capacity, allocation strategy, or capacity-full handling. Spec 039 FR-012 applies.
- Changes to the lifecycle operations themselves (revoke, suspend, reinstate, refresh). Those continue to live in spec 039 and are not touched here.
- Governance of who can write to a status list. Unchanged from spec 039.
- X.509 trust chain handling for the list issuer's signing key. That is spec 096. Until 096 ships, the IETF JWT is signed by the issuer's DID-bound classical key using the same verification chain as credential issuance.
- OpenID4VCI wire protocol, issuer metadata, credential endpoint. Spec 097.
- OpenID4VP wire protocol. Spec 098.
- mdoc status list handling. Deferred follow-up. mdoc status is distinct and is not covered by IETF Token Status List.
- A mechanism for mass-migrating existing internal-path credentials from the W3C claim form to the IETF claim form. Not needed — both forms are valid in perpetuity per the parallel-envelopes design.

## Assumptions

- The existing `StatusListManager` in `Sorcha.Blueprint.Service` stores the backing bitstring in a representation from which both a W3C `encodedList` (gzip) and an IETF `lst` (zlib) can be derived. If the internal representation is gzip-only, the IETF derivation path will need to decompress and re-compress, which is acceptable.
- The IETF Token Status List draft's `typ` header value is `statuslist+jwt` as of the current stable draft. If the IETF draft advances to `statuslist+cwt` or a renamed value before this spec ships, the planning phase will track it.
- The IETF draft's payload shape (`status_list: { bits, lst }`) is stable. It is referenced directly by HAIP 1.0 and has been unchanged for several drafts.
- HAIP 1.0 continues to reference IETF Token Status List as the MTI status mechanism for SD-JWT VC. This is unchanged since HAIP 1.0 was finalised in December 2025.
- The compression algorithm difference (gzip for W3C, zlib for IETF) does not affect bit semantics. Both decompress to the same byte sequence for the same logical bitstring.
- Spec 094's classical-co-key work is far enough along that the list signing key for HAIP issuer wallets is available when spec 095 ships. The two can technically ship independently because 095 can sign list JWTs with any classical key the issuer holds, but practical deployment sequences 094 before 095.
- The existing W3C endpoint authorization model (public, anonymous, cacheable) is correct. The new IETF endpoint uses the same model.

## Dependencies

- **Depends on spec 093** (verifier fix and credentialStatus embedding — the embedded-claim contract is the anchor the IETF form also plugs into).
- **Depends on spec 094** (SD-JWT VC HAIP hardening — the credential payload must be extensible to carry either status claim form, and the classical co-key is what signs the IETF list envelope for HAIP issuer wallets).
- **Required by spec 097** (OpenID4VCI issuer endpoint — cannot emit HAIP-compliant credentials without this).
- **Independent of spec 096** (X.509 org trust) — 095 and 096 can run in parallel branches once 093 and 094 are merged. 096 will later add an X.509-based alternative to DID-based list issuer trust.
- **Independent of spec 098** (OpenID4VP verifier) — 098 will consume both claim forms via the extensions this spec provides, but 095 and 098 can be authored in parallel.

## Amendment note on spec 039

This spec extends `specs/039-verifiable-presentations` FR-007 through FR-012 rather than superseding them:

- 039 FR-007 (W3C Bitstring Status List v1.0 with revocation and suspension bitstrings) remains in force.
- 039 FR-008 (unique index allocation per credential) remains in force and is reused by this spec.
- 039 FR-009 (embed `credentialStatus` claim in every issued VC) remains in force for internal-path issuance and is implemented by spec 093 for that path; this spec adds an alternate claim form for HAIP-path issuance under FR-020.
- 039 FR-010 (canonical status list as on-register control transaction) remains in force. The same control transaction now backs two envelope derivations.
- 039 FR-011 (public cached HTTP endpoint, default 5-minute TTL) remains in force for the W3C endpoint; FR-009 of this spec mirrors it for the IETF endpoint.
- 039 FR-012 (minimum list size 131,072) remains in force for both envelopes.

All other 039 requirements remain in force and this spec must not regress any of them (see SC-008).
