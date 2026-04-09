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
    public CredentialDisplayConfig? Display { get; set; }
    public TimeSpan? Validity { get; set; }
    // ... plus status list binding
}

public class ClaimMapping
{
    public string Source { get; set; } = string.Empty;  // JSON pointer into processed action data
    public string Target { get; set; } = string.Empty;  // Dot path in VC credentialSubject
    public bool Selective { get; set; } = true;         // Whether this claim is SD
}
```

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
    public string? KeyBindingJwt { get; set; }
}
```

## SD-JWT Service Usage

`Sorcha.Cryptography.SdJwt.ISdJwtService` is the one API surface for SD-JWT operations. Do not import a second SD-JWT library.

```csharp
public interface ISdJwtService
{
    Task<SdJwtToken> CreateAsync(
        IReadOnlyDictionary<string, object> claims,
        IReadOnlySet<string> selectivelyDisclosableClaims,
        string issuerDid,
        string subjectDid,
        byte[] signingKey,
        string algorithm,
        CancellationToken ct = default);

    Task<SdJwtPresentation> PresentAsync(
        SdJwtToken token,
        IReadOnlySet<string> claimsToReveal,
        string audience,
        string nonce,
        byte[] holderSigningKey,
        CancellationToken ct = default);

    Task<SdJwtVerificationResult> VerifyAsync(
        string compactPresentation,
        string expectedAudience,
        string expectedNonce,
        IDidResolverRegistry didResolver,
        CancellationToken ct = default);
}
```

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
        // 1. Apply claim mappings → flat dictionary
        var claims = ApplyMappings(config.ClaimMappings, processedData);

        // 2. Identify selectively disclosable claims from the mapping flags
        var sdClaims = config.ClaimMappings
            .Where(m => m.Selective)
            .Select(m => m.Target)
            .ToHashSet();

        // 3. Create the SD-JWT — SdJwtService handles the _sd hashes + canonicalisation
        var token = await sdJwt.CreateAsync(
            claims, sdClaims, issuerDid, recipientDid, signingKey, algorithm, ct);

        return new IssuedCredentialInfo
        {
            CredentialId = token.CredentialId,
            CredentialType = config.CredentialType,
            CompactToken = token.Compact,
            IssuedAt = token.IssuedAt,
            ExpiresAt = token.ExpiresAt,
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

| Algorithm | Multicodec | Varint bytes |
|-----------|------------|--------------|
| Ed25519   | `0xed`    | `ed 01`      |
| P-256     | `0x1200`  | `80 24`      |
| secp256k1 | `0xe7`    | `e7 01`      |

### API

```csharp
// Returns null for PQC (ML-DSA, SLH-DSA, ML-KEM) — no assigned multicodec.
string? multibase = Multicodec.ToMultibasePublicKey("EdDSA", ed25519PublicKey);

// Decode the other direction — strips the "z" prefix and varint.
byte[]? raw = Multicodec.DecodePublicKeyBytes(multibase);

// Build just the varint + key (without the "z" + base58btc wrapper).
byte[]? prefixed = Multicodec.EncodePublicKey("ES256", p256PublicKey);
```

### When `ToMultibasePublicKey` returns null

The caller must choose:

1. **Fall back to `publicKeyJwk`** — emit a JWK-shaped verification method instead.
2. **Fail closed** — refuse to issue / resolve, per FR-014 in `specs/093-vc-security-fixes/spec.md`.

Never fall back to a hand-rolled multibase encoding — that is exactly the bug feature 093 fixes.

## Verification Result

The real type:

```csharp
public class CredentialValidationResult
{
    public bool IsValid { get; set; }
    public IReadOnlyList<CredentialValidationError> Errors { get; set; } = [];
    public IReadOnlyList<CredentialValidationWarning>? Warnings { get; set; }
}
```

Accumulate errors — do not short-circuit on the first one. `CredentialValidationError` carries a `Code` + `Message` + optional `ClaimPath`. The SelfBuildHouse walkthrough asserts against multiple simultaneous errors.

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
