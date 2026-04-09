# Phase 1 Data Model: X.509 Organisation Trust Integration

**Feature**: 096-x509-org-trust

## Entities

### 1. `TenantRootCa` (new, first-class domain entity per Q4.1 Option B)

Represents the Tenant Root CA for a Sorcha tenant. One per tenant.

**Fields:**
- `TenantId` (PK) — the tenant identifier
- `Subject` — X.500 distinguished name (CN = tenant name, O = Sorcha)
- `SerialNumber` — base16
- `NotBefore`, `NotAfter` — validity period (default 10 years)
- `Algorithm` — classical only (Ed25519, ES256, RS256)
- `PublicKeyDer` — DER-encoded SubjectPublicKeyInfo
- `CertDer` — DER-encoded certificate
- `PrivateKeyProtectionMode` — `Local` | `KmsResident` | `ExternallyImported`
- `PrivateKeyRef` — for `Local`/`KmsResident`, the reference into the key-protection provider; for `ExternallyImported`, null
- `DerivationSeedIndex` — optional, non-null when the key was internally generated from a tenant CA recovery seed under `sorcha:tenant-ca-signing`
- `CreatedAt`, `UpdatedAt`

**Semantics:**
- Not part of the wallet HD hierarchy (Q4.1 Option B)
- Storage reuses the wallet KMS integration via a thin `ICaKeyProtection` adapter
- Tenant can swap `PrivateKeyProtectionMode` at provisioning time but cannot change it after the root is generated (rotation is out of scope)

### 2. `OrgCertEnrolment` (new)

Represents a per-organisation cert issued by the Tenant Root CA.

**Fields:**
- `Id` (PK) — surrogate
- `TenantId` — foreign key to tenant
- `OrgWalletAddress` — the Sorcha org wallet whose classical HAIP issuer signing key is bound
- `SerialNumber` — unique per Tenant Root CA
- `Subject` — CN = org display name, O = tenant name
- `SubjectAltName` — URI = `did:sorcha:org:{orgWalletAddress}`
- `Algorithm` — classical, matches the org wallet's HAIP issuer signing key (ES256 default)
- `PublicKeyDer` — matches the org wallet's `HaipIssuerCoKey.PublicKey`
- `CertDer` — DER-encoded certificate
- `NotBefore`, `NotAfter` — validity period (default 2 years)
- `RevokedAt` — null when active, set when revoked
- `RevocationReason` — RFC 5280 reason code, null when active
- `CrlDistributionPoint` — URL of the tenant CRL
- `CreatedAt`

**Semantics:**
- Bound to the existing classical HAIP issuer signing key — the key is not generated at enrolment time
- Attempting to enrol for a wallet that lacks the `HaipIssuer` capability fails with a clear prerequisite error pointing at spec 094
- Revocation is permanent; a new enrolment generates a new cert with a new serial number
- After revocation, the Wallet Service refuses HAIP-path issuance from the underlying wallet until a fresh Org Cert is enrolled

### 3. `TenantCrl` (new)

Represents the current CRL state for a tenant.

**Fields:**
- `TenantId` (PK)
- `ThisUpdate` — CRL generation time
- `NextUpdate` — scheduled refresh time (24 hours default)
- `CrlDer` — DER-encoded CRL signed by the Tenant Root CA
- `RevokedSerialNumbers` — list of (serial, revokedAt, reason) tuples, mirrors the CRL content for querying
- `Version` — monotonic counter

**Semantics:**
- Regenerated on every revocation and on the scheduled refresh interval
- Serves via the `/api/v1/trust/tenants/{tenantId}/crl` endpoint with `Cache-Control: public, max-age=3600`
- Tenant Root CA key signs each version

### 4. `ITrustProvider` (new pluggable interface)

Contract for swapping between internal CA and externally-rooted trust providers.

**Methods:**
- `ProvisionTrustAnchor(tenantId, options, ct)` → `TenantRootCa`
- `IssueOrgCert(tenantId, orgWalletAddress, options, ct)` → `OrgCertEnrolment`
- `RevokeOrgCert(tenantId, serialNumber, reason, ct)` → `TenantCrl`
- `PublishCrl(tenantId, ct)` → current `TenantCrl` (may regenerate)
- `FetchTrustAnchor(tenantId, ct)` → `TenantRootCa`

**Default implementation**: `InternalCaTrustProvider` — generates a self-signed root using BCL primitives. Custom implementations can swap in externally-rooted chains.

### 5. `Wallet` capability check (no field change)

No change to `Wallet` entity itself — the `HaipIssuer` flag is defined by spec 094. Spec 096 reads this flag as a precondition at enrolment time.

## Validation rules

- Tenant Root CA validity period must be ≥ 1 year and ≤ 20 years
- Org Cert validity period must be ≤ Tenant Root CA validity period remaining
- Org Cert's public key MUST match the wallet's `HaipIssuerCoKey.PublicKey` at enrolment time
- Revocation requires the caller to have tenant-admin scope (enforced by JWT claim check)
- CRL serial number list MUST be ordered by serial value per RFC 5280

## Migration notes

New EF migration for `TenantRootCa`, `OrgCertEnrolment`, `TenantCrl` tables. Folded into the pre-release consolidated migration per user guidance (`feedback_migration_squash` memory note). No runtime migration needed since nothing exists in production for these tables yet.

## On-wire format impacts

- **HAIP-path SD-JWT VC outer JWS header**: gains an `x5c` array with Org Cert (leaf) and Tenant Root CA (root) as DER-encoded base64 entries
- **Internal-path SD-JWT VC**: unchanged — no `x5c`, DID-based trust only
- **New public endpoints**: trust anchor publication, CRL publication, enrolment/revocation admin endpoints
