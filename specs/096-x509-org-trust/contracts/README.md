# Phase 1 Contracts: X.509 Organisation Trust Integration

**Feature**: 096-x509-org-trust

## Public HTTP endpoints

### `POST /api/v1/trust/tenants/{tenantId}/provision`

**Auth**: Tenant admin
**Body**:
```json
{
  "algorithm": "Ed25519",
  "validityYears": 10,
  "signingMode": "Local",
  "externalCertDer": null
}
```
**Behaviour**: idempotent — running twice on the same tenant returns the existing root without regenerating
**200 Response**: `TenantRootCa` record (cert in base64 DER, public key, validity)

### `GET /api/v1/trust/tenants/{tenantId}/trust-anchor`

**Auth**: Anonymous (public, cacheable)
**Cache-Control**: `public, max-age=3600`
**200 Response**: Tenant Root CA certificate in DER-encoded base64

### `GET /api/v1/trust/tenants/{tenantId}/crl`

**Auth**: Anonymous (public, cacheable)
**Cache-Control**: `public, max-age=3600`
**Content-Type**: `application/pkix-crl`
**200 Response**: DER-encoded CRL signed by the tenant root

### `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol`

**Auth**: Tenant admin
**Preconditions**: org wallet must carry `HaipIssuer` capability (spec 094)
**Body**:
```json
{
  "displayName": "ACME Ltd",
  "validityYears": 2
}
```
**200 Response**: `OrgCertEnrolment` record with serial, cert DER, validity
**400**: wallet lacks `HaipIssuer` capability — points at spec 094

### `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/revoke`

**Auth**: Tenant admin
**Body**: `{ "reason": "keyCompromise" | "superseded" | "unspecified" }`
**200 Response**: updated `TenantCrl`

## Internal service contracts

### `ITrustProvider` — pluggable interface

```csharp
public interface ITrustProvider
{
    Task<TenantRootCa> ProvisionTrustAnchorAsync(string tenantId, ProvisionOptions options, CancellationToken ct);
    Task<OrgCertEnrolment> IssueOrgCertAsync(string tenantId, string orgWalletAddress, IssueOptions options, CancellationToken ct);
    Task<TenantCrl> RevokeOrgCertAsync(string tenantId, string serialNumber, string reason, CancellationToken ct);
    Task<TenantCrl> PublishCrlAsync(string tenantId, CancellationToken ct);
    Task<TenantRootCa?> FetchTrustAnchorAsync(string tenantId, CancellationToken ct);
}
```

### `ICaKeyProtection` — CA key storage adapter

```csharp
public interface ICaKeyProtection
{
    Task<string> StoreAsync(byte[] privateKeyDer, string tenantId, CancellationToken ct);
    Task<byte[]> RetrieveAsync(string keyRef, CancellationToken ct);
    Task DeleteAsync(string keyRef, CancellationToken ct);
}
```

Two implementations: `LocalCaKeyProtection` (wraps the existing local encryption provider) and `KmsResidentCaKeyProtection` (wraps the existing KMS integration).

### Wallet Service / issuer integration

`CredentialEndpoints.IssueCredential` gains a new branch: when the request indicates HAIP-path issuance (via the new `StatusClaimForm: IetfTokenStatusList` from spec 095 or an explicit `IncludeX5c: true` flag), the handler:
1. Fetches the Org Cert chain via a new `IOrgCertChainProvider.GetChainForAsync(walletAddress, ct)` that calls the Tenant Service's trust endpoints
2. Passes the chain into `ISdJwtService.CreateTokenAsync` as a new optional `x5cChain` parameter
3. `SdJwtService` embeds the chain in the JWS header before signing

### Verifier integration

The verifier (`PresentationRequestService` per spec 093, and the new HAIP verifier per spec 098) gains:
- `ITrustStore` — holds accepted root certs for chain validation
- On presentation verify, if the token's JWS header contains `x5c`, walk the chain against the trust store; fall back to DID-based trust if no chain is present

## Wire format impacts

- **New public endpoints** under `/api/v1/trust/tenants/{tenantId}/...`
- **HAIP-path SD-JWT VC JWS header** gains `x5c` array with DER-encoded base64 certs
- **Internal-path SD-JWT VC JWS header** unchanged (no `x5c`)
- **`CertificateRevocationListDistributionPoints`** extension on Org Certs points at the tenant CRL URL
- **`SubjectAlternativeName`** URI entry on Org Certs carries the `did:sorcha:org:` DID
