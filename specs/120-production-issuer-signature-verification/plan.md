# Implementation Plan: Production Issuer Signature Verification

**Branch**: `120-production-issuer-signature-verification` | **Date**: 2026-05-09 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/120-production-issuer-signature-verification/spec.md`
**Authoritative design**: `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md` — source of truth for every locked decision (D1–D6), file paths, class names, phase boundaries.

## Summary

Replace the OptOut/JwkRegistry credential-issuer key resolution surface with a production DID-resolver-backed implementation. Publish every Sorcha-hosted org as both `did:sorcha:org:{addr}` (primary) and `did:web:{platform}:orgs:{orgId}` (federation) linked via `alsoKnownAs` with key-material cross-verification. Retire the legacy parallel `IDIDResolver` interface as a precursor PR. Reserve forward-compat slots on the genesis control record for "Future B" (validator-side at seal time, deferred). Default-on at ship leveraging Sorcha's pre-production posture; walkthrough-suite-green-with-enforce-on is the ship gate.

The architectural call is "verifier-side enforcement now, validator-side deferred." Same resolver code lifts into the validator unchanged when Future B is triggered — the spec slots reserved at v1 are exactly the seam.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: `Sorcha.ServiceClients.Http` (existing W3C DID resolver stack: `SorchaDidResolver`, `WebDidResolver`, `KeyDidResolver`, `DidResolverRegistry`); `Sorcha.Cryptography` (RFC 7638 thumbprint computation, multibase encoding); `Sorcha.Tenant.Service` (Organization model, governance ops); `Sorcha.Wallet.Service` (Feature 083 key derivation, slot 1 `KeyUsage.VCIssuance`); `Sorcha.Citizen.Verifier` (`VerifiablePresentationValidator`, `IIssuerKeyResolver`); `Sorcha.Register.Models` (`RegisterControlRecord`, `RegisterPolicy` for forward-compat slot reservation); `Sorcha.Events` (`IEventSubscriber` for `transaction:confirmed` Redis stream cache invalidation).
**Storage**: PostgreSQL (Tenant Service — Organization model extension for `KidStyle`, generated DID documents persisted as cached column on Organization or new `OrgDidDocument` row), Redis (resolver cache; `transaction:confirmed` stream subscription for invalidation), no MongoDB changes.
**Testing**: xUnit + FluentAssertions + Moq for unit/integration; Playwright E2E for walkthrough-level. Walkthrough suite is the production-readiness ship gate per FR-019 / SC-003.
**Target Platform**: Linux containers (Docker), Windows dev (PowerShell). Aspire orchestration unchanged.
**Project Type**: web (microservices) — Tenant Service, Wallet Service, Citizen Verifier all touched; ServiceClients.Http carries the new resolver method.
**Performance Goals**: Verifier latency for repeat-issuer credentials within one cache window ≤ pre-feature baseline + 0 (hot cache); first-resolution per issuer ≤ 500ms p95 (one `did:sorcha:*` lookup + one `did:web` HTTPS GET, both cacheable). DID document JSON ≤ 16KB per org including dual VMs across 1–3 active keys.
**Constraints**: Pre-production posture (no in-flight credentials to nurse, default-on at ship is safe per Assumptions in spec); walkthrough-suite-green is the gate (FR-019 / SC-003); legacy `IDIDResolver` retirement (FR-024) MUST ship before this feature's main body to avoid two parallel resolvers in production code; no schema migration on existing register policy records (SC-007).
**Scale/Scope**: ~25 functional requirements across 7 categories; 6 user stories (2 P1, 2 P2, 2 P3); 7 phase milestones (Phase 0 cleanup standalone + Phases 1–6 sequential); estimated 2-3 weeks for a single engineer with codebase familiarity. Touches 5 services, ~12 new files, ~8 modified files, 3 deletions.

## Constitution Check

*GATE: Pre-Phase-0 — must pass before research kicks off.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Microservices-First Architecture | ✅ PASS | Independent deployability preserved. New code lands in existing services (Tenant, Wallet, Citizen Verifier) and the shared `Sorcha.ServiceClients.Http` library. No new services. No upward dependencies introduced — `IIssuerKeyResolver` lives in the verifier app and consumes the `Sorcha.ServiceClients.Did` registry; no Core→Application leakage. |
| II | Security First | ✅ PASS | Whole feature **is** a security upgrade. Issuance keys remain custodial-mode (Feature 083); revocation gated by admin-quorum governance op (FR-016). Cross-resolution (FR-008) closes the impersonation gap that pure federation would otherwise open. SSRF-protected `WebDidResolver` already in place. No secrets committed. Input validation via FluentValidation on the org DID document publishing endpoint and on governance op payloads. |
| III | API Documentation | ✅ PASS | New `GET /orgs/{orgId}/did.json` endpoint (Tenant Service) gets `.WithName`/`.WithSummary`/`.WithDescription`. New service-internal contracts on `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` documented via XML comments. OpenAPI exposed unchanged. |
| IV | Testing Requirements | ✅ PASS | Target ≥85% coverage on new code per project default. Unit tests for `DidResolverBackedIssuerKeyResolver`, `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` (cross-resolution happy path + 4 failure modes), `IOrgDidDocumentService` (regeneration on key events), `IIssuanceKeyService` (lazy derivation idempotency, rotation, revocation). Integration tests use the existing reflection-based static-handler invocation pattern (per `PresentationEndpointTests`). Walkthrough suite is the integration-level gate. |
| V | Code Quality | ✅ PASS | C# 14 / .NET 10 conventions; nullable reference types enabled; async/await throughout; DI for all new services; no compiler warnings. License header on every new file. |
| VI | Blueprint Creation Standards | ✅ PASS | No blueprint format changes. `CredentialRequirement.AcceptedIssuers` already exists; this feature adds equivalence-aware matching at consumption time, not at authoring time. JSON-e usage unaffected. |
| VII | Domain-Driven Design | ✅ PASS | Ubiquitous language preserved: "issuer", "credential", "verification method" all map to existing W3C terms used in the codebase. New entity `OrgDidDocument` follows the same naming convention as existing entities. No "user/workflow/step/visibility/deploy" drift. |
| VIII | Observability by Default | ✅ PASS | Three new OTel meters defined: `Sorcha.Verifier.IssuerSignature` (three failure-mode counters per FR-003 / SC-006), `Sorcha.Did.Resolver` (cache hit/miss + cross-resolve mismatch counters), and a new span `verifier.issuer-resolve` parented to `verifier.presentation`. Structured logging only (no string interpolation). Health checks unchanged — no new external dependency to probe at startup. |

**Result**: All eight principles pass. No `## Complexity Tracking` entries needed.

## Project Structure

### Documentation (this feature)

```text
specs/120-production-issuer-signature-verification/
├── plan.md              # This file
├── spec.md              # Feature specification (already written)
├── research.md          # Phase 0 output — alternatives considered + decisions
├── data-model.md        # Phase 1 output — entity definitions
├── quickstart.md        # Phase 1 output — operator runbook
├── contracts/
│   ├── org-did-document-endpoint.openapi.yaml   # Public did.json endpoint
│   └── did-resolver-registry-contract.md        # IDidResolverRegistry interface contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (already written)
└── tasks.md             # Phase 2 output (/speckit.tasks command — not created here)
```

### Source code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.ServiceClients.Http/Did/
│   │   ├── IDidResolverRegistry.cs                    # MODIFIED: + ResolveWithAlsoKnownAsAsync
│   │   ├── DidResolverRegistry.cs                     # MODIFIED: cross-resolution + cache
│   │   ├── SorchaDidResolver.cs                       # MODIFIED: dual-VM, alsoKnownAs, issuance VM
│   │   ├── WebDidResolver.cs                          # UNCHANGED
│   │   ├── KeyDidResolver.cs                          # UNCHANGED
│   │   ├── KidThumbprintHelper.cs                     # NEW: RFC 7638 thumbprint + JWK→VM matching
│   │   └── DidResolverCache.cs                        # NEW: per-method TTL + Redis-stream invalidation
│   └── Sorcha.Register.Models/
│       └── RegisterControlRecord.cs                   # MODIFIED: + RegisterPolicy slots (FR-020, FR-021)
│
├── Services/
│   ├── Sorcha.Tenant.Service/
│   │   ├── Models/
│   │   │   ├── Organization.cs                        # MODIFIED: + DefaultKidStyle slot (FR-013)
│   │   │   └── OrgDidDocument.cs                      # NEW: persisted DID document cache
│   │   ├── Services/
│   │   │   ├── IOrgDidDocumentService.cs              # NEW: Phase 1
│   │   │   └── OrgDidDocumentService.cs               # NEW: builds, stores, serves DID documents
│   │   └── Endpoints/
│   │       └── OrgDidDocumentEndpoints.cs             # NEW: GET /orgs/{orgId}/did.json (anonymous, CDN-cacheable)
│   │
│   └── Sorcha.Wallet.Service/
│       ├── Services/
│       │   ├── Interfaces/IIssuanceKeyService.cs      # NEW: Phase 2
│       │   └── Implementation/IssuanceKeyService.cs   # NEW: lazy derive, rotate, revoke
│       └── Credentials/
│           └── CredentialMatcher.cs                   # MODIFIED: alsoKnownAs-equivalent issuer match
│
├── Apps/
│   └── Sorcha.Citizen.Verifier/
│       └── Services/
│           ├── IIssuerKeyResolver.cs                  # MODIFIED: replace OptOut as default
│           └── DidResolverBackedIssuerKeyResolver.cs  # NEW: production impl, Phase 4
│
└── Core/
    └── Sorcha.Register.Core/
        └── Services/
            ├── IDIDResolver.cs                        # DELETED (Phase 0)
            └── DIDResolver.cs                         # DELETED (Phase 0)

tests/
├── Sorcha.ServiceClients.Tests/Did/
│   ├── DidResolverRegistryCrossResolutionTests.cs    # NEW
│   ├── KidThumbprintHelperTests.cs                   # NEW
│   └── DidResolverCacheTests.cs                      # NEW
├── Sorcha.Tenant.Service.Tests/
│   ├── OrgDidDocumentServiceTests.cs                 # NEW
│   └── OrgDidDocumentEndpointsTests.cs               # NEW
├── Sorcha.Wallet.Service.Tests/
│   ├── IssuanceKeyServiceTests.cs                    # NEW
│   └── Credentials/CredentialMatcherAlsoKnownAsTests.cs  # NEW
├── Sorcha.Citizen.Verifier.Tests/
│   └── DidResolverBackedIssuerKeyResolverTests.cs    # NEW
└── (walkthroughs/)
    └── AssuredIdentity, TradeFinance, ConstructionPermit, SelfBuildHouse — green with enforce-on
```

**Structure decision**: Standard Sorcha microservices layout. New shared logic lives in `Sorcha.ServiceClients.Http` (where the existing DID resolver stack already lives). Service-specific logic stays inside its owning service. The verifier app adds the production `IIssuerKeyResolver` implementation; the existing `JwkRegistryIssuerKeyResolver` is retained for tests + the `DemoMintEndpoint` test fixture (open question Q5 in design doc — confirmed in research.md as "keep registry as test escape").

## Phasing (per design doc)

Each phase is independently testable and ships its own atomic commit set per Sorcha conventions. Phase 0 ships first as a standalone PR; Phases 1–6 are a sequential chain on this branch.

| Phase | Scope | Cost | Atomic? |
|-------|-------|------|---------|
| **Phase 0** | Retire legacy `IDIDResolver` (FR-024). Migrate `Sorcha.Register.Service/Program.cs:205` consumer to `IDidResolverRegistry`. Delete `IDIDResolver`, `DIDResolver`, `DIDResolutionResult`. Update `specs/031-register-governance/` and `specs/039-verifiable-presentations/` references. **Ships as a separate PR before the rest of this feature.** | 1-2h | Yes — independent |
| **Phase 1** | DID document publishing — `IOrgDidDocumentService` + endpoint + `OrgDidDocument` model. Generate both forms (`did:sorcha:org`, `did:web`) with `alsoKnownAs`. Dual-VM publishing template. Regeneration triggers on key events. (FR-004, FR-005, FR-006, FR-007, FR-011) | 2-3 days | After Phase 0 |
| **Phase 2** | Issuance key lifecycle — `IIssuanceKeyService` with lazy slot-1 derivation, manual rotation handler, revocation handler (`VAL_CRED_GOV_001` proto-rule). Wire `IOrgDidDocumentService.RegenerateAsync` calls on key events. (FR-016, FR-017, FR-018) | 2-3 days | After Phase 1 |
| **Phase 3** | Resolver enhancements — `SorchaDidResolver` dual-VM + `alsoKnownAs` emission + issuance-key VM surfacing. `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` with cross-resolution, key-material verification, caching, Redis-stream invalidation. `KidThumbprintHelper` exact-match → thumbprint-fallback. (FR-008, FR-009, FR-010, FR-012, FR-022, FR-023, FR-025) | 3-4 days | After Phases 1+2 |
| **Phase 4** | `DidResolverBackedIssuerKeyResolver` in `Sorcha.Citizen.Verifier`. Replace `OptOutIssuerKeyResolver` as production default. `JwkRegistryIssuerKeyResolver` retained for tests. Three-way failure-mode logging (`Sorcha.Verifier.IssuerSignature` meter). Update `RequireIssuerSignature` default to `true`. (FR-001, FR-002, FR-003, FR-019) | 2-3 days | After Phase 3 |
| **Phase 5** | Genesis schema slots (`RegisterPolicy.requireIssuerSignature`, `RegisterPolicy.permittedIssuers`, `Organization.DefaultKidStyle`). `CredentialMatcher` accepts `alsoKnownAs`-equivalent issuer match. (FR-013, FR-014, FR-015, FR-020, FR-021) | 1-2 days | After Phase 4 |
| **Phase 6** | Walkthroughs + integration. Update walkthroughs (AssuredIdentity, TradeFinance, ConstructionPermit, SelfBuildHouse) — confirm enforce-on green. Demo-mint flow stays via `JwkRegistryIssuerKeyResolver` as documented test escape. Document the AssuredIdentity action's optional `acceptedIssuers` pin as a hardening recommendation. **Ship gate.** | 1-2 days | After Phase 5; gates ship |

**Total estimate**: ~2-3 weeks for an engineer with codebase familiarity.

## Complexity Tracking

No constitution violations. No entries.
