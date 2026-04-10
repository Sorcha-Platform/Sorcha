# Feature Specification: X.509 Organisation Trust Integration

**Feature Branch**: `096-x509-org-trust`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "Internal Sorcha CA per tenant issuing X.509 org certs for HAIP issuer identity, pluggable trust provider, x5c chain in SD-JWT VC JWS header"

## Context

HAIP 1.0 external verifiers — GOV.UK Wallet, EUDI Wallet, registered Digital Verification Services, generic HAIP-conformant test harnesses — validate issuer identity against **X.509 certificate chains rooted in trust lists**, not against the DID-based trust model Sorcha uses internally. Phase 1 confirmed that Sorcha has zero X.509 integration on master: no certificate handling, no CA, no `x5c` headers on issued SD-JWT VCs, no CRL publication. Without this, any Sorcha-issued credential reaching a HAIP wallet is architecturally unverifiable at the trust layer regardless of how conformant the rest of the credential format is.

Phase 2 D4 chose **internal Sorcha CA per tenant** as the default trust model, sitting behind a **pluggable trust provider interface** so a real-world deployment can later swap in a publicly-rooted chain (eIDAS QTSP, HMG DVS-accepted CA) without changing the credential format or the rest of the HAIP spec set. D4 also scoped this first spec to **org-level** certificates only; tenant-level and register-level scopes are explicitly deferred.

This spec stands up the PKI stack, wires it into the HAIP credential issuance path introduced by spec 097, and publishes the chain on the wire via the standard JOSE `x5c` header on the SD-JWT VC's outer JWS. Internal Sorcha credential issuance and verification paths continue to use DID-based trust unchanged — the two trust stacks run in parallel on the same credentials.

**Related specs.**
- **Depends on** spec 094 (`sdjwt-haip-hardening`) — the Org Cert's subject public key is the classical signing key that 094 derives under `sorcha:haip-issuer-signing`.
- **Runs in parallel to** spec 095 (`ietf-token-status-list`) — no dependency in either direction. The IETF Token Status List JWT signing key can be verified via this spec's X.509 chain for HAIP-facing scenarios, but neither spec blocks the other.
- **Required by** spec 097 (`openid4vci-issuer`) — the HAIP issuer endpoint cannot emit credentials acceptable to HAIP wallets without an X.509 chain in `x5c`.
- **Required by** spec 098 (`openid4vp-verifier`) — the HAIP verifier endpoint needs to validate incoming credentials' `x5c` chains against its trust store.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A HAIP external wallet trusts a Sorcha-issued credential via X.509 chain (Priority: P1)

A Sorcha-using authority (a council, a regulator, a professional body) issues a licence credential via the HAIP path. The signed SD-JWT VC carries an `x5c` header containing a two-element certificate chain: the Org Cert for the issuing authority and the Tenant Root CA that signed it. A HAIP wallet receives the credential, walks the chain, checks the Tenant Root against whatever trust store it holds, verifies the credential signature using the public key from the Org Cert's Subject Public Key Info, and accepts the credential without needing any Sorcha-specific knowledge.

After this spec ships, the X.509 pieces work end to end for the Sorcha-internal CA case. A deployment that wants real-world trust (EUDI Wallet enrolment, HMG DVS) swaps the pluggable trust provider for a publicly-rooted one without touching the credential format or the HAIP spec set.

**Why this priority**: This is the hard prerequisite for HAIP external wallet interop at the trust layer. Spec 097 can emit HAIP-shaped credentials, but without a valid trust chain in `x5c` no real HAIP wallet will accept them. This story is also what the "Sorcha is the workflow layer above GOV.UK Wallet" positioning stands or falls on once a demo progresses past credential-format validation.

**Independent Test**: Provision a tenant root CA, issue an org cert for a test authority, issue a credential through a test HAIP path with the `x5c` header populated, then verify the credential with a generic third-party HAIP client that has the test tenant root in its trust store. Confirm the client accepts the credential. Remove the tenant root from the client's trust store and confirm the client rejects it with a trust anchor error.

**Acceptance Scenarios**:

1. **Given** a provisioned tenant root CA and an issued org cert for a HAIP issuer wallet, **When** the wallet issues a credential via the HAIP path, **Then** the signed SD-JWT VC's JWS header contains an `x5c` member carrying the Org Cert and the Tenant Root CA in DER-encoded base64 form, leaf first.
2. **Given** a credential with a valid two-element `x5c` chain, **When** a generic HAIP client walks the chain and finds the Tenant Root in its trust store, **Then** chain validation succeeds, the credential signature is verified against the Org Cert's Subject Public Key Info, and the credential is accepted.
3. **Given** the same credential, **When** the Tenant Root is not in the HAIP client's trust store, **Then** chain validation fails with a trust anchor error and the credential is rejected before any claim is considered.
4. **Given** a credential whose Org Cert has expired, **When** the HAIP client walks the chain, **Then** validation fails with a certificate expiry error naming the offending cert.
5. **Given** a credential whose Org Cert has been revoked (CRL listing), **When** the HAIP client fetches the CRL and checks the Org Cert's serial number, **Then** validation fails with a revoked certificate error.
6. **Given** a credential whose issuer identifier embedded in the SD-JWT `iss` claim does not match the Org Cert's Subject Alternative Name URI, **When** the HAIP client validates consistency, **Then** validation fails with an issuer mismatch error.

---

### User Story 2 - A Sorcha tenant provisions a fresh PKI stack with a single operation (Priority: P1)

A new Sorcha deployment needs X.509-capable HAIP issuance. The tenant operator runs a provisioning operation that creates the Tenant Root CA, publishes its public trust anchor information, and initialises the CRL distribution point. From that point forward, org certs can be issued on demand whenever an organisation in the tenant registers as a HAIP issuer.

After this spec ships, provisioning is a single tenant-scoped operation with idempotent semantics. The Tenant Root CA key material is held in the same key-management mode as the tenant's other high-sensitivity keys (local or KMS-resident, following the existing `signingMode` pattern from spec 094). Trust anchor publication uses a stable URL so external verifiers can fetch and cache it.

**Why this priority**: This is the setup story. Every other User Story in this spec assumes a provisioned tenant CA. Without a clean provisioning model, the whole spec becomes an ops-heavy collection of manual steps.

**Independent Test**: Run the provisioning operation on an empty tenant and confirm a Tenant Root CA exists, its trust anchor URL resolves to a valid document, and the CRL distribution URL resolves to a valid empty CRL. Run the provisioning operation a second time on the same tenant and confirm it is idempotent (no second root is created).

**Acceptance Scenarios**:

1. **Given** a tenant with no existing PKI, **When** the tenant provisioning operation runs, **Then** a Tenant Root CA is created, its private key is stored according to the tenant's declared signing mode (local or KMS-resident), and its public trust anchor is published at a stable URL.
2. **Given** a provisioned tenant, **When** the provisioning operation runs a second time, **Then** the operation is a no-op and the existing Tenant Root CA is unchanged.
3. **Given** a provisioned tenant, **When** the trust anchor URL is fetched, **Then** the response contains the Tenant Root CA certificate in DER-encoded base64 form and metadata identifying the tenant.
4. **Given** a provisioned tenant, **When** the CRL distribution URL is fetched, **Then** the response contains a valid CRL signed by the Tenant Root CA, initially listing zero revoked certificates, with a defined `nextUpdate` for refresh.
5. **Given** a tenant where the configured signing mode is `KmsResident`, **When** the root CA is created, **Then** the private key is generated inside the configured KMS and never extracted.
6. **Given** a deployment that swaps the pluggable trust provider from the default internal CA to an externally-rooted provider, **When** the provisioning operation runs, **Then** the externally-rooted chain is installed in place of a newly generated internal root, and the credential format and HAIP plumbing are unchanged.

---

### User Story 3 - An organisation's X.509 identity is bound to its existing DID identity (Priority: P1)

An organisation in a Sorcha tenant already has a `did:sorcha:org:{walletAddress}` identifier and a wallet holding its classical HAIP issuer co-key (from spec 094). When the organisation is enrolled as a HAIP issuer, the tenant CA issues an Org Cert whose subject public key is the existing classical co-key, whose Subject CN is the human-readable org name, and whose Subject Alternative Name includes the `did:sorcha:org:{walletAddress}` as a URI SAN. After enrolment, the same classical signing key has two identities: its Sorcha DID verification method entry and its X.509 cert. HAIP external verifiers pick the X.509 path; Sorcha-internal verifiers continue to use the DID path. The underlying signatures are identical regardless of which trust stack the verifier walks.

**Why this priority**: This is the binding story. Without it, a Sorcha organisation would have to generate a new key when enrolling for HAIP, splitting its identity across two keys and two trust stacks and creating a correlation nightmare. The whole point of the two-path design is that the classical key is shared; only the trust wrapping differs.

**Independent Test**: Take an organisation with an existing HAIP-issuer wallet. Enrol it for X.509 trust. Confirm the resulting Org Cert's Subject Public Key Info matches the wallet's classical co-key, the Subject CN matches the org name, and the Subject Alternative Name contains the `did:sorcha:org:{walletAddress}` URI. Sign a credential with the wallet and confirm the signature verifies both via the DID path (resolve the DID, extract the verification method, check signature) and via the X.509 path (walk the `x5c` chain, extract the Subject Public Key Info, check signature) and yields the same result.

**Acceptance Scenarios**:

1. **Given** an organisation with an existing `did:sorcha:org:{walletAddress}` and a wallet holding a classical HAIP issuer co-key, **When** the org is enrolled as a HAIP issuer, **Then** the tenant CA issues an Org Cert whose Subject Public Key Info encodes the same classical public key as the wallet holds.
2. **Given** an Org Cert issued for a Sorcha org, **When** the cert is inspected, **Then** its Subject CN is the org's human-readable name, its Subject Alternative Name includes a URI entry naming the org's `did:sorcha:org:{walletAddress}`, and its CRL Distribution Points extension points at the tenant's CRL URL.
3. **Given** a HAIP-issued credential signed by the org's classical co-key, **When** the credential is verified via the DID path (Sorcha-internal verifier), **Then** signature verification succeeds using the DID's verification method.
4. **Given** the same credential, **When** it is verified via the X.509 path (HAIP external verifier), **Then** signature verification succeeds using the Org Cert's Subject Public Key Info and returns the same result as the DID path.
5. **Given** an org whose primary wallet key is PQC and that has not yet been upgraded for HAIP issuance, **When** enrolment for X.509 trust is attempted, **Then** the enrolment fails with a clear prerequisite error pointing at the missing classical co-key (spec 094 `HaipIssuer` capability not set).
6. **Given** an existing Org Cert whose underlying wallet classical co-key is rotated via spec 094's derivation path, **When** the rotation completes, **Then** the Org Cert must be re-issued by the tenant CA to bind the new public key; outstanding credentials signed by the old key continue to verify against the old Org Cert until that cert's validity period expires.

---

### User Story 4 - A tenant operator revokes an org cert and downstream HAIP verifiers stop trusting the issuer within the CRL refresh window (Priority: P2)

A tenant operator needs to disable an organisation's ability to issue new HAIP credentials — for example, after a security incident, policy violation, or organisation closure. They add the org's cert serial number to the tenant CRL. The updated CRL is republished at the distribution point. HAIP verifiers that refresh the CRL within its `nextUpdate` window see the revocation and start rejecting any credential signed by that Org Cert on the next verification attempt.

**Why this priority**: Revocation is the standard PKI counterpart to issuance. It is non-optional for any production deployment but the mechanism is simple enough (CRL only, no OCSP) that it does not carry the risks that a full OCSP stack would at this scope.

**Independent Test**: Issue a credential, confirm it verifies, revoke the Org Cert that signed it, republish the CRL, and confirm that a HAIP verifier fetching the CRL after the refresh window rejects the credential with a revoked-certificate error. Also confirm that credentials signed by the Org Cert *before* revocation continue to verify in a "validity was correct at signing time" mode if the verifier is configured for that posture (standard PKI behaviour).

**Acceptance Scenarios**:

1. **Given** a tenant with an issued and active Org Cert, **When** the operator issues a revoke operation against the Org Cert's serial number, **Then** the cert is added to the tenant CRL, the CRL is re-signed by the Tenant Root CA, and the updated CRL is published at the distribution point.
2. **Given** a published CRL with a revoked Org Cert entry, **When** a HAIP verifier fetches the CRL after its cached copy's `nextUpdate` elapses, **Then** the verifier sees the revocation and rejects any credential signed by the revoked Org Cert.
3. **Given** a HAIP verifier in default strict mode, **When** it encounters a credential signed by a revoked Org Cert, **Then** it returns a revoked-certificate error regardless of whether the credential was issued before or after the revocation timestamp.
4. **Given** a revoked Org Cert, **When** the operator attempts to use the same wallet to issue a new HAIP credential, **Then** the credential issuance endpoint refuses because the Org Cert that would sign the chain is no longer valid, and the operator must enrol for a fresh Org Cert before further issuance.
5. **Given** a CRL that grows past a configurable threshold, **When** the refresh interval elapses, **Then** the CRL is published using standard CRL delta mechanisms or is allowed to grow, according to deployment configuration. Default is allow to grow; delta CRLs are a future concern.

---

### User Story 5 - A deployment swaps the internal CA for a publicly-rooted chain without changing anything else (Priority: P2)

A specific Sorcha deployment — for example, a Scottish council partner that wants to participate in a HMG DVS pilot — needs credentials that chain to a publicly-accepted CA rather than a self-signed Sorcha tenant root. They configure a trust provider that returns an externally-issued Tenant Root Cert from an eIDAS QTSP or equivalent. The rest of the Sorcha HAIP stack is unchanged: spec 097 still emits credentials with `x5c` chains, spec 098 still verifies incoming chains, and nothing in the Blueprint authoring path, the Wallet Service, or the presentation flow is touched.

**Why this priority**: This is the escape hatch that makes the default internal CA acceptable to ship. Without a clean swap mechanism, every deployment that needs real-world trust would have to fork Sorcha. With the swap mechanism, the default is "works out of the box for walkthroughs and internal demos" and the production path is "configure a different trust provider and continue".

**Independent Test**: Implement a mock external trust provider that returns a pre-generated Tenant Root Cert signed by a test CA. Configure a Sorcha tenant to use the mock provider. Run the provisioning operation. Confirm the tenant's trust anchor URL returns the externally-signed Tenant Root Cert rather than a freshly generated self-signed one. Issue a credential and confirm its `x5c` chain ends at the externally-signed Tenant Root.

**Acceptance Scenarios**:

1. **Given** a tenant configured with a custom trust provider, **When** tenant provisioning runs, **Then** the provider's `ProvisionTrustAnchor` operation is called and the returned Tenant Root Cert replaces the default self-signed cert.
2. **Given** a tenant using a custom trust provider, **When** an org cert is requested, **Then** the provider's `IssueOrgCert` operation is called and the returned Org Cert is used in credential `x5c` chains.
3. **Given** a tenant using a custom trust provider whose `IssueOrgCert` operation returns an error, **When** org enrolment is attempted, **Then** the enrolment fails with the provider's error surfaced to the operator without leaking sensitive details.
4. **Given** a custom trust provider that publishes its own CRL at an external URL, **When** a HAIP verifier fetches credentials signed via that chain, **Then** the CRL Distribution Points extension on the Org Cert points at the external URL, not at a Sorcha-hosted one.
5. **Given** a tenant swapped from the default internal CA to a custom trust provider mid-life, **When** the swap completes, **Then** credentials issued before the swap continue to validate against the old Tenant Root for the remainder of their Org Cert's validity period, and credentials issued after the swap validate against the new provider's chain.

---

### Edge Cases

- What happens when a HAIP verifier receives a credential whose `x5c` chain references a Tenant Root the verifier does not know? Chain validation fails with a trust anchor error. This is the correct behaviour — trust anchor management is deployment configuration.
- What happens when an Org Cert's validity period is shorter than a credential's validity period it signed? The credential becomes unverifiable once the Org Cert expires, per standard PKI behaviour. Operators should set Org Cert lifetimes at or beyond expected credential lifetimes.
- What happens when a tenant operator tries to revoke the Tenant Root CA itself? Revocation of the root is not supported via the CRL mechanism (the root signs the CRL; a root cannot revoke itself). Tenant root rotation is a separate operational ceremony, out of scope for this spec and deferred.
- What happens when a Sorcha-internal verifier encounters a credential whose `x5c` chain is malformed? The internal verifier falls back to the DID-based verification path, because internal verification does not require the X.509 path. A warning is logged for operator visibility.
- What happens when a classical co-key is rotated (spec 094) but the Org Cert is not re-issued? New credentials signed with the new key fail `x5c` signature verification because the Org Cert still binds the old key. The operator must re-issue the Org Cert after rotation, per User Story 3 AC-6.
- What happens when the tenant CA's own signing key is compromised? This is a tenant-wide incident. All issued Org Certs must be revoked via CRL, and the tenant root is rotated via a ceremony that is out of scope for this spec. All outstanding credentials signed under the compromised root are untrusted.
- What happens when the CRL fetch fails at verification time? Behaviour depends on the HAIP verifier's configured revocation check policy. Default is fail-closed; deployments can choose fail-open with an audit warning, matching the existing `RevocationCheckPolicy` pattern in the status list consumer.
- What happens when an Org Cert's Subject Alternative Name URI (the `did:sorcha:org:...`) does not resolve because the referenced wallet no longer exists? The DID resolution path fails gracefully; the X.509 verification path continues to work independently because it does not depend on DID resolution. The two trust stacks are genuinely independent.
- What happens when a deployment wants to issue credentials under multiple public trust anchors (for example, one chain for UK HMG DVS and another for EUDI Wallet)? This spec covers one trust provider per tenant. Multi-provider support is a future extension; a deployment that needs both can run two tenants or wait for the extension.
- What happens when a credential is issued by a HAIP issuer wallet whose classical co-key was derived for a different tenant? The Org Cert is tenant-scoped; attempting to cross-sign across tenants is rejected at enrolment.

## Requirements *(mandatory)*

### Functional Requirements

**Tenant Root CA provisioning:**
- **FR-001**: The system MUST provide a tenant-scoped provisioning operation that creates a Tenant Root CA if one does not already exist. The operation MUST be idempotent.
- **FR-002**: The Tenant Root CA is a first-class domain entity with its own key storage, independent of the wallet HD hierarchy (see Clarifications Q4.1 ruling). Its private key MUST be stored in one of three modes: (a) generated internally and optionally derivable from a tenant CA recovery seed under the purpose `sorcha:tenant-ca-signing` for deterministic recovery, (b) imported from an external source such as an HSM, eIDAS QTSP-issued signer, or an existing PKI signing key, in which case no derivation is performed, or (c) `KmsResident` mode honouring the same KMS integration Sorcha already uses for wallet keys. The tenant picks the mode at provisioning time.
- **FR-003**: The Tenant Root CA's public trust anchor MUST be published at a stable tenant-scoped URL so external verifiers can fetch and cache it.
- **FR-004**: The Tenant Root CA MUST sign a tenant CRL that is published at a stable tenant-scoped URL with a configurable refresh interval (default 24 hours).
- **FR-005**: The Tenant Root CA certificate MUST use a classical signing algorithm (ES256 default, matching spec 094 FR-030). PQC algorithms MUST NOT be used for the CA chain because HAIP 1.0 is classical-only at the trust boundary.
- **FR-006**: The Tenant Root CA certificate's validity period MUST be configurable at provisioning time, with a sensible default (10 years for internal CAs).

**Org Cert issuance:**
- **FR-007**: The system MUST issue an Org Cert on demand when an organisation is enrolled as a HAIP issuer. Enrolment MUST require that the organisation's wallet already carries the `HaipIssuer` capability (spec 094 FR-028).
- **FR-008**: The Org Cert's Subject Public Key Info MUST encode the organisation's existing classical HAIP issuer signing key (spec 094's `sorcha:haip-issuer-signing` derivation). A new key MUST NOT be generated; the existing key is reused.
- **FR-009**: The Org Cert's Subject CN MUST be the organisation's human-readable display name.
- **FR-010**: The Org Cert's Subject Alternative Name extension MUST contain a URI entry whose value is the organisation's `did:sorcha:org:{walletAddress}` identifier.
- **FR-011**: The Org Cert's CRL Distribution Points extension MUST reference the tenant's CRL URL from FR-004.
- **FR-012**: The Org Cert MUST NOT set an Extended Key Usage extension in this spec. HAIP 1.0 does not name a dedicated EKU OID for SD-JWT VC credential issuance and most HAIP verifiers do not enforce EKU. This is a known gap that will be revisited when HAIP 1.1 names an OID or when a named deployment partner (for example GOV.UK Wallet or EUDI Wallet) requires a specific EKU (see Clarifications Q4.2 ruling). A follow-up operational spec may later add a configurable EKU set per trust provider without changing the rest of this spec.
- **FR-013**: The Org Cert MUST be issued with a validity period configurable at issuance time, with a sensible default (2 years).
- **FR-014**: The Org Cert MUST be signed by the Tenant Root CA using the default signing algorithm declared at provisioning time.
- **FR-015**: Attempting to issue an Org Cert for a wallet that does not carry the `HaipIssuer` capability MUST fail with a clear prerequisite error pointing at spec 094.
- **FR-016**: Attempting to issue a second Org Cert for the same wallet without first revoking the existing one MUST fail with a clear "cert already active" error.

**x5c embedding in SD-JWT VC:**
- **FR-017**: When an SD-JWT VC is signed via the HAIP issuance path (spec 097), its outer JWS header MUST contain an `x5c` member.
- **FR-018**: The `x5c` array MUST contain the Org Cert as the first (leaf) element and the Tenant Root CA as the last element, each in DER-encoded base64 form, matching RFC 7515 §4.1.6.
- **FR-019**: The SD-JWT VC's `iss` claim MUST be consistent with the Org Cert's identity. Specifically, the `iss` value MUST match either the Org Cert's Subject Alternative Name URI or a derived form of the Subject CN agreed with HAIP 1.0.
- **FR-020**: Credentials issued via the Sorcha-internal path MUST NOT carry an `x5c` header. Internal-path credentials continue to use DID-based trust exclusively.

**CRL publication and consumption:**
- **FR-021**: The system MUST expose a public, anonymous, cacheable HTTP endpoint that serves the tenant's CRL in DER-encoded base64 form.
- **FR-022**: The CRL MUST be re-signed and republished whenever an Org Cert is revoked or at the scheduled refresh interval, whichever is sooner.
- **FR-023**: The CRL's `nextUpdate` field MUST match the configured refresh interval so verifiers know when to refresh their cached copy.
- **FR-024**: Revoking an Org Cert MUST add the cert's serial number to the CRL and set its revocation reason per RFC 5280.
- **FR-025**: After an Org Cert is revoked, the HAIP credential issuance endpoint (spec 097) MUST refuse to issue new credentials signed by the underlying wallet until a fresh Org Cert is enrolled.

**HAIP verifier chain validation (spec 098 substrate):**
- **FR-026**: The HAIP verifier (spec 098) MUST extract the `x5c` chain from an incoming credential's JWS header and walk it to a trusted root in its configured trust store.
- **FR-027**: Chain validation MUST check each certificate's validity period, signature, and the CRL Distribution Points extension (fetching and checking the CRL per FR-021).
- **FR-028**: If `x5c` chain validation fails for any reason (trust anchor missing, expiry, revocation, signature mismatch, malformed chain), the verifier MUST fail the credential with a specific error identifying the failure cause.
- **FR-029**: The HAIP verifier's trust store MUST be configurable per deployment — a set of accepted Tenant Root CAs, external public roots, or both.
- **FR-030**: The HAIP verifier's revocation check policy MUST be configurable (fail-closed, fail-open-with-warning) matching the existing status list consumer's policy model.

**Pluggable trust provider:**
- **FR-031**: The system MUST define a trust provider interface with operations for: provisioning a tenant trust anchor, issuing an org cert, revoking an org cert, publishing the CRL, and fetching the trust anchor.
- **FR-032**: The default trust provider MUST implement the interface using an internal self-signed Tenant Root CA.
- **FR-033**: A deployment MUST be able to configure a custom trust provider implementation at tenant provisioning time, replacing the default internal CA with an externally-rooted chain without changing the credential format or any other spec in the HAIP spec set.
- **FR-034**: A custom trust provider's `IssueOrgCert` operation MUST return certificates that satisfy FR-008 through FR-013 (subject key, SAN URI, CRL distribution points, EKU, validity period) so the rest of the HAIP stack is unchanged regardless of trust provider choice.
- **FR-035**: A custom trust provider MAY host the CRL externally; in that case the Org Cert's CRL Distribution Points extension MUST reference the external URL.

**Scope restrictions:**
- **FR-036**: This spec introduces org-scoped certificates only. Tenant-scoped identity certificates and register-scoped certificates are out of scope and deferred to follow-up specs.
- **FR-037**: This spec introduces only the default two-level chain (Tenant Root → Org Cert). Three-level chains (Tenant Root → Org → Per-Wallet or Per-Credential) are out of scope and deferred.

**Cross-cutting:**
- **FR-038**: All new behaviour MUST be covered by automated tests at unit and integration level, including round-trip tests from provisioning through issuance, signing, verification and revocation.
- **FR-039**: The spec MUST not regress any acceptance scenario from specs 039, 093, 094, or 095.
- **FR-040**: The spec MUST not change the DID-based verification path for Sorcha-internal credentials. Both trust stacks run in parallel.

### Key Entities *(include if feature involves data)*

- **Tenant Root CA** (new): A self-signed (default) or externally-rooted (via custom trust provider) certificate authority, one per Sorcha tenant. Signs Org Certs and the tenant CRL. Private key stored according to the tenant's signing mode. Public trust anchor published at a stable URL. Validity period default 10 years.
- **Org Cert** (new): An X.509 certificate issued by the Tenant Root CA for a specific HAIP issuer organisation. Subject CN is the org's display name. Subject Public Key Info is the org wallet's classical HAIP issuer signing key (from spec 094). Subject Alternative Name contains the URI form of the org's `did:sorcha:org:{walletAddress}`. CRL Distribution Points references the tenant CRL. EKU declares credential issuer usage. Validity period default 2 years.
- **Tenant CRL** (new): An X.509 Certificate Revocation List signed by the Tenant Root CA, listing revoked Org Cert serial numbers. Published at a stable public URL. Refreshed on every revocation and at a configurable interval (default 24 hours).
- **Trust provider interface** (new): A pluggable contract with operations `ProvisionTrustAnchor`, `IssueOrgCert`, `RevokeOrgCert`, `PublishCrl`, `FetchTrustAnchor`. The default implementation uses the internal self-signed CA; custom implementations can swap in external roots.
- **HAIP issuer enrolment record** (new, conceptual): Tracks which organisations in a tenant have been enrolled for HAIP issuance, their current Org Cert serial number, and the enrolment state. Used by spec 097 to gate credential issuance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A generic HAIP client with a configured trust store containing a Sorcha tenant root successfully validates a Sorcha-issued credential's `x5c` chain and accepts the credential in 100 % of conforming test cases.
- **SC-002**: The same client rejects credentials whose Tenant Root is not in its trust store, whose Org Cert has expired, or whose Org Cert has been revoked via CRL, in 100 % of those negative test cases.
- **SC-003**: Tenant provisioning is idempotent — running it twice on the same tenant produces the same Tenant Root CA and does not mint a second one.
- **SC-004**: Org Cert issuance binds the correct classical signing key in 100 % of enrolments; no cert is ever issued whose Subject Public Key Info does not match the underlying wallet's `sorcha:haip-issuer-signing` key.
- **SC-005**: Revoking an Org Cert propagates to HAIP verifiers within the CRL refresh window (default 24 hours, configurable down to 1 hour for test deployments).
- **SC-006**: After revoking an Org Cert, the HAIP credential issuance endpoint refuses to mint new credentials signed by the underlying wallet until a fresh Org Cert is enrolled.
- **SC-007**: A deployment that swaps the default trust provider for a custom externally-rooted provider continues to issue and verify credentials without any change to the credential format or the rest of the HAIP spec set, confirmed by an end-to-end swap test using a mock external provider.
- **SC-008**: Sorcha-internal credential verification (DID-based path) continues to work unchanged for credentials that do not carry `x5c`, and falls back gracefully for credentials that do carry `x5c` but are presented to an internal verifier.
- **SC-009**: A credential whose `x5c` chain validates is the same credential that the DID-based path would also have accepted, confirmed by a parallel-verification regression test that runs both paths over every credential in a corpus.
- **SC-010**: No acceptance scenario from specs 039, 093, 094, or 095 regresses after this spec ships.

## Out of Scope

The following are explicitly deferred to later specs or later phases:

- Tenant-level and register-level certificate scopes. This spec covers org-level only. A future spec may add tenant-scoped certs (representing the whole Sorcha deployment as an identity) and register-scoped certs (representing a specific Sorcha register as an identity).
- Three-level certificate chains (Tenant Root → Org → Per-Wallet or Per-Credential). Default is two-level.
- OCSP revocation. CRLs only in this spec.
- Delta CRLs. CRLs grow without delta support; deployments that need delta CRLs can configure shorter refresh intervals.
- Tenant Root CA rotation ceremonies. Out of scope; operational concern.
- Cross-signing with publicly-accepted CAs for trust list enrolment (EUDI Wallet trust list, HMG DVS acceptance). This is a deployment operations concern; this spec provides the pluggable interface, not the enrolment ceremony itself.
- Multi-provider tenants (one tenant issuing credentials under two different trust anchors simultaneously). Future extension.
- Trust lists and "known good" Tenant Root directories. A HAIP verifier's trust store is configured per deployment; a shared Sorcha-wide trust list is a future operational concern.
- Certificate Transparency log submission. Future extension if needed for public trust interop.
- PQC-signed CA certificates. HAIP 1.0 is classical-only; this spec tracks HAIP 1.0.

## Assumptions

- The Tenant Root CA is a new first-class domain entity with its own key storage and lifecycle, parallel to the wallet domain rather than embedded in it (Q4.1 Option B ruled by the user). Internally generated CA keys MAY be derived from a tenant CA recovery seed under purpose `sorcha:tenant-ca-signing` for deterministic recovery; externally imported CA keys carry no derivation history. The long-term intent is to support importing signing keys from external sources (HSMs, eIDAS QTSPs, existing PKI), which motivates the separate-storage choice.
- The existing `signingMode: KmsResident` model in spec 094 is sufficient to secure the Tenant Root CA key without requiring new KMS-specific plumbing.
- A suitable .NET X.509 library is available (`System.Security.Cryptography.X509Certificates` is part of the BCL and covers certificate issuance, CRL generation, and chain validation for classical algorithms).
- HAIP 1.0's use of `x5c` as the carrier for the issuer chain is stable and will not change between HAIP 1.0 and HAIP 1.1.
- RFC 5280 CRL semantics (including CRL Distribution Points extension, nextUpdate, revocation reasons) are stable and directly usable.
- The tenant CRL will remain small enough in practical deployments that ordinary (non-delta) CRLs are viable for at least the first several years of operation.
- The `did:sorcha:org:{walletAddress}` identifier format is stable and suitable for embedding in an X.509 SAN URI field.
- The classical HAIP issuer co-key derived under `sorcha:haip-issuer-signing` is the correct binding target for the Org Cert. If spec 094's derivation is later revised, this spec's Org Cert issuance path must be revised in lockstep.
- A mock external trust provider implementation is sufficient for User Story 5's test — the interface need not be stress-tested against a real eIDAS QTSP in this spec.

## Clarifications

These architectural questions arose during drafting and have been resolved by user ruling. Retained here for traceability.

### Q4.1 — CA signing key in the wallet domain: new purpose, new entity, or existing HAIP issuer co-key?

**Ruling: Option B (new first-class Tenant CA entity).** The Tenant Root CA is a domain entity separate from the wallet HD hierarchy, with its own key storage and lifecycle. User rationale: the end goal is to support importing signing keys from external sources (HSMs, eIDAS QTSPs, existing PKI), which requires storage independent of the wallet-derivation model. When a deployment generates the CA key internally, it can still be derived under purpose `sorcha:tenant-ca-signing` from a tenant CA recovery seed for deterministic recovery — so recoverability is preserved without forcing the CA key into the wallet HD hierarchy. Reflected in FR-002 and in Assumptions.

### Q4.2 — Extended Key Usage OID set for HAIP credential issuer Org Certs

**Ruling: Option D (do not set EKU; defer to HAIP 1.1 or a named partner requirement).** The Org Cert does not set an EKU extension in this spec. HAIP 1.0 does not name a dedicated EKU OID for SD-JWT VC credential issuance and most HAIP verifiers do not enforce EKU. When HAIP 1.1 names an OID, or when a deployment partner such as GOV.UK Wallet or EUDI Wallet requires a specific EKU, a follow-up operational spec will add a configurable EKU set per trust provider. Reflected in FR-012.

## Dependencies

- **Depends on spec 094** (`sdjwt-haip-hardening`) — hard dependency. The Org Cert's Subject Public Key Info is the classical HAIP issuer co-key derived there. Without 094, there is no classical key to bind.
- **Independent of spec 095** (`ietf-token-status-list`) — 095 and 096 can run in parallel branches.
- **Required by spec 097** (`openid4vci-issuer`) — the HAIP issuer endpoint cannot emit credentials acceptable to HAIP wallets without a valid `x5c` chain.
- **Required by spec 098** (`openid4vp-verifier`) — the HAIP verifier endpoint needs `x5c` chain validation to accept external HAIP wallet presentations.
- **Independent of spec 093** (`vc-security-fixes`) — 093 is a prerequisite for the HAIP spec set as a whole but 096 does not specifically depend on 093's content beyond "the verifier works".

## Amendment note on earlier specs

This spec does not supersede any earlier spec. It amends:

- `specs/039-verifiable-presentations` FR-019 (DID resolution) — X.509 chain validation is added as a parallel trust path for HAIP-facing credentials. DID resolution continues to be the only trust path for Sorcha-internal credentials.
- No requirement from spec 039 is retired by this spec.

All existing trust model behaviour for internal credentials is unchanged.
