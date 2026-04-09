# Phase 0 Research: X.509 Organisation Trust Integration

**Feature**: 096-x509-org-trust
**Date**: 2026-04-09

## Research items

1. BCL capability check — can `System.Security.Cryptography.X509Certificates` build a self-signed root, issue end-entity certs, and generate CRLs without third-party libraries?
2. CA private key storage — which existing Sorcha signingMode abstraction to reuse
3. SAN URI encoding for `did:sorcha:org:` identifiers
4. Org Cert Subject Public Key Info — how to bind the classical HAIP issuer signing key from spec 094
5. CRL publication URL and cache strategy
6. Trust provider switch at runtime — how a deployment selects internal vs external provider
7. Chain walk on the verifier side — where the trust store lives and how verifiers are configured

---

## R1. BCL X.509 capability

### Findings

.NET 10 `System.Security.Cryptography.X509Certificates`:
- `CertificateRequest` builds a certificate signing request with subject, public key, extensions
- `CertificateRequest.CreateSelfSigned` builds a self-signed root
- `CertificateRequest.Create(issuer, signatureGenerator, notBefore, notAfter, serialNumber)` signs an end-entity cert with a root
- `X509Certificate2.CopyWithPrivateKey` associates a private key with a cert for storage
- CRL generation: `CertificateRevocationListBuilder` (added in .NET 7+) produces RFC 5280 CRLs signed by the issuer

All classical algorithms (Ed25519, ECDsa P-256, RSA) are supported. Ed25519 cert signing is supported in .NET 10 via the `EdDsaOpenSsl` integration.

### Decision: use the BCL for everything, no third-party cert library

**Rationale.** BCL coverage is complete for classical algorithms. `BouncyCastle` would add bloat and overlap. The `CertificateRevocationListBuilder` ships CRL generation in-the-box.

**Consequence.** All cert and CRL work lives in new helper classes (`X509CertificateBuilder`, `TenantCrlBuilder`) under `Sorcha.Tenant.Service/Trust/`. No new NuGet dependencies.

---

## R2. CA private key storage

### Findings

The wallet domain has `signingMode: "Local" | "KmsResident"` per spec 094. The KMS integration pattern (Azure Key Vault, AWS KMS) is available for wallet keys. The same abstraction can be reused for CA keys — the key itself is just bytes, and the storage backend doesn't care whether it's a wallet signing key or a CA signing key.

### Decision: introduce a parallel `CaKeyStorage` abstraction that delegates to the same KMS / local providers the wallet layer uses

**Rationale.** Q4.1 Option B (new first-class entity) says CA keys are distinct from wallet keys. But the *storage mechanism* can still reuse the wallet's KMS integration — only the logical ownership and lifecycle differ. The `CaKeyStorage` is a thin adapter that wraps the existing key protection providers with a CA-scoped key identifier naming scheme.

**Consequence.** New `ICaKeyProtection` interface in `Sorcha.Tenant.Service/Trust/`. Two implementations: `LocalCaKeyProtection` (using existing local encryption provider) and `KmsResidentCaKeyProtection` (using existing KMS integration). Tenant configures which one at provisioning time.

**Alternative rejected.** Reusing `IWalletRepository` or `IKeyManagementService` directly. Rejected because it would blur the wallet/CA domain boundary that Q4.1 ruled against.

---

## R3. SAN URI encoding for `did:sorcha:org`

### Findings

RFC 5280 Subject Alternative Name supports a `uniformResourceIdentifier` field carrying any URI. `did:sorcha:org:{walletAddress}` is a valid URI (colon-separated, scheme is `did`).

`CertificateRequest` exposes `X509Extension` builders; `X509SubjectAlternativeNameBuilder.AddUri(Uri)` adds a URI-type SAN entry.

### Decision: use `AddUri(new Uri("did:sorcha:org:..."))`

**Rationale.** Standard, interoperable, already well-supported in the BCL.

**Consequence.** `X509CertificateBuilder.BuildOrgCert(...)` method takes a `did:sorcha:org:` DID string and adds it as a SAN URI alongside the CN.

---

## R4. Org Cert Subject Public Key Info

### Decision: bind the classical HAIP issuer signing key from spec 094

**Rationale.** Per FR-008, the Org Cert's Subject Public Key Info encodes the existing classical HAIP issuer signing key. No new key is generated at enrolment time. The key comes from `IHaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync(walletAddress)` (spec 094).

**Consequence.** The enrolment flow:
1. Verify the target wallet carries the `HaipIssuer` capability (spec 094 precondition)
2. Call `IHaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync(walletAddress)` to get the classical `(privateKey, algorithm, publicJwk)` tuple
3. Build a `CertificateRequest` using the public key from step 2
4. Sign with the Tenant Root CA's private key
5. Persist the resulting cert as an `OrgCertEnrolment` record

**Key rotation note**: if the underlying classical co-key is rotated (spec 094 derives a new key at a new index), the Org Cert must be re-issued. Spec 094 marks rotation as out of scope, so this spec matches that scope — rotation is a follow-up.

---

## R5. CRL publication URL and cache

### Decision: one CRL per tenant at `/api/v1/trust/tenants/{tenantId}/crl`

**Rationale.** Tenant-scoped is the simplest unit. The CRL's CRLDP extension on each Org Cert points at this URL. Cache-Control: `public, max-age=3600` (1 hour — shorter than the 24-hour refresh interval so clients don't serve stale CRLs too long).

Refresh interval is configurable. Default 24 hours. Refresh happens on every revocation AND on a scheduled background task.

**Consequence.** New endpoint handler `GetTenantCrl` in `TrustEndpoints.cs`. Uses the same `CachedResult` wrapper used by the status list endpoint.

---

## R6. Trust provider switch

### Decision: `ITrustProvider` interface resolved from tenant config at service startup

**Rationale.** Per spec FR-031–FR-035. The interface surfaces `ProvisionTrustAnchor`, `IssueOrgCert`, `RevokeOrgCert`, `PublishCrl`, `FetchTrustAnchor`. Default implementation is `InternalCaTrustProvider` (self-signed root). A deployment can register a custom implementation in DI before the default is registered; the registry picks the first registered implementation per tenant.

**Consequence.** `TrustProviderRegistry.GetProviderForTenant(tenantId)` returns the configured provider. Tenants can override by setting `Trust:Provider` in config.

---

## R7. Chain walk on the verifier side

### Decision: the verifier's trust store is deployment configuration

**Rationale.** Per FR-029 the trust store is configurable per deployment. For Sorcha-internal verification, the trust store contains the tenant's own root cert (added at provisioning time). For external verifiers (HAIP wallets), trust store population is out of Sorcha's control.

**Consequence.** A new `ITrustStore` service in the verifier side holds the configured roots. `PresentationRequestService` (or the new HAIP verifier service in spec 098) walks the `x5c` chain against this store during credential verification.

Internal credentials (no `x5c`) continue to use DID-based trust per spec 093/039.

---

## Summary

All seven research items resolved. Key decisions:

1. BCL only — no new cert libraries
2. CA key storage reuses wallet KMS integration via a thin `ICaKeyProtection` adapter
3. `did:sorcha:org:{addr}` goes into a URI-type SAN on the Org Cert
4. Org Cert binds the spec 094 classical HAIP issuer signing key
5. One tenant CRL at `/api/v1/trust/tenants/{tenantId}/crl`, 1-hour cache, 24-hour refresh
6. `ITrustProvider` interface with `InternalCaTrustProvider` default; deployments override in DI
7. Verifier trust store is per-deployment config; `x5c` walks match against it

Ready for Phase 1.
