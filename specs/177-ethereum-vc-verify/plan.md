# Implementation Plan: Ethereum-key VC verification — Phase 1 (verify-only)

**Branch**: `177-ethereum-vc-verify` | **Date**: 2026-07-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/177-ethereum-vc-verify/spec.md`

**Authoritative design**: [`docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md`](../../docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md) — the HOW is already settled there; this plan operationalises it and resolves the three open code-level risks in Phase 0.

## Summary

Enable Sorcha to **verify** secp256k1/ES256K-signed SD-JWT / W3C credentials — at both the issuer-signature and holder-key-binding positions — where the DID resolves **offline** to a secp256k1 key (`did:key` secp256k1, `did:jwk`). Approach: an **isolated, pure-managed cryptographic primitive** — a new project `Sorcha.Cryptography.Secp256k1` (BouncyCastle-only, WASM-safe: ES256K verify + keccak256 + secp256k1 JWK + EIP-55 address) — that **both** verification code paths delegate to. Trust reuses the existing allowlist resolver, with a scoped, default-off per-requirement Warn-fallback for signature-valid-but-unlisted issuers. New project, **no new dependency**, no node/RPC, verify-only.

> **Phase 0 correction (resolved in `research.md`):** the primitive cannot live *inside* `Sorcha.Cryptography` as originally sketched. `Sorcha.Verifier.Engine` is consumed by the Blazor **WASM** PWA (`Sorcha.Wallet.Pwa`) and therefore must stay native-dependency-free — it cannot reference `Sorcha.Cryptography` (which pulls native `Sodium`/`Mcl`). Both verification paths (`SdJwtService` in `Sorcha.Cryptography` and `VerifiablePresentationValidator` in `Sorcha.Verifier.Engine`) need secp256k1, and .NET's built-in `ECDsa` does not reliably support secp256k1 on Windows/WASM — so the primitive is extracted to a shared, pure-managed BouncyCastle project that both can reference.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: `BouncyCastle.Cryptography` (already in `Directory.Packages.props`; provides managed secp256k1 curve ops + `KeccakDigest`) is the *only* package the new `Sorcha.Cryptography.Secp256k1` project consumes. Reuses `SdJwtService` (in `Sorcha.Cryptography`, which does its own JWS verification — not HeroSD-JWT), the existing DID resolver registry (`Sorcha.ServiceClients.Did`), and the credential trust pipeline (`Sorcha.Blueprint.Engine.Credentials`). **New project, but no new third-party package.**

**Storage**: N/A — verify-only, no new persistence. The only new persisted surface is one optional boolean on the blueprint credential-requirement model (existing storage path).

**Testing**: xUnit v3 + FluentAssertions; known-answer vectors for ES256K / keccak256 / EIP-55; integration tests through the existing format handler; the existing credential-verification regression suite as a fail-closed guard. All tests run **offline**.

**Target Platform**: Cross-platform .NET libraries + services. The new primitive MUST be **pure-managed** (BouncyCastle only, no native/`Sodium`/`Mcl` dependency) so it runs wherever verification runs, including any WASM-hosted verifier path.

**Project Type**: Shared-library + engine change spanning four existing projects (no new service, no new project).

**Performance Goals**: None specific — secp256k1 verify is sub-millisecond and off any hot path. Verification latency is dominated by existing DID resolution/caching, unchanged.

**Constraints**: Fully **offline** (no network, no blockchain, no RPC). **Verify-only** (no signing, no `WalletNetworks` change). **Fail-closed preserved** — the one behavioural change is inert unless a per-requirement flag is set. No personal data on-chain.

**Scale/Scope**: Small and bounded — one new primitive folder, one new DID resolver, and ~8 delegation/branch edits across the existing pipeline (enumerated in the Insertion-point table produced by Phase 0 research).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | PASS — no new service; the primitive lives in a low-level common library (`Sorcha.Cryptography`) and is consumed upward. Dependencies flow downward only; no upward or cross-service coupling introduced. |
| **II. Security First** | PASS (emphasis) — security-critical crypto. Fail-closed default preserved; Warn only via explicit opt-in; tampered/malformed/off-curve/unresolvable → Reject; input validation on all JWK/DID parsing; no secrets; no PII on-chain. **Gate: negative-path + fail-closed regression tests are mandatory.** |
| **III. API Documentation** | PASS — no new HTTP endpoints (internal verification pipeline). XML docs required on all new public types. No OpenAPI change. |
| **IV. Testing** | PASS (gate) — >85% on new code; unit (primitive, resolvers) + integration (format handler end-to-end, all four outcome cases) + regression (existing suite unchanged). Known-answer vectors. |
| **V. Code Quality** | PASS — nullable enabled, zero new warnings in Release, DI for the new resolver, verify is CPU-bound (sync acceptable). |
| **VI. Blueprint Standards** | PASS — `warnOnUnlistedVerifiedIssuer` is a JSON credential-requirement field, consistent with blueprint-as-JSON. |
| **VII. Domain-Driven Design** | PASS — uses Credential / Issuer / Holder / Disclosure vocabulary; no term drift. |
| **VIII. Observability** | PASS — reuse the existing `Sorcha.Trust` meter (`TrustMetrics`) to record eth trust decisions (tags: outcome, format, assurance); structured logging; no new meter. **Gate: the new trust branch must emit the existing metric.** |

**Result: all gates pass, no violations.** Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/177-ethereum-vc-verify/
├── plan.md              # This file
├── research.md          # Phase 0 — resolves the 3 open code-level risks
├── data-model.md        # Phase 1 — entities + the requirement-model field + outcome states
├── quickstart.md        # Phase 1 — how to run the verify path against a fixture, offline
├── contracts/           # Phase 1 — the internal seam contracts (ISecp256k1Verifier, resolver, trust flag)
├── checklists/
│   └── requirements.md   # speckit.specify quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root) — the concrete change map

```text
src/Common/Sorcha.Cryptography.Secp256k1/     # NEW pure-managed project (BouncyCastle only; WASM-safe)
├── Sorcha.Cryptography.Secp256k1.csproj      #   single PackageReference: BouncyCastle.Cryptography
├── ISecp256k1Verifier.cs                      #   ES256K verify seam
├── Secp256k1Verifier.cs                       #   BouncyCastle ECDSA-over-SHA256, 64-byte r‖s
├── Secp256k1Jwk.cs                            #   parse/build EC JWK (crv:secp256k1) + compressed-point decode
├── Keccak256.cs                               #   BouncyCastle KeccakDigest(256)   (foundation)
└── EthereumAddress.cs                         #   keccak→last20→EIP-55   (foundation, no P1 caller)

src/Common/Sorcha.Cryptography/
├── Sorcha.Cryptography.csproj                 # EDIT — add ProjectReference → Sorcha.Cryptography.Secp256k1
└── SdJwt/SdJwtService.cs                      # EDIT — Verify(): alg=="ES256K" branch (issuer JWS + KB-JWT via
                                               #        ExportPublicKeyFromJwk crv:secp256k1 arm); MapAlgorithm(): ES256K

src/Common/Sorcha.ServiceClients.Http/
├── Sorcha.ServiceClients.Http.csproj         # EDIT — add ProjectReference → Sorcha.Cryptography.Secp256k1
└── Did/
    ├── KeyDidResolver.cs                      # EDIT — 0xe701 branch → BuildSecp256k1Document emitting publicKeyJwk
    ├── JwkDidResolver.cs                      # NEW — did:jwk resolver (all curves)
    └── ../Extensions/HttpServiceCollectionExtensions.cs  # EDIT — register JwkDidResolver in AddDidResolvers

src/Common/Sorcha.Verifier.Engine/
├── Sorcha.Verifier.Engine.csproj             # EDIT — add ProjectReference → Sorcha.Cryptography.Secp256k1
└── VerifiablePresentationValidator.cs        # EDIT — VerifyJwsSignature: "ES256K" => VerifyEs256k (delegates to primitive)

src/Services/Sorcha.Blueprint.Service/
├── Sorcha.Blueprint.Service.csproj           # EDIT — add ProjectReference → Sorcha.Cryptography.Secp256k1
└── Credentials/DidX5cIssuerKeyResolver.cs    # EDIT — ExtractPublicKeyFromJwk: crv:secp256k1 via primitive (not ECDsa)

src/Common/Sorcha.Blueprint.Models/Credentials/
└── TrustPolicy.cs                            # EDIT — add bool WarnOnUnlistedVerifiedIssuer (default false)

src/Core/Sorcha.Blueprint.Engine/Credentials/
├── TrustEvaluator.cs                         # EDIT — scoped Warn-fallback inside no-vouch branch (inert when flag false) + digest
├── TrustDecision.cs                          # EDIT — add ReducedAssurance/Warn signal (binary IsTrusted can't express Warn)
└── AssuranceLevel.cs                         # EDIT — add None (lowest) for the verified-but-untrusted outcome

src/Apps/Sorcha.Wallet.Pwa/Services/Verification/
└── RealVerifierEngine.cs                     # EDIT — Map(): reduced-assurance/verified-but-untrusted => VerifyOutcome.Warn

tests/
├── Sorcha.Cryptography.Secp256k1.Tests/      # NEW — primitive KATs (ES256K, keccak, EIP-55, JWK round-trip)
├── Sorcha.ServiceClients.Tests/Did/          # NEW — did:key(secp256k1) + did:jwk resolver tests
├── Sorcha.Cryptography.Tests/SdJwt/          # NEW — ES256K issuer JWS + KB-JWT verify
└── (blueprint/verifier engine tests)         # NEW — format-handler integration (Pass/Warn/Reject×2) + fail-closed regression
```

**Structure Decision**: Single-repo, multi-project .NET solution. The secp256k1 crypto is extracted into a **new pure-managed leaf project** (`Sorcha.Cryptography.Secp256k1`, BouncyCastle-only) that four consumers reference — this is Approach C, corrected: a shared leaf rather than a folder inside `Sorcha.Cryptography`, because the WASM-consumed `Sorcha.Verifier.Engine` cannot take `Sorcha.Cryptography`'s native dependencies (see the Summary correction and `research.md` §Dependency boundary). Everything else is delegation seams and branches inside the **existing** DID-resolution, SD-JWT, and credential-trust pipeline. Exact insertion points (file : method : line) are enumerated in `research.md`.

## Complexity Tracking

No constitution violations — this section is intentionally empty.
