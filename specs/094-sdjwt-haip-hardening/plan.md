# Implementation Plan: SD-JWT VC HAIP Hardening

**Branch**: `094-sdjwt-haip-hardening` | **Date**: 2026-04-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/094-sdjwt-haip-hardening/spec.md`

## Summary

Library-level hardening of `Sorcha.Cryptography.SdJwt` to close the five HAIP 1.0 gaps identified in the Phase 1 gap analysis:

1. **`cnf` holder key binding** at issuance — credentials stop being bearer tokens.
2. **Key Binding JWT** at presentation — the creator actually builds one, the verifier actually checks it (`aud`, `nonce`, `iat`, `sd_hash`).
3. **Nested and array-element selective disclosure** via JSON Pointer paths.
4. **Purpose-derived holder binding key** (`sorcha:credential-holder-binding`) for Sorcha-internal holders — follows the Feature 086 / 092 BIP32 purpose precedent.
5. **Classical co-key for HAIP issuance** (`sorcha:haip-issuer-signing`, ES256 default) — wallets with primary PQC keys can still sign HAIP-facing credentials.

Supersedes `specs/031-verifiable-credentials` and carries forward FR-035–FR-038 from it. Depends on spec 093 (verifier baseline must be correct). Required by specs 097 and 098.

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `Sorcha.Cryptography.SdJwt` (extending), `Sorcha.Cryptography.Core` (BIP32 key derivation), `Sodium.Core` (EdDSA), `System.Security.Cryptography` (ECDsa / RSA), `System.Text.Json` (SD-JWT payload). BIP32 derivation library check needed in Phase 0 — Feature 086 and 092 already established purpose-derivation paths in the codebase, so the primitive exists.
**Storage**: PostgreSQL (Wallet Service): existing `WalletDbContext.Credentials` table unchanged. New wallet domain fields for `HaipIssuer` capability flag and an optional marker that the holder binding key has been derived. **No schema migration required** — per user guidance, pre-release EF migrations are squashed into the initial setup migration.
**Testing**: xUnit, FluentAssertions, Moq. New unit tests in `tests/Sorcha.Cryptography.Tests/SdJwt` and `tests/Sorcha.Wallet.Service.Tests/Services`. Integration tests for holder-binding-key derivation in `tests/Sorcha.Wallet.Service.IntegrationTests` against an in-memory repo.
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: Existing multi-service monorepo. No new services. Library and Wallet Service domain additions only.
**Performance Goals**: KB-JWT creation < 10 ms P95, KB-JWT verification < 10 ms P95, nested disclosure reconstruction < 20 ms P95 for credentials with ≤ 20 disclosable fields.
**Constraints**: Legacy credentials without `cnf` MUST continue to verify (FR-006). Existing Blueprints declaring top-level name-keyed disclosables MUST continue to produce byte-for-byte identical output (FR-021, SC-006). ES256 is the HAIP MTI default for the classical co-key. Spec 093 must be merged before this spec executes.
**Scale/Scope**: Largest crypto-library change in the 093–098 series. Touches `SdJwtService.cs` significantly, adds two new BIP32 purposes to the wallet domain, extends `Disclosable` configuration on Blueprint action credential issuance.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. Changes land in `Sorcha.Cryptography` (shared library), `Sorcha.Wallet.Core` (domain), `Sorcha.Wallet.Service` (holder binding key endpoint), and the Blueprint Fluent API. No cross-service dependency changes. |
| **II. Security First** | PASS — this spec *is* a security hardening. `cnf` + KB-JWT promote credentials from bearer tokens to holder-bound tokens; nested disclosure removes a data-leakage pressure in real-world schemas; the classical co-key gate prevents accidentally-PQC-signed HAIP credentials. |
| **III. API Documentation** | PASS. New `ISdJwtService` method overloads get XML documentation. New Wallet Service internal endpoints (holder binding key lookup, KB-JWT signing) are documented in the README and exposed via Scalar. |
| **IV. Testing Requirements** | PASS. FR-039 mandates unit + integration test coverage for every new behaviour. Target > 85 % on new code. Regression against spec 093 is an explicit acceptance criterion (FR-041). |
| **V. Code Quality** | PASS. Standard C# conventions, nullable enabled. No compiler warnings. |
| **VI. Blueprint Creation Standards** | PASS. `Disclosable` configuration gains JSON Pointer path support as an *additive* extension — existing name-keyed blueprints continue to work unchanged (FR-021). Fluent API gets a new `MakeDisclosablePath(path)` method alongside the existing `MakeDisclosable(name)`. |
| **VII. Domain-Driven Design** | PASS. New wallet domain concepts: `HolderBindingKey` (value object, derived from wallet seed), `HaipIssuerCapability` (flag), `HaipIssuerCoKey` (value object). Reuses existing BIP32 derivation purpose terminology. |
| **VIII. Observability by Default** | PASS. Adds structured log events for each key derivation, each `cnf` embedding at issuance, each KB-JWT verification failure branch. Uses existing `ILogger` instances. No new metrics. |

**Constitution gate: PASS.** No violations. `Complexity Tracking` section empty.

## Project Structure

### Documentation (this feature)

```text
specs/094-sdjwt-haip-hardening/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── tasks.md             # /speckit.tasks output
└── checklists/
    └── requirements.md  # Spec quality checklist (complete)
```

### Source Code (repository root)

Existing Sorcha multi-service monorepo. Paths touched by this spec:

```text
src/
├── Common/
│   └── Sorcha.Cryptography/
│       └── SdJwt/
│           ├── ISdJwtService.cs                  # CHANGE — new overloads for cnf, KB-JWT, nested disclosure
│           ├── SdJwtService.cs                   # CHANGE — cnf embedding, KB-JWT build/verify, nested _sd arrays
│           ├── SdJwtToken.cs                     # CHANGE — optional Cnf property
│           ├── SdJwtPresentation.cs              # CHANGE — optional KbJwt property
│           ├── SdJwtVerificationResult.cs        # CHANGE — HolderKeyVerified flag
│           └── NestedDisclosure.cs               # NEW — JSON Pointer → nested _sd digest translator
├── Core/
│   └── Sorcha.Wallet.Portable/
│       └── Domain/
│           ├── Entities/
│           │   └── Wallet.cs                      # CHANGE — add HaipIssuer capability flag
│           └── ValueObjects/
│               ├── HolderBindingKey.cs            # NEW — derived key under sorcha:credential-holder-binding
│               └── HaipIssuerCoKey.cs             # NEW — derived classical key under sorcha:haip-issuer-signing
├── Core/
│   └── Sorcha.Blueprint.Fluent/
│       └── CredentialIssuanceBuilder.cs          # CHANGE — add MakeDisclosablePath(jsonPointer) method
├── Common/
│   └── Sorcha.Blueprint.Models/
│       └── Credentials/
│           └── CredentialIssuanceConfig.cs       # CHANGE — Disclosable type supports mixed name + path entries
└── Services/
    └── Sorcha.Wallet.Service/
        ├── Endpoints/
        │   ├── CredentialEndpoints.cs            # CHANGE — accept holder key JWK in issue request; embed cnf
        │   └── WalletEndpoints.cs                 # CHANGE — expose holder binding key and KB-JWT signing endpoints
        └── Services/
            ├── Implementation/
            │   ├── HolderBindingKeyService.cs    # NEW — derives + signs with sorcha:credential-holder-binding
            │   └── HaipIssuerCoKeyService.cs     # NEW — derives + signs with sorcha:haip-issuer-signing
            └── Interfaces/
                ├── IHolderBindingKeyService.cs   # NEW
                └── IHaipIssuerCoKeyService.cs    # NEW

tests/
├── Sorcha.Cryptography.Tests/
│   └── SdJwt/
│       ├── SdJwtCnfBindingTests.cs               # NEW
│       ├── SdJwtKeyBindingJwtTests.cs            # NEW
│       ├── SdJwtNestedDisclosureTests.cs         # NEW
│       └── SdJwtLegacyCompatTests.cs             # NEW
├── Sorcha.Wallet.Service.Tests/
│   └── Services/
│       ├── HolderBindingKeyServiceTests.cs       # NEW
│       └── HaipIssuerCoKeyServiceTests.cs        # NEW
└── Sorcha.Wallet.Service.IntegrationTests/
    └── HaipIssuanceRoundTripTests.cs             # NEW — full issue→present→verify with cnf and KB-JWT
```

**Structure Decision**: Existing monorepo. Changes concentrated in `Sorcha.Cryptography.SdJwt` (the crypto primitive) and new domain entities under `Sorcha.Wallet.Portable/Domain/ValueObjects/`. New service-layer implementations in `Sorcha.Wallet.Service/Services/Implementation/`. No new projects, no new service boundaries. Blueprint Fluent API gains one new method for JSON Pointer disclosable paths.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
