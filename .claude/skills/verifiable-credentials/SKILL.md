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
| `PresentationRequestService` | `src/Services/Sorcha.Wallet.Service/Services/` | Builds SD-JWT presentations with selective disclosure |

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

The signing key is retrieved via the Wallet Service using the `"sorcha:vc-issuance"` derivation purpose — never the root wallet key. Mirror the feature 086 docket-signing convention.

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
    public string CredentialType { get; set; } = string.Empty;
    public IEnumerable<ClaimMapping> ClaimMappings { get; set; } = [];
    public string RecipientParticipantId { get; set; } = string.Empty;
    // ... plus display config, status list binding, validity window
}

// Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs
public class CredentialRequirement
{
    public string Type { get; set; } = string.Empty;
    public IEnumerable<string>? AcceptedIssuers { get; set; }
    public IEnumerable<ClaimConstraint>? RequiredClaims { get; set; }
    public RevocationCheckPolicy RevocationCheckPolicy { get; set; }
}

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

## Selective Disclosure (SD-JWT)

SD-JWT compact form: `<issuer JWT>~<disclosure1>~<disclosure2>~...~<key binding JWT>`.

- **Issuer** (`SdJwtService.CreateAsync`): picks which claims are selectively disclosable, hashes each disclosure, embeds the hashes in the JWT payload under `_sd`.
- **Holder** (`SdJwtService.PresentAsync`): strips disclosures not requested by the verifier, signs a key-binding JWT with nonce + audience.
- **Verifier** (`SdJwtService.VerifyAsync`): verifies issuer signature, recomputes disclosure hashes, verifies key-binding JWT, checks status list.

`PresentationRequestService` in the Wallet Service is the holder-side orchestrator. Feature 093 added stricter verification (nonce binding, audience check, revocation fail-closed) in `PresentationRequestVerificationTests.cs` — read those tests to understand the contract.

## Blueprint Integration

Actions carry credential configs as first-class fields (not an `x-*` schema extension). The real JSON shape uses `claimName` + `sourceField` on each mapping, and the set of selectively-disclosable claims is declared **once** on the config via the `disclosable` array:

```json
{
  "actionId": "issue-graduation",
  "credentialIssuance": {
    "credentialType": "GraduationCredential",
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
      "acceptedIssuers": ["did:sorcha:org:ws1q..."],
      "revocationCheckPolicy": "FailClosed"
    }
  ]
}
```

`expiryDuration` is an ISO 8601 duration string (`P10Y`, `P90D`), not a `TimeSpan`.

`ActionExecutionService` reads `CredentialRequirements` before the action runs and `CredentialIssuance` after. No custom per-blueprint credential code.

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

## Common Pitfalls

- **Do not** use Data Integrity Proofs / JSON-LD canonicalisation. Sorcha chose SD-JWT VC. `DataIntegrityProof`, `eddsa-rdfc-2022`, and RDF canonicalisation are **not** in the codebase and should not be added.
- **Do not** hand-roll `publicKeyMultibase` — call `Multicodec.ToMultibasePublicKey(algorithm, keyBytes)`. Feature 093 exists because someone did this wrong before.
- **Do not** use `Newtonsoft.Json`. All VC serialisation is `System.Text.Json` with `JsonDefaults.Api` on the wire (see `CLAUDE.md` § Critical Pattern 4 — JsonSchema.Net expects `JsonElement`).
- **Do not** sign from the root wallet key. Use derivation purpose `"sorcha:vc-issuance"` via `SignTransactionAsync(..., "sorcha:vc-issuance", isPreHashed: true)`.
- **Do not** put `HttpClient` calls inside `CredentialVerifier`. Revocation lookups go through `IRevocationChecker`; the WASM-friendly in-memory implementation is what makes offline verification bundles (feature 079) possible.
- **Do not** cache DID documents indefinitely. Validator key rotation (feature 086) must invalidate cached documents. Use `IMemoryCache` with a `CancellationChangeToken` driven off `IValidatorKeyCache.OnRotated`.
- **Do not** default `RevocationCheckPolicy` to `FailOpen` — feature 093 made `FailClosed` the default for a reason.
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
