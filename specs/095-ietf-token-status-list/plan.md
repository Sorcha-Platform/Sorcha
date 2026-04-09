# Implementation Plan: IETF Token Status List (Parallel to W3C)

**Branch**: `095-ietf-token-status-list` | **Date**: 2026-04-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/095-ietf-token-status-list/spec.md`

## Summary

Add an IETF Token Status List (TSL) producer, consumer, and credential status claim form alongside the existing W3C Bitstring Status List, per Phase 2 D3 Option B (parallel envelopes, shared backing bitstring).

Key outcomes:
- New public HTTP endpoint serving an IETF-shape signed JWT (`typ: "statuslist+jwt"`, payload `status_list: { bits, lst }`) alongside the existing W3C endpoint at `/api/v1/credentials/status-lists/{listId}`.
- Single backing bitstring in `StatusListManager`. A single lifecycle operation (revoke, suspend, reinstate) flips one bit and both envelopes re-derive from the updated source. W3C uses gzip; IETF uses zlib — identical decompressed bytes.
- Presentation verifier extended to consume both W3C `credentialStatus` and IETF `status.status_list` claim forms. Prefers IETF when both are present.
- Blueprint issuance selects the claim form by path: internal path embeds W3C (unchanged), HAIP path embeds IETF `status.status_list`.

Extends `specs/039-verifiable-presentations` FR-007–FR-012. Depends on spec 093 (verifier baseline) and spec 094 (classical HAIP signing key for list envelope signatures). Required by spec 097. Independent of spec 096 (can run in parallel).

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: Existing `StatusListManager` in `Sorcha.Blueprint.Service/Services/StatusListManager.cs`; `System.IO.Compression` for zlib (`ZLibStream`); `Sorcha.Cryptography.SdJwt` for signing the IETF TSL JWT (the list envelope is itself a JWT, signed by the list issuer's classical signing key from spec 094).
**Storage**: Blueprint Service's existing in-memory `StatusListManager._lists` concurrent dictionary; on-register control records via the existing anchoring path from spec 039 FR-010. No new storage surface.
**Testing**: xUnit, FluentAssertions, Moq. New unit tests in `tests/Sorcha.Blueprint.Service.Tests/Services/` for the `IetfTokenStatusListSerializer` and the dual-envelope verifier consumer. Integration test in `tests/Sorcha.Blueprint.Service.IntegrationTests/` (may need to work around the pre-existing `BlueprintRecoveryServiceTests.cs` compile issue).
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: Existing multi-service monorepo. No new services.
**Performance Goals**: IETF endpoint fetch P95 < 200 ms (SC-007). Cache TTL default 5 minutes (same as W3C endpoint).
**Constraints**: Decompressed W3C `encodedList` and IETF `lst` bytes MUST be byte-identical (SC-004). Existing W3C endpoint behaviour unchanged (FR-018). Both endpoints MUST resolve to the same on-register control record (FR-017). Zero regressions to specs 039, 093, 094.
**Scale/Scope**: Moderate. Adds one new endpoint handler, one new serializer class, one new claim-form embedder, and extends the verifier's status check. 131,072 entries per list (W3C minimum, inherited).

## Constitution Check

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. Changes land in `Sorcha.Blueprint.Service` (new endpoint, new serializer) and `Sorcha.Wallet.Service` (verifier consumer extension). No cross-service dependency changes. |
| **II. Security First** | PASS. IETF TSL JWTs are signed by the list issuer's classical key; verifiers check signatures. Status list endpoints remain public and anonymous (matches W3C precedent and spec 039 FR-011). |
| **III. API Documentation** | PASS. New endpoint gets Scalar documentation; new claim form documented in data-model.md. |
| **IV. Testing Requirements** | PASS. Parametrised tests for W3C/IETF byte-identity, endpoint performance, claim-form routing. |
| **V. Code Quality** | PASS. |
| **VI. Blueprint Creation Standards** | PASS. Blueprint author sees a single "record on status list" config knob. The claim form is driven by issuance path, not exposed to authors (FR-023). |
| **VII. Domain-Driven Design** | PASS. New entities: `IetfTokenStatusListJwt` (wire shape), `statusListClaim` (IETF form). Reuses existing `BitstringStatusList`, `StatusListAllocation`, `StatusListBitUpdate`. |
| **VIII. Observability by Default** | PASS. Adds structured log events on each envelope serialisation (cache miss path) and on claim-form consumer dispatch in the verifier. |

**Constitution gate: PASS.**

## Project Structure

```text
specs/095-ietf-token-status-list/
├── spec.md              # (complete)
├── plan.md              # This file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md

src/
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Endpoints/
│   │   │   └── StatusListEndpoints.cs            # CHANGE — add IETF endpoint handler
│   │   └── Services/
│   │       ├── StatusListManager.cs              # CHANGE — add GetIetfEnvelopeAsync method
│   │       └── IetfTokenStatusListSerializer.cs  # NEW — serialise backing bitstring to signed IETF JWT
│   └── Sorcha.Wallet.Service/
│       └── Services/
│           └── PresentationRequestService.cs    # CHANGE — read IETF status.status_list claim alongside W3C credentialStatus
├── Common/
│   └── Sorcha.Blueprint.Models/
│       └── Credentials/
│           └── IetfTokenStatusListClaim.cs       # NEW — typed representation of the status.status_list claim
└── Services/
    └── Sorcha.Wallet.Service/
        └── Endpoints/
            └── CredentialEndpoints.cs            # CHANGE — HAIP-path issuance embeds status.status_list instead of credentialStatus

tests/
├── Sorcha.Blueprint.Service.Tests/
│   └── Services/
│       ├── IetfTokenStatusListSerializerTests.cs         # NEW
│       └── StatusListDualEnvelopeIdentityTests.cs        # NEW — byte-identical W3C vs IETF decompressed
└── Sorcha.Wallet.Service.Tests/
    └── Presentations/
        └── PresentationRequestVerificationTests.cs       # EXTEND — IETF status claim consumption tests
```

**Structure Decision**: Existing monorepo. Changes focused in the Blueprint Service's status list module and the Wallet Service's presentation verifier. No new projects, no new service boundaries.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
