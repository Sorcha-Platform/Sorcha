# Implementation Plan: Verification-correctness

**Branch**: `148-verification-correctness` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/148-verification-correctness/spec.md`
**Design source**: `docs/superpowers/specs/2026-06-03-verification-correctness-design.md`

## Summary

Three verification-correctness fixes, each independently shippable:

1. **H3** — the offline device (PWA) verifier silently returns `Pass` when it can't confirm the issuer signature. Add an explicit issuer-signature status to `VerificationOutcome` (`Verified` / `NotVerified`); `RealVerifierEngine` maps an accepted-but-issuer-`NotVerified` result to the existing `VerifyOutcome.Warn` (already rendered by `VerificationTrustView`), so the citizen sees reduced-assurance rather than plain Pass. Document the offline scoped exception. Authoritative server verifiers (`requireIssuerSignature:true`) are unaffected.
2. **M3a** — `OidcExchangeService.ValidateIdTokenAsync` skips JWS signature validation. Add JWKS-based signature verification (Microsoft.IdentityModel), fail-closed, keep the existing iss/aud/exp/nonce checks, make the method genuinely async, remove the TODO.
3. **M3b** — `PasskeyRecoveryService` and `OrgRecoveryService` re-key without their cryptographic proof (both feature-gated off). Throw `NotSupportedException` at the unverified point so the gate can't be opened with broken verification.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: `Sorcha.Verifier.Engine` (`VerificationOutcome`, `VerifiablePresentationValidator`), `Sorcha.UI.Components.User` (`VerifyOutcome.Warn`, `VerificationTrustView`), `Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt` + (transitive) `Microsoft.IdentityModel.Protocols.OpenIdConnect` (already referenced by Tenant via `Microsoft.AspNetCore.Authentication.OpenIdConnect`), `IHttpClientFactory`
**Storage**: N/A (no schema/data changes). JWKS / OIDC configuration cached in-memory.
**Testing**: xUnit + FluentAssertions; runner is Microsoft.Testing.Platform (`--filter` ignored — whole-project runs)
**Target Platform**: Blazor WASM (PWA verifier) + Linux server containers (Tenant, Wallet)
**Project Type**: web (multi-service + WASM client)
**Performance Goals**: No measurable change. JWKS fetch is cached (per-IdP, rotation-tolerant); verification stays offline-friendly on the device.
**Constraints**: H3 additive (no behaviour change for server verifiers / Blueprint Service); M3a fail-closed; M3b must not alter the disabled-by-default behaviour; offline device verification stays usable (FR-002).
**Scale/Scope**: 3 components across `Sorcha.Verifier.Engine` + `Sorcha.Wallet.Pwa` + `Sorcha.Tenant.Service` + `Sorcha.Wallet.Service`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| II. Security First | **Directly advances** — verifies signatures for real (M3a), is honest where it cannot (H3), and fails loud on a latent no-op (M3b). No violation. |
| IV. Testing (>85% new code, xUnit, deterministic, AAA) | Met — TDD against existing test projects (`Sorcha.Verifier.Tests`, `Sorcha.Wallet.Pwa.Tests`, `Sorcha.Tenant.Service.Tests`, `Sorcha.Wallet.Service.Tests`); deterministic (local test keys/JWKS, injected `TimeProvider`). |
| V. Code Quality (nullable, no warnings, DI) | Met — additive nullable-clean changes; JWKS fetch via injected `IHttpClientFactory`. |
| III. API Documentation | No endpoint surface change; XML docs on new members. |
| I. Microservices-First (no upward deps) | Met — changes are service-local + the shared `Verifier.Engine` change is additive and consumed downward. |
| VIII. Observability | M3a may add a counter for signature-validation outcome on an existing meter (optional); H3 reuses the existing `FederationVerifierMetrics` trust-rejection counters. No new infra. |

**Result: PASS — no violations, no Complexity Tracking entries.**

## Project Structure

### Documentation (this feature)

```text
specs/148-verification-correctness/
├── plan.md              # This file
├── spec.md              # Feature spec
├── research.md          # Phase 0 — decisions & rationale
├── data-model.md        # Phase 1 — outcome status + OIDC validation model
├── quickstart.md        # Phase 1 — how to verify each fix
├── contracts/
│   └── verification-behaviour.md   # Phase 1 — behaviour contracts (no new HTTP endpoints)
└── checklists/
    └── requirements.md  # Spec quality checklist (done)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Verifier.Engine/
├── Models/VerifierSession.cs            # EDIT — add IssuerSignatureStatus enum + VerificationOutcome.IssuerSignature
└── VerifiablePresentationValidator.cs   # EDIT — track issuer-verified, thread into the success outcome

src/Apps/Sorcha.Wallet.Pwa/Services/Verification/
└── RealVerifierEngine.cs                # EDIT — map accepted+NotVerified → VerifyOutcome.Warn + message
src/Apps/Sorcha.Wallet.Pwa/                # EDIT — README/docs: offline scoped-exception note
                                           # (doorstep UI already renders Warn via VerificationTrustView)

src/Services/Sorcha.Tenant.Service/Services/
└── OidcExchangeService.cs               # EDIT — JWKS signature validation (async), remove TODO

src/Services/Sorcha.Wallet.Service/Services/Implementation/
├── PasskeyRecoveryService.cs            # EDIT — throw NotSupportedException at the unverified unwrap point
└── OrgRecoveryService.cs                # EDIT — throw NotSupportedException at the unverified unwrap point (line ~82 TODO)

tests/
├── Sorcha.Verifier.Tests/Services/VerifiablePresentationValidatorTests.cs   # ADD H3 status cases
├── Sorcha.Wallet.Pwa.Tests/Services/Verification/RealVerifierEngineTests.cs # ADD Warn-mapping case
├── Sorcha.Tenant.Service.Tests/Services/OidcExchangeServiceTests.cs         # ADD signature-validation cases
├── Sorcha.Wallet.Service.Tests/Services/PasskeyRecoveryServiceTests.cs      # ADD fail-loud case
└── Sorcha.Wallet.Service.Tests/Services/OrgRecoveryServiceTests.cs          # ADD fail-loud case
```

**Structure Decision**: Multi-service + WASM. All changes are edits to existing files plus tests added to existing test projects. The only shared-library change (`Sorcha.Verifier.Engine.VerificationOutcome`) is additive — a new optional property + enum — consumed by both the desk verifier and the PWA.

## Complexity Tracking

No constitution violations — section intentionally empty.
