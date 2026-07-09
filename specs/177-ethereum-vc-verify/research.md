# Phase 0 Research: Ethereum-key VC verification (Phase 1)

Resolves the open code-level unknowns from the design doc §10 plus one dependency-boundary
discovery. All line numbers are **indicative** — re-locate during implementation.

## R1 — SD-JWT verification is Sorcha-owned (not HeroSD-JWT)

- **Decision**: Add the `ES256K` branch directly in `SdJwtService`'s private static `Verify(...)` (indicative `SdJwtService.cs:~1026-1050`), plus an `"ES256K"/"SECP256K1" => "ES256K"` case in `MapAlgorithm(...)` (`~988-997`) for JOSE-header canonicalisation. No HeroSD-JWT pre-verify shim is needed.
- **Rationale**: Despite the "Wraps HeroSD-JWT" comment, `VerifyTokenAsync` parses the JWS and checks the signature itself, dispatching through the private `Verify` (issuer path `~277-284`). HeroSD-JWT does not own the crypto.
- **Holder key-binding (KB-JWT)**: verified in `VerifyPresentationAsync` (`~538-557`); the holder algorithm is selected by `ExportPublicKeyFromJwk(cnfJwk, out holderAlg)` (`~840-887`), which today rejects `crv != "P-256"` (`~854`). Add a `crv == "secp256k1"` arm to the `case "EC"` block returning the key bytes + `algorithm = "ES256K"`. The shared `Verify` branch then covers issuer, holder, and the request-object path (`TryVerifyCompactJwsWithEmbeddedJwk`, `~902-959`) automatically.
- **Alternatives considered**: a pre-verify JWS shim (rejected — unnecessary once `Verify` is patched); verifying via `System.Security.Cryptography.ECDsa` with the secp256k1 named curve (rejected — see R6, unreliable on Windows/WASM).

## R2 — The Warn flag belongs on `TrustPolicy`

- **Decision**: Add `bool WarnOnUnlistedVerifiedIssuer { get; set; } = false;` to `TrustPolicy` (`Sorcha.Blueprint.Models/Credentials/TrustPolicy.cs:~26`), and include it in `TrustEvaluator.ComputePolicyDigest`'s canonical object (`~227-241`) so it is covered by the pinnable policy digest.
- **Rationale**: `TrustEvaluator.EvaluateAsync(IssuerContext, TrustPolicy?, ...)` receives the policy only — never the `CredentialRequirement`. Placing the flag on `TrustPolicy` makes it reachable in the fail-closed branch with no signature change. `CredentialRequirement.TrustPolicy` is the nesting point the blueprint author already configures (alongside `Sources`/`AllowedIssuers`).
- **Alternatives considered**: a property on `CredentialRequirement` (rejected — not visible to the evaluator); a global config switch (rejected — the spec requires a per-requirement, auditable choice; also violates fail-closed-by-default).

## R3 — TrustEvaluator no-vouch branch + missing Warn/None representation

- **Decision**: Inside `TrustEvaluator.EvaluateAsync`'s `if (!trusted)` block (`~94-109`), **before** the reject return, add:
  `if (policy.WarnOnUnlistedVerifiedIssuer) return a trusted-with-warn TrustDecision (assurance None, ReducedAssurance=true);`
  Signature validity is a hard precondition at Step 0 (`~50-60`), so reaching this branch already guarantees `SignatureVerified == true`. The branch is **inert when the flag is false** (falls straight through to the existing reject).
- **Two type gaps that must be closed** (design doc §10 #3 concretised):
  1. **`AssuranceLevel` has no lowest/sentinel** — only `Low=0`, `Substantial=1`, `High=2` (`AssuranceLevel.cs:~15-28`). Add `None = -1` so a verified-but-untrusted outcome is *below* any `MinAssuranceLevel` floor (comparisons are `>=`), which correctly prevents it from ever satisfying an assurance requirement silently.
  2. **`TrustDecision` is binary** (`bool IsTrusted`, no Warn concept — `TrustDecision.cs:~12-41`). Add a `bool ReducedAssurance` (a.k.a. Warn) signal so a warn return is distinguishable from a full-trust pass by consumers.
- **Rationale**: without both, a "warn" return is indistinguishable from a `Pass` and would silently upgrade an untrusted issuer to trusted. Adding `None` + a `ReducedAssurance` flag keeps the change explicit and greppable.
- **Alternatives considered**: reuse `AssuranceLevel.Low` for warn (rejected — implies it meets a Low floor); throw/second return type (rejected — breaks the single `TrustDecision` contract).

## R4 — Surfacing Warn as `VerifyOutcome.Warn`

- **Decision**: Thread the `ReducedAssurance` signal from `TrustDecision` onto the `VerificationOutcome` / `VerificationResult` the verifier produces, and extend `RealVerifierEngine.Map` (`Sorcha.Wallet.Pwa/Services/Verification/RealVerifierEngine.cs:~135-148`) so a verified-but-reduced-assurance outcome maps to `VerifyOutcome.Warn` — exactly mirroring the existing `issuerUnverified => Warn` precedent (F114 review H3 / F155).
- **Rationale**: `VerifyOutcome` (`Pass`/`Warn`/`Fail`) lives in `Sorcha.UI.Components.User/Models/Verification/VerificationResult.cs:~39-47`; the mapping is already driven off a `VerificationOutcome` flag (`IssuerSignatureStatus.NotVerified`), not off `TrustDecision`. Reusing that shape means one new flag on `VerificationOutcome` + one added condition in `Map`.
- **Server-side note**: the Blueprint-engine credential gate treats a reduced-assurance decision as **accepted** (`IsTrusted=true`) but records the reduced assurance in `TrustEvidence` for receipts/audit. "Warn" is a verifier-UI outcome; on the gating path it is an accept-with-evidence, per the spec.
- **Alternatives considered**: a brand-new outcome enum value (rejected — `Warn` already exists and carries the intended semantics).

## R5 — `did:key` secp256k1 + the two secp256k1-rejecting JWK parse sites

- **Decision (did:key)**: In `KeyDidResolver.cs`, add secp256k1 multicodec constants `0xe7 0x01` (`~20-24`) and a dispatch branch (`BuildDocument ~81-97`) to a new `BuildSecp256k1Document` mirroring `BuildP256Document` (`~129-157`) with a 33-byte compressed-key length check. **Emit `publicKeyJwk`** (`kty:"EC"`, `crv:"secp256k1"`, `x`, `y` — decompressed via the primitive), because the downstream issuer-key resolvers require `publicKeyJwk` (a VM carrying only `publicKeyMultibase` is not consumed by the verify path). Emit `publicKeyMultibase` too for consistency.
- **Decision (new `did:jwk`)**: New `JwkDidResolver.cs` decodes the base64url JWK from the identifier into a single VM; curve-agnostic (P-256 / Ed25519 / secp256k1). Register in `HttpServiceCollectionExtensions.AddDidResolvers`.
- **Decision (JWK parse gates)** — the two sites that reject secp256k1 today, both to route through the primitive:
  1. `DidX5cIssuerKeyResolver.ExtractPublicKeyFromJwk` (`Sorcha.Blueprint.Service/Credentials/DidX5cIssuerKeyResolver.cs:~146-168`) hard-codes `ECCurve.NamedCurves.nistP256` with no `crv` check — add a `crv` switch so `secp256k1` is parsed via the primitive (not `ECDsa`).
  2. `VerifiablePresentationValidator.VerifyJwsSignature` switch (`Sorcha.Verifier.Engine/...:~585-590`) + `VerifyEs256` (`~603-620`) hard-code nistP256 and fall `ES256K` through to `false`. Add `"ES256K" => VerifyEs256k(...)` delegating to the primitive.
- **Rationale**: the issuer-key resolvers consume `publicKeyJwk`; a secp256k1 VM must therefore carry it. Both parse sites currently assume P-256 and would either mis-parse or reject secp256k1.

## R6 — Dependency boundary (the correction): the primitive is a shared pure-managed project

- **Decision**: Create a **new project `src/Common/Sorcha.Cryptography.Secp256k1`** — pure-managed, single package `BouncyCastle.Cryptography`, **no Sorcha or native dependency** — housing `ISecp256k1Verifier`, `Secp256k1Verifier`, `Secp256k1Jwk`, `Keccak256`, `EthereumAddress`. Referenced by `Sorcha.Cryptography` (SD-JWT `Verify`), `Sorcha.Verifier.Engine` (`VerifyEs256k`), `Sorcha.ServiceClients.Http` (`did:key`/`did:jwk` JWK build), and `Sorcha.Blueprint.Service` (`ExtractPublicKeyFromJwk`).
- **Rationale**: `Sorcha.Verifier.Engine` is a **ProjectReference of the Blazor WASM app `Sorcha.Wallet.Pwa`**, so it must stay native-dependency-free; it references only `Sorcha.ServiceClients.Http` and cannot take `Sorcha.Cryptography` (which pulls native `Sodium.Core` + `Nethermind.MclBindings`). Both verification paths need secp256k1, so the primitive must be a shared leaf that the WASM path can also reference. Additionally, .NET's built-in `System.Security.Cryptography.ECDsa` does **not** reliably support the secp256k1 curve on Windows (CNG) or in WASM — BouncyCastle is required for correctness, and it is pure-managed and WASM-safe.
- **Consequence**: this supersedes the design doc's "folder inside `Sorcha.Cryptography`, no new project" line. It is still **no new third-party dependency** (BouncyCastle is already in `Directory.Packages.props`), and the primitive stays entirely off the wallet signing path.
- **Alternatives considered**: folder inside `Sorcha.Cryptography` (rejected — unreachable from the WASM-safe Verifier engine); duplicating a small BouncyCastle verify in each path (rejected — two copies of security-critical crypto drift); a general `System.Security.Cryptography` secp256k1 (rejected — not portable to Windows CNG / WASM).

## Insertion-point table (indicative line numbers)

| # | File : method | Change |
|---|---|---|
| 1 | `Sorcha.Cryptography.Secp256k1/*` (NEW project) | `ISecp256k1Verifier`, `Secp256k1Verifier` (ES256K, SHA-256, 64-byte r‖s), `Secp256k1Jwk` (parse/build + decompress), `Keccak256`, `EthereumAddress` (EIP-55). BouncyCastle only. |
| 2 | `Sorcha.Cryptography/SdJwt/SdJwtService.cs` : `Verify` (~1026) | `ES256K`/`SECP256K1` branch → `ISecp256k1Verifier` (covers issuer + KB-JWT + request-object) |
| 3 | same : `MapAlgorithm` (~988) | `"ES256K" or "SECP256K1" => "ES256K"` header canonicalisation |
| 4 | same : `ExportPublicKeyFromJwk` (~849) | `crv == "secp256k1"` arm in `case "EC"` → key bytes + `algorithm = "ES256K"` |
| 5 | `Sorcha.Blueprint.Models/Credentials/TrustPolicy.cs` (~26) | `bool WarnOnUnlistedVerifiedIssuer = false` |
| 6 | `Sorcha.Blueprint.Engine/Credentials/TrustEvaluator.cs` : no-vouch branch (~94) + `ComputePolicyDigest` (~236) | scoped warn return (assurance None, ReducedAssurance); flag in digest |
| 7 | `Sorcha.Blueprint.Engine/Credentials/TrustDecision.cs` (~24) + `AssuranceLevel.cs` (~28) | `bool ReducedAssurance`; `AssuranceLevel.None = -1` |
| 8 | `Sorcha.Wallet.Pwa/Services/Verification/RealVerifierEngine.cs` : `Map` (~141) | reduced-assurance/verified-but-untrusted → `VerifyOutcome.Warn` (via a `VerificationOutcome` flag) |
| 9 | `Sorcha.ServiceClients.Http/Did/KeyDidResolver.cs` : constants (~24) + `BuildDocument` (~90) | `0xe701` branch → `BuildSecp256k1Document` emitting `publicKeyJwk` |
| 10 | `Sorcha.ServiceClients.Http/Did/JwkDidResolver.cs` (NEW) + `AddDidResolvers` | `did:jwk` resolver (all curves) + registration |
| 11 | `Sorcha.Blueprint.Service/Credentials/DidX5cIssuerKeyResolver.cs` : `ExtractPublicKeyFromJwk` (~152) | `crv` switch; `secp256k1` via primitive (not `ECDsa`) |
| 12 | `Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs` : `VerifyJwsSignature` (~585) + new `VerifyEs256k` | `"ES256K" => VerifyEs256k` via primitive |
| — | 4 `.csproj` edits | add `ProjectReference → Sorcha.Cryptography.Secp256k1` to `Sorcha.Cryptography`, `Sorcha.ServiceClients.Http`, `Sorcha.Verifier.Engine`, `Sorcha.Blueprint.Service` |

## Open items deliberately NOT resolved here (out of Phase 1 scope)

`ecrecover` / `did:pkh` / address-form `did:ethr` (Phase 2, need RPC/recovery); EIP-712 JSON-LD / EAS; secp256k1 signing / `WalletNetworks`; EVM RPC / Nethereum. The new project is the intended home for the Phase 2/3 additions (recovery, signing) but ships none of them now.
