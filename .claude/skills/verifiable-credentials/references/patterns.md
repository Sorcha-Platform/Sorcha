# VC Patterns & Type Reference

Detailed patterns for the real Sorcha VC stack: SD-JWT VC, `Sorcha.Blueprint.Models.Credentials` types, `DidDocument`, and the `Multicodec` utility. Read this when implementing or modifying anything in `Sorcha.Blueprint.Engine/Credentials/`, `Sorcha.Cryptography/SdJwt/`, or `Sorcha.ServiceClients.Http/Did/`.

## Format Choice — SD-JWT VC, not JSON-LD

Sorcha chose SD-JWT VC over JSON-LD + Data Integrity Proofs because:

1. **No RDF canonicalisation.** RDF canonicalisation is a dependency sinkhole. SD-JWT canonicalises JWT payloads the same way every other JWT library does — with JCS over the sorted object.
2. **Compact serialisation.** `<jwt>~<d1>~<d2>~<kb-jwt>` is URL-safe and fits in a QR code comfortably.
3. **Fits existing JWT stack.** `Sorcha.Cryptography.SdJwt` builds on the same JOSE primitives already used by `Sorcha.Tenant.Service` for platform JWTs.
4. **Native selective disclosure.** Per-claim hashing is baked into the format — no bolt-on layer.

If a proposal lands to add JSON-LD / Data Integrity Proofs, treat it as a material architectural change and flag it for review.

## Credential Model Types (real locations)

All live in `Sorcha.Blueprint.Models.Credentials/`. Mutable classes, `DataAnnotations` validation, `System.Text.Json` serialisation.

### CredentialIssuanceConfig

Describes how an action mints a VC. Consumed by `ICredentialIssuer.IssueAsync`.

```csharp
public class CredentialIssuanceConfig
{
    public string CredentialType { get; set; } = string.Empty;
    public IEnumerable<ClaimMapping> ClaimMappings { get; set; } = [];
    public string RecipientParticipantId { get; set; } = string.Empty;
    public string? ExpiryDuration { get; set; }              // ISO 8601 duration, e.g. "P10Y"
    public string? RegisterId { get; set; }
    public IEnumerable<string>? Disclosable { get; set; }    // Claim names that are selectively disclosable
    public UsagePolicy UsagePolicy { get; set; } = UsagePolicy.Reusable;
    public int? MaxPresentations { get; set; }
    public CredentialDisplayConfig? DisplayConfig { get; set; }
}

public class ClaimMapping
{
    public string ClaimName { get; set; } = string.Empty;   // Target key in the issued credential
    public string SourceField { get; set; } = string.Empty; // JSON pointer into processed action data
}
```

**Selective disclosure is a config-level list, not a per-mapping flag.** The `Disclosable` collection names which `ClaimName` values participate in selective disclosure. Claims not in `Disclosable` are always revealed.

### CredentialRequirement

Action precondition — the holder must present a VC matching this shape. Consumed by `ICredentialVerifier.VerifyAsync`.

```csharp
public class CredentialRequirement
{
    public string Type { get; set; } = string.Empty;
    public IEnumerable<string>? AcceptedIssuers { get; set; }
    public IEnumerable<ClaimConstraint>? RequiredClaims { get; set; }
    public RevocationCheckPolicy RevocationCheckPolicy { get; set; } = RevocationCheckPolicy.FailClosed;
}

public class ClaimConstraint
{
    public string ClaimPath { get; set; } = string.Empty;   // e.g. "/credentialSubject/age"
    public ClaimConstraintOperator Operator { get; set; }    // Exists, Equals, GreaterThan, ...
    public object? ExpectedValue { get; set; }
}
```

`RevocationCheckPolicy.FailClosed` is the default since feature 093 — do not change it.

### CredentialPresentation

Holder-submitted payload. The `RawPresentation` field carries the compact SD-JWT form.

```csharp
public class CredentialPresentation
{
    public string CredentialId { get; set; } = string.Empty;    // DID URI
    public Dictionary<string, object> DisclosedClaims { get; set; } = new();
    public string RawPresentation { get; set; } = string.Empty; // <jwt>~<d>~<d>~<kb-jwt>
    public string? KeyBindingProof { get; set; }                // Holder key-binding JWT
}
```

## SD-JWT Service Usage

`Sorcha.Cryptography.SdJwt.ISdJwtService` is the one API surface for SD-JWT operations. Do not import a second SD-JWT library. The real method names are `CreateTokenAsync` / `VerifyTokenAsync` / `CreatePresentationAsync` / `VerifyPresentationAsync` — not the bare `CreateAsync` / `PresentAsync` / `VerifyAsync` verbs.

```csharp
public interface ISdJwtService
{
    Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,                       // "EdDSA", "ES256", "RS256"
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);

    Task<SdJwtVerificationResult> VerifyTokenAsync(
        string rawToken,
        byte[] issuerPublicKey,
        string algorithm,
        CancellationToken cancellationToken = default);

    Task<SdJwtPresentation> CreatePresentationAsync(
        string rawToken,
        IEnumerable<string> claimsToDisclose,
        byte[]? holderKey = null,
        string? audience = null,
        string? nonce = null,
        CancellationToken cancellationToken = default);

    Task<SdJwtVerificationResult> VerifyPresentationAsync(
        string rawPresentation,
        byte[] issuerPublicKey,                 // NOT IDidResolverRegistry — caller must resolve first
        string algorithm,
        CancellationToken cancellationToken = default);
}
```

**Key implication:** `ISdJwtService` does **not** resolve DIDs itself. The caller is responsible for resolving the issuer DID via `IDidResolverRegistry`, extracting the matching verification method's public key bytes, and passing them in. `CredentialVerifier` is the orchestrator that wires those steps together.

### Issuer side

```csharp
public sealed class CredentialIssuer(ISdJwtService sdJwt) : ICredentialIssuer
{
    public async Task<IssuedCredentialInfo> IssueAsync(
        CredentialIssuanceConfig config,
        Dictionary<string, object> processedData,
        string issuerDid,
        string recipientDid,
        byte[] signingKey,
        string algorithm,
        CancellationToken ct = default)
    {
        // 1. Apply claim mappings → flat dictionary keyed by ClaimName
        var claims = config.ClaimMappings.ToDictionary(
            mapping => mapping.ClaimName,
            mapping => ResolveJsonPointer(processedData, mapping.SourceField));

        // 2. Disclosable claims come straight from config.Disclosable — not per-mapping flags.
        //    Pass null to make everything disclosable; pass an empty list to disclose nothing selectively.
        var disclosable = config.Disclosable;

        // 3. Compute expiry from the ISO 8601 duration string, if any.
        DateTimeOffset? expiresAt = config.ExpiryDuration is { } iso
            ? DateTimeOffset.UtcNow + XmlConvert.ToTimeSpan(iso)
            : null;

        // 4. Create the SD-JWT — SdJwtService handles the _sd hashes + canonicalisation
        var token = await sdJwt.CreateTokenAsync(
            claims, disclosable, issuerDid, recipientDid, signingKey, algorithm, expiresAt, ct);

        return new IssuedCredentialInfo
        {
            CredentialId = token.CredentialId,
            CredentialType = config.CredentialType,
            CompactToken = token.RawToken,
            IssuedAt = token.IssuedAt,
            ExpiresAt = expiresAt,
        };
    }
}
```

### Signing key sourcing

Never pass raw root wallet keys. The issuance endpoint resolves a derived key via the Wallet Service:

```csharp
var derived = await _wallet.DeriveKeyAsync(
    walletAddress: issuerWallet,
    derivationContext: "sorcha:vc-issuance",
    cancellationToken: ct);

var issuedInfo = await _credentialIssuer.IssueAsync(
    config, processedData, issuerDid, recipientDid,
    signingKey: derived.PrivateKey,
    algorithm: derived.Algorithm,
    ct);
```

Zero the key bytes after the call — `Sorcha.Cryptography` provides `.Zeroize()` on the key buffer.

## DID Document Assembly

`DidDocument` is a mutable class — build it imperatively, assign properties, then hand it back.

```csharp
private async Task<DidDocument> BuildOrgDidDocumentAsync(string walletAddress, CancellationToken ct)
{
    var keys = await _wallet.ListPublicKeysAsync(walletAddress, ct);

    var methods = new List<VerificationMethod>();
    foreach (var key in keys)
    {
        // key.Algorithm is a wallet algorithm string ("ED25519", "NIST-P256", "RSA-4096").
        var multibase = Multicodec.ToMultibasePublicKey(key.Algorithm, key.PublicKey);
        if (multibase is null)
        {
            // PQC algorithm — emit publicKeyJwk instead, or fail closed per FR-014.
            methods.Add(new VerificationMethod
            {
                Id = $"did:sorcha:org:{walletAddress}#{key.KeyId}",
                Type = "JsonWebKey2020",
                Controller = $"did:sorcha:org:{walletAddress}",
                PublicKeyJwk = JwkConverter.ToJsonElement(key),
            });
        }
        else
        {
            methods.Add(new VerificationMethod
            {
                Id = $"did:sorcha:org:{walletAddress}#{key.KeyId}",
                Type = "Ed25519VerificationKey2020",
                Controller = $"did:sorcha:org:{walletAddress}",
                PublicKeyMultibase = multibase,
            });
        }
    }

    return new DidDocument
    {
        Id = $"did:sorcha:org:{walletAddress}",
        VerificationMethod = methods,
        AssertionMethod = methods.Select(m => m.Id).ToList(),
        Authentication = methods.Select(m => m.Id).ToList(),
    };
}
```

Note the absence of `@context` — the Sorcha resolver deliberately omits it because JSON-LD is not in the stack. A JSON-LD verifier that requires `@context` can synthesise it on its own side.

## Multicodec Utility

Feature 093 introduced `Multicodec` in `Sorcha.ServiceClients.Http.Utilities` after the original DID resolver was emitting `"z" + Base64(publicKey)` — which is syntactically valid multibase but not what DID Core expects.

### The correct encoding

```
publicKeyMultibase = "z" || Base58Btc( multicodec_varint || rawKeyBytes )
```

| Algorithm string (pass to Multicodec) | Multicodec | Varint bytes |
|---------------------------------------|------------|--------------|
| `ED25519`                             | `0xed`    | `ed 01`      |
| `NIST-P256` / `P-256` / `P256` / `ECDSA-P256` | `0x1200` | `80 24` |
| `RSA` / `RSA-4096`                    | `0x1205`  | `85 24`      |

secp256k1 is **not** currently supported by `Multicodec` — do not add an entry for it unless you also extend the switch in `Multicodec.ResolveMulticodec`.

### API

```csharp
// Algorithm names match Sorcha wallet algorithm strings — NOT JOSE "alg" values.
// "EdDSA" and "ES256" both return null and fall through to the PQC-style fallback path.
string? multibase = Multicodec.ToMultibasePublicKey("ED25519", ed25519PublicKey);

// Decode the other direction — strips the "z" prefix and varint.
byte[]? raw = Multicodec.DecodePublicKeyBytes(multibase);

// Build just the varint + key (without the "z" + base58btc wrapper).
byte[]? prefixed = Multicodec.EncodePublicKey("NIST-P256", p256PublicKey);
```

### When `ToMultibasePublicKey` returns null

The caller must choose:

1. **Fall back to `publicKeyJwk`** — emit a JWK-shaped verification method instead.
2. **Fail closed** — refuse to issue / resolve, per FR-014 in `specs/093-vc-security-fixes/spec.md`.

Never fall back to a hand-rolled multibase encoding — that is exactly the bug feature 093 fixes.

## Verification Result

The real type lives in `Sorcha.Blueprint.Engine.Credentials` (not `Blueprint.Models.Credentials` — it references `Blueprint.Models` types but it is defined in the engine):

```csharp
public class CredentialValidationResult
{
    public bool IsValid { get; set; }
    public List<CredentialValidationError> Errors { get; set; } = new();
    public List<VerifiedCredentialDetail> VerifiedCredentials { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class VerifiedCredentialDetail
{
    public string CredentialId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string IssuerDid { get; set; } = string.Empty;
    public Dictionary<string, object> VerifiedClaims { get; set; } = new();
    public bool SignatureValid { get; set; }
    public string RevocationStatus { get; set; } = "Active";  // "Active" | "Revoked" | "Unknown"
}
```

`CredentialValidationError` (in `Sorcha.Blueprint.Models.Credentials`) carries `RequirementType` + `FailureReason` (enum) + `Message`. Accumulate errors — do not short-circuit on the first one. The SelfBuildHouse walkthrough asserts against multiple simultaneous errors. `Warnings` is a plain `List<string>` for non-fatal notes like "revocation check unavailable with fail-open policy".

## Test Fixtures

Look at existing tests before inventing fixtures:

- `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs` — feature 093 added comprehensive SD-JWT presentation verification tests covering nonce binding, audience checks, and revocation fail-closed behaviour. Reuse the fixture builders there.
- `tests/Sorcha.ServiceClients.Tests/Utilities/MulticodecTests.cs` — multicodec encoding round trips for Ed25519, P-256, secp256k1.
- `tests/Sorcha.ServiceClients.Tests/Did/SorchaDidResolverTests.cs` — DID resolution happy paths + error cases.

When adding a new test, prefer extending those harnesses over building fresh ones.

## Performance Notes

- SD-JWT creation is dominated by the JWT sign step — Ed25519 wins over P-256 by ~3x in `Sorcha.Cryptography` benchmarks. Default to `EdDSA` unless the caller needs P-256 for interop.
- `CredentialMatcher` (in `Sorcha.Wallet.Service.Credentials`) uses a precomputed index keyed by credential type. When adding new requirement types, update the matcher index rather than scanning the store linearly.
- Bitstring status lists compress to ~16KB for 131072 entries. Cache them aggressively in Redis with a 60-second TTL — see `RegisterService` for the existing cache wiring.
