# Implementation Plan: EUDI Credential Format & Unified Trust

**Branch**: `135-eudi-credential-format-trust` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/135-eudi-credential-format-trust/spec.md`

## Summary

Add EUDI wallet interop in one feature with three coupled capabilities: (1) a **credential-format seam** (`ICredentialFormatHandler`) with two implementations — the existing SD-JWT VC and a new `mso_mdoc` built on BCL `System.Formats.Cbor` + `System.Security.Cryptography.Cose`; (2) a **unified trust model** (`ITrustEvaluator` + `ITrustResolverRegistry`) consulted by **both** the mature HAIP verifier and the naive engine verifier — replacing the flat `AcceptedIssuers` list and the ad-hoc `_trustedRoots`, closing the engine verifier's `SignatureValid=false` correctness gap, unifying the W3C + IETF status checkers, and producing pinnable `TrustEvidence`; and (3) a **selectable issuer trust-anchor** (`format` + `trustAnchor` on `CredentialIssuanceConfig`) that teaches `HaipCredentialMinter` to emit `mso_mdoc` and attach the right x5c chain. Prerelease clean break: old shapes are deleted, not shimmed.

The dominant architectural constraint: the engine verifier (`Sorcha.Blueprint.Engine`) is **WASM-friendly and HttpClient-free** so it runs in Blazor. The trust evaluator therefore lives in the engine library, with all network-bound trust sources (CRL fetch, trust-list load, DID resolution, status-list fetch) injected behind interfaces that have in-memory / no-op variants — mirroring the existing `IRevocationChecker` pattern that already makes offline verification bundles work.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: `System.Formats.Cbor` 10.0.0, `System.Security.Cryptography.Cose` **10.0.8** (10.0.0 carries advisory NU1903/GHSA-qvhc-9v3j-5rfw — pin patched); existing `Sorcha.Cryptography.SdJwt`, `Sorcha.ServiceClients.Http` (`Did/`, `Trust/`), `JsonSchema.Net`. Tests: xUnit + FluentAssertions + Moq under Microsoft.Testing.Platform.
**Storage**: No new primary store. Tenant X.509 trust + CRL already in PostgreSQL (spec 096). External trust-list snapshot persists as an operator-supplied versioned artifact (config/file) cached in memory; DID/status caches use `IMemoryCache` with rotation-driven invalidation (Feature 086 pattern).
**Testing**: xUnit + FluentAssertions + Moq; coverage target >85% (SC-008). Round-trip + known-answer vectors for CBOR/COSE; cross-path parity tests (engine vs HAIP same decision).
**Target Platform**: Linux containers via .NET Aspire. The engine credential/trust library MUST also run in Blazor WASM (no `HttpClient`, no platform APIs in the engine path).
**Project Type**: Web — single .NET solution, multiple service + library projects.
**Performance Goals**: Trust evaluation is O(policy sources); verification overhead bounded by at most one signature verify + the configured status/CRL/trust-source lookups (cacheable). No new hot path beyond existing verification.
**Constraints**: FailClosed default (FR-013); engine path WASM-friendly & offline-pinnable (FR-015); BCL-only crypto for mdoc, no third-party CBOR/COSE (SC-009); no PQC regression — mdoc is ES256/P-256 only and additive (FR-006).
**Scale/Scope**: Cross-cutting. Touches `Sorcha.Blueprint.Models`, `Sorcha.Blueprint.Engine`, `Sorcha.Cryptography`, `Sorcha.ServiceClients.Http`, `Sorcha.Haip.Service`, `Sorcha.Wallet.Service`, `Sorcha.Tenant.Service`, and their test projects.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle (constitution v1.3) | Assessment | Status |
|---|---|---|
| **§2 Security First / Zero Trust** | FailClosed default for every trust decision (FR-013); real signature verification on all paths (FR-008); trust resolution pinnable + auditable (FR-014/015). Strengthens posture. | ✅ Pass |
| **§2 Cryptographic Standards** | BCL crypto only for mdoc; ES256/P-256 added without removing PQC options (FR-006); no mnemonics; no new key storage. | ✅ Pass |
| **§3 Testing >80% (we target >85%)** | SC-008 sets ≥85%; KAT vectors + cross-path parity + format round-trip planned. | ✅ Pass |
| **§3 API Docs (Scalar, no Swagger; WithSummary/WithDescription)** | FR-023 mandates summary+description on every new endpoint; OpenAPI via built-in .NET 10 + Scalar. | ✅ Pass |
| **§1 Service Communication — internal MUST be gRPC** | New cross-service trust/cert lookups reuse the **existing REST `Sorcha.ServiceClients.Http`** clients (`IOrgCertChainProvider`/`TrustServiceClient`, DID resolvers). | ⚠️ Deviation — see Complexity Tracking |
| **§5/§7 DDD boundaries** | Format seam + trust evaluator are domain libraries in `Blueprint.Engine`/`Cryptography`; service layers only wire network-bound sources. | ✅ Pass |
| **§8 Observability** | FR-024: meters + structured logs on every trust decision (outcome, source, format, assurance), no subject data. New `Sorcha.Trust` meter. | ✅ Pass |
| **§3 Blueprint as JSON** | Trust policy + format/anchor are JSON fields on requirement/issuance config; no Fluent-only surface. | ✅ Pass |
| **§10 License headers** | All new files carry the SPDX/Copyright header. | ✅ Pass |

**Gate result: PASS** (one documented, pre-existing-pattern deviation tracked below).

## Project Structure

### Documentation (this feature)

```text
specs/135-eudi-credential-format-trust/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── trust-policy.schema.md      # TrustPolicy JSON on CredentialRequirement
│   ├── issuance-config.schema.md   # format + trustAnchor on CredentialIssuanceConfig
│   ├── trust-evaluator.contract.md # ITrustEvaluator / ITrustResolverRegistry service contract
│   ├── mdoc-presentation.openapi.md# OpenID4VP mso_mdoc acceptance (HAIP)
│   └── trustlist-admin.openapi.md  # Trust-list snapshot management (Tenant)
└── tasks.md             # /speckit.tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Blueprint.Models/Credentials/
├── CredentialFormat.cs            # NEW enum: SdJwtVc | MsoMdoc
├── TrustPolicy.cs                 # NEW: sources + combinator + minAssuranceLevel
├── TrustSourceRef.cs              # NEW: source kind + config (register|x509-tenant|trustlist|did-allowlist)
├── AssuranceLevel.cs              # NEW enum: Low | Substantial | High
├── CredentialRequirement.cs       # CHANGED: remove AcceptedIssuers; add Format, TrustPolicy
└── CredentialIssuanceConfig.cs    # CHANGED: add Format, TrustAnchor

src/Common/Sorcha.Cryptography/Mdoc/                    # NEW namespace, parallels SdJwt/
├── IMdocService.cs                # issue / present / verify primitives
├── MdocService.cs
├── Cbor/                          # tag-24 helpers, deterministic encoding
├── Cose/CoseX5Chain.cs            # x5chain (label 33) encode/decode helper (no BCL constant)
├── MobileSecurityObject.cs        # MSO + ValueDigests + DeviceKeyInfo + ValidityInfo
├── IssuerSigned.cs                # IssuerSignedItem(Bytes), IssuerNameSpaces
├── DeviceResponse.cs              # Document, DeviceSigned, DeviceAuth
└── SessionTranscript.cs           # OpenID4VPHandover / OpenID4VPDCAPIHandover

src/Core/Sorcha.Blueprint.Engine/Credentials/
├── ICredentialFormatHandler.cs    # NEW seam: Issue/Present/Verify per format
├── SdJwtVcFormatHandler.cs        # NEW: wraps existing SdJwt path
├── MdocFormatHandler.cs           # NEW: wraps Mdoc service
├── ITrustEvaluator.cs / TrustEvaluator.cs   # NEW: the single evaluator
├── ITrustResolverRegistry.cs      # NEW: mirrors IDidResolverRegistry
├── ITrustSourceResolver.cs        # NEW: one per source kind
├── TrustDecision.cs / TrustEvidence.cs       # NEW result + audit record
├── IStatusListChecker.cs          # NEW unified status seam (W3C + IETF behind one)
├── CredentialVerifier.cs          # CHANGED: delegate trust to ITrustEvaluator; real signature verify
└── BitstringStatusListChecker.cs  # CHANGED: implement IStatusListChecker

src/Common/Sorcha.ServiceClients.Http/Trust/
├── ITrustListProvider.cs          # NEW seam (operator snapshot shipped; live LOTL future)
├── OperatorSnapshotTrustListProvider.cs      # NEW
└── (reuse) IOrgCertChainProvider / TrustServiceClient

src/Services/Sorcha.Haip.Service/
├── Services/HaipPresentationVerifier.cs      # CHANGED: route through ITrustEvaluator; drop _trustedRoots
├── Services/IetfTokenStatusListChecker.cs    # CHANGED: implement IStatusListChecker
├── Services/HaipCredentialMinter.cs          # CHANGED: emit mso_mdoc; accept + thread x5cChain
├── Services/MdocPresentationVerifier.cs      # NEW: DeviceResponse + SessionTranscript verify
├── Endpoints/CredentialEndpoints.cs          # CHANGED: resolve x5c at IssueCredential call site
└── Program.cs                                 # CHANGED: register IOrgCertChainProvider, format handlers

src/Services/Sorcha.Wallet.Service/Credentials/
├── IssuerEquivalenceMatcher.cs               # (reuse) wired into register trust source
└── IssueCredentialChainResolver.cs           # (reuse / promote to shared) fail-soft x5c resolve

src/Services/Sorcha.Tenant.Service/Trust/
├── (reuse) ITrustProvider feeds x509-tenant source
└── TrustListSnapshotStore + admin endpoint    # NEW: operator snapshot upload/version

tests/  (mirrors above: Cryptography.Tests/Mdoc, Blueprint.Engine.Tests/Credentials,
         Haip.Service.Tests, Wallet.Service.Tests, Tenant.Service.Tests)
```

**Structure Decision**: Single .NET solution, existing microservice + shared-library layout. The trust evaluator and format seam are placed in the **domain libraries** (`Sorcha.Blueprint.Engine`, `Sorcha.Cryptography`) so both the engine and HAIP verification paths share one implementation and the engine stays WASM-runnable; network-bound trust sources are injected from the service layer behind WASM-safe interfaces.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Cross-service trust/cert lookups over **REST** (`Sorcha.ServiceClients.Http`) rather than gRPC (constitution §1) | The trust sources this feature needs — org cert chain (`IOrgCertChainProvider`/`TrustServiceClient`) and DID resolution — **already exist only as REST service clients** in the shared HTTP client assembly, and are consumed that way platform-wide. | Introducing gRPC contracts solely for these two lookups would fork the existing trust/DID client surface, duplicate wiring, and diverge from every current consumer — adding complexity without changing the security properties (both are authenticated service-to-service calls). Aligning a REST→gRPC migration of the trust surface is out of scope for this feature. |
| New `mso_mdoc` format engine alongside SD-JWT VC (a second full credential codec) | Hard requirement for EUDI interop (FR-001/002/021); mdoc is a fundamentally different wire format (CBOR/COSE vs JOSE) and cannot reuse the SD-JWT codec. | A single codec cannot represent both; the format seam (`ICredentialFormatHandler`) is the minimum abstraction that keeps verifier/issuer call sites format-agnostic. |
