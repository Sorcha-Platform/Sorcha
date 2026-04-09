# Implementation Plan: X.509 Organisation Trust Integration

**Branch**: `096-x509-org-trust` | **Date**: 2026-04-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/096-x509-org-trust/spec.md`

## Summary

Stand up an X.509 trust anchor stack so Sorcha-issued credentials can be verified by HAIP wallets that expect PKI chains. Per Phase 2 D4 Option B with Q4.1 → Option B and Q4.2 → Option D rulings captured in spec.md:

- **Tenant Root CA** is a new first-class domain entity (not a wallet HD sub-key), with its own key storage. Internally generated CA keys MAY be derived from a tenant CA recovery seed under `sorcha:tenant-ca-signing` for deterministic recovery; externally imported CA keys (HSMs, eIDAS QTSPs) carry no derivation history.
- **Two-level chain**: Tenant Root → Org Cert. Org Cert's Subject Public Key Info encodes the existing classical HAIP issuer signing key (from spec 094). `did:sorcha:org:{walletAddress}` goes into a Subject Alternative Name URI entry.
- **`x5c` chain** on the outer JWS header of HAIP-path SD-JWT VCs, per RFC 7515 §4.1.6.
- **CRLs** only (no OCSP) — tenant CRL signed by the root, 24-hour default refresh, no delta CRLs.
- **Pluggable trust provider** interface so a deployment can swap the default internal CA for an externally-rooted chain without touching the HAIP plumbing.
- **No EKU** on the Org Cert (Q4.2 Option D) — defer to HAIP 1.1 or a named partner requirement.

Depends on spec 094 (classical HAIP issuer co-key). Independent of spec 095. Required by specs 097 and 098.

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `System.Security.Cryptography.X509Certificates` (BCL — full cert issuance, CRL generation, chain validation for classical algorithms), `System.Formats.Asn1` (BCL, for ASN.1 DER serialisation), `Sorcha.Cryptography` for the BIP32 purpose derivation when generating CA keys internally. No new NuGet packages.
**Storage**: PostgreSQL — new `TenantRootCa` table (tenant ID, cert bytes, creation/validity, signing mode) and `OrgCertEnrolment` table (org wallet address, serial number, cert bytes, issued/expires, revoked flag). New EF migration folded into the pre-release consolidated migration per user guidance.
**Testing**: xUnit, FluentAssertions, Moq. Unit tests for cert issuance, chain walk, CRL generation/consumption. Integration tests with round-trip enrol→issue→verify→revoke→re-enrol.
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: Multi-service monorepo. **No new services** (trust provider lives in `Sorcha.Tenant.Service` because tenant-level identity fits the tenant domain). New domain entities under `Sorcha.Tenant.Models`.
**Performance Goals**: Cert issuance < 100 ms P95. Chain validation < 10 ms P95 (classical cryptographic signatures). CRL fetch < 200 ms P95 under cache.
**Constraints**: Internal Sorcha credentials MUST NOT carry `x5c` (FR-020). DID-based trust path for internal credentials MUST remain unchanged (FR-040). CA signing algorithm MUST be classical only (FR-005 — HAIP 1.0 is classical at the trust boundary). Spec 094 must be merged before the Org Cert binding-key step can be implemented.
**Scale/Scope**: New first-class domain entity (Tenant Root CA). ~10 source files, ~5 test files. New HTTP endpoints for trust anchor publication, CRL publication, org enrolment, cert revocation.

## Constitution Check

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. Tenant Root CA lives in the Tenant Service (tenant-scoped identity is a natural fit). No new services. Blueprint, Wallet, Register services unchanged beyond consuming the new chain at issue/verify time. |
| **II. Security First** | PASS — this spec is a trust layer hardening. CA private keys live under the same `signingMode` abstraction (`Local`, `KmsResident`) that wallet keys use. No secrets in source control. Input validation on all enrolment endpoints via FluentValidation. |
| **III. API Documentation** | PASS. New endpoints documented via Scalar. XML doc comments on all new public APIs. |
| **IV. Testing Requirements** | PASS. FR-038 mandates unit + integration coverage. |
| **V. Code Quality** | PASS. Nullable enabled. Standard C# conventions. |
| **VI. Blueprint Creation Standards** | N/A. No blueprint changes. |
| **VII. Domain-Driven Design** | PASS. New domain terms: `TenantRootCa`, `OrgCert`, `TenantCrl`, `TrustProvider`, `EnrolmentRecord`. Distinct from wallet domain concepts per Q4.1 ruling. |
| **VIII. Observability by Default** | PASS. Structured log events on cert issuance, CRL generation, chain validation failures, trust provider switching. |

**Constitution gate: PASS.**

## Project Structure

```text
specs/096-x509-org-trust/
├── spec.md              # (complete, Q4.1+Q4.2 resolved)
├── plan.md              # This file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md

src/
├── Common/
│   └── Sorcha.Tenant.Models/
│       ├── Trust/
│       │   ├── TenantRootCa.cs                    # NEW — domain entity
│       │   ├── OrgCertEnrolment.cs                # NEW — per-org cert record
│       │   ├── TenantCrl.cs                       # NEW — CRL state/version
│       │   └── TrustProviderMode.cs               # NEW — enum (InternalCa | External)
│       └── Credentials/
│           └── (unchanged)
└── Services/
    └── Sorcha.Tenant.Service/
        ├── Trust/
        │   ├── ITrustProvider.cs                  # NEW — pluggable interface
        │   ├── InternalCaTrustProvider.cs         # NEW — default implementation
        │   ├── X509CertificateBuilder.cs          # NEW — builds Org Certs bound to the classical signing key
        │   ├── TenantCrlBuilder.cs                # NEW — generates signed CRLs
        │   └── TrustProviderRegistry.cs           # NEW — resolves provider per tenant
        ├── Endpoints/
        │   └── TrustEndpoints.cs                  # NEW — provisioning, enrolment, revocation, trust anchor GET, CRL GET
        └── Services/
            ├── Interfaces/
            │   └── ITrustAnchorService.cs         # NEW — tenant-facing facade
            └── Implementation/
                └── TrustAnchorService.cs          # NEW

src/Services/Sorcha.Wallet.Service/
└── Endpoints/
    └── CredentialEndpoints.cs                     # CHANGE — when HAIP-path issuance, fetch Org Cert chain and embed in x5c JWS header

src/Services/Sorcha.Blueprint.Service/  (or Haip service when spec 097 lands)
└── (presentation verifier extended to walk x5c chains — may land here or in Wallet Service verifier)

tests/
├── Sorcha.Tenant.Service.Tests/
│   └── Trust/
│       ├── X509CertificateBuilderTests.cs         # NEW
│       ├── TenantCrlBuilderTests.cs               # NEW
│       ├── InternalCaTrustProviderTests.cs        # NEW
│       └── TrustAnchorServiceTests.cs             # NEW
└── Sorcha.Tenant.Service.IntegrationTests/
    └── TrustRoundTripTests.cs                     # NEW — provision → enrol → issue → verify → revoke → re-enrol
```

**Structure Decision**: Existing monorepo. CA stack lives in `Sorcha.Tenant.Service` (not Wallet — the CA is not a wallet key per Q4.1 Option B). New `Trust/` namespace inside the Tenant Service holds the provider interface, default implementation, cert/CRL builders, and registry. Domain entities under `Sorcha.Tenant.Models/Trust/`. HTTP endpoints exposed via a new `TrustEndpoints.cs`.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
