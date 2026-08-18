---
name: verifiable-credentials
description: |
  Implements W3C Verifiable Credentials 2.0 (SD-JWT VC profile), DID documents, and selective disclosure across Sorcha services and UI.
  Use when: issuing or verifying VCs, building DID documents, resolving did:sorcha, wiring credential wallet UI, implementing SD-JWT presentations, or working on feature 093 VC security fixes.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Verifiable Credentials Skill

Sorcha implements W3C Verifiable Credentials 2.0 using the **SD-JWT VC profile** (RFC 9901 + SD-JWT VC). The ecosystem choice deliberately avoids JSON-LD canonicalisation and Data Integrity Proofs — instead, SD-JWT provides compact serialisation, built-in selective disclosure, and plays well with existing JWT infrastructure. All SD-JWT primitives live in `Sorcha.Cryptography.SdJwt`; credential issuance, verification, and selective disclosure sit in `Sorcha.Blueprint.Engine/Credentials/`.

**In-flight context (feature 093).** There is an active spec at `specs/093-vc-security-fixes/` that is hardening VC security, including the `publicKeyMultibase` encoding bug that prompted the `Multicodec` utility. Read `specs/093-vc-security-fixes/spec.md` before touching the DID resolver or multibase encoding paths.

**Unified trust + mdoc (feature 135, shipped).** Verification trust is now decided by ONE `ITrustEvaluator` (`Sorcha.Blueprint.Engine.Credentials`) consulted by BOTH the engine `CredentialVerifier` and HAIP's verifier — `CredentialRequirement.AcceptedIssuers` is gone, replaced by a `TrustPolicy` of pluggable trust sources; the old `CredentialVerifier` `SignatureValid=false` shortcut is removed (signatures verify for real, fail-closed). A second credential format — ISO `mso_mdoc` (CBOR/COSE, `Sorcha.Mdoc` — extracted from `Sorcha.Cryptography` by F185 so the WASM wallet can consume it) — sits beside SD-JWT VC behind an `ICredentialFormatHandler` seam (online/OpenID4VP only). **Read the "EUDI credential format & unified trust (Feature 135)" section of the `sorcha-architecture` skill before touching credential verification, issuance, trust policy, or mdoc.**

**Ethereum-key VC verification (Feature 177, verify-only — PR #1140).** Sorcha can VERIFY ES256K-signed SD-JWT/JWT VCs at BOTH the issuer-signature and holder key-binding positions, where the DID resolves **offline** to a secp256k1 key. New offline resolvers `did:key`(secp256k1 multicodec `0xe701`, emits `publicKeyJwk`) + a new all-curve `did:jwk` (`Sorcha.ServiceClients.Http/Did/`, registered in `AddDidResolvers`). ES256K is delegated to the pure-managed `Sorcha.Cryptography.Secp256k1` primitive from `SdJwtService.Verify`/`ExportPublicKeyFromJwk` (issuer + holder) and `VerifiablePresentationValidator.VerifyEs256k` (verifier engine). Issuer trust: `TrustPolicy.WarnOnUnlistedVerifiedIssuer` (**default false → fail-closed preserved**) accepts a signature-valid-but-unvouched issuer at reduced assurance via `TrustDecision.ReducedAssurance` + `AssuranceLevel.None`. **Out of scope (later phases):** `ecrecover` / `did:pkh` / address-form `did:ethr` (need EVM RPC), secp256k1 signing, EIP-712 JSON-LD / EAS. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase1-design.md`; spec `specs/177-ethereum-vc-verify/`.

**Ethereum address-form issuer DIDs (Feature 178, verify-only — Phase 2).** Sorcha can now VERIFY ES256K-signed VCs in the **issuer** position where the DID resolves **offline to an ADDRESS ONLY** — `did:pkh` (CAIP-10) and default-document (no-rotation) address-form `did:ethr` (ERC-1056). Their DID document carries an `EcdsaSecp256k1RecoveryMethod2020` VM with a `blockchainAccountId` (e.g. `eip155:1:0x…`) and **no** `publicKeyJwk` (new `VerificationMethod.BlockchainAccountId`). Verification is **recover-then-match**: `Secp256k1Recovery.TryRecover` (recid 0/1) → `EthereumAddress.FromPublicKey` → case-insensitive match to the DID address (`Secp256k1Verifier.VerifyByAddress`, in the Phase-1 primitive). New offline resolvers `PkhDidResolver` + `EthrDidResolver` (`Sorcha.ServiceClients.Http/Did/`, registered in `AddDidResolvers`; shared shape-validation in `EvmDid`). The seam is **recovery-JWK envelope + one verify branch**: the two issuer-key resolvers accept an address VM and carry the address forward — `DidResolverBackedIssuerKeyResolver` synthesises `{kty:EC,crv:secp256k1,blockchainAccountId}` (verifier engine); `IssuerKeyResolution.BlockchainAccountId` + an optional `SdJwtService` `issuerRecoveryAddress` param (blueprint engine). Trust reuses Phase 1 **unchanged** (allowlist Pass / `WarnOnUnlistedVerifiedIssuer` Warn-or-Reject). Chain-id is DID identity, not crypto (recovery ignores it; the allowlist matches the DID string). **Out of scope (later phases):** ERC-1056 registry read / rotation / delegates (needs EVM RPC — Phase 2b, `EthrDidResolver` has an unused `IEvmRpcClient` seam), holder address-binding, signing, EIP-712 / EAS. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase2-design.md`; spec `specs/178-ethereum-ecrecover/`.

**`did:ethr` on-chain resolution (Feature 179, verify-only — Phase 2b).** `EthrDidResolver` now resolves the **current** `did:ethr` document from ERC-1056 registry state over **read-only EVM RPC** (`eth_call` `changed`/`identityOwner` + `eth_getLogs` walking `previousChange`), so ES256K issuer signatures verify against currently-authorised keys: the **current owner** (rotation), unexpired **`veriKey`/`sigAuth` delegates**, and unexpired **`did/pub/{Secp256k1|Ed25519}/…` key attributes** (honouring `validTo`). New pure-managed `Sorcha.ServiceClients.Http/Evm`: `IEvmRpcClient`/`EvmRpcClient` (SSRF-guarded, 3-outcome `NotConfigured`/`Error`/`Ok`, never throws), `AbiCodec` (selectors/topics via the `Keccak256` primitive — no ABI library), `Erc1056Registry` (event walk → `Erc1056State`). **Zero verify-seam change** — the current document uses only Phase-1 key VMs (`publicKeyJwk`) + Phase-2 recovery VMs (`blockchainAccountId`, `veriKey`→assertionMethod, `sigAuth`→authentication). **Server-side only**: registered via `AddEvmRpc` inside `AddHttpServiceClients` (the WASM PWA omits it → `EthrDidResolver` stays offline; `registry: null` → Phase-2 default document). **SAFETY:** a **configured** RPC that errors → resolver returns **null** (fail closed), never a stale default doc; only an **unconfigured** chain / `changed==0` uses the offline default. Config: `DidResolver:Ethr:Rpc:{chainId}` + optional `…:RegistryAddress:{chainId}` (default `0xdca7…f21b`). **Out of scope (Phase 3+):** service endpoints, browser/WASM RPC, secp256k1 signing, transacting/Nethereum. Design: `docs/superpowers/specs/2026-07-09-ethereum-verify-phase2b-design.md`; spec `specs/179-ethr-rpc-resolution/`.

**The .NET VC ecosystem is sparse.** Do not look for a turnkey NuGet package. Sorcha implements SD-JWT VC directly against the spec using `System.Text.Json` and `Sorcha.Cryptography`. The existing `CredentialIssuer`, `CredentialVerifier`, `BitstringStatusListChecker`, and `SdJwtService` types are the canonical implementations — extend them rather than starting over.

## Quick Start

### Existing Building Blocks (real file paths)

| Component | Location | Purpose |
|-----------|----------|---------|
| `ICredentialIssuer` / `CredentialIssuer` | `src/Core/Sorcha.Blueprint.Engine/Credentials/` | Builds + signs SD-JWT VCs from action execution data |
| `ICredentialVerifier` / `CredentialVerifier` | `src/Core/Sorcha.Blueprint.Engine/Credentials/` | Verifies presentations against action credential requirements |
| `BitstringStatusListChecker` | `src/Core/Sorcha.Blueprint.Engine/Credentials/` | W3C Bitstring Status List revocation checks |
| `ISdJwtService` / `SdJwtService` | `src/Common/Sorcha.Cryptography/SdJwt/` | RFC 9901 SD-JWT primitives — create, verify, present |
| `SdJwtToken` / `SdJwtPresentation` | `src/Common/Sorcha.Cryptography/SdJwt/` | Token + presentation types |
| `CredentialIssuanceConfig` | `src/Common/Sorcha.Blueprint.Models/Credentials/` | Blueprint action credential minting config |
| `CredentialRequirement` | `src/Common/Sorcha.Blueprint.Models/Credentials/` | Action precondition: "present this type of VC" |
| `CredentialPresentation` | `src/Common/Sorcha.Blueprint.Models/Credentials/` | Holder-submitted SD-JWT presentation |
| `IDidResolverRegistry` + `IDidResolver` | `src/Common/Sorcha.ServiceClients.Http/Did/` | DID resolution — `SorchaDidResolver`, `WebDidResolver`, `KeyDidResolver` |
| `DidDocument` | `src/Common/Sorcha.ServiceClients.Http/Did/` | W3C DID Core document |
| `Multicodec` | `src/Common/Sorcha.ServiceClients.Http/Utilities/` | `publicKeyMultibase` varint + base58btc encoding |
| `ICredentialStore` (server) | `src/Services/Sorcha.Wallet.Service/Credentials/` | Wallet-side credential persistence |
| `CredentialMatcher` | `src/Services/Sorcha.Wallet.Service/Credentials/` | Matches stored credentials against requirements |
| `PresentationRequestService` | `src/Services/Sorcha.Wallet.Service/Services/` | Legacy OID4VP request lifecycle — creates/matches/VERIFIES a caller-built vpToken (in-memory store; it builds nothing) |

### Issuing a VC — the actual API shape

```csharp
// Blueprint action execution reaches the point where a credential should be minted.
// Signature matches ICredentialIssuer.IssueAsync — no VerifiableCredential record,
// the issuer builds the SD-JWT directly from the config + processed data.
public async Task<IssuedCredentialInfo> MintAsync(
    CredentialIssuanceConfig config,
    Dictionary<string, object> processedData,
    string issuerDid,       // e.g. "did:sorcha:org:ws1q..."
    string recipientDid,    // e.g. "did:sorcha:w:ws1q..."
    byte[] issuerSigningKey,
    string algorithm,       // "EdDSA" | "ES256"
    CancellationToken ct)
{
    return await _credentialIssuer.IssueAsync(
        config, processedData, issuerDid, recipientDid, issuerSigningKey, algorithm, ct);
}
```

The signing key is the **org's VC-issuance key**, derived under `KeyUsage.VCIssuance` (Feature 083 slot 1) via `IOrgKeyDerivationService` and surfaced by `IIssuanceKeyService.GetActiveSigningMaterialAsync` (Feature 120) as `IssuanceSigningMaterial` (issuer DID + a `#vc-issuance-{rotationIndex}` kid + the private key bytes) — **never the root wallet key**. There is no `"sorcha:vc-issuance"` derivation-purpose string; the key is selected by `KeyUsage`, not a purpose label.

### Verifying a presentation — the actual API shape

```csharp
// Verifier takes action requirements + submitted presentations.
// Returns a CredentialValidationResult with per-requirement success/failure.
var result = await _credentialVerifier.VerifyAsync(
    action.CredentialRequirements,
    submittedPresentations,
    ct);

if (!result.IsValid)
    _logger.LogWarning("Credential verification failed: {Errors}",
        string.Join(", ", result.Errors.Select(e => e.Message)));
```

This runs in both the Blueprint Service (server-side action execution) and the holder's UI (pre-flight check before submitting). The verifier is portable — no `HttpClient`, no platform APIs — so it works in Blazor WASM too.

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| `did:sorcha:org:{walletAddress}` | Organisation issuer DID | `did:sorcha:org:ws1q8tuvvd...` |
| `did:sorcha:w:{walletAddress}` | Wallet / holder DID | `did:sorcha:w:ws1q8tuvvd...` |
| `did:sorcha:r:{registerId}:t:{txId}` | Register transaction reference | `did:sorcha:r:abc:t:xyz` |
| SD-JWT compact form | Wire format | `<jwt>~<disclosure1>~<disclosure2>~<kb-jwt>` |
| `ClaimMapping` | Blueprint field → VC claim | `/applicant/name → credentialSubject.name` |
| `ClaimConstraint` | Verifier requirement | "must equal X" / "must exist" |
| `BitstringStatusList` | Revocation mechanism | 131K entries per status credential |
| `RevocationCheckPolicy` | FailClosed / FailOpen | Default is `FailClosed` |

## Core Models — actual locations

All credential domain models live in `Sorcha.Blueprint.Models.Credentials` (note: `Blueprint.Models`, **not** `Blueprint.Engine.Credentials.Models`).

```csharp
// Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs
public class CredentialIssuanceConfig
{
    public string CredentialType { get; set; } = string.Empty;   // short-name / fallback / readable id — NOT the wire vct
    public string? Vct { get; set; }                             // canonical absolute URI, e.g. https://sorcha.dev/vc/assured-identity/v1 — the SD-JWT VC's sole type identifier
    public string? DisplayName { get; set; }                     // authored human card label, e.g. "Assured Identity"
    public IEnumerable<ClaimMapping> ClaimMappings { get; set; } = [];
    public string RecipientParticipantId { get; set; } = string.Empty;
    // ... plus display config, status list binding, validity window
}
```

**VCT / display decoupling (2026-07-15).** `Vct` is the canonical, **case-sensitive absolute URI** that is the credential's sole machine-matching identity — written to `claims["vct"]` and nowhere else. The wire SD-JWT VC carries **`vct` only; there is no `type` claim** (SD-JWT VC §3.2.2.1 — Sorcha previously wrote both, which was non-standard, and has stopped). `DisplayName` is the authored human card label; when omitted the card falls back to `Humanize(vct)`. `CredentialType` is demoted to a short-name/fallback/readable id, used only when `Vct` is omitted. Matching is **case-sensitive exact** (`Ordinal`) everywhere — `CredentialVerifier`/`CredentialMatcher` moved off `OrdinalIgnoreCase`; the PWA's `PresentationEngine` was already `Ordinal`. Every platform credential type has a canonical constant in `Sorcha.CitizenWallet.Abstractions.Constants.VctUris` (e.g. `VctUris.AssuredIdentityV1 = "https://sorcha.dev/vc/assured-identity/v1"`) — new blueprints should declare `vct` from that registry, not rely on the bare-name fallback. A parametrised `BlueprintVctConformanceTests` enforces every shipped blueprint's `vct` matches its `VctUris` constant.

```csharp
// Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs
public class CredentialRequirement
{
    public string Type { get; set; } = string.Empty;
    public CredentialFormat Format { get; set; } = CredentialFormat.SdJwtVc; // feature 135 (SdJwtVc | MsoMdoc)
    public TrustPolicy? TrustPolicy { get; set; }                            // feature 135 — replaced AcceptedIssuers
    public IEnumerable<ClaimConstraint>? RequiredClaims { get; set; }
    public RevocationCheckPolicy RevocationCheckPolicy { get; set; }
}
// NOTE (feature 135): the flat AcceptedIssuers list was REMOVED from CredentialRequirement.
// Issuer trust is now a TrustPolicy decided by the unified ITrustEvaluator. See the
// "EUDI credential format & unified trust (Feature 135)" section of the sorcha-architecture skill.

// Sorcha.Blueprint.Models/Credentials/CredentialPresentation.cs
public class CredentialPresentation
{
    public string CredentialId { get; set; } = string.Empty;    // DID URI of the VC
    public Dictionary<string, object> DisclosedClaims { get; set; } = new();
    public string RawPresentation { get; set; } = string.Empty; // SD-JWT compact form
    // ... plus key-binding JWT
}
```

These are mutable `class` types with `set;` properties — Sorcha chose this over records for JSON round-trip compatibility with `DataAnnotations` validation. Do **not** redesign them as records.

## DID Documents

`DidDocument` is a mutable class in `Sorcha.ServiceClients.Did`, serialised with `System.Text.Json`. It is **not** a record, does **not** include `@context`, and carries both `publicKeyJwk` and `publicKeyMultibase` on each verification method.

```csharp
public class DidDocument
{
    public required string Id { get; set; }
    public IReadOnlyList<VerificationMethod> VerificationMethod { get; set; } = [];
    public IReadOnlyList<string>? Authentication { get; set; }
    public IReadOnlyList<string>? AssertionMethod { get; set; }
    public IReadOnlyList<ServiceEndpoint>? Service { get; set; }
}

public class VerificationMethod
{
    public required string Id { get; set; }
    public required string Type { get; set; }       // "Ed25519VerificationKey2020", "JsonWebKey2020"
    public required string Controller { get; set; }
    public JsonElement? PublicKeyJwk { get; set; }
    public string? PublicKeyMultibase { get; set; }  // z-prefixed, encoded via Multicodec
}
```

Resolution goes through `IDidResolverRegistry` — it dispatches to the method-specific resolver (`SorchaDidResolver`, `WebDidResolver`, `KeyDidResolver`). Call it directly, do not reinvent:

```csharp
public class MyVerifier(IDidResolverRegistry dids)
{
    public async Task<DidDocument?> ResolveIssuerAsync(string did, CancellationToken ct)
        => await dids.ResolveAsync(did, ct);
}
```

**`Multicodec` is the canonical `publicKeyMultibase` encoder.** Feature 093 introduced it after the original DID resolver was emitting literal `"z" + hex/base64` — which is malformed. Always use:

```csharp
// Algorithm names match Sorcha.Cryptography's wallet algorithm strings — NOT JOSE "alg" values.
// Accepted: "ED25519", "NIST-P256" / "P-256" / "P256" / "ECDSA-P256", "RSA" / "RSA-4096".
// Passing "EdDSA" or "ES256" silently returns null — that is the feature 093 bug class.
string? multibase = Multicodec.ToMultibasePublicKey("ED25519", rawPublicKeyBytes);
// Returns null for PQC algorithms (ML-DSA, SLH-DSA, ML-KEM) that have no assigned codec —
// callers must fall back to publicKeyJwk or fail closed per FR-014 in spec 093.
```

Never hand-roll `"z" + Base58.Encode(publicKey)` — that was the original bug.

## Org VC-Issuer Signing & DID Anchoring (the three-address model)

> Hard-won during the CyberEssentialsUac live-test blocker (2026-06). Read this before touching org credential issuance, `did:sorcha:org:`, trust allowlists, or the issuer-key resolver — the model is **split-brained** and the seams are non-obvious.

A single credential-issuing org has **three distinct identities**, and `did:sorcha:org:{addr}` does **not** mean the same `addr` everywhere:

| | Identity | How created | `did:sorcha:org:{…}` used for |
|---|---|---|---|
| **A** | Operational / participant wallet — `Organization.WalletAddress` | plain `New-SorchaWallet`, linked as a participant | register ownership, governance roster, **register invitations** (`RegisterInvitationService`), **X.509 org-cert SAN** (`InternalCaTrustProvider.IssueOrgCertAsync`), trust `did-allowlist` pins, participant publishing |
| **B** | Feature 083 org **master seed** | `Set-SorchaOrgMasterKey` → `OrgKeyDerivationService.ProvisionMasterKeyAsync` | **none** — it has *no address*; it is an encrypted BIP39 seed + master public key, a derivation root only |
| **C** | Derived **VC-issuance child wallet** (`KeyUsage.VCIssuance`, F083 slot 1, F120) | lazily derived by `IssuanceKeyService.GetOrDeriveAsync` under B | the credential's **`iss`**, the published **did.json `id`**, `kid = …#vc-issuance-{n}`, and what `IOrgDidDocumentClient.ResolveCanonicalDidAsync` returns (the **F127 verifier `client_id`**) |

**The split-brain:** `did:sorcha:org:{A}` is canonical for register/trust/invitation/X.509, but issuance + did.json + the F127 verifier identity all use `did:sorcha:org:{C}`. **A and C never match** (C is a BIP32 child of B with its own address). This is the root cause behind a cluster of latent bugs:

- **An org with NO master key signs with its root wallet key** and emits `iss` = a **bare wallet address** (not a `did:`), with **no `kid`** and **no `jwk`** in the JWS header → the issuer key is unresolvable → `TrustEvaluator: issuer signature not verified`. (`CredentialEndpoints.cs` fallback when `IssuanceKeyService.GetActiveSigningMaterialAsync` returns null.)
- **The dev "embedded JWS `jwk`" resolver path can never fire** — `SdJwtService` issuance writes only `alg`/`typ`/optional `kid`/optional `x5c`; it has **no code path that embeds `jwk`**. Don't propose "embed the issuer JWK for dev verification" without first adding the emit side.
- **A `did-allowlist` pinning `did:sorcha:org:{A}` won't match a credential whose `iss` is `did:sorcha:org:{C}`** (no `alsoKnownAs` bridges them). Adding a master key to an org that pins its operational DID silently breaks its own trust check.
- **Blueprint Service's verifier doesn't read the published did.json.** `SorchaDidResolver` is wired there with the 2-arg ctor (no `HttpClient`), so it **skips** the Tenant `/orgs/{id}/did.json` fetch and **rebuilds the doc locally from the wallet row**, synthesising a **hardcoded `#vc-issuance-1`** VM. Works only for rotation index 1 — `#vc-issuance-2` won't match → latent rotation bug.

> ⚠ **SHIPPED — this section describes the PRE-F149 world.** The re-anchor below landed in
> **#1002 (2026-06-13)**. Today `IssuanceKeyService.GetActiveSigningMaterialAsync` anchors
> `iss`/`kid` on the canonical operational wallet **A** (via `IOrgInfoClient.ResolveCanonicalWalletAddressAsync`)
> and **fails closed** when A is unresolvable; `CredentialEndpoints` refuses to mint (409) rather
> than fall back to a bare-wallet `iss`. `SorchaDidResolver` no longer rebuilds an org document
> from the wallet row — it resolves the Tenant-published `did.json` by DID, or returns null.
> **Do not diagnose a modern issuer failure as the three-address split-brain**; on a post-#1002
> org, `iss` == the published document's `id` == A. A demo's own "issuer wallet" in `state.json`
> is NOT A — A is Tenant's `Organization.WalletAddress` — so seeing three addresses is expected
> and is not itself evidence of a fault.
>
> **The failure this actually masked (fixed #1316, 2026-07-28):** the document resolved fine and
> was then discarded by `DidResolverRegistry.CrossResolveAsync`, because every org publishes a
> `did:web:{PlatformDomain}:orgs:{orgId}` `alsoKnownAs` that **nothing serves** — and an
> unreachable link used to `return null`. An unverifiable advisory hint had veto power over a
> verified identity. Unreachable links are now advisory. See "did:web is half-built" below.

**The pre-F149 fix, for historical context:** re-anchor the issuer `iss`/`kid`/did.json `id` from the derived child **C** to the operational wallet **A** (publish C's public key as a VM *under* `did:sorcha:org:{A}`, e.g. `did:sorcha:org:{A}#vc-issuance-{n}`). Edit points: `IssuanceKeyService.cs:128,233-234` + `OrgDidDocumentService.cs:52` (change together + regenerate cached docs, or the verifier fails closed). Blast radius is strictly positive — fixes F127 `client_id`, VC-`iss`↔invitation↔X.509-SAN consistency, and the allowlist footgun; tests assert the `#vc-issuance-{n}` *suffix*, not the address. Add the old C-DID to `alsoKnownAs` for backward-compat with already-issued creds. (Anchoring to the stable `orgId` GUID would be even more rotation-proof but touches all the A-consumers — a bigger, separate decision.)

**Walkthrough rule:** any org that issues native SorchaLocalWallet VCs **MUST** call `Set-SorchaOrgMasterKey` for that org, or it falls to the bare-wallet-`iss` path above. `ForestryCertification`/`TradeFinance`/`SelfBuildHouse` do; `CyberEssentialsUac`/`AssuredIdentity` historically did **not** (they only provisioned HAIP enrolment).

### `did:web` is half-built — and the resolution paths that mislead

Sorcha **computes, stores and indexes** a federated `did:web:{PlatformDomain}:orgs:{orgId}`
(`OrgDidDocumentService.cs:58`, `OrgDidDocument.FederatedDid`, `IX_OrgDidDocuments_FederatedDid`)
and advertises it in every published document's `alsoKnownAs` — but **nothing serves a
`did:web`-shaped document, and nothing reads that index**. Two independent reasons it cannot
resolve, so don't "fix" only the first:

1. `TenantSettings.PlatformDomain` defaults to `sorcha.dev` (the marketing site) and is set in no
   `appsettings.json` — so it is the default on every deployment. The document lives on the node host.
2. **Even at the right host it fails**: `WebDidResolver.cs:81-86` requires `doc.Id == did`, and the
   served document's `id` is the `did:sorcha` form. Repointing the domain fetches the document and
   then rejects it on ID mismatch.

Finishing it means serving a *second*, `did:web`-shaped document (same key material, `id` = the
did:web form) off the existing route, keyed on that unused index. Per DID Core the link is
non-reciprocated and unverifiable regardless, so this buys federation reach, **not trust**.

**Resolution paths that produce misleading 404s** (all three cost real diagnosis time):

| Probe | Result | Why |
|---|---|---|
| `GET /orgs/{orgId}/did.json` | ✅ 200 | the gateway route (`tenant-org-did-document`) |
| `GET /api/orgs/{orgId}/did.json` | ❌ bodiless 404 | **no `/api` prefix on this route** |
| `GET /orgs/by-did/{did}/did.json` | ❌ 404 via gateway, ✅ 200 direct to Tenant | F149's route has **no gateway route at all** |

A bodiless 404 is the gateway-routing signature (same tell as the F111 trap). Blueprint Service
calls Tenant *directly*, so the missing by-did gateway route does not affect internal verification —
but it does mean an external verifier cannot resolve by DID, and it makes manual probing lie to you.

**No repair path for a published document:** `OrgDidDocumentService.RegenerateAsync(orgId, reason)`
**throws `NotSupportedException`**, while `OrgDidDocumentClient` logs "lazy rebuild will recover"
on a failed publish. That comment is false. The publish is fire-and-forget, non-throwing, and fires
only on the first key *derivation* — so a document that fails to publish stays missing until a key
event. Signing fails closed on canonical-address resolution but **not** on "is the document actually
published", so issuance can succeed while the document is absent.

### Standards conformance of the issuer-signing model

| Item | Verdict |
|---|---|
| F120 DID path (`iss`=did, kid-matched `assertionMethod` VM, `cnf`+KB-JWT) | **conformant** profile choice — DID is a sanctioned `iss` form; `assertionMethod` is the right relationship for issuer keys |
| **`iss` = bare wallet address** (no-master-key fallback) | **divergence** — not a URI/DID, no `kid`/`jwk`; unresolvable by any conformant verifier. Should fail *closed at issuance* rather than mint an unverifiable credential |
| `typ = "vc+sd-jwt"` → **`dc+sd-jwt`** | **resolved (Feature 181 US1)** — issuance now emits the final `dc+sd-jwt` media subtype; verify **dual-accepts** the legacy `vc+sd-jwt` during transition |
| embedded-`jwk` dev resolver path | off-spec (self-certifying issuer); acceptable only if strictly non-prod |
| `.well-known/jwt-vc-issuer` metadata path | not implemented — legitimately substituted by DID resolution |

### Two trust rails — pick by who the verifier is

Credential trust runs on **two distinct rails**; reaching for the wrong one ("use the X.509 CA so my credential is trusted") is a common mistake.

- **Rail 1 — register/DID-native (intra-ecosystem).** Verifiers *inside* Sorcha (engine `CredentialVerifier`, HAIP verifier, another Sorcha node) anchor on the **register** (wallet signatures + validator roster) + `did:sorcha:org:` resolution. **No X.509, no external CA.** The register is the trust root (DAD model). This is the correct rail whenever the verifier is itself a Sorcha participant (e.g. an insurer org consuming an assessor's credential).
- **Rail 2 — X.509/x5c (EUDI/external bridge).** Verifiers *outside* Sorcha that only speak PKI (EUDI wallets, third parties) need a cert chain to a root **they already trust**. F135's `CredentialIssuanceConfig.TrustAnchor` = `x509-tenant` (per-tenant **self-signed** root, `InternalCaTrustProvider`) vs `x509-lotl` (external trusted-list anchor).

**Current-state (post Feature 181 US3/US4/US5):** the external X.509 rail now exists end to end. **US3**
lets operators import a signed ETSI TS 119 612 trusted-list snapshot; verifying services resolve CA
anchors from it for `x509-lotl` / `trustlist` (live LOTL refresh still deferred). **US4** adds the
outbound half: an org generates a CSR bound to its P-256 issuing key (primary ES256 key, else a derived
HAIP co-key), imports an externally-issued cert+chain, and issues with `TrustAnchor=x509-lotl` chaining
to the external root — failing closed `CERT_EXTERNAL_ANCHOR_UNAVAILABLE`. **US5** removes the two prior
blockers: the `X509CertificateBuilder` P-256-only limit is now a **typed `CERT_KEY_NOT_ELIGIBLE` 422**
(no more ASN.1 500), enrol server-resolves the org key (no caller-supplied key) and re-issues with
auditable history, and **auto-enrol** runs best-effort after wallet provisioning plus an `OrgSettings`
admin certificates panel — so a normal org does reach an externally-usable X.509 identity. Full detail:
`sorcha-architecture` skill → "EUDI conformance — DCQL dialect, trust rail, verifier auth (Feature 181)".

**Verifier authentication (Feature 181 US6):** presentations now flow over the OpenID4VP 1.0 **DCQL**
dialect (`dcql_query`; Presentation Exchange retired). The HAIP verifier signs its request object (ES256)
with an X.509 verifier certificate carrying an `x5c` chain and a prefixed `x509_san_dns:{host}`
`client_id`; the wallet authenticates it via `RequestObjectValidator` (`Sorcha.Verifier.Engine`, pure
BouncyCastle / WASM-safe) into a three-state `VerifierAuthState` (`TrustedListVerified` /
`AuthenticUntrusted` / `Unverifiable`). Tampered signature / SAN mismatch is a hard refusal; absent
anchors never block. See the `sorcha-architecture` skill US6 subsection.

## Selective Disclosure (SD-JWT)

SD-JWT compact form: `<issuer JWT>~<disclosure1>~<disclosure2>~...~<key binding JWT>`.

- **Issuer** (`SdJwtService.CreateAsync`): picks which claims are selectively disclosable, hashes each disclosure, embeds the hashes in the JWT payload under `_sd`.
- **Holder** (`SdJwtService.PresentAsync`): strips disclosures not requested by the verifier, signs a key-binding JWT with nonce + audience.
- **Verifier** (`SdJwtService.VerifyAsync`): verifies issuer signature, recomputes disclosure hashes, verifies key-binding JWT, checks status list.

Presentations are built CLIENT-side: the PWA's PresentationEngine and the web app's SorchaWalletLocalPresenter (Sorcha.UI.Components.User) assemble jwt~disclosures~kb-jwt, with server-custody KB-JWT signing via POST /api/v1/wallet/presentations/sign-kb. PresentationRequestService only VERIFIES a submitted vpToken (its /submit endpoint) — it has no build path. Feature 093 added stricter verification (nonce binding, audience check, revocation fail-closed) in `PresentationRequestVerificationTests.cs` — read those tests to understand the contract.

## Blueprint Integration

Actions carry credential configs as first-class fields (not an `x-*` schema extension). The real JSON shape uses `claimName` + `sourceField` on each mapping, and the set of selectively-disclosable claims is declared **once** on the config via the `disclosable` array.

**Feature 103: nested source paths.** `sourceField` is a JSON Pointer, so it can resolve nested values from Sorcha core primitive references.

- A blueprint that references `PersonName/v1` via `$ref` can map `/name/givenName`, `/name/familyName`, `/name/fullName` etc. directly.
- The `ActionExecutionService.BuildClaimsFromMappings` walker is used by both the internal issuance path and the HAIP external-wallet path. It descends nested `Dictionary<string, object?>` and `JsonElement` structures and applies RFC 6901 `~1` / `~0` escape decoding.
- Missing source values are logged at `LogWarning` and the claim is dropped from the credential — silently issuing a credential with fewer attributes than the action promised is a correctness defect worth surfacing.
- See `ActionExecutionService.BuildClaimsFromMappings` and `TryResolveJsonPointer` for the walker, and the `HaipVerifiedCitizen` walkthrough for a worked example.


```json
{
  "actionId": "issue-graduation",
  "credentialIssuance": {
    "credentialType": "CompletionCertificateCredential",
    "vct": "https://sorcha.dev/vc/completion-certificate/v1",
    "displayName": "Completion Certificate",
    "recipientParticipantId": "student",
    "claimMappings": [
      { "claimName": "name",           "sourceField": "/student/name" },
      { "claimName": "graduationDate", "sourceField": "/student/graduationDate" }
    ],
    "disclosable": ["graduationDate"],
    "expiryDuration": "P10Y",
    "usagePolicy": "Reusable"
  },
  "credentialRequirements": [
    {
      "type": "IdentityAttestation",
      "trustPolicy": {
        "sources": [
          { "kind": "did-allowlist", "allowedIssuers": ["did:sorcha:org:ws1q..."] }
        ]
      },
      "revocationCheckPolicy": "FailClosed"
    }
  ]
}
```

`vct` is the canonical URI (from `VctUris.CompletionCertificateV1`) — the credential's sole wire type identifier; `credentialType` is the short-name fallback used only when `vct` is omitted; `displayName` is the card label. `IdentityAttestation` above is an action-local requirement type, not a platform-catalogued credential, so it stays a bare name — there is no `VctUris` constant for it.

> **Feature 135:** `CredentialRequirement.acceptedIssuers` was **removed** and replaced by `trustPolicy` (a `sources[]` + `combinator` + `minAssuranceLevel` shape decided by the unified `ITrustEvaluator`). The `did-allowlist` source above is the direct equivalent of the old flat issuer list; omit `trustPolicy` (or use a `{ "kind": "register" }` source) to trust register-resolved issuers. Full shape: the "EUDI credential format & unified trust (Feature 135)" section of the `sorcha-architecture` skill. **Do not** write `acceptedIssuers` — it is silently ignored.

`expiryDuration` is an ISO 8601 duration string (`P10Y`, `P90D`), not a `TimeSpan`.

`ActionExecutionService` reads `CredentialRequirements` before the action runs and `CredentialIssuance` after. No custom per-blueprint credential code.

### Citizen-PWA delivery (Feature 114 US4)

When `credentialIssuanceConfig.targetAudience: "SorchaLocalWallet"` and the resolved recipient wallet is a citizen's holder wallet (slot 108), the credential is delivered to the citizen-PWA inbox with optional SignalR push. The flow lives entirely in Wallet Service — Blueprint Service is unchanged from the org-credential path.

```
ActionExecutionService                            (Blueprint Service)
   ↓ AEAD-encrypts SD-JWT VC to recipient wallet's X25519 key
   ↓ submits credential-issuance transaction
Validator seals docket
   ↓
InboundCredentialDetector.TryExtractAsync         (Wallet Service)
   ↓ decrypts envelope with recipient wallet's X25519 private key
   ↓ persists CredentialEntity
   → CredentialStore.AddAsync(credential)
   → ICitizenInboxProjector.OnCredentialAddedAsync(credential)
        ↓ IHolderAddressLookup.ResolvePlatformUserIdAsync(recipientAddress)
        ↓   null  → org credential, no-op (existing org-credential path takes over)
        ↓   guid  → citizen credential
        ↓ insert CitizenCredentialEventLog row, Seq = MAX(Seq)+1 per PlatformUserId
        ↓ try { hub.Clients.Group(WalletHub.GroupNameFor(pid)).CredentialAvailable(id) }
          catch { log; swallow }     // pull-on-open /sync stays authoritative
```

Status mutations follow the same projector seam: `CredentialStore.PatchStatusAsync` and `UpdateStatusAsync` invoke `ICitizenInboxProjector.OnCredentialStatusChangedAsync` after a successful mutation. Active→Revoked/Declined writes a `Revoked` event-log entry; replacement transitions write a `Replaced` entry.

**Authority model.** The hub emit is an optimisation; the `/sync` endpoint reading `CitizenCredentialEventLog` via `EfCoreCitizenCredentialEventStream` is authoritative. Closing the PWA before issuance and reopening after still surfaces the credential because the projector wrote the log row regardless of hub-emit success.

**Key index population.** `CitizenHolderIndex` (`(WalletAddress → PlatformUserId)`) is written from `CitizenWalletEndpoints.EnrolDevice` at the one moment the citizen JWT carries both the wallet address and the platform user id. Without that row, `IHolderAddressLookup` returns null and the credential falls back to the org path — meaning citizen-credential push only works for citizens who have completed at least one device enrolment.

Worked-example blueprint (council issuing Assured Identity to a late-bound citizen applicant) is in `.claude/skills/blueprint-builder/SKILL.md` and `.claude/skills/sorcha-architecture/SKILL.md` § "Citizen Wallet PWA (Feature 114)".

### Holder→device delegation: algorithm support & the `/verify` diagnostic panel

The citizen presentation chain is **curve-mixed by construction**, so any single-algorithm assumption is a bug:

| Key | Curve | Why |
|-----|-------|-----|
| **Device** / KB-JWT | always **EC P-256 / ES256** | WebCrypto non-extractable key in the browser |
| **Holder** (signs the device delegation; the credential's `cnf.jwk`) | **derives from the wallet algorithm** — Ed25519 (OKP/EdDSA) for the default Sorcha wallet, P-256 for a P-256 wallet (`HolderKeyService`, slot 108) | the holder *is* the citizen's wallet |
| **Issuer** (credential JWS) | Ed25519 **or** P-256 (org VC-issuance key) | org wallets are frequently Ed25519 |

`VerifiablePresentationValidator.VerifyJwsSignature` dispatches on the JWS header `alg` and verifies **both** `ES256` (EC, via `ECDsa`) and `EdDSA` (Ed25519/OKP, via **BouncyCastle** — pure-managed so it works in a Blazor WASM host where libsodium P/Invoke does not). `DeviceDelegationIssuer` emits the **honest** header `alg` from the holder key type (`EdDSA` for Ed25519) — a hardcoded `ES256` over an Ed25519 signature is unverifiable and was the cause of *"Delegation credential signature verification failed against holder key."* on default (Ed25519) wallets.

The decoded key facts ride the **Feature 155 verdict trail** (`VerificationOutcome.Layers`), not a separate structure: the `LivePresentation` layer's `Detail` carries `holder-key` (`"OKP / Ed25519"` or `"EC / P-256"`) and `delegation` (`"{alg} · device key {kty/crv}"`), alongside the existing `IssuerSignature` layer's `alg`. The **Open Verifier PWA** (`Sorcha.Verifier`) renders every layer's `Detail` dictionary generically (`Outcome.razor`), so an operator reads `holder-key  OKP / Ed25519` in the "Live presentation" panel with no browser dev tools — no bespoke diagnostic surface required.

> ⚠ Latent (deferred): `DeviceEnrolmentResponse.HolderPublicJwk` is still typed `EcP256PublicJwk` and coerces an Ed25519 holder JWK to a `Y=""` P-256 shape (`CitizenWalletEndpoints.ParseHolderJwk`). It is a *verifier-convenience copy that no consumer reads* — the verifier takes the holder key from the credential's `cnf.jwk`, not this field — so it is not on the failure path. Widen it to a faithful JWK when a consumer actually needs it.

## MAUI Blazor UI

The **server** already has `Sorcha.Wallet.Service.Credentials.ICredentialStore`. The UI needs a separate render-mode-agnostic abstraction — use `ICredentialUiStore` under `Sorcha.UI.Core/Services/Credentials/` to avoid naming collision. Platform services (`SecureStorage`, biometrics) hide behind `IBiometricGate` and `IQrScanner`. Razor components never touch MAUI APIs directly.

```csharp
// Razor shell (render-mode agnostic — works in InteractiveServer and InteractiveWebAssembly)
@inject ICredentialUiStore Store
@inject IBiometricGate Biometric
@inject ISdJwtService SdJwt

<CredentialList Credentials="_credentials" OnPresent="HandlePresentAsync" />

@code {
    private IReadOnlyList<StoredCredentialSummary> _credentials = [];

    protected override async Task OnInitializedAsync()
        => _credentials = await Store.ListAsync();

    private async Task HandlePresentAsync(StoredCredentialSummary selected, PresentationRequest request)
    {
        if (!await Biometric.UnlockAsync("Confirm to present credential"))
            return;

        // Build the SD-JWT presentation directly via ISdJwtService — there is no
        // server-side "build" endpoint; the server exposes CreateRequestAsync /
        // FindMatchingCredentialsAsync / SubmitPresentationAsync on PresentationRequestService.
        var compactToken = await Store.GetRawTokenAsync(selected.Id);
        var presentation = await SdJwt.CreatePresentationAsync(
            rawToken: compactToken,
            claimsToDisclose: request.RequestedClaimNames,
            holderKey: await Store.GetHolderKeyAsync(selected.Id),
            audience: request.VerifierDid,
            nonce: request.Nonce);

        Navigation.NavigateTo($"/present/qr?token={Uri.EscapeDataString(presentation.Compact)}");
    }
}
```

Platform registration:
- **MAUI** host → `MauiCredentialUiStore` (wraps `Microsoft.Maui.Storage.SecureStorage`) + `MauiBiometricGate` (wraps `Plugin.Fingerprint`)
- **WASM** host → `IndexedDbCredentialUiStore` + `NoOpBiometricGate`

See `references/maui-ui.md` for full component set, QR/deep-link flows, and Playwright test harness.

## Credential lifecycle — the operational contract (learned the hard way, 2026-08-17)

The endpoints are `POST /api/v1/credentials/{id}/{revoke|suspend|reinstate|refresh}` in
`Sorcha.Blueprint.Service/Endpoints/CredentialEndpoints.cs`. All four go through
`GetAndVerifyIssuer`, so a defect there kills the whole lifecycle surface at once — which is exactly
what happened (#1475: every one of them returned a **bodiless 404** for credentials that existed).

### Two status-list specs, and they disagree about suspension

Sorcha serves both rails. They model suspension incompatibly, so one must be a projection —
**W3C is what Sorcha stores; the IETF view is derived at serve time.**

| | W3C Bitstring Status List | IETF Token Status List |
|---|---|---|
| suspension is… | a separate **list** with its own `statusPurpose` | a distinct **value** in one list |
| encoding | 1 bit per entry | `bits` ∈ {1,2,4,8}; **≥2 required** for SUSPENDED |
| values | bit set/unset, meaning from the list's purpose | `0x00` VALID, `0x01` INVALID, `0x02` SUSPENDED |

W3C, verbatim: `revocation` "is not reversible"; `suspension` "is reversible". A credential may carry
several `credentialStatus` entries with different purposes, and a verifier MUST raise
`STATUS_VERIFICATION_ERROR` if the purpose it checks is absent from the list.

**Two rules that follow, both learned the expensive way:**

- **Never relabel a bit width — re-encode.** Declaring `bits: 2` is a claim about byte layout. Handing
  a 1-bit array over while declaring 2 makes a reader take entry N from bits 2N..2N+1, so revoking
  index 1 reports index 0 as not-valid: a status invented for a credential nobody touched.
  `IetfStatusListPacker` projects the two W3C lists into a real 2-bit array.
- **Revocation is terminal in BOTH specs.** Nothing may clear a revocation bit. A reinstate clears
  suspension only, and a revocation list must IGNORE an `Active` event rather than un-revoke.

And when folding status events from the ledger, decide the purpose by the **status word**, not by the
list id the event names — events written before the purposes were split all name the revocation list,
suspensions included.

### The state machine

| Operation | Requires | Effect |
|---|---|---|
| `revoke` | `Active` or `Suspended` | terminal — nothing reinstates a revoked credential |
| `suspend` | `Active` | reversible |
| `reinstate` | **`Suspended` only** | back to `Active` |

**Revocation is terminal by design** — `reinstate` refuses anything that is not `Suspended`. Do not
"fix" that by widening it.

**Suspend and revoke have had separate lists since #1491** — one list per purpose sharing one index,
issuance emitting one `credentialStatus` entry per purpose (array form only when a suspension list
exists, so single-purpose credentials keep the old object shape). The suspension bit is **not
monotonic** (reinstate clears it), so anything projecting these events must apply them in **ledger
order**, or two nodes converge on opposite answers.

### Suspension is reported as suspension (Feature 192)

The full chain now carries which status applied — `CredentialStatusValue { Valid, Invalid, Suspended,
Unresolved }` on `IStatusListChecker` (the old `StatusListBit` tri-state is gone), then
`TrustFailureReason.Suspended` → `CredentialFailureReason.Suspended` →
`PresentationDeclineReason.Suspended`.

- **`Unresolved` is a first-class answer, never a status.** "I could not tell" is a third thing:
  an unreachable list, and also an IETF value of `0x03`+ (reserved, application-specific). Claiming
  "revoked" off a value you cannot interpret is a false accusation. It routes to the fail-closed
  policy, which still refuses — it just stops us saying why.
- **Precedence is `Invalid > Suspended > Unresolved > Valid`, and every reference is read before
  anything is decided.** Returning on the first non-Valid answer would make the reported reason
  depend on the order the credential happens to list its entries in.
- **A resolved status outranks an unresolved one.** Suspension set + revocation list unreachable ⇒
  refuse as *suspended*, not as "could not check".

⚠ **Adding a member to any of these enums is NOT compiler-checked.** Every downstream consumer tests
**equality**, not an exhaustive switch — `MdocPresentationVerifier`, `HaipPresentationVerifier`, both
`HaipPresentationConsumer` copies, `SorchaWalletPresentationConsumer`. A new member compiles clean
and falls silently to the else branch: before F192 wired them up, a suspension would have made mdoc
report `StatusCheckResult = null` (*no status problem*) and the consumer return `VerifierError` (*the
verifier broke*) — both **worse** than the revocation they used to claim, and both green in CI. Grep
the consumers; do not trust the build.

⚠ **The refusal message's plain status word is load-bearing.** Those consumers substring-match it for
"revoked" / "suspended". #1495 changed the message to `Credential status '{purpose}' is set.`, which
contains neither word and therefore matched nothing. Keep the word in the message.

⚠ **There is a THIRD status rail** — `IStatusListCache` / `StatusListVerdict` in
`Sorcha.Verifier.Engine`, used for the delegation credential on the F127 local route. It is still a
tri-state, publishes **LSB-first** where both specs and every other Sorcha component are MSB-first,
and ignores `bits` entirely. Issue **#1499**; do not assume a fix on the other two rails reached it.

### Two different wire shapes for one credential — do not conflate them

- **Issuance** returns `CredentialIssuanceResult` — `credentialId`, `claims` (an object).
- **Reading** a stored credential returns the persisted `CredentialEntity` — **`id`**, **`claimsJson`**
  (a serialised *string*), plus `statusListUrl` / `statusListIndex` which may be EMPTY even though
  the SD-JWT itself carries a complete `credentialStatus`.

Deserialising a read into the issuance type throws on required members, and if that throw is
swallowed the caller reports "not found" for a credential that plainly exists. Use
`WalletCredentialRecord` (`Sorcha.ServiceClients.Http/Wallet/`) for reads and map; never relax
`CredentialIssuanceResult` to accommodate the read shape.

### Status values are kebab-case-lower on the wire

`CredentialStatus` serialises through `SorchaJson.Options`, whose enum converter is
`JsonNamingPolicy.KebabCaseLower`. The wire carries **`"active"`**, **`"pending-acceptance"`** —
never `"Active"`. Any gate written as an exact match against PascalCase
(`Status is not ("Active" or "Suspended")`) can never match. Normalise in the mapping layer, and
keep the gate strict — it is a real authorisation decision.

### Where the truth lives (and where it does not)

Revocation state currently has **three homes that can disagree**: the wallet credential row
(durable), the bitstring status list (**process memory only** — lost on restart, #1482), and a
`CredentialStatusChange` register transaction. The register is the one that replicates across nodes,
so it is the source of truth; treat the bitstring as a cache that must be rebuildable from the ledger.

### Two behaviours worth knowing before you debug

- **Revocation is enforced, synchronously.** A revoked credential presented to a credential-gated
  action is refused with **HTTP 400** at submit time — not the 202-then-never-seal shape a *schema*
  violation takes. If you see a 202 and a seal, revocation was not the thing that refused.
- **The wallet's default credential listing returns `Active` only.** A holder cannot casually present
  a revoked credential; you must ask for `?status=All` to see one. Good default — but it means a test
  that thinks it is presenting a revoked credential may be presenting nothing, or something else
  entirely (see the `walkthrough-builder` skill).

## Common Pitfalls

- **Do not** use Data Integrity Proofs / JSON-LD canonicalisation. Sorcha chose SD-JWT VC. `DataIntegrityProof`, `eddsa-rdfc-2022`, and RDF canonicalisation are **not** in the codebase and should not be added.
- **Do not** hand-roll `publicKeyMultibase` — call `Multicodec.ToMultibasePublicKey(algorithm, keyBytes)`. Feature 093 exists because someone did this wrong before.
- **Do not** use `Newtonsoft.Json`. All VC serialisation is `System.Text.Json` with `JsonDefaults.Api` on the wire (see `CLAUDE.md` § Critical Pattern 4 — JsonSchema.Net expects `JsonElement`).
- **Do not** sign from the root wallet key. VC issuance uses the org's `KeyUsage.VCIssuance` key (Feature 083 slot 1) via `IIssuanceKeyService` (Feature 120) — there is **no** `"sorcha:vc-issuance"` derivation-purpose string (that label does not exist in the codebase; key selection is by `KeyUsage`).
- **Do not** put `HttpClient` calls inside `CredentialVerifier`. Revocation lookups go through `IRevocationChecker`; the WASM-friendly in-memory implementation is what makes offline verification bundles (feature 079) possible.
- **Do not** cache DID documents indefinitely. Validator key rotation (feature 086) must invalidate cached documents. Use `IMemoryCache` with a `CancellationChangeToken` driven off `IValidatorKeyCache.OnRotated`.
- **Do not** default `RevocationCheckPolicy` to `FailOpen` — feature 093 made `FailClosed` the default for a reason.
- **Do not** deserialise a *stored* credential into `CredentialIssuanceResult` — that is the issuance
  shape. The read shape is `CredentialEntity` (`id`, `claimsJson`); use `WalletCredentialRecord`.
  Conflating them killed revoke/suspend/reinstate/refresh platform-wide (#1475).
- **Do not** compare a credential status with an exact PascalCase literal. The wire is
  kebab-case-lower (`"active"`, `"pending-acceptance"`), so `Status is "Active"` never matches.
- **Do not** treat `null` from a credential lookup as "not found". Distinguish a genuine 404 from a
  failed read — collapsing them reports absence for a credential that exists and sends the next
  person hunting in the wrong place.
- **Do not** assume the status-list bit survives a restart. `StatusListManager` holds lists in
  process memory (#1482); after a restart every status URL 404s and, because fail-closed genuinely
  works, **every credential-gated action refuses**.
- **Do not** redesign the `CredentialIssuanceConfig` / `CredentialRequirement` / `CredentialPresentation` types as records. They are mutable classes by deliberate choice for `DataAnnotations` interop.

## See Also

- [patterns](references/patterns.md) — SD-JWT layout, `ISdJwtService` usage, Multicodec encoding, DID document assembly
- [workflows](references/workflows.md) — Issue, present, verify, revoke, resolve flows with the real API signatures
- [maui-ui](references/maui-ui.md) — MAUI Blazor credential wallet components, QR/deep-link flows, SecureStorage + biometric gate

## Related Skills

- **cryptography** — Ed25519 / P-256 signing primitives under every proof
- **nbitcoin** — HD derivation paths, including the VC issuance purpose node
- **blueprint-builder** — `credentialIssuance` / `credentialRequirements` action fields
- **blazor** — Component structure for `InteractiveServer` + `InteractiveWebAssembly`
- **sorcha-ui** — Credential wallet pages and Playwright tests
- **walkthrough-builder** — `SelfBuildHouse` walkthrough exercises cross-register VCs

## Documentation Resources

> W3C and IETF specs are authoritative. Fetch with Context7 — do not rely on blog posts.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for the spec (e.g. `"sd-jwt vc"`, `"w3c did core"`)
2. Prefer website documentation (IDs starting with `/websites/`) — spec pages are ground truth
3. Query with `mcp__context7__query-docs` using the resolved library ID

**Library IDs:**
- `/websites/w3c_tr_vc-data-model-2_0` — VC Data Model 2.0
- `/websites/w3c_tr_did-core` — DID Core 1.0
- `/websites/w3c_tr_vc-bitstring-status-list` — Bitstring Status List
- `/websites/datatracker_ietf_org_doc_html_draft-ietf-oauth-sd-jwt-vc` — SD-JWT VC profile
- `/websites/datatracker_ietf_org_doc_html_rfc9901` — RFC 9901 (SD-JWT)

**Recommended Queries:**
- "sd-jwt vc disclosure paths holder key binding"
- "verifiable credential data model 2.0 required properties"
- "did core resolution algorithm"
- "bitstring status list credential status"
- "multicodec varint public key ed25519"

**NuGet landscape (2026):**
- `IdentityModel` — useful for OAuth/OIDC token primitives; does **not** implement SD-JWT VC
- `DIF.DIDCore` — partial DID Core support; watch for .NET 10 compatibility
- `Jose-JWT` — JWS/JWT primitives; Sorcha uses it under `SdJwtService`
- `SimpleBase` — base58btc encoding, used by `Multicodec`
- No complete SD-JWT VC implementation exists — `Sorcha.Cryptography.SdJwt` + `Sorcha.Blueprint.Engine/Credentials/` is the in-house solution and must track the spec directly
