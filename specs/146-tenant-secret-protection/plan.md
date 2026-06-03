# Implementation Plan: Tenant Service At-Rest Secret Protection

**Branch**: `146-tenant-secret-protection` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/146-tenant-secret-protection/spec.md`
**Authoritative design**: [`docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md`](../../docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md)

## Summary

Replace three broken secret/key mechanisms in `Sorcha.Tenant.Service` with a single Tenant-local `ISecretProtectionProvider` seam (AES-256-GCM, body byte-identical to Wallet's `SoftwareKeyProtectionProvider`). The protection key derives from the existing JWT signing key via HKDF-SHA256 by default (no new mandatory config), with an optional `Tenant:SecretProtection:Key` override; resolution is **fail-closed** in Production/Staging. TOTP secrets and OIDC client secrets become reversible AEAD ciphertext (each tagged with a `KeyId`); the 2FA intermediate-token HMAC key is derived from the same root (distinct HKDF `info`) so it is stable across replicas/restarts. Pre-release **clean break**: no migration, no legacy decode — column changes are squashed into the existing initial migration. The seam mirrors Wallet's contract so the two converge during the future Hardware Key Storage initiative (note left in code).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core (Minimal APIs, Generic Host), EF Core + Npgsql, BCL `System.Security.Cryptography` (`AesGcm`, `HKDF`), existing `JwtConfiguration` (Tenant). **No new NuGet packages.**
**Storage**: PostgreSQL (Tenant DB) via EF Core. Protected secrets stored as `bytea` ciphertext + a `KeyId` `varchar` column (mirrors Wallet's `EncryptedPrivateKey` + `EncryptionKeyId`).
**Testing**: xUnit + FluentAssertions + Moq — `tests/Sorcha.Tenant.Service.Tests/`.
**Target Platform**: Linux/Windows containers (Aspire-orchestrated service).
**Project Type**: Single microservice (`src/Services/Sorcha.Tenant.Service`) within the solution.
**Performance Goals**: protect/unprotect is in-memory AES-GCM with no network round-trip — negligible added latency on the login / TOTP-verify / OIDC-exchange paths. Key + login-token key derived once at startup.
**Constraints**: fail-closed in Production/Staging if no key resolves; **no new mandatory configuration** (default derives from the JWT signing key); pre-release clean break (no data migration, no legacy-format decode); cross-replica/restart stability for the 2FA token; never log secret material.
**Scale/Scope**: ~10–12 files in `Sorcha.Tenant.Service` (3 new + ~9 modified, incl. the 3 migration/snapshot files); pre-release, single service.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| **II. Security First** ("all sensitive data encrypted at rest (AES-256-GCM)"; "support for Azure Key Vault, AWS KMS"; "never commit secrets") | **Directly enforced.** This feature *remediates* a violation: it brings TOTP/OIDC secrets to AES-256-GCM at rest, leaves a KMS-ready seam, and introduces no committed secret (key derives from existing material; optional override goes through config/secret store). ✅ |
| **I. Microservices-First** (no upward deps; service-independent) | Tenant-local seam, no new cross-service dependency. Intentionally **not** shared with Wallet yet (convergence deferred with a note). ✅ |
| **IV. Testing** (>85% new code, xUnit, deterministic, AAA) | Plan includes unit tests (provider round-trip/tamper/fail-closed/derivation determinism) + integration tests (TOTP, OIDC store→recover, cross-replica token) + a clean-break repo-grep guard. ✅ |
| **V. Code Quality** (async/await, DI, nullable, no warnings) | Provider is async, DI-registered, nullable-clean; deletes dead code. ✅ |
| **VIII. Observability** (structured logging, OTel) | Structured logs on key resolution (which source, never the key), startup fail-closed, and decrypt failures — **no secret values logged**. No new metric required; may add a debug log only. ✅ |
| **III. API Documentation** | No new HTTP endpoint; the new interface gets full XML docs (incl. the convergence note). ✅ |

**Result: PASS — no violations.** Complexity Tracking section intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/146-tenant-secret-protection/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions consolidated from the design doc
├── data-model.md        # Phase 1 — column/entity changes
├── quickstart.md        # Phase 1 — configure + verify
├── contracts/
│   └── secret-protection-provider.md   # Internal seam contract (no new HTTP API)
└── tasks.md             # Phase 2 — /speckit.tasks (NOT created here)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Services/
│   ├── Interfaces/
│   │   └── ISecretProtectionProvider.cs          # NEW — seam (mirrors Wallet IOrgKeyProtectionProvider)
│   ├── Implementation/
│   │   ├── SoftwareSecretProtectionProvider.cs   # NEW — AES-256-GCM (Wallet body), key+keyId injected
│   │   └── TenantSecretKeyResolver.cs            # NEW — HKDF-from-JWT-key | override | fail-closed
│   ├── TotpService.cs                            # MOD — use provider + derived HMAC key; delete v1: + GenerateStableKey
│   ├── IdpConfigurationService.cs                # MOD — use provider; delete SHA-256 EncryptSecret/DecryptSecret
│   └── OidcExchangeService.cs                    # MOD — consume real decrypted client secret (~:127)
├── Models/
│   ├── TotpConfiguration.cs                      # MOD — EncryptedSecret string→byte[]; add EncryptionKeyId
│   └── IdentityProviderConfiguration.cs          # MOD — add ClientSecretKeyId
├── Data/
│   ├── TenantDbContext.cs                        # MOD — column config: TOTP entity + IdP entity (~:358, ~:466)
│   └── DatabaseInitializer.cs                    # MOD — IdP seed via provider (~:479)
├── Migrations/
│   ├── 20260513152714_InitialCreate.cs           # MOD — squash columns (NO new migration)
│   ├── 20260513152714_InitialCreate.Designer.cs  # MOD
│   └── TenantDbContextModelSnapshot.cs           # MOD
└── Program.cs / Extensions/ServiceCollectionExtensions.cs  # MOD — DI + startup fail-closed

tests/Sorcha.Tenant.Service.Tests/
└── Services/
    ├── SoftwareSecretProtectionProviderTests.cs  # NEW
    ├── TenantSecretKeyResolverTests.cs           # NEW
    ├── TotpServiceTests.cs                        # MOD/NEW — round-trip, stored-not-recoverable
    └── IdpConfigurationServiceTests.cs           # MOD/NEW — store→recover real secret
```

**Structure Decision**: Single-service change, all within `src/Services/Sorcha.Tenant.Service` and its test project. New code follows the established service folder layout (`Services/Interfaces` + `Services/Implementation`). No solution-wide or cross-service structural change (convergence with Wallet is deferred).

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
