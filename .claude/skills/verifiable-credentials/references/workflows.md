# VC Workflows — Issue, Present, Verify, Revoke, Resolve

End-to-end flows mapped to the **real** Sorcha APIs. Read this when planning a new VC feature — it shows which service owns which step, and which interface to call.

## 1. Issue a VC (via blueprint action)

```
Blueprint action executes
   │
   ▼
ActionExecutionService detects action.CredentialIssuance
   │
   ▼
CredentialIssuer.IssueAsync(config, processedData, issuerDid, recipientDid, signingKey, algorithm)
   │  ├─► Wallet.Service:    derive signing key under "sorcha:vc-issuance"
   │  ├─► SdJwtService:      build compact SD-JWT with selective disclosure hashes
   │  ├─► Register.Service:  allocate BitstringStatusList entry
   │  └─► Wallet.Service:    persist issued VC to recipient's CredentialStore
   ▼
IssuedCredentialInfo returned (credentialId + compact token)
```

### The real API call

```csharp
public sealed class ActionCredentialMinter(
    ICredentialIssuer issuer,
    IWalletServiceClient wallet,
    ILogger<ActionCredentialMinter> logger)
{
    public async Task<IssuedCredentialInfo> MintForActionAsync(
        ActionExecutionContext ctx,
        CredentialIssuanceConfig config,
        CancellationToken ct)
    {
        var derived = await wallet.DeriveIssuerKeyAsync(
            ctx.IssuerWalletAddress, "sorcha:vc-issuance", ct);

        try
        {
            return await issuer.IssueAsync(
                config,
                ctx.ProcessedData,
                issuerDid: $"did:sorcha:org:{ctx.IssuerWalletAddress}",
                recipientDid: $"did:sorcha:w:{ctx.RecipientWalletAddress}",
                signingKey: derived.PrivateKey,
                algorithm: derived.Algorithm,
                cancellationToken: ct);
        }
        finally
        {
            // Zeroize private key buffer — crypto hygiene
            Array.Clear(derived.PrivateKey);
        }
    }
}
```

### Required wiring

- `ActionExecutionService.Implementation/ActionExecutionService.cs` already calls the issuer when `action.CredentialIssuance` is present — do not add a parallel path.
- The endpoint pair lives in the Blueprint Service; rate-limit via the shared `RateLimitPolicies.Api` policy from ServiceDefaults.
- Use `JsonDefaults.Api` for wire deserialisation. Never construct a fresh `JsonSerializerOptions`.
- Update `specs/093-vc-security-fixes/` if the change touches DID resolution, signing keys, or multibase encoding.

## 2. Present a VC

```
Verifier ──[PresentationRequest QR]──▶ Holder (Sorcha.UI / MAUI)
                                              │
                         Holder picks credentials to disclose
                                              │
                                              ▼
                    PresentationRequestService.BuildPresentationAsync
                                              │
                                              ▼
                       SdJwtService.PresentAsync (compact form)
                                              │
                         ┌────────────────────┴──────────────────┐
                         │ <issuer jwt>~<d1>~<d2>~...~<kb-jwt>    │
                         └────────────────────┬──────────────────┘
                                              ▼
                                          Verifier
                                              │
                         SdJwtService.VerifyAsync + CredentialVerifier
```

### Presentation request shape

```csharp
// Sorcha.Blueprint.Models.Credentials or Sorcha.Wallet.Core depending on direction
public sealed record PresentationRequest(
    string Id,
    string VerifierDid,
    IReadOnlyList<PresentationClaimRequest> RequestedClaims,
    string Nonce,
    DateTimeOffset ExpiresAt);

public sealed record PresentationClaimRequest(
    string CredentialType,
    IReadOnlyList<string> ClaimPaths,
    bool Optional);
```

### Building the presentation (holder)

```csharp
public async Task<string> BuildPresentationAsync(
    PresentationRequest request,
    IReadOnlyList<StoredCredential> selected,
    CancellationToken ct)
{
    // Current implementation: Sorcha.Wallet.Service.Services.PresentationRequestService
    //
    // 1. For each selected credential, determine which claims to reveal.
    // 2. Call SdJwtService.PresentAsync with those claim names + the verifier's nonce.
    // 3. Return the compact string directly — no VP wrapping envelope.

    if (selected.Count == 1)
    {
        var credential = selected[0];
        var reveal = request.RequestedClaims
            .First(c => c.CredentialType == credential.CredentialType)
            .ClaimPaths
            .ToHashSet();

        var presentation = await _sdJwt.PresentAsync(
            credential.Token,
            claimsToReveal: reveal,
            audience: request.VerifierDid,
            nonce: request.Nonce,
            holderSigningKey: _holder.SigningKey,
            ct);

        return presentation.Compact;
    }

    // Multi-credential case uses a JSON envelope holding multiple compact forms.
    // See PresentationRequestService for the current shape.
    throw new NotImplementedException("Multi-credential path lives in PresentationRequestService.");
}
```

The nonce from the request is embedded in the holder's key-binding JWT — the verifier rejects any presentation whose KB-JWT does not bind the same nonce. Feature 093 hardened this (`PresentationRequestVerificationTests.cs`).

## 3. Verify a presentation

### Unified verification entry point (real signature)

```csharp
public interface ICredentialVerifier
{
    Task<CredentialValidationResult> VerifyAsync(
        IEnumerable<CredentialRequirement> requirements,
        IEnumerable<CredentialPresentation> presentations,
        CancellationToken cancellationToken = default);
}
```

The verifier internally:

1. **Parses** each `CredentialPresentation.RawPresentation` via `SdJwtService.VerifyAsync`.
2. **Resolves** the issuer DID via `IDidResolverRegistry` to obtain the signing key.
3. **Verifies** the issuer JWT signature against the resolved `VerificationMethod`.
4. **Recomputes** each disclosure hash and checks membership in the `_sd` array.
5. **Verifies** the holder key-binding JWT — signature, audience, nonce.
6. **Checks** `validFrom`/`validUntil`.
7. **Checks** status via `IRevocationChecker` honouring `requirement.RevocationCheckPolicy` (default: `FailClosed`).
8. **Evaluates** each `ClaimConstraint` in the requirement against the disclosed claims.

Accumulate errors into `CredentialValidationResult.Errors` — short-circuiting hides compound failures. Feature 093's test suite depends on multi-error reporting.

### Calling it

```csharp
var result = await _verifier.VerifyAsync(
    action.CredentialRequirements,
    payload.CredentialPresentations,
    ct);

if (!result.IsValid)
{
    return Results.Problem(new ProblemDetails
    {
        Title = "Credential verification failed",
        Status = StatusCodes.Status403Forbidden,
        Extensions = { ["errors"] = result.Errors },
    });
}
```

## 4. Revoke a VC

Revocation is **not** a DB flag — it is a bitstring status list update plus a revocation transaction.

```csharp
public async Task RevokeAsync(
    string credentialId,
    RevocationReason reason,
    string issuerOrgId,
    CancellationToken ct)
{
    // 1. Resolve the status list entry allocated at issuance time.
    var entry = await _statusListStore.GetEntryAsync(credentialId, ct);

    // 2. Flip the bit. Each status list credential packs ~131K entries.
    await _statusListStore.SetBitAsync(entry.ListCredentialId, entry.Index, true, ct);

    // 3. Submit a revocation transaction to the Register Service (feature 079 API).
    await _register.SubmitRevocationAsync(new RevocationPayload
    {
        TargetTransactionId = credentialId,
        Reason = reason,
        IssuerOrgId = issuerOrgId,
    }, ct);
}
```

Use the existing `RevocationReason` enum from feature 079. Do not add credential-specific reasons:

- `Superseded`, `Erroneous`, `Compromised`, `Expired`, `Withdrawn`, `Regulatory`

## 5. Resolve a DID

Go through `IDidResolverRegistry`. Never write a bespoke resolver for `did:sorcha:*`.

```csharp
public sealed class MyAuditService(IDidResolverRegistry didResolver, ILogger<MyAuditService> logger)
{
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct)
    {
        var doc = await didResolver.ResolveAsync(did, ct);
        if (doc is null)
            logger.LogWarning("DID {Did} did not resolve", did);
        return doc;
    }
}
```

### The three Sorcha DID shapes

- `did:sorcha:org:{walletAddress}` — resolved by walking the organisation's administrative wallet to its registered public keys.
- `did:sorcha:w:{walletAddress}` — resolved by the wallet service (public key metadata).
- `did:sorcha:r:{registerId}:t:{txId}` — resolved by fetching the target register transaction (useful for "the key that signed this specific action").

`SorchaDidResolver.ResolveAsync` dispatches on the prefix. `WebDidResolver` and `KeyDidResolver` handle `did:web:*` and `did:key:*` for interop.

### Cache invalidation

Validator key rotation (feature 086) is a cache-bust signal. If caching `DidDocument` values, subscribe to `IValidatorKeyCache.OnRotated` and invalidate entries whose `Id` matches the rotated register's DID. Use `IMemoryCache` with a `CancellationChangeToken` driven off the rotation event.

## Test Scaffolding

Every VC workflow needs an integration test that exercises the full loop. Existing harnesses to reuse:

- `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs` — presentation verification, nonce binding, revocation fail-closed. Feature 093 added these.
- `tests/Sorcha.ServiceClients.Tests/Did/SorchaDidResolverTests.cs` — DID resolution success + error paths.
- `tests/Sorcha.ServiceClients.Tests/Utilities/MulticodecTests.cs` — multibase encoding round trips.
- `tests/Sorcha.Blueprint.Engine.Tests/Credentials/` — issuer and verifier unit tests.

When adding a new test, prefer extending those harnesses. Mock `IRegisterClient`, `IParticipantServiceClient`, `IDidResolverRegistry`, and Redis through `WebApplicationFactory<Program>` (see the `CLAUDE.md` testing notes — there is a "mock Redis + `VerifySignatureAsync`" convention baked into memory).

```csharp
[Fact]
public async Task IssueThenVerify_RoundTrip_Succeeds()
{
    var factory = new SorchaWebApplicationFactory();
    using var client = factory.CreateClient();

    // Issue — calls the Blueprint Service issue endpoint
    var issueResponse = await client.PostAsJsonAsync(
        "/api/credentials/issue",
        TestData.GraduationConfig,
        JsonDefaults.Api);
    issueResponse.EnsureSuccessStatusCode();
    var issued = await issueResponse.Content
        .ReadFromJsonAsync<IssuedCredentialInfo>(JsonDefaults.Api);

    // Present — build the compact form via SdJwtService directly in the test
    var presentation = await _sdJwt.PresentAsync(
        SdJwtToken.FromCompact(issued!.CompactToken),
        claimsToReveal: new HashSet<string> { "name", "graduationDate" },
        audience: "did:sorcha:org:verifier-wallet",
        nonce: TestData.Nonce,
        holderSigningKey: TestData.HolderKey);

    // Verify
    var verifyResponse = await client.PostAsJsonAsync(
        "/api/credentials/verify",
        new
        {
            requirements = TestData.GraduationRequirements,
            presentations = new[]
            {
                new CredentialPresentation
                {
                    CredentialId = issued.CredentialId,
                    RawPresentation = presentation.Compact,
                    DisclosedClaims = presentation.DisclosedClaims,
                }
            }
        },
        JsonDefaults.Api);

    var result = await verifyResponse.Content
        .ReadFromJsonAsync<CredentialValidationResult>(JsonDefaults.Api);
    result!.IsValid.Should().BeTrue();
    result.Errors.Should().BeEmpty();
}
```
