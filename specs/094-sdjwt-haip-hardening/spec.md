# Feature Specification: SD-JWT VC HAIP Hardening

**Feature Branch**: `094-sdjwt-haip-hardening`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "SD-JWT VC HAIP hardening: cnf, KB-JWT, nested disclosure, holder binding key, classical issuer co-key"

## Context

Phase 1 gap analysis against HAIP 1.0 confirmed that the existing `Sorcha.Cryptography.SdJwt` implementation produces something that looks like an SD-JWT VC from 30 feet but is not HAIP-conformant in five concrete ways:

1. No `cnf` holder key binding at issuance — every Sorcha-issued credential is effectively a bearer token.
2. No Key Binding JWT at presentation — `CreatePresentationAsync` accepts holder key, audience and nonce parameters but the `[kb-jwt]` slot in the serialised presentation is always empty. `VerifyPresentationAsync` never checks a KB-JWT.
3. Selective disclosure is name-keyed top-level only. Nested object fields and array elements cannot be selectively disclosed. HAIP real-world credentials routinely need `address.locality` without `address.country`, or one qualification out of a list, and the current API cannot express that.
4. No holder signing key exists in the Sorcha wallet domain for Sorcha-internal holders. External HAIP wallets bring their own holder key, but Sorcha-internal holders (the common case today — Blueprint action issues a credential to another Sorcha participant) have nowhere to get a holder key from.
5. Issuing wallets that hold PQC keys (ML-DSA-65, SLH-DSA) cannot sign HAIP-facing SD-JWT VCs. HAIP 1.0 is classical-only on the signing boundary. The SD-JWT signer only branches for EdDSA / ES256 / RS256 and will throw on any PQC algorithm.

This spec closes all five gaps at the library and wallet-key-hierarchy level. It introduces no HTTP endpoints — specs 097 (OpenID4VCI issuer) and 098 (OpenID4VP verifier) will build the wire-level HAIP boundary on top of the hardened primitives produced here.

**Related specs.**
- **Supersedes** `specs/031-verifiable-credentials`. All non-superseded functional requirements from 031 are carried forward in the Requirements section below.
- **Builds on** spec 093 (`vc-security-fixes`), which fixes the presentation verifier so that the KB-JWT path introduced by this spec has a working substrate to plug into.
- **Required by** specs 097 (OpenID4VCI issuer endpoint) and 098 (OpenID4VP verifier endpoint).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A credential is bound to its holder's key and cannot be replayed by a thief (Priority: P1)

A licensing authority issues a professional licence credential to a practitioner. The credential is signed by the authority, stored in the practitioner's wallet, and later presented to a verifier for a specific transaction. Today, if an attacker intercepts the stored credential token, they can replay it to any verifier and claim to be the practitioner — the token is a bearer token.

After this spec ships, every credential carries a `cnf` claim identifying the holder's public key, and every presentation is accompanied by a Key Binding JWT signed by the corresponding private key and bound to the verifier's audience and nonce. A stolen credential cannot be replayed without also compromising the holder's binding key, and the KB-JWT's audience binding prevents a verifier from replaying a presentation to a different verifier.

**Why this priority**: This is the single most important conformance gap against HAIP 1.0. Without `cnf` and KB-JWT, Sorcha credentials are bearer tokens in everything but name, and the "Sorcha is a proof infrastructure" positioning is hollow. All of specs 097 and 098 presume this works.

**Independent Test**: Issue a credential to a wallet whose holder binding key is known, then present it to a verifier with a specific audience and nonce. Confirm the KB-JWT is present in the serialised presentation, is signed by the holder key, carries the verifier's audience and nonce, and verifies against the holder public key named in the credential's `cnf` claim. Replay the same presentation to a different audience and confirm verification fails.

**Acceptance Scenarios**:

1. **Given** an issuer wallet and a holder wallet with a derived holder binding key, **When** the issuer issues a credential to the holder, **Then** the signed SD-JWT payload contains a `cnf` claim whose `jwk` member is the holder's public key in JWK form.
2. **Given** a stored credential with a `cnf` claim, **When** the holder creates a presentation specifying an audience and nonce, **Then** the serialised presentation ends with a Key Binding JWT signed by the holder's binding private key, whose payload contains `aud` matching the audience, `nonce` matching the nonce, `iat` within the current clock skew window, and `sd_hash` matching the SHA-256 hash of the preceding portion of the presentation.
3. **Given** a presentation with a correctly signed KB-JWT, **When** the verifier validates it, **Then** signature verification succeeds using the public key named in the credential's `cnf` claim, the `aud`, `nonce` and `sd_hash` checks all pass, and the presentation is accepted.
4. **Given** a presentation with a correctly signed KB-JWT for audience A, **When** a hostile verifier B attempts to verify it against its own audience, **Then** verification fails with an audience mismatch error.
5. **Given** a presentation with a KB-JWT signed by a key other than the one named in the credential's `cnf` claim, **When** the verifier validates it, **Then** verification fails with a key binding mismatch error.
6. **Given** a presentation with no KB-JWT at all (empty trailing slot), **When** the verifier validates it and the credential has a `cnf` claim, **Then** verification fails with a missing KB-JWT error.
7. **Given** a legacy credential with no `cnf` claim (issued before this spec shipped), **When** the holder creates a presentation and the verifier validates it, **Then** the verifier accepts the presentation without a KB-JWT, preserving backwards compatibility for historical credentials.

---

### User Story 2 - A verifier can ask for one part of a nested field without forcing the holder to reveal the rest (Priority: P1)

A short-term-let operator holds a licence credential that contains their full address as a nested object: `address: { street, locality, region, postcode, country }`. A booking platform needs to confirm which council area the licence covers — it needs `address.locality` only. Today the credential's disclosure model is name-keyed at the top level, so either the whole `address` object is disclosable or none of it is; there is no way to disclose `locality` without also revealing `street`, `postcode`, `region` and `country`.

After this spec ships, disclosure is expressed by JSON Pointer path. The issuer can declare `/address/locality` as an independently disclosable field, and the holder can present that path alone. The verifier receives `address.locality` and nothing else. The same mechanism works for array elements — a credential listing three professional qualifications can disclose one without revealing the others.

**Why this priority**: Nested and array-element disclosure is table stakes for real-world credentials. Without it Sorcha-issued credentials cannot carry realistic attribute shapes, and the minimum-disclosure story Sorcha tells in competitive positioning does not survive contact with a real-world permit or qualification schema. Spec 097 depends on this working before it can offer HAIP wallets anything non-trivial.

**Independent Test**: Issue a credential with a nested `address` object where `/address/locality` and `/address/country` are independently disclosable but `/address/street` is not. Present the credential disclosing only `/address/locality`. Confirm the verifier sees `address.locality` and no other address field. Repeat with an array of qualifications and confirm single-element disclosure works.

**Acceptance Scenarios**:

1. **Given** a credential issuance request naming `/address/locality` and `/address/country` as disclosable via JSON Pointer, **When** the issuer creates the credential, **Then** the signed payload contains nested `_sd` digest arrays placing disclosure hashes at the correct depth and the parent object carries no plaintext values for the disclosable fields.
2. **Given** a credential with nested disclosable fields, **When** the holder presents it disclosing only `/address/locality`, **Then** the presentation carries exactly one disclosure for `/address/locality` and the verifier reconstructs only `address.locality` at the top level of the result.
3. **Given** a credential whose `qualifications` claim is an array of three objects where each element is independently disclosable via `/qualifications/0`, `/qualifications/1`, `/qualifications/2`, **When** the holder discloses only element index 1, **Then** the verifier receives a `qualifications` array containing only that element and the other two are cryptographically hidden.
4. **Given** a credential mixing top-level name-keyed disclosures with nested JSON-Pointer disclosures, **When** the holder presents it, **Then** both disclosure styles are honoured in the same presentation and the verifier correctly reconstructs the disclosed subset.
5. **Given** a request to disclose a JSON Pointer path that does not correspond to a disclosable field in the credential, **When** the holder attempts to present it, **Then** the presentation creation fails with a clear error identifying the unknown path.

---

### User Story 3 - A Sorcha participant can hold a credential without the Blueprint author having to think about key management (Priority: P1)

A Blueprint author configures an action that issues a professional licence credential to a specific participant. The participant is another Sorcha wallet on the same network. Today there is no holder key for that participant — the Sorcha wallet model is BIP32/39/44 hierarchical deterministic, and nothing in the hierarchy is reserved for "credential holder binding". The Blueprint author would have to either skip `cnf` (broken), or pick an arbitrary wallet key as the holder key (mixes concerns and breaks on rotation).

After this spec ships, every Sorcha wallet has an implicit holder binding key derived under a new BIP32 purpose `sorcha:credential-holder-binding`. It is one key per wallet, not per credential. The Wallet Service exposes the public form of this key so the issuer can embed it in `cnf` at issuance time, and the same Wallet Service signs the KB-JWT at presentation time without the Blueprint author having to know the key exists. The key recovers from the wallet mnemonic like every other Sorcha HD sub-key.

**Why this priority**: Without this, the Blueprint-internal credential issuance path (the dominant use case today) cannot be upgraded to HAIP-conformant without forcing every Blueprint author to become a cryptographer. This is the piece that makes User Stories 1 and 2 usable on the Sorcha side.

**Independent Test**: Create a wallet from a fresh mnemonic. Ask the Wallet Service for its `credential-holder-binding` public key. Recreate the wallet from the same mnemonic on a clean machine and confirm the derived public key matches. Use the key to bind and then prove possession of a credential end-to-end.

**Acceptance Scenarios**:

1. **Given** a new Sorcha wallet, **When** the Wallet Service is asked for the wallet's `credential-holder-binding` public key, **Then** a stable public key is returned and the corresponding private key is derived deterministically from the wallet seed under the new purpose.
2. **Given** a wallet with a `credential-holder-binding` key, **When** a credential is issued to it via the Blueprint issuance path, **Then** the resulting credential's `cnf` claim names that public key.
3. **Given** a credential bound to a holder's `credential-holder-binding` key, **When** the holder creates a presentation, **Then** the Wallet Service signs the KB-JWT using the same derived private key without the Blueprint layer having to pass any key material.
4. **Given** a wallet mnemonic, **When** the wallet is recovered on a different machine, **Then** the `credential-holder-binding` public key is identical on both machines and all existing credentials continue to verify.
5. **Given** a wallet whose primary signing key is rotated, **When** the rotation completes, **Then** the `credential-holder-binding` key is unchanged and outstanding credentials continue to verify. Rotation of the binding key itself is out of scope for this spec and deferred.

---

### User Story 4 - A PQC-preferring Sorcha organisation can still issue HAIP-compliant credentials (Priority: P2)

A Sorcha organisation wallet is configured with ML-DSA-65 as its primary signing algorithm — a reasonable choice given Sorcha's PQC-in-core posture and the thirty-year lifetimes of product passports and permits. That same organisation now wants to issue a licence credential that a GOV.UK Wallet or EUDI Wallet can accept. HAIP 1.0 requires classical signatures (ES256 minimum). Today the wallet cannot sign an SD-JWT VC at all — the signer throws on PQC algorithms.

After this spec ships, any wallet declared as a HAIP issuer additionally holds a classical co-key (ES256 by default) derived under a new BIP32 purpose `sorcha:haip-issuer-signing`. The organisation's primary PQC key continues to sign all Sorcha-internal transactions; HAIP-facing credentials are signed by the classical co-key. The wallet's DID document exposes both keys as distinct verification methods so internal Sorcha consumers can prefer the PQC key while external HAIP wallets pick the classical one. Key selection at issuance time is automatic based on the issuance path (internal vs HAIP).

**Why this priority**: This is the concrete form of the "PQC internally, classical at the boundary" compromise. Without it, any Sorcha organisation that chose PQC for its primary key is locked out of HAIP issuance entirely, which defeats the point of the HAIP spec set.

**Independent Test**: Create a wallet with primary algorithm ML-DSA-65 and declare it as a HAIP issuer. Confirm a classical co-key exists, its public key appears as a separate verification method in the org's DID document, and an SD-JWT VC signed via the HAIP issuance path uses that classical key. Confirm a Sorcha-internal transaction from the same wallet still uses ML-DSA-65.

**Acceptance Scenarios**:

1. **Given** a wallet with primary algorithm ML-DSA-65 and the `HaipIssuer` capability set, **When** the wallet is created or upgraded, **Then** a classical co-key is derived under `sorcha:haip-issuer-signing` with default algorithm ES256.
2. **Given** a HAIP-issuer wallet, **When** its DID document is resolved, **Then** the document lists at least two verification methods — one for the primary PQC key and one for the classical co-key — each with its own key identifier.
3. **Given** a HAIP-issuer wallet, **When** a credential is issued via the HAIP issuance path, **Then** the SD-JWT VC is signed with the classical co-key and its `iss` and signing key reference name the classical verification method.
4. **Given** the same HAIP-issuer wallet, **When** it signs a Sorcha-internal transaction (non-HAIP path), **Then** the signature is produced by the primary PQC key and the classical co-key is not touched.
5. **Given** a wallet with primary algorithm already in the classical set (Ed25519 or NIST P-256), **When** the `HaipIssuer` capability is set, **Then** no new co-key is derived; the primary key is used directly for HAIP issuance.
6. **Given** a wallet without the `HaipIssuer` capability, **When** a caller attempts to issue a HAIP-path credential through it, **Then** the request fails with a clear capability-missing error, not a cryptographic one.

---

### User Story 5 - A Blueprint author writes a disclosable address once and it works in both internal and HAIP presentations (Priority: P2)

A Blueprint author declares an action that issues a credential with a nested address where `/address/locality` is independently disclosable. They do not know or care whether the credential will ultimately be presented to a Sorcha-internal verifier (spec 098 internal path) or to an external HAIP wallet that forwards it to a third-party verifier. The same disclosure declaration must produce a credential that behaves correctly in both flows.

After this spec ships, the existing Blueprint `Disclosable` configuration accepts JSON Pointer paths in addition to top-level claim names, and the resulting SD-JWT VC is valid for both the internal Sorcha presentation path and the HAIP OID4VP path (once spec 098 ships). The same credential, the same disclosure rules, two verification paths.

**Why this priority**: This is the usability glue that ties spec 094 to the Blueprint layer and to both of the eventual OID4VP verifier paths. Without it, Blueprint authors would have to write two different disclosure configurations per credential, which defeats the compose-once-verify-anywhere story.

**Independent Test**: Take a Blueprint template that previously issued a credential with top-level name-keyed disclosables. Add a nested field with a JSON-Pointer disclosable and re-run the same Blueprint. Confirm the issued credential has both disclosure styles working in parallel. Present it twice — once via the fixed internal verifier (spec 093), once via a test harness that simulates a HAIP wallet consuming the raw SD-JWT VC — and confirm both paths reconstruct the same disclosed subset.

**Acceptance Scenarios**:

1. **Given** a Blueprint credential issuance configuration that previously named disclosable claims by top-level name only, **When** the same configuration is used after this spec ships, **Then** it continues to behave identically (backward compatibility for existing Blueprints).
2. **Given** a Blueprint credential issuance configuration naming `Disclosable` entries by JSON Pointer, **When** a credential is issued, **Then** the resulting SD-JWT VC carries nested `_sd` arrays at the correct depth for each path.
3. **Given** a Blueprint that mixes top-level name-keyed disclosables with JSON-Pointer nested disclosables in a single credential, **When** the credential is issued and presented, **Then** the verifier correctly reconstructs the exact disclosed subset across both disclosure styles.

---

### Edge Cases

- What happens when a holder binding key is not yet derivable because the wallet was created before this spec shipped? Legacy wallets lazily derive a binding key on first use. The derivation is deterministic from the existing seed, so no new state is required beyond the new purpose.
- What happens when an issuer attempts to set `cnf` on a credential whose holder is not a Sorcha wallet (for example an external HAIP wallet that brought its own holder key)? The external wallet's public key is accepted as-is and embedded verbatim into `cnf`. No derivation is performed. This is the path spec 097 will exercise.
- What happens to a credential issued with `cnf` if the holder later loses the binding key? The credential becomes unusable — presentation requires the binding private key. This is the intended behaviour. Recovery is via wallet mnemonic restore, which reconstitutes the binding key under its purpose.
- What happens if a KB-JWT's `iat` is in the future or far in the past? Verification fails with a clock-skew error. The acceptable skew window is `±60 seconds` by default; later specs may make this configurable per verifier.
- What happens if the requested disclosure path references an array element that has been removed since issuance? The disclosure creation fails with an unknown-path error. A holder cannot invent array elements that the issuer did not emit.
- What happens if a HAIP issuer wallet has the `HaipIssuer` capability but its classical co-key has been compromised? The classical co-key can be rotated under the same purpose derivation (new index). All outstanding credentials signed by the old key become unverifiable — that is correct behaviour and matches how classical key rotation works elsewhere. A holistic rotation ceremony is outside this spec's scope.
- What happens when a Sorcha-internal verifier encounters a credential whose `cnf` key is not a Sorcha key (it came from an external HAIP wallet)? The verifier still validates the KB-JWT against whichever public key `cnf` names. Nothing in the verifier is Sorcha-specific to the holder identity.
- What happens if the credential's disclosure set is empty (everything is non-disclosable)? Issuance succeeds. Presentations of such a credential carry no disclosures beyond the always-visible claims; `cnf` and KB-JWT semantics are unchanged.
- What happens when the IETF SD-JWT draft changes its `typ` header from `vc+sd-jwt` to `dc+sd-jwt`? This spec tracks HAIP 1.0 and uses whichever `typ` HAIP 1.0 currently names (`vc+sd-jwt`). A follow-up operational spec can bump this if and when HAIP 1.1 lands.

## Requirements *(mandatory)*

### Functional Requirements

**Holder key binding at issuance (cnf):**
- **FR-001**: The SD-JWT VC creator MUST accept a holder public key in JWK form as part of every issuance request.
- **FR-002**: The signed credential payload MUST contain a `cnf` claim whose `jwk` member is the holder's public key.
- **FR-003**: The `cnf` claim MUST be a non-disclosable top-level claim. It is always visible when the token is read.
- **FR-004**: If the holder public key is missing from an issuance request, the issuer MUST fail the request with a clear error. There is no anonymous-holder path.
- **FR-005**: The existing Blueprint-internal issuance path MUST supply the holder's `credential-holder-binding` public key automatically from the Wallet Service without requiring Blueprint authors to manage key material.
- **FR-006**: Legacy credentials without `cnf` (issued before this spec shipped) remain valid and the presentation verifier MUST treat the absence of `cnf` as a signal to skip KB-JWT verification. This is the backward compatibility contract.

**Key Binding JWT at presentation:**
- **FR-007**: The presentation creator MUST build a Key Binding JWT and append it to the serialised presentation when the credential carries a `cnf` claim.
- **FR-008**: The KB-JWT payload MUST contain `aud` (the verifier audience), `nonce` (the verifier nonce), `iat` (issuance time), and `sd_hash` (SHA-256 digest of the preceding portion of the serialised presentation).
- **FR-009**: The KB-JWT MUST be signed using the private key corresponding to the public key named in the credential's `cnf` claim.
- **FR-010**: For Sorcha-internal holders, the Wallet Service MUST sign the KB-JWT using the wallet's `credential-holder-binding` private key without the caller needing to pass key material.
- **FR-011**: For external holders (holder key was supplied by the caller at issuance time), the holder's own infrastructure signs the KB-JWT; Sorcha only verifies it.
- **FR-012**: The presentation verifier MUST verify the KB-JWT signature against the public key in `cnf`, check that `aud` matches the verifier's expected audience, `nonce` matches the verifier's issued nonce, `iat` is within the acceptable skew window (`±60 seconds` default), and `sd_hash` matches a SHA-256 of the preceding portion of the presentation.
- **FR-013**: If any KB-JWT check fails, the verifier MUST fail the presentation with a specific error naming which check failed (audience mismatch, nonce mismatch, clock skew, sd_hash mismatch, signature invalid, binding key mismatch, missing KB-JWT).
- **FR-014**: The verifier MUST NOT fall back to "looser" verification if the KB-JWT fails. There is no acceptance path for a credential with `cnf` and a broken KB-JWT.

**Nested and array-element selective disclosure:**
- **FR-015**: The disclosure API MUST accept JSON Pointer paths in addition to top-level claim names. Both styles MUST be usable in the same credential.
- **FR-016**: The issuer MUST produce nested `_sd` digest arrays at the correct depth for each disclosable nested field so that undisclosed fields are cryptographically hidden within their parent object.
- **FR-017**: The issuer MUST support disclosing individual array elements by their index (via paths such as `/qualifications/0`). Undisclosed elements MUST be cryptographically hidden and not merely absent.
- **FR-018**: The holder MUST be able to select any subset of the declared disclosable paths at presentation time, including zero.
- **FR-019**: The verifier MUST correctly reconstruct the disclosed subset when presented with a mix of top-level and nested disclosures.
- **FR-020**: If a holder attempts to disclose a path that does not correspond to a declared disclosable field, presentation creation MUST fail with an unknown-path error.
- **FR-021**: Existing Blueprints that declare disclosables by top-level name only MUST continue to behave identically (backward compatibility).

**Holder binding key (Sorcha-internal holders):**
- **FR-022**: Every Sorcha wallet MUST have a deterministic holder binding key derived from the wallet seed under the new BIP32 purpose `sorcha:credential-holder-binding`.
- **FR-023**: The binding key is one per wallet, not one per credential.
- **FR-024**: The Wallet Service MUST expose an endpoint (or internal API) that returns the holder binding public key in JWK form for a given wallet address.
- **FR-025**: The Wallet Service MUST expose an endpoint (or internal API) that signs a KB-JWT using the holder binding private key for a given wallet address, given the `aud`, `nonce`, and `sd_hash` inputs.
- **FR-026**: Wallet recovery from mnemonic MUST deterministically reconstruct the holder binding key on any machine.
- **FR-027**: Rotation of the holder binding key is explicitly out of scope for this spec and deferred. A rotation mechanism is a later operational concern.

**Classical co-key for HAIP issuance:**
- **FR-028**: A wallet MUST be able to carry a capability flag `HaipIssuer` indicating it is eligible to issue HAIP-facing credentials.
- **FR-029**: Setting `HaipIssuer` on a wallet whose primary algorithm is in the classical set (Ed25519, NIST P-256) MUST NOT derive a new key; the primary key is used directly for HAIP issuance.
- **FR-030**: Setting `HaipIssuer` on a wallet whose primary algorithm is in the PQC set (ML-DSA, SLH-DSA) MUST derive a classical co-key under the new BIP32 purpose `sorcha:haip-issuer-signing` with default algorithm ES256.
- **FR-031**: The classical co-key's public key MUST appear as a separate verification method in the wallet's DID document, with a distinct key identifier from the primary key.
- **FR-032**: The issuer MUST automatically select the classical co-key (or the primary key, whichever is classical) when the issuance path is HAIP. Callers do not pick the signing key.
- **FR-033**: Internal Sorcha transaction signing paths MUST continue to use the wallet's primary key (PQC if that is what the wallet declared). HAIP key selection applies only to HAIP-facing credential issuance.
- **FR-034**: Attempting to sign an SD-JWT VC via a wallet that lacks the `HaipIssuer` capability MUST fail with a clear capability-missing error before any cryptographic operation is attempted.

**Carried forward from spec 031 (non-superseded):**
- **FR-035**: The credential engine MUST allow Blueprint actions to require a credential as an entry gate, specifying acceptable credential type and accepted issuers. (031 FR-carry)
- **FR-036**: The credential engine MUST allow Blueprint actions to issue a credential as an output, with claim mappings drawn from the action's input data. (031 FR-carry)
- **FR-037**: Credentials MUST be cryptographically verifiable against the issuer's public key using standard SD-JWT VC verification rules. (031 FR-carry)
- **FR-038**: The credential format MUST align with the IETF SD-JWT VC profile currently referenced by HAIP 1.0. (031 FR-carry)

**Cross-cutting:**
- **FR-039**: All new behaviour MUST be covered by automated tests at unit and integration level, including round-trip tests that issue, present and verify credentials with every supported combination of disclosure styles and holder binding scenarios.
- **FR-040**: The spec MUST not change any HTTP wire format of existing endpoints. Changes are confined to library behaviour, wallet domain, and credential payload content.
- **FR-041**: The spec MUST not regress any acceptance scenario from spec 093 (`vc-security-fixes`).

### Key Entities *(include if feature involves data)*

- **cnf claim** (new, inside the signed SD-JWT payload): The holder key binding. Carries a `jwk` member with the holder's public key. Non-disclosable. Required for every credential issued after this spec ships; absent on legacy credentials (which remain valid).
- **Key Binding JWT** (new, appended to serialised presentations): A small JWT signed by the holder's binding private key, binding the presentation to a specific verifier audience and nonce. Payload contains `aud`, `nonce`, `iat`, `sd_hash`. Required whenever the credential carries `cnf`.
- **Nested `_sd` digest array** (new, inside the signed SD-JWT payload): Selective disclosure hashes placed at nested depth within the credential payload to enable JSON-Pointer disclosable fields. Existing top-level `_sd` arrays continue to work alongside.
- **Holder binding key** (new, in the wallet domain): A BIP32-derived key under purpose `sorcha:credential-holder-binding`, one per wallet, deterministic from the wallet seed. Used to sign KB-JWTs on behalf of Sorcha-internal holders. Recovered from mnemonic like any other Sorcha HD sub-key.
- **HAIP issuer co-key** (new, in the wallet domain): A BIP32-derived key under purpose `sorcha:haip-issuer-signing`, classical algorithm (ES256 default). Present only on wallets carrying the `HaipIssuer` capability where the primary algorithm is PQC. Exposed as a distinct verification method in the wallet's DID document.
- **HaipIssuer wallet capability** (new, on the wallet entity): A boolean flag or capability token indicating the wallet is eligible to issue HAIP-facing credentials. Gate for the classical co-key derivation and for the HAIP issuance path.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of credentials issued after this spec ships contain a `cnf` claim in the signed payload, verified by a corpus sweep against the credential store. Legacy credentials without `cnf` are counted separately and continue to verify.
- **SC-002**: 100 % of presentations of `cnf`-bearing credentials contain a valid Key Binding JWT bound to the verifier's audience and nonce, confirmed by round-trip tests covering every holder key path.
- **SC-003**: A presentation captured from verifier A cannot be replayed to verifier B without failing KB-JWT audience verification, confirmed by a replay test.
- **SC-004**: Nested disclosure works for credentials containing at least one real-world schema shape (an address with locality disclosed and country hidden, or equivalent), confirmed by an end-to-end test using a schema drawn from the DPP or construction-permit walkthroughs.
- **SC-005**: Array-element disclosure works for credentials with at least one array-of-objects claim, confirmed by a test that discloses one element out of three and confirms the other two are cryptographically hidden.
- **SC-006**: Existing Blueprints that declare top-level name-keyed disclosables continue to produce credentials whose behaviour is byte-for-byte identical to pre-spec output for those disclosable sets, confirmed by a regression corpus.
- **SC-007**: Wallets whose primary algorithm is ML-DSA-65 and that carry the `HaipIssuer` capability successfully issue HAIP-path credentials signed with ES256, confirmed by an end-to-end test covering issuance, presentation, and external verification.
- **SC-008**: Internal Sorcha transaction signing from the same ML-DSA-65 wallet continues to produce ML-DSA-65 signatures, confirmed by an independent signature-algorithm audit.
- **SC-009**: Holder binding keys recover deterministically from a wallet mnemonic on a clean machine, and all previously issued credentials continue to verify, confirmed by a recovery integration test.
- **SC-010**: No acceptance scenario from spec 093 regresses after this spec ships, confirmed by running the 093 regression suite.

## Out of Scope

The following are explicitly deferred to later specs:

- OpenID4VCI wire protocol, issuer metadata, token endpoint, credential endpoint, credential offer URIs — belong to spec 097.
- HAIP-shaped OpenID4VP Authorization Request, `presentation_definition`, `presentation_submission`, `direct_post` response mode — belong to spec 098.
- IETF Token Status List envelope format for credential status — belongs to spec 095.
- X.509 trust chain integration for issuer identity — belongs to spec 096.
- mdoc, ISO 18013-5, ISO 18013-7, CBOR encoding, device binding — deferred follow-up beyond the current spec set.
- Rotation of the holder binding key. Rotation of the HAIP issuer co-key. Both deferred.
- Hybrid JOSE algorithm identifiers (PQC + classical in a single signature). HAIP 1.0 does not name any; this is a HAIP 1.1+ concern.
- Changes to the top-level name-keyed disclosure API. That continues to work unchanged; nested disclosure is added alongside.
- A new citizen-facing wallet UI. Sorcha remains the backend; the citizen wallet is GOV.UK Wallet / EUDI Wallet / equivalent.

## Assumptions

- The BIP32/39/44 HD wallet machinery in `Sorcha.Cryptography` already supports purpose-derived sub-keys under arbitrary derivation contexts, per the precedents established by Feature 086 (`sorcha:docket-signing`) and Feature 092 (`sorcha:persona-vault`). This spec adds two new purposes without changing how purpose derivation works.
- The existing SD-JWT implementation's wire format (`header.payload.signature~d1~d2~…~kb-jwt`) is close enough to the IETF SD-JWT draft that the `kb-jwt` slot can be populated by appending a signed JWT rather than requiring a structural overhaul.
- HAIP 1.0 continues to reference `vc+sd-jwt` as the SD-JWT VC `typ` header value. If HAIP 1.1 renames it to `dc+sd-jwt`, a follow-up spec will track the change.
- ES256 is the correct default classical algorithm for the HAIP issuer co-key. It is the HAIP 1.0 MTI. EdDSA is acceptable as an alternate per HAIP but ES256 is the conservative default.
- The existing wallet capability model is extensible to new capability flags without a schema migration requiring downtime. If this assumption is wrong, spec 094's planning phase will need a migration sub-task.
- Legacy credentials without `cnf` are rare enough in production that running them alongside `cnf`-bearing credentials is acceptable. If a mass re-issuance is needed, it is a separate operational concern.
- The JSON Pointer disclosure extension does not break the hash chain used by existing top-level disclosures, because nested `_sd` arrays are additive — each one is scoped to its parent object and does not interfere with top-level digests.

## Dependencies

- **Hard dependency on spec 093.** The presentation verifier fix in 093 is the substrate onto which KB-JWT verification is added here. This spec cannot ship correctly against the broken verifier.
- **Required by specs 097 and 098.** Both HAIP endpoint specs assume the SD-JWT library produces HAIP-conformant credentials and consumes HAIP-conformant presentations.
- **No dependency on specs 095 or 096.** The IETF Token Status List and X.509 trust work are orthogonal and can run in parallel branches after 093 lands.

## Amendment note on spec 031

This spec supersedes `specs/031-verifiable-credentials`. The following requirements from 031 are carried forward verbatim or with tightening:

- 031 entry-gate credential requirement on Blueprint actions → FR-035.
- 031 credential issuance from Blueprint actions with claim mappings → FR-036.
- 031 cryptographic verifiability against issuer public key → FR-037 (strengthened by FR-007 through FR-014).
- 031 SD-JWT VC format alignment → FR-038 (now bound to HAIP 1.0).

The following 031 requirements are explicitly superseded and no longer in force once this spec ships:

- 031's implicit "top-level name-keyed disclosure only" model → superseded by FR-015 (JSON Pointer paths).
- 031's implicit "bearer token" behaviour → superseded by FR-001 through FR-014 (`cnf` + KB-JWT).

Any 031 requirement not listed above remains in force through spec 094 unless explicitly noted.
