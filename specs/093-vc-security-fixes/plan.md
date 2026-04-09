# Implementation Plan: Credential & Presentation Security Fixes (HAIP Prep)

**Branch**: `093-vc-security-fixes` | **Date**: 2026-04-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/093-vc-security-fixes/spec.md`

## Summary

Three surgical fixes to the existing credential and presentation path, identified during the HAIP 1.0 gap analysis:

1. **Presentation verifier** (`Sorcha.Wallet.Service.Services.PresentationRequestService.VerifyPresentationAsync`) must cryptographically verify the submitted `vpToken` via `ISdJwtService.VerifyPresentationAsync` and source claim values from the verified token rather than the server-side credential store row.
2. **Credential issuance** (`Sorcha.Wallet.Service.Endpoints.CredentialEndpoints.IssueCredential`) must allocate its status list index **before** signing and embed a `credentialStatus` claim (W3C `BitstringStatusListEntry` shape) in the signed SD-JWT VC payload.
3. **DID resolution** (`Sorcha.ServiceClients.Http.Did.SorchaDidResolver`) must emit W3C-valid multibase `publicKeyMultibase` values using multicodec prefixes and base58btc encoding. The existing `Sorcha.Cryptography.Utilities.Base58` helper provides the encoding primitive.

No new endpoints, no wire-format changes, no new services. This spec is purely behavioural correction inside the existing Wallet Service and the shared cryptography/service-client libraries.

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `Sorcha.Cryptography.SdJwt`, `Sorcha.Cryptography.Utilities.Base58`, EF Core 10, `Sorcha.Blueprint.Service` service client (for `IStatusListManager.AllocateIndexAsync`), `Sorcha.ServiceClients.Http.Did` resolver registry
**Storage**: PostgreSQL (Wallet Service credential rows via `WalletDbContext.Credentials`); on-register status list control records (unchanged)
**Testing**: xUnit 2.x, FluentAssertions 8.x, Moq 4.x — matching existing Sorcha test conventions. New unit tests in `tests/Sorcha.Wallet.Service.Tests` and `tests/Sorcha.Cryptography.Tests`; new integration tests in `tests/Sorcha.Wallet.Service.IntegrationTests` and `tests/Sorcha.ServiceClients.Http.Tests`
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: Existing multi-service monorepo (Sorcha's seven services plus shared libraries). No new projects.
**Performance Goals**: Presentation verification under 100 ms at P95 in the common path (matching NFR-4 in spec). Signature verification is the dominant cost; status list HTTP fetch is the existing cache-friendly call.
**Constraints**: Zero wire-format changes (NFR-1). Zero public API signature changes on `CredentialEndpoints` and `PresentationEndpoints` (NFR-2). Pre-fix credentials MUST continue to verify (FR-010). No regression of any spec 039 acceptance scenario (NFR-5, FR-018).
**Scale/Scope**: Small. Three call sites in three files, four new unit test files, two new integration test files, one small multicodec helper utility.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. No changes to service boundaries. Wallet Service continues to depend only on the Blueprint Service client (for status list allocation, existing client) and the DID resolver (existing client). |
| **II. Security First** | PASS — this spec is a security fix. Presentation verification now actually verifies. Claim values are sourced from the cryptographically verified token. No new secrets. No new encryption. |
| **III. API Documentation** | PASS. No new endpoints. Existing endpoints' XML documentation will be updated to note the corrected verification behaviour. No OpenAPI/Scalar doc changes beyond descriptions. |
| **IV. Testing Requirements** | PASS. FR-009 and FR-016 mandate tests at unit and integration level for each fix. Target >85 % coverage on new code. Tests fail against master before the fix and pass after (explicit acceptance criterion). |
| **V. Code Quality** | PASS. Standard C# conventions, nullable enabled, async/await throughout, DI via existing container wiring. |
| **VI. Blueprint Creation Standards** | N/A. No blueprint changes. |
| **VII. Domain-Driven Design** | PASS. Reuses existing domain terms: `CredentialEntity`, `PresentationRequest`, `VerificationResult`, `DidDocument`. No new aggregates or bounded contexts. |
| **VIII. Observability by Default** | PASS. Adds structured log events on each verification-failure branch (signature fail, nonce mismatch, audience mismatch, clock skew, disclosure integrity) using the existing `ILogger<PresentationRequestService>` instance. No new metrics; existing presentation request counters suffice. |

**Constitution gate: PASS.** No violations, no complexity justifications required.

## Project Structure

### Documentation (this feature)

```text
specs/093-vc-security-fixes/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (credentialStatus claim shape)
├── contracts/           # Phase 1 output (internal contracts only)
├── quickstart.md        # Phase 1 output (how to verify the fixes locally)
├── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
└── checklists/
    └── requirements.md  # Spec quality checklist (complete)
```

### Source Code (repository root)

Existing Sorcha multi-service monorepo. Paths touched by this spec:

```text
src/
├── Common/
│   ├── Sorcha.Cryptography/
│   │   ├── SdJwt/
│   │   │   ├── ISdJwtService.cs                     # unchanged
│   │   │   └── SdJwtService.cs                      # unchanged (FR-002 assumes the signer works)
│   │   └── Utilities/
│   │       ├── Base58.cs                            # unchanged (reused)
│   │       └── Multicodec.cs                        # NEW — multicodec prefix helper
│   └── Sorcha.ServiceClients.Http/
│       └── Did/
│           └── SorchaDidResolver.cs                 # CHANGE — FR-012 to FR-015
├── Core/
│   └── Sorcha.Wallet.Portable/
│       └── Domain/
│           └── Entities/
│               └── CredentialEntity.cs              # unchanged (shape stable)
└── Services/
    ├── Sorcha.Wallet.Service/
    │   ├── Endpoints/
    │   │   └── CredentialEndpoints.cs               # CHANGE — IssueCredential ordering + credentialStatus embed (FR-006 to FR-011)
    │   └── Services/
    │       └── PresentationRequestService.cs        # CHANGE — VerifyPresentationAsync calls ISdJwtService (FR-001 to FR-005)
    └── Sorcha.Blueprint.Service/
        └── Services/
            └── Implementation/
                └── ActionExecutionService.cs        # MINOR CHANGE — status list allocation moves ahead of IssueCredentialAsync call (FR-006)

tests/
├── Sorcha.Cryptography.Tests/
│   └── Utilities/
│       └── MulticodecTests.cs                       # NEW — round-trip tests per algorithm
├── Sorcha.ServiceClients.Http.Tests/
│   └── Did/
│       └── SorchaDidResolverMultibaseTests.cs       # NEW — FR-013 to FR-015
├── Sorcha.Wallet.Service.Tests/
│   ├── Endpoints/
│   │   └── CredentialEndpointsIssueTests.cs        # NEW — FR-006 to FR-011 allocation-before-sign
│   └── Services/
│       └── PresentationRequestVerificationTests.cs # NEW — FR-001 to FR-005 signature verification
└── Sorcha.Wallet.Service.IntegrationTests/
    ├── PresentationReplayIntegrationTests.cs       # NEW — round-trip with signature verification
    └── CredentialStatusEmbeddingIntegrationTests.cs # NEW — decode issued payload and assert credentialStatus
```

**Structure Decision**: Existing Sorcha multi-service monorepo. The fix lives in the existing Wallet Service, the shared cryptography library (for the new `Multicodec` helper), and the shared DID resolver. Two call sites in one Blueprint Service file (`ActionExecutionService.cs`) shift their sequencing. No new projects, no new service boundaries.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
