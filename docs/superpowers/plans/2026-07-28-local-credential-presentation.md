# Local Credential Presentation on /app Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A citizen signed in on `/app` whose server-custody wallet holds a matching credential satisfies a `presentationSource: SorchaWallet` gate on the device they're using — no QR, no second device — by locally completing the existing F111/F127 async lifecycle.

**Architecture:** Zero server change. A new `ISorchaWalletLocalPresenter` in `Sorcha.UI.Components.User` reproduces, in the browser, exactly what `demos/AIAS/rehearse.ps1`'s `Complete-SorchaWalletPresentation` (lines 476-625) proved live on n1: fetch request-object JWT → match own credential → consent → export raw SD-JWT → strip undisclosed disclosures → `sd_hash` → server-custody KB-JWT via `POST /api/v1/wallet/presentations/sign-kb` → form-encoded direct_post of the DCQL envelope to the F127 callback. `PresentationRequestCard` renders a "Use this device" consent panel primary with the QR collapsed beneath; the existing `IPresentationSignal`/transport machinery observes the outcome unchanged. The pre-submit `CredentialGatePanel` stops blocking submission for async-source requirements.

**Tech Stack:** Blazor WASM (net10.0, C# 14), MudBlazor, `Sorcha.Verifier.Engine.Dcql` (already referenced by Components.User), xUnit v3 + FluentAssertions + Moq + bUnit.

**Spec:** `docs/superpowers/specs/2026-07-28-local-credential-presentation-design.md` (read it first).

## Global Constraints

- Branch: `fix/revert-unsigned-presentation-threading` (already carries the revert, brief, and spec). All work commits here.
- **NEVER `git add -A` / `git add .`** — the tree carries unrelated untracked files (`genesis-validator-key.json.bak-pre471`, `tests/Sorcha.UI.Core.Tests/Components/Forms/PostalAddressRenderRepro.cs`). Stage explicit paths only. Do not touch, delete, or commit those two files.
- `CredentialGatePanel.razor` has an **uncommitted working-tree modification** (a corrected comment — the evidence trail for #1330). Task 5 builds on top of it and commits it. Do not revert it.
- Every new `.cs`/`.razor` file starts with the license header: `// SPDX-License-Identifier: MIT` + `// Copyright (c) 2026 Sorcha Contributors` (Razor: `@* ... *@` form).
- `Sorcha.UI.Components.User` RootNamespace is **`Sorcha.UI.Core`** — files under `Services/User/Presentation/` use namespace `Sorcha.UI.Core.Services.User.Presentation`; components under `Components/Presentation/` use `@namespace Sorcha.UI.Core.Components.Presentation`.
- File-scoped namespaces; test naming `Method_Scenario_ExpectedBehavior`; import order System → Microsoft → third-party → Sorcha.
- `dotnet build` before `dotnet test` (stale DLLs → phantom fails). `dotnet test` takes ONE project path. The runner is MTP-based; to filter use `dotnet test <proj> -- --filter "..."` and fall back to running the whole project if filtering misbehaves.
- Test project for everything here: `tests/Sorcha.UI.Core.Tests` (references `Sorcha.UI.Core`, which re-exports `Sorcha.UI.Components.User`; bunit + Moq + FluentAssertions already referenced).
- String comparisons on claim names / vct / algorithms are **`StringComparison.Ordinal`** — never OrdinalIgnoreCase (vct matching is case-sensitive by design, #1187).
- Every new guard/test must be mutation-tested once: perturb the guarded thing, observe RED, restore. Note the perturbation in the commit message body.

---

### Task 1: `ISorchaWalletLocalPresenter` — contract, models, and `ProbeAsync`

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/ISorchaWalletLocalPresenter.cs`
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletLocalPresenter.cs`
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletLocalPresenterProbeTests.cs`

**Interfaces:**
- Consumes: `IHolderKeyClient.GetHolderKeysAsync(ct)` → `HolderKeysView { JsonElement HolderJwk, string EncryptionPublicKey, string Algorithm, string WalletAddress }` (`Sorcha.UI.Core.Services.HolderKeys`); `ICredentialApiService.MatchCredentialsAsync(string walletAddress, List<CredentialRequirement>, ct)` → `List<CredentialMatchResult>` (throws on non-success, #1324); `DcqlRequestParser.ParseFromRequestObjectPayload(JsonElement)` + `DcqlRequestParser.SplitClaims(DcqlCredentialQuery)` from `Sorcha.Verifier.Engine.Dcql`.
- Produces (Tasks 2-4 rely on these exact shapes):

```csharp
public interface ISorchaWalletLocalPresenter
{
    /// <summary>Null = no local route (no wallet, no match, cross-origin, parse failure). Never throws.</summary>
    Task<LocalPresentationCandidate?> ProbeAsync(string presentationRequestUri, CancellationToken ct = default);

    /// <summary>Builds + signs + direct_posts the presentation. Never throws — failures come back as a result.</summary>
    Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default);
}

public sealed class LocalPresentationCandidate
{
    public required string CredentialId { get; init; }
    public required string WalletAddress { get; init; }
    public required string Vct { get; init; }
    public required IReadOnlyList<string> RequiredClaims { get; init; }
    public required IReadOnlyList<string> OptionalClaims { get; init; }
    public required string Nonce { get; init; }
    public required string ClientId { get; init; }
    /// <summary>Same-origin RELATIVE path — the presenter refuses cross-origin response targets.</summary>
    public required string ResponseUri { get; init; }
    public required string QueryId { get; init; }
    public required string RequestState { get; init; }
    /// <summary>JOSE alg for the KB-JWT header: "EdDSA" or "ES256", mapped from the wallet algorithm.</summary>
    public required string JoseAlgorithm { get; init; }
    /// <summary>RFC 7638 thumbprint of the holder JWK — the KB-JWT kid.</summary>
    public required string KidThumbprint { get; init; }
    public string? IssuerDid { get; init; }
}

public enum LocalPresentStatus { Submitted, Declined, Failed }

public sealed class LocalPresentResult
{
    public required LocalPresentStatus Status { get; init; }
    public string? Detail { get; init; }
    public static LocalPresentResult Submitted() => new() { Status = LocalPresentStatus.Submitted };
    public static LocalPresentResult Declined(string detail) => new() { Status = LocalPresentStatus.Declined, Detail = detail };
    public static LocalPresentResult Failed(string detail) => new() { Status = LocalPresentStatus.Failed, Detail = detail };
}
```

- [ ] **Step 1: Write the interface + models file**

`ISorchaWalletLocalPresenter.cs` — exactly the block above, with license header, `namespace Sorcha.UI.Core.Services.User.Presentation;`, and XML doc on the interface explaining it is the browser-side port of `Complete-SorchaWalletPresentation` (rehearse.ps1:476-625) / the PWA's `Present.razor` server-custody path, and that it exists so a web citizen can satisfy a SorchaWallet gate with no second device (#1330).

- [ ] **Step 2: Write the failing probe tests**

`SorchaWalletLocalPresenterProbeTests.cs`. Test HTTP via a capturing handler; mock `IHolderKeyClient` + `ICredentialApiService` with Moq. Key content:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class SorchaWalletLocalPresenterProbeTests
{
    private const string Vct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>Unsigned request-object JWT with the payload fields the real endpoint serves.</summary>
    private static string BuildRequestObjectJwt(
        string? nonce = "n-123", string clientId = "did:sorcha:org:ws1qabc",
        string responseUri = "https://unit.test/api/presentations/callbacks/sorcha-wallet/rid-1",
        string state = "rid-1")
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"oauth-authz-req+jwt"}"""));
        var payload = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["response_uri"] = responseUri,
            ["nonce"] = nonce,
            ["state"] = state,
            ["response_mode"] = "direct_post",
            ["dcql_query"] = JsonDocument.Parse($$"""
                {"credentials":[{"id":"credential","format":"dc+sd-jwt",
                  "meta":{"vct_values":["{{Vct}}"]},
                  "claims":[{"path":["givenName"]},{"path":["familyName"]}],
                  "claim_sets":[["givenName","familyName"],["givenName","familyName","portrait"]]}]}
                """).RootElement,
        };
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{payloadSeg}.";
    }

    private static (SorchaWalletLocalPresenter Presenter, CapturingHandler Http,
        Mock<IHolderKeyClient> Keys, Mock<ICredentialApiService> Creds)
        Build(string requestObjectJwt, string baseAddress = "https://unit.test/")
    {
        var handler = new CapturingHandler(req =>
            req.RequestUri!.PathAndQuery.Contains("/request-object")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(requestObjectJwt) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        var keys = new Mock<IHolderKeyClient>();
        keys.Setup(k => k.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolderKeysView
            {
                HolderJwk = JsonDocument.Parse("""{"kty":"OKP","crv":"Ed25519","x":"abc"}""").RootElement,
                Algorithm = "ED25519",
                WalletAddress = "ws1qcitizen",
                EncryptionPublicKey = "pk",
            });
        var creds = new Mock<ICredentialApiService>();
        creds.Setup(c => c.MatchCredentialsAsync("ws1qcitizen",
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult
                { RequirementType = Vct, Matched = true, CredentialId = "urn:uuid:c1", IssuerDid = "did:sorcha:org:ws1qabc" }]);
        var presenter = new SorchaWalletLocalPresenter(
            http, keys.Object, creds.Object, TimeProvider.System,
            NullLogger<SorchaWalletLocalPresenter>.Instance);
        return (presenter, handler, keys, creds);
    }

    private static string DeepLink(string requestUri = "https://unit.test/api/presentations/rid-1/request-object")
        => $"openid4vp://authorize?request_uri={Uri.EscapeDataString(requestUri)}";

    [Fact]
    public async Task ProbeAsync_MatchingCredential_ReturnsCandidateWithRequestObjectFields()
    {
        var (presenter, _, _, _) = Build(BuildRequestObjectJwt());
        var candidate = await presenter.ProbeAsync(DeepLink());
        candidate.Should().NotBeNull();
        candidate!.Vct.Should().Be(Vct);
        candidate.Nonce.Should().Be("n-123");
        candidate.ClientId.Should().Be("did:sorcha:org:ws1qabc");
        candidate.ResponseUri.Should().Be("/api/presentations/callbacks/sorcha-wallet/rid-1"); // relative
        candidate.QueryId.Should().Be("credential");
        candidate.RequestState.Should().Be("rid-1");
        candidate.RequiredClaims.Should().BeEquivalentTo(["givenName", "familyName"]);
        candidate.OptionalClaims.Should().BeEquivalentTo(["portrait"]);
        candidate.JoseAlgorithm.Should().Be("EdDSA");
        candidate.WalletAddress.Should().Be("ws1qcitizen");
        candidate.CredentialId.Should().Be("urn:uuid:c1");
    }

    [Fact]
    public async Task ProbeAsync_CrossOriginRequestUri_ReturnsNullWithoutFetching()
    {
        var (presenter, http, _, _) = Build(BuildRequestObjectJwt());
        var candidate = await presenter.ProbeAsync(DeepLink("https://evil.example/api/presentations/x/request-object"));
        candidate.Should().BeNull();
        http.Requests.Should().BeEmpty("a cross-origin request_uri must not be fetched at all");
    }

    [Fact]
    public async Task ProbeAsync_CrossOriginResponseUri_ReturnsNull()
    {
        var jwt = BuildRequestObjectJwt(responseUri: "https://evil.example/collect");
        var (presenter, _, _, _) = Build(jwt);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull(
            "the bearer-carrying direct_post must never target a foreign origin");
    }

    [Fact]
    public async Task ProbeAsync_NoMatchingCredential_ReturnsNull()
    {
        var (presenter, _, _, creds) = Build(BuildRequestObjectJwt());
        creds.Setup(c => c.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult { RequirementType = Vct, Matched = false }]);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_HolderKeysUnavailable_ReturnsNull()
    {
        var (presenter, _, keys, _) = Build(BuildRequestObjectJwt());
        keys.Setup(k => k.GetHolderKeysAsync(It.IsAny<CancellationToken>())).ReturnsAsync((HolderKeysView?)null);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_MatchThrows_ReturnsNull()
    {
        // #1324: MatchCredentialsAsync throws on transport failure. The probe swallows it —
        // a probe failure degrades to the QR route, never to a dead end.
        var (presenter, _, _, creds) = Build(BuildRequestObjectJwt());
        creds.Setup(c => c.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_MissingNonce_ReturnsNull()
    {
        var (presenter, _, _, _) = Build(BuildRequestObjectJwt(nonce: null));
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }
}

/// <summary>Records every request and answers via the supplied responder.</summary>
internal sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return responder(request);
    }
}
```

Note: if `DcqlRequestParser.ParseFromRequestObjectPayload` rejects the fixture's `dcql_query` shape (`claims` path form / `claim_sets`), open `src/Common/Sorcha.Verifier.Engine/Dcql/DcqlRequestParser.cs` and `DcqlModels.cs` and adjust the **fixture** to the exact wire shape `SorchaWalletPresentationConsumer.ResolveDeclaredQuery` produces — the parser is the consumer contract, do not adjust the parser. `SplitClaims` decides required vs optional; assert whatever split the real parser produces for a fixture that mirrors the AIAS cyber request (givenName+familyName required, portrait optional), and if the produced fixture can't express an optional claim, drop the `OptionalClaims` assertion to `candidate.OptionalClaims.Should().NotBeNull()` and record the finding in the commit body.

- [ ] **Step 3: Run tests to verify they fail**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```
Expected: build FAILS — `SorchaWalletLocalPresenter` not defined. That is the RED.

- [ ] **Step 4: Implement `SorchaWalletLocalPresenter` (probe half + shared helpers)**

`SorchaWalletLocalPresenter.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Browser-side port of the proven server-custody presentation flow
/// (demos/AIAS/rehearse.ps1 Complete-SorchaWalletPresentation; PWA Present.razor). Lets a web
/// citizen satisfy a SorchaWallet gate on this device: the holder private key never leaves
/// server custody — the KB-JWT is signed by POST /api/v1/wallet/presentations/sign-kb (#1195
/// Phase 2, Task 6a) and the assembled vp_token is direct_posted to the F127 callback. #1330.
/// </summary>
public sealed class SorchaWalletLocalPresenter : ISorchaWalletLocalPresenter
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    private readonly HttpClient _http;
    private readonly IHolderKeyClient _holderKeys;
    private readonly ICredentialApiService _credentials;
    private readonly TimeProvider _clock;
    private readonly ILogger<SorchaWalletLocalPresenter> _logger;

    public SorchaWalletLocalPresenter(
        HttpClient http,
        IHolderKeyClient holderKeys,
        ICredentialApiService credentials,
        TimeProvider clock,
        ILogger<SorchaWalletLocalPresenter> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _holderKeys = holderKeys ?? throw new ArgumentNullException(nameof(holderKeys));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LocalPresentationCandidate?> ProbeAsync(
        string presentationRequestUri, CancellationToken ct = default)
    {
        // A probe failure of ANY kind degrades to the QR route — it must never block the gate.
        try
        {
            return await ProbeCoreAsync(presentationRequestUri, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local-presentation probe failed; falling back to QR.");
            return null;
        }
    }

    private async Task<LocalPresentationCandidate?> ProbeCoreAsync(
        string presentationRequestUri, CancellationToken ct)
    {
        // 1. request_uri out of the openid4vp:// deep link.
        var queryStart = presentationRequestUri.IndexOf('?');
        if (queryStart < 0) return null;
        var query = HttpUtility.ParseQueryString(presentationRequestUri[(queryStart + 1)..]);
        var requestUri = query["request_uri"];
        if (string.IsNullOrEmpty(requestUri)) return null;

        // Same-origin or nothing: this client carries the citizen's bearer on every call.
        var requestPath = ToSameOriginRelative(requestUri);
        if (requestPath is null) return null;

        // 2. Fetch + decode the request object (content type application/oauth-authz-req+jwt).
        var requestObjectJwt = await _http.GetStringAsync(requestPath, ct);
        var segments = requestObjectJwt.Split('.');
        if (segments.Length is not (2 or 3) || segments[1].Length == 0) return null;

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
        var root = payload.RootElement;
        var clientId = ReadString(root, "client_id");
        var nonce = ReadString(root, "nonce");
        var responseUri = ReadString(root, "response_uri");
        var state = ReadString(root, "state");
        if (clientId is null || nonce is null || responseUri is null || state is null) return null;

        var responsePath = ToSameOriginRelative(responseUri);
        if (responsePath is null)
        {
            _logger.LogWarning("Presentation response_uri is not same-origin; refusing the local route.");
            return null;
        }

        // 3. The single credential ask (the SorchaWallet consumer is single-ask today).
        var dcql = DcqlRequestParser.ParseFromRequestObjectPayload(root);
        if (dcql.Credentials.Count == 0) return null;
        var credentialQuery = dcql.Credentials[0];
        var vct = credentialQuery.Meta?.VctValues is { Count: > 0 } vcts ? vcts[0] : null;
        if (string.IsNullOrEmpty(vct)) return null;
        var (required, optional) = DcqlRequestParser.SplitClaims(credentialQuery);

        // 4. The citizen's wallet + algorithm.
        var keys = await _holderKeys.GetHolderKeysAsync(ct);
        if (keys is null) return null;
        var joseAlg = ToJoseAlgorithm(keys.Algorithm);
        if (joseAlg is null) return null;

        // 5. Does the wallet hold a match?
        var requirement = new CredentialRequirement
        {
            Type = vct,
            RequiredClaims = required.Select(n => new ClaimConstraint { ClaimName = n }).ToList(),
        };
        var matches = await _credentials.MatchCredentialsAsync(keys.WalletAddress, [requirement], ct);
        var match = matches.FirstOrDefault(m => m.Matched && !string.IsNullOrEmpty(m.CredentialId));
        if (match is null) return null;

        return new LocalPresentationCandidate
        {
            CredentialId = match.CredentialId!,
            WalletAddress = keys.WalletAddress,
            Vct = vct,
            RequiredClaims = required,
            OptionalClaims = optional,
            Nonce = nonce,
            ClientId = clientId,
            ResponseUri = responsePath,
            QueryId = credentialQuery.Id,
            RequestState = state,
            JoseAlgorithm = joseAlg,
            KidThumbprint = ComputeJwkThumbprint(keys.HolderJwk),
            IssuerDid = match.IssuerDid,
        };
    }

    /// <summary>Absolute same-origin URLs become relative paths; cross-origin returns null.</summary>
    private string? ToSameOriginRelative(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var abs))
            return uri; // already relative — same origin by construction
        var baseAddress = _http.BaseAddress;
        if (baseAddress is null) return null;
        return string.Equals(abs.Authority, baseAddress.Authority, StringComparison.OrdinalIgnoreCase)
            ? abs.PathAndQuery
            : null;
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Wallet algorithm → KB-JWT JOSE alg. Mirrors rehearse.ps1 Get-JoseAlgorithmForWalletAlgorithm.</summary>
    internal static string? ToJoseAlgorithm(string walletAlgorithm) => walletAlgorithm switch
    {
        "ED25519" => "EdDSA",
        "NISTP256" or "NIST-P256" or "P-256" => "ES256",
        _ => null,
    };

    /// <summary>
    /// RFC 7638 JWK thumbprint — EC (crv,kty,x,y) and OKP (crv,kty,x). Mirror of the PWA's
    /// PresentationEngine.ComputeJwkThumbprint (no project reference between the apps).
    /// </summary>
    internal static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var crv = jwk.GetProperty("crv").GetString();
        var kty = jwk.GetProperty("kty").GetString();
        var x = jwk.GetProperty("x").GetString();
        var canonical = string.Equals(kty, "OKP", StringComparison.Ordinal)
            ? $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\"}}"
            : $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    // PresentAsync arrives in Task 2.
    public Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default)
        => throw new NotImplementedException();
}
```

(If `JsonDefaults.Api` lives elsewhere than `Sorcha.UI.Core.Extensions`, copy the using from `CredentialApiService.cs:7` which already imports it.)

- [ ] **Step 5: Run the probe tests**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj -- --filter "SorchaWalletLocalPresenterProbe"
```
Expected: all 7 PASS.

- [ ] **Step 6: Mutation-test one guard**

Temporarily invert the authority comparison in `ToSameOriginRelative` (`==` → `!=`). Re-run. Expected: `ProbeAsync_CrossOriginRequestUri_ReturnsNullWithoutFetching` and `ProbeAsync_MatchingCredential_...` go RED. Restore, re-run green.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/ISorchaWalletLocalPresenter.cs src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletLocalPresenter.cs tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletLocalPresenterProbeTests.cs
git commit -m "feat: [#1330] - local presenter probe: request-object parse + own-wallet match"
```

---

### Task 2: `PresentAsync` — export, disclosure strip, sd_hash, sign-kb, direct_post

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletLocalPresenter.cs` (replace the `NotImplementedException` stub)
- Test: `tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletLocalPresenterPresentTests.cs`

**Interfaces:**
- Consumes: Task 1's models + helpers; wallet endpoints `GET /api/v1/wallets/{addr}/credentials/{id}/export` → `{ id, type, rawToken }` (`CredentialEndpoints.cs:381-398`), `POST /api/v1/wallet/presentations/sign-kb` `{ signingInput }` → `{ signature, algorithm }` (`CitizenWalletEndpoints.cs:246-311`), and the F127 form-encoded direct_post (`vp_token` + `state`) which returns JSON with a `kind` property (`"Success"` on accept — the shape rehearse.ps1:622 asserts).
- Produces: working `PresentAsync` per the Task 1 interface.

- [ ] **Step 1: Write the failing tests**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class SorchaWalletLocalPresenterPresentTests
{
    private static string Disclosure(string name, string value)
        => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { "salt-" + name, name, value }));

    private static readonly string CredentialJwt = "eyJhbGciOiJFZERTQSJ9.eyJ2Y3QiOiJ4In0.c2ln";
    private static readonly string DGiven = Disclosure("givenName", "Ada");
    private static readonly string DFamily = Disclosure("familyName", "Lovelace");
    private static readonly string DPortrait = Disclosure("portrait", "base64...");
    private static string RawToken => $"{CredentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1",
        WalletAddress = "ws1qcitizen",
        Vct = "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims = ["givenName", "familyName"],
        OptionalClaims = ["portrait"],
        Nonce = "n-123",
        ClientId = "did:sorcha:org:ws1qabc",
        ResponseUri = "/api/presentations/callbacks/sorcha-wallet/rid-1",
        QueryId = "credential",
        RequestState = "rid-1",
        JoseAlgorithm = "EdDSA",
        KidThumbprint = "thumb",
    };

    private static (SorchaWalletLocalPresenter Presenter, CapturingHandler Http) Build(
        string signKbAlgorithm = "EdDSA",
        string callbackKind = "Success",
        HttpStatusCode callbackStatus = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/export"))
                return Json($$"""{"id":"urn:uuid:c1","type":"x","rawToken":"{{RawToken}}"}""");
            if (path.Contains("/sign-kb"))
                return Json($$"""{"signature":"ZmFrZXNpZw","algorithm":"{{signKbAlgorithm}}"}""");
            if (path.Contains("/callbacks/"))
                return new HttpResponseMessage(callbackStatus)
                    { Content = new StringContent($$"""{"kind":"{{callbackKind}}"}""", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") };
        var presenter = new SorchaWalletLocalPresenter(
            http, Mock.Of<IHolderKeyClient>(), Mock.Of<ICredentialApiService>(),
            TimeProvider.System, NullLogger<SorchaWalletLocalPresenter>.Instance);
        return (presenter, handler);

        static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task PresentAsync_FullConsent_PostsEnvelopeWithNonceBoundKbJwt()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName", "portrait"]);

        result.Status.Should().Be(LocalPresentStatus.Submitted);

        // The direct_post is the last request; decode what actually went on the wire.
        var form = http.RequestBodies[^1];
        var pairs = form.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        pairs["state"].Should().Be("rid-1");

        using var envelope = JsonDocument.Parse(pairs["vp_token"]);
        var vp = envelope.RootElement.GetProperty("credential")[0].GetString()!;

        // vp = jwt~d1~d2~d3~kbJwt — all three consented disclosures, then the KB-JWT.
        var segments = vp.Split('~');
        segments[0].Should().Be(CredentialJwt);
        segments.Skip(1).Take(3).Should().BeEquivalentTo([DGiven, DFamily, DPortrait]);
        var kbJwt = segments[^1];
        kbJwt.Count(c => c == '.').Should().Be(2);

        // KB-JWT payload binds aud + nonce and carries the RFC 9901 sd_hash of the exact prefix.
        var kbParts = kbJwt.Split('.');
        using var kbHeader = JsonDocument.Parse(Base64Url.DecodeFromChars(kbParts[0]));
        kbHeader.RootElement.GetProperty("typ").GetString().Should().Be("kb+jwt");
        kbHeader.RootElement.GetProperty("alg").GetString().Should().Be("EdDSA");
        using var kbPayload = JsonDocument.Parse(Base64Url.DecodeFromChars(kbParts[1]));
        kbPayload.RootElement.GetProperty("aud").GetString().Should().Be("did:sorcha:org:ws1qabc");
        kbPayload.RootElement.GetProperty("nonce").GetString().Should().Be("n-123");

        var expectedHashable = $"{CredentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";
        var expectedSdHash = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(expectedHashable)));
        kbPayload.RootElement.GetProperty("sd_hash").GetString().Should().Be(expectedSdHash);
    }

    [Fact]
    public async Task PresentAsync_PortraitWithheld_OmitsItsDisclosureAndHashesTheShorterPrefix()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Submitted);

        var form = http.RequestBodies[^1];
        var vpTokenJson = Uri.UnescapeDataString(
            form.Split('&').First(p => p.StartsWith("vp_token=")).Split('=', 2)[1].Replace('+', ' '));
        using var envelope = JsonDocument.Parse(vpTokenJson);
        var vp = envelope.RootElement.GetProperty("credential")[0].GetString()!;
        vp.Should().NotContain(DPortrait);

        var expectedSdHash = Base64Url.EncodeToString(SHA256.HashData(
            Encoding.ASCII.GetBytes($"{CredentialJwt}~{DGiven}~{DFamily}~")));
        var kbPayloadSeg = vp.Split('~')[^1].Split('.')[1];
        using var kbPayload = JsonDocument.Parse(Base64Url.DecodeFromChars(kbPayloadSeg));
        kbPayload.RootElement.GetProperty("sd_hash").GetString().Should().Be(expectedSdHash);
    }

    [Fact]
    public async Task PresentAsync_RequiredClaimNotConsented_FailsWithoutAnyHttpCall()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PresentAsync_SignKbAlgorithmMismatch_FailsBeforeDirectPost()
    {
        // Mirror of rehearse.ps1:599 — a mismatched signature would fail verification
        // downstream with no local error, so it must be refused here, loudly.
        var (presenter, http) = Build(signKbAlgorithm: "ES256");
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/callbacks/"));
    }

    [Fact]
    public async Task PresentAsync_CallbackDeclines_ReturnsDeclined()
    {
        var (presenter, _) = Build(callbackKind: "Decline");
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Declined);
        result.Detail.Should().Contain("Decline");
    }

    [Fact]
    public async Task PresentAsync_CallbackHttpError_ReturnsFailed()
    {
        var (presenter, _) = Build(callbackStatus: HttpStatusCode.InternalServerError);
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj -- --filter "SorchaWalletLocalPresenterPresent"
```
Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement `PresentAsync`**

Replace the stub:

```csharp
    /// <inheritdoc />
    public async Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(consentedClaims);
        try
        {
            return await PresentCoreAsync(candidate, consentedClaims, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local presentation failed for request {State}.", candidate.RequestState);
            return LocalPresentResult.Failed(ex.Message);
        }
    }

    private async Task<LocalPresentResult> PresentCoreAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct)
    {
        var consented = new HashSet<string>(consentedClaims, StringComparer.Ordinal);

        // Every required claim must be consented — mirrors PresentationEngine's sanity check.
        foreach (var required in candidate.RequiredClaims)
        {
            if (!consented.Contains(required))
                return LocalPresentResult.Failed($"Required claim '{required}' was not consented.");
        }

        // 1. Export the held credential's raw SD-JWT.
        var export = await _http.GetFromJsonAsync<CredentialExportResponse>(
            $"/api/v1/wallets/{Uri.EscapeDataString(candidate.WalletAddress)}/credentials/{Uri.EscapeDataString(candidate.CredentialId)}/export",
            JsonOptions, ct);
        if (export is null || string.IsNullOrEmpty(export.RawToken))
            return LocalPresentResult.Failed("Credential export returned no raw token.");

        // 2. Keep only consented disclosures. Issued tokens carry no KB-JWT, but guard anyway
        //    (a 2-dot final segment is a KB-JWT, not a disclosure).
        var segments = export.RawToken.Split('~');
        var credentialJwt = segments[0];
        var selected = new List<string>();
        for (var i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrEmpty(seg) || seg.Count(c => c == '.') == 2) continue;
            if (ReadDisclosureName(seg) is { } name && consented.Contains(name))
                selected.Add(seg);
        }

        // 3. RFC 9901 sd_hash over the exact to-be-presented prefix (order preserved, trailing ~).
        var hashable = credentialJwt + string.Concat(selected.Select(s => "~" + s)) + "~";
        var sdHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(hashable)));

        // 4. KB-JWT signed server-custody: the holder key never leaves the Wallet Service.
        var now = _clock.GetUtcNow();
        var header = new Dictionary<string, object>
        {
            ["alg"] = candidate.JoseAlgorithm,
            ["typ"] = "kb+jwt",
            ["kid"] = candidate.KidThumbprint,
        };
        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(), // Feature 138 US5 window
            ["aud"] = candidate.ClientId,
            ["nonce"] = candidate.Nonce,
            ["sd_hash"] = sdHash,
        };
        var signingInput =
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header))}." +
            $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(kbPayload))}";

        using var signResponse = await _http.PostAsJsonAsync(
            "/api/v1/wallet/presentations/sign-kb", new { signingInput }, JsonOptions, ct);
        if (!signResponse.IsSuccessStatusCode)
            return LocalPresentResult.Failed($"sign-kb returned {(int)signResponse.StatusCode}.");
        var sign = await signResponse.Content.ReadFromJsonAsync<KbSignResponse>(JsonOptions, ct);
        if (sign is null || string.IsNullOrEmpty(sign.Signature))
            return LocalPresentResult.Failed("sign-kb returned no signature.");
        if (!string.Equals(sign.Algorithm, candidate.JoseAlgorithm, StringComparison.Ordinal))
        {
            // A silently mismatched alg fails verification downstream with no local error
            // (rehearse.ps1:599 carries the same guard).
            return LocalPresentResult.Failed(
                $"sign-kb signed '{sign.Algorithm}' but the KB-JWT header declares '{candidate.JoseAlgorithm}'.");
        }

        // 5. Assemble + direct_post the OpenID4VP 1.0 object-keyed envelope.
        var vpToken = hashable + $"{signingInput}.{sign.Signature}";
        var envelope = JsonSerializer.Serialize(
            new Dictionary<string, string[]> { [candidate.QueryId] = [vpToken] });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = envelope,
            ["state"] = candidate.RequestState,
        });
        using var callback = await _http.PostAsync(candidate.ResponseUri, form, ct);
        if (!callback.IsSuccessStatusCode)
            return LocalPresentResult.Failed($"Presentation callback returned {(int)callback.StatusCode}.");

        var body = await callback.Content.ReadAsStringAsync(ct);
        var kind = ReadKind(body);
        return string.Equals(kind, "Success", StringComparison.OrdinalIgnoreCase)
            ? LocalPresentResult.Submitted()
            : LocalPresentResult.Declined(kind ?? "unknown outcome");
    }

    /// <summary>Claim name of a 3-element disclosure ([salt, name, value]). A 2-element
    /// disclosure is an unnamed array element — never claim-selectable, so null.</summary>
    internal static string? ReadDisclosureName(string segment)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segment));
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 3
                ? doc.RootElement[1].GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadKind(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
                ? k.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class CredentialExportResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? RawToken { get; set; }
    }

    private sealed class KbSignResponse
    {
        public string Signature { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
    }
```

- [ ] **Step 4: Run the tests**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj -- --filter "SorchaWalletLocalPresenter"
```
Expected: all Task 1 + Task 2 tests PASS.

- [ ] **Step 5: Mutation-test the sd_hash composition**

Temporarily drop the trailing `+ "~"` from `hashable`. Expected: both sd_hash assertions go RED (this is the transcript-composition trap the tests exist to pin). Restore, re-run green.

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Presentation/SorchaWalletLocalPresenter.cs tests/Sorcha.UI.Core.Tests/Services/Presentation/SorchaWalletLocalPresenterPresentTests.cs
git commit -m "feat: [#1330] - local presenter: export, disclosure strip, sd_hash, sign-kb, direct_post"
```

---

### Task 3: `UseThisDevicePanel` consent component

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/UseThisDevicePanel.razor`
- Test: `tests/Sorcha.UI.Core.Tests/Components/Presentation/UseThisDevicePanelTests.cs`

**Interfaces:**
- Consumes: `ISorchaWalletLocalPresenter.PresentAsync` (Task 1/2).
- Produces: `UseThisDevicePanel` with parameters `Candidate` (`LocalPresentationCandidate`, required), `CredentialDisplayName` (`string`, default `"credential"`), `OnSubmitted` (`EventCallback`). Task 4's card mounts it.

- [ ] **Step 1: Write the failing bUnit tests**

Follow the existing bUnit setup pattern in `tests/Sorcha.UI.Core.Tests` (find a component test near `Components/` for the `TestContext` + MudBlazor services pattern; typical shape: `Services.AddMudServices()`, `JSInterop.Mode = JSRuntimeMode.Loose`).

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Presentation;

public class UseThisDevicePanelTests : TestContext
{
    private readonly Mock<ISorchaWalletLocalPresenter> _presenter = new();

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1", WalletAddress = "ws1q", Vct = "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims = ["givenName", "familyName"], OptionalClaims = ["portrait"],
        Nonce = "n", ClientId = "did:sorcha:org:x", ResponseUri = "/cb", QueryId = "credential",
        RequestState = "rid-1", JoseAlgorithm = "EdDSA", KidThumbprint = "t",
    };

    private IRenderedComponent<UseThisDevicePanel> Render()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_presenter.Object);
        return RenderComponent<UseThisDevicePanel>(p => p
            .Add(x => x.Candidate, Candidate())
            .Add(x => x.CredentialDisplayName, "Assured Identity"));
    }

    [Fact]
    public void Render_ListsRequiredClaimsLockedAndOptionalToggledOn()
    {
        var cut = Render();
        cut.Markup.Should().Contain("givenName").And.Contain("familyName").And.Contain("portrait");
        // Optional claims default ON — a default-off portrait converts the cyber happy path
        // into a hard reject (the agent refuses portrait-less presentations).
        var toggles = cut.FindComponents<MudBlazor.MudCheckBox<bool>>();
        toggles.Should().HaveCount(1); // only the optional claim is toggleable
        toggles[0].Instance.Value.Should().BeTrue();
    }

    [Fact]
    public void ShareAndContinue_PassesRequiredPlusCheckedOptionalClaims()
    {
        IReadOnlyCollection<string>? sent = null;
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<LocalPresentationCandidate, IReadOnlyCollection<string>, CancellationToken>(
                (_, claims, _) => sent = claims)
            .ReturnsAsync(LocalPresentResult.Submitted());

        var cut = Render();
        cut.Find("[data-testid=use-this-device-share]").Click();

        sent.Should().NotBeNull();
        sent.Should().BeEquivalentTo(["givenName", "familyName", "portrait"]);
    }

    [Fact]
    public void ShareAndContinue_Submitted_InvokesOnSubmitted()
    {
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocalPresentResult.Submitted());
        var submitted = false;
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_presenter.Object);
        var cut = RenderComponent<UseThisDevicePanel>(p => p
            .Add(x => x.Candidate, Candidate())
            .Add(x => x.OnSubmitted, () => submitted = true));
        cut.Find("[data-testid=use-this-device-share]").Click();
        submitted.Should().BeTrue();
    }

    [Fact]
    public void ShareAndContinue_Failed_ShowsInlineErrorAndDoesNotInvokeOnSubmitted()
    {
        _presenter.Setup(p => p.PresentAsync(It.IsAny<LocalPresentationCandidate>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LocalPresentResult.Failed("boom"));
        var cut = Render();
        cut.Find("[data-testid=use-this-device-share]").Click();
        cut.Markup.Should().Contain("couldn't share"); // inline error, QR remains the fallback
    }
}
```

(Adjust `TestContext` base / setup helper to whatever the neighbouring component tests in this project actually use — read one first. If `MudCheckBox<bool>` generic arg differs in this MudBlazor version, match the existing usage in the codebase.)

- [ ] **Step 2: Run to verify failure** — build fails: `UseThisDevicePanel` not defined.

- [ ] **Step 3: Implement the component**

```razor
@* SPDX-License-Identifier: MIT *@
@* Copyright (c) 2026 Sorcha Contributors *@
@*
    Feature 127 / #1330 — the "Use this device" half of the presentation gate. Rendered by
    PresentationRequestCard when the signed-in citizen's own server-custody wallet holds a
    matching credential. Consent here mirrors the PWA ConsentSheet semantics: required claims
    locked on, optional claims toggleable (default ON — a default-off portrait turns the AIAS
    cyber happy path into a hard reject). On success the card's existing IPresentationSignal
    observes the outcome; this panel never drives gate state itself.
*@
@namespace Sorcha.UI.Core.Components.Presentation
@using Sorcha.UI.Core.Services.User.Presentation
@inject ISorchaWalletLocalPresenter Presenter

<MudPaper Outlined="true" Class="pa-4 mb-3" Style="width: 100%; max-width: 400px;">
    <MudStack Spacing="2">
        <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
            <MudIcon Icon="@Icons.Material.Filled.Badge" Color="Color.Primary" />
            <MudText Typo="Typo.subtitle1">Use this device</MudText>
        </MudStack>
        <MudText Typo="Typo.body2">
            You already hold a matching <b>@CredentialDisplayName</b> in your Sorcha wallet.
            Confirm what you're happy to share:
        </MudText>

        @foreach (var claim in Candidate.RequiredClaims)
        {
            <MudCheckBox T="bool" Value="true" Disabled="true" Dense="true"
                         Label="@($"{claim} (required)")" />
        }
        @foreach (var claim in Candidate.OptionalClaims)
        {
            var name = claim;
            <MudCheckBox T="bool" Value="@_optionalConsent[name]" Dense="true" Label="@name"
                         ValueChanged="@((bool v) => _optionalConsent[name] = v)" />
        }

        @if (_error is not null)
        {
            <MudAlert Severity="Severity.Error" Dense="true" data-testid="use-this-device-error">
                We couldn't share your credential from this device — you can try again, or scan
                the QR code with your phone instead. (@_error)
            </MudAlert>
        }

        <MudButton Variant="Variant.Filled" Color="Color.Primary" Disabled="@_busy"
                   data-testid="use-this-device-share" OnClick="ShareAsync">
            @(_busy ? "Sharing..." : "Share & continue")
        </MudButton>
    </MudStack>
</MudPaper>

@code {
    /// <summary>The probed local candidate — presence is what makes this panel render at all.</summary>
    [Parameter, EditorRequired] public LocalPresentationCandidate Candidate { get; set; } = default!;

    /// <summary>Human name of the credential, e.g. "Assured Identity".</summary>
    [Parameter] public string CredentialDisplayName { get; set; } = "credential";

    /// <summary>Fires after a successful direct_post. The card's signal machinery takes over.</summary>
    [Parameter] public EventCallback OnSubmitted { get; set; }

    private readonly Dictionary<string, bool> _optionalConsent = new(StringComparer.Ordinal);
    private bool _busy;
    private string? _error;

    protected override void OnParametersSet()
    {
        foreach (var claim in Candidate.OptionalClaims)
        {
            _optionalConsent.TryAdd(claim, true); // default ON — see file header comment
        }
    }

    private async Task ShareAsync()
    {
        _busy = true;
        _error = null;
        StateHasChanged();
        try
        {
            var consented = Candidate.RequiredClaims
                .Concat(_optionalConsent.Where(kv => kv.Value).Select(kv => kv.Key))
                .ToList();
            var result = await Presenter.PresentAsync(Candidate, consented);
            if (result.Status == LocalPresentStatus.Submitted)
            {
                await OnSubmitted.InvokeAsync();
            }
            else
            {
                _error = result.Detail ?? result.Status.ToString();
            }
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }
}
```

Note: the failed-share error copy must contain the phrase `couldn't share` (asserted). It deliberately does NOT say anything resembling "no matching credential" — that lie is #1324.

- [ ] **Step 4: Run tests** — expected: 4 PASS.

- [ ] **Step 5: Mutation-test the default-on rule**

Flip `TryAdd(claim, true)` to `false`. Expected: `Render_ListsRequiredClaims...` and `ShareAndContinue_PassesRequired...` go RED. Restore, green.

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/UseThisDevicePanel.razor tests/Sorcha.UI.Core.Tests/Components/Presentation/UseThisDevicePanelTests.cs
git commit -m "feat: [#1330] - UseThisDevicePanel consent surface (required locked, optional default-on)"
```

---

### Task 4: Card integration + host registration

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/PresentationRequestCard.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs` (after the `AddSorchaPresentationGate` call, ~line 155)
- Test: `tests/Sorcha.UI.Core.Tests/Components/Presentation/PresentationRequestCardLocalRouteTests.cs`

**Interfaces:**
- Consumes: `ISorchaWalletLocalPresenter` (via `IServiceProvider.GetService` — nullable: council portal and any host that doesn't register it degrade to QR-only), `UseThisDevicePanel` (Task 3).
- Produces: no new public surface. Existing card parameters unchanged.

- [ ] **Step 1: Write the failing bUnit tests**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Presentation;

public class PresentationRequestCardLocalRouteTests : TestContext
{
    /// <summary>Transport that never resolves — keeps the card in Pending for render assertions.</summary>
    private sealed class PendingTransport : IPresentationGateTransport
    {
        public PresentationSource Source => PresentationSource.SorchaWallet;
        public Task<GateOutcome> WaitForOutcomeAsync(Guid requestId, IProgress<GateOutcome> progress, CancellationToken ct)
            => new TaskCompletionSource<GateOutcome>().Task.WaitAsync(ct);
        public Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(Guid requestId, string? token, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
    }

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1", WalletAddress = "ws1q", Vct = "vct",
        RequiredClaims = ["givenName"], OptionalClaims = [],
        Nonce = "n", ClientId = "c", ResponseUri = "/cb", QueryId = "credential",
        RequestState = "rid", JoseAlgorithm = "EdDSA", KidThumbprint = "t",
    };

    private IRenderedComponent<PresentationRequestCard> Render(ISorchaWalletLocalPresenter? presenter)
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IEnumerable<IPresentationGateTransport>>(
            new IPresentationGateTransport[] { new PendingTransport() });
        if (presenter is not null) Services.AddSingleton(presenter);
        return RenderComponent<PresentationRequestCard>(p => p
            .Add(x => x.PresentationRequestUri, "openid4vp://authorize?request_uri=x")
            .Add(x => x.RequestId, Guid.NewGuid())
            .Add(x => x.Source, PresentationSource.SorchaWallet));
    }

    [Fact]
    public void SorchaWalletGate_ProbeReturnsCandidate_RendersLocalPanelWithCollapsedQr()
    {
        var presenter = new Mock<ISorchaWalletLocalPresenter>();
        presenter.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidate());
        var cut = Render(presenter.Object);
        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<UseThisDevicePanel>().Should().HaveCount(1);
            cut.Markup.Should().Contain("scan with your phone"); // QR still reachable
        });
    }

    [Fact]
    public void SorchaWalletGate_ProbeReturnsNull_RendersQrOnly()
    {
        var presenter = new Mock<ISorchaWalletLocalPresenter>();
        presenter.Setup(p => p.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocalPresentationCandidate?)null);
        var cut = Render(presenter.Object);
        cut.FindComponents<UseThisDevicePanel>().Should().BeEmpty();
    }

    [Fact]
    public void SorchaWalletGate_NoPresenterRegistered_RendersQrOnly()
    {
        // The council sample portal never registers the presenter — QR-only, no throw.
        var cut = Render(presenter: null);
        cut.FindComponents<UseThisDevicePanel>().Should().BeEmpty();
    }
}
```

(Match `IPresentationGateTransport`'s real member signatures from `Services/User/Presentation/IPresentationGateTransport.cs` — read it before writing the fake; the shape above is indicative, the file is authoritative.)

- [ ] **Step 2: Run to verify failure** — `UseThisDevicePanel` never rendered → first test FAILS.

- [ ] **Step 3: Integrate into the card**

In `PresentationRequestCard.razor`:

1. Add injections after line 25:
```razor
@inject IServiceProvider ServiceProvider
@using Sorcha.UI.Core.Services.User.Presentation
```
2. Add state fields beside `_outcome`:
```csharp
    private LocalPresentationCandidate? _localCandidate;
```
3. At the end of `OnParametersSet()` (after `_ = AwaitOutcomeAsync(...)`), start the probe:
```csharp
        _localCandidate = null;
        if (Source == PresentationSource.SorchaWallet)
        {
            _ = ProbeLocalRouteAsync(RequestId, PresentationRequestUri, _cts.Token);
        }
```
4. Add the probe method:
```csharp
    /// <summary>
    /// #1330 — can the signed-in citizen satisfy this gate from their own server-custody
    /// wallet? Resolved via GetService so hosts that don't register the presenter (the
    /// council sample portal) and any probe failure degrade silently to the QR route.
    /// </summary>
    private async Task ProbeLocalRouteAsync(Guid requestId, string requestUri, CancellationToken ct)
    {
        var presenter = ServiceProvider.GetService<ISorchaWalletLocalPresenter>();
        if (presenter is null) return;

        var candidate = await presenter.ProbeAsync(requestUri, ct);
        if (candidate is null) return;

        await InvokeAsync(() =>
        {
            if (_disposed || requestId != _awaitedRequestId || GateOutcomes.IsTerminal(_outcome)) return;
            _localCandidate = candidate;
            StateHasChanged();
        });
    }

    private void OnLocalSubmitted()
    {
        if (GateOutcomes.IsTerminal(_outcome)) return;
        _outcome = GateOutcome.Submitted; // the transport's signal drives it to Success/Declined
        StateHasChanged();
    }
```
   (`GetService` needs `@using Microsoft.Extensions.DependencyInjection` in the file or the fully-qualified extension call.)
5. Replace the final `else` render branch (currently lines 129-139, the QR block) with:
```razor
        else if (_localCandidate is not null)
        {
            <UseThisDevicePanel Candidate="@_localCandidate"
                                CredentialDisplayName="@NameOfMissingCredentialType"
                                OnSubmitted="@OnLocalSubmitted" />
            <MudExpansionPanels Elevation="0" Class="mb-2" Style="max-width: 400px; width: 100%;">
                <MudExpansionPanel Text="Or scan with your phone">
                    <div class="presentation-request-card__qr mb-2 d-flex justify-center">
                        <HybridQrAffordance QrUrl="@PresentationRequestUri"
                                            Layout="HybridQrAffordance.HybridQrLayout.Auto" />
                    </div>
                </MudExpansionPanel>
            </MudExpansionPanels>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Waiting for your credential...
            </MudText>
        }
        else
        {
            <div class="presentation-request-card__qr mb-4">
                <HybridQrAffordance QrUrl="@PresentationRequestUri"
                                    Layout="HybridQrAffordance.HybridQrLayout.Auto" />
            </div>
            <MudProgressLinear Color="Color.Primary" Indeterminate="true" Class="mb-2" Style="max-width: 280px;" />
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Waiting for your wallet...
            </MudText>
        }
```
6. Also clear `_localCandidate = null;` in the `OnParametersSet` reset block (step 3 already places it there) so a new request re-probes.

- [ ] **Step 4: Register the presenter in the web host**

In `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs`, immediately after the `AddSorchaPresentationGate(...)` call (~line 155):

```csharp
// #1330 — local (same-device) completion of a SorchaWallet presentation gate. Registered ONLY
// on hosts with a signed-in citizen session: every call (export, sign-kb, direct_post) is
// consumer-tier, so the client MUST carry the bearer (see the ambient-HttpClient warning above).
// PresentationRequestCard resolves this via GetService — hosts without it (the council sample
// portal) fall back to the QR route.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Presentation.ISorchaWalletLocalPresenter,
                               Sorcha.UI.Core.Services.User.Presentation.SorchaWalletLocalPresenter>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();
```

If `TimeProvider` is not already registered in this host (check: `AddSorchaPresentationGate` TryAdds it at `PresentationServiceCollectionExtensions.cs:76` and runs first, so it is), nothing more is needed.

- [ ] **Step 5: Run tests**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj -- --filter "PresentationRequestCard"
```
Expected: new tests PASS, and any pre-existing `PresentationRequestCard` tests still PASS (if an existing test asserts the QR renders in Pending, it still passes — QR-only is unchanged when no presenter is registered).

- [ ] **Step 6: Build both hosts**

```
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj
dotnet build samples/strathcarron-portal
```
Expected: both compile (the sample portal proves the no-presenter path still composes).

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/PresentationRequestCard.razor src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs tests/Sorcha.UI.Core.Tests/Components/Presentation/PresentationRequestCardLocalRouteTests.cs
git commit -m "feat: [#1330] - presentation gate offers 'Use this device' beside the QR"
```

---

### Task 5: `CredentialGatePanel` stops lying; renderer stops blocking async-source gates

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Panels/CredentialGatePanel.razor` (builds on the uncommitted comment correction already in the working tree — keep it)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/SorchaFormRenderer.razor:553` and `:1153`
- Test: `tests/Sorcha.UI.Core.Tests/Components/Forms/CredentialGatePanelAsyncSourceTests.cs`

**Interfaces:**
- Consumes: `CredentialRequirement.PresentationSource` (`SorchaInternal = 0` default | `HaipExternalWallet` | `SorchaWallet` — `CredentialRequirement.cs:91-104`).
- Produces: behavioural contract — async-source requirements never block `SorchaFormRenderer` submission and never open the Select dialog.

- [ ] **Step 1: Find and read the existing tests that pin current behaviour**

```
Grep pattern "CredentialGatePanel|CredentialGateSatisfied" in tests/ (files_with_matches)
```
Read the matches. Any test asserting that an unselected requirement blocks submission must be split: it remains true **only** for `PresentationSource.SorchaInternal` requirements.

- [ ] **Step 2: Write the failing tests**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

public class CredentialGatePanelAsyncSourceTests : TestContext
{
    private readonly Mock<ICredentialApiService> _api = new();

    private IRenderedComponent<Sorcha.UI.Core.Components.Forms.Panels.CredentialGatePanel> Render(
        PresentationSource source, FormContext formContext)
    {
        _api.Setup(a => a.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<CredentialRequirement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult
                { RequirementType = "vct", Matched = true, CredentialId = "urn:uuid:c1" }]);
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<MudBlazor.IDialogService>());
        return RenderComponent<Sorcha.UI.Core.Components.Forms.Panels.CredentialGatePanel>(p => p
            .AddCascadingValue(formContext)
            .Add(x => x.Requirements, new[] { new CredentialRequirement { Type = "vct", PresentationSource = source } })
            .Add(x => x.WalletAddress, "ws1q"));
    }

    [Fact]
    public void AsyncSourceRequirement_DoesNotBlockSubmission()
    {
        var ctx = new FormContext();
        ctx.CredentialGateSatisfied = false; // renderer's init for a gated action
        var cut = Render(PresentationSource.SorchaWallet, ctx);
        cut.WaitForAssertion(() => ctx.CredentialGateSatisfied.Should().BeTrue(
            "the gate for an async-source requirement is enforced post-submit by the presentation lifecycle"));
    }

    [Fact]
    public void AsyncSourceRequirement_RendersInfoNotSelectButton()
    {
        var cut = Render(PresentationSource.SorchaWallet, new FormContext());
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("after you submit");
            cut.FindAll("button").Where(b => b.TextContent.Contains("Select")).Should().BeEmpty();
        });
    }

    [Fact]
    public void InternalSourceRequirement_StillRendersSelectFlow()
    {
        var ctx = new FormContext { CredentialGateSatisfied = false };
        var cut = Render(PresentationSource.SorchaInternal, ctx);
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("button").Where(b => b.TextContent.Contains("Select")).Should().HaveCount(1);
            ctx.CredentialGateSatisfied.Should().BeFalse("the inline path still requires a selection");
        });
    }
}
```

(Adapt the cascading-value / `FormContext` construction to the real `FormContext` API — read `Models/User/Forms/FormContext.cs` first; if `CredentialGateSatisfied` is init-true, set it explicitly as shown.)

- [ ] **Step 3: Run to verify failure** — the first two tests FAIL against current behaviour.

- [ ] **Step 4: Implement**

In `CredentialGatePanel.razor`:

1. Split requirements in `OnParametersSetAsync`:
```csharp
    private static bool IsAsyncSource(CredentialRequirement r)
        => r.PresentationSource is PresentationSource.SorchaWallet or PresentationSource.HaipExternalWallet;
```
2. In the render loop, for `IsAsyncSource(req)` render an informational row instead of Select/Change buttons:
```razor
                @if (IsAsyncSource(req))
                {
                    <MudText Typo="Typo.caption" Color="@(isMatched ? MudBlazor.Color.Success : MudBlazor.Color.Info)">
                        @(isMatched
                            ? "You hold a matching credential — you'll confirm sharing after you submit."
                            : "You'll be asked to present this credential after you submit.")
                    </MudText>
                }
                else if (isMatched && !isSelected) { /* existing Select button */ }
                else if (isSelected) { /* existing Change button */ }
```
   (Keep the existing severity/match-status rendering; only the action affordance changes.)
3. `UpdateRequirementStatus()` counts only inline requirements:
```csharp
    private void UpdateRequirementStatus()
    {
        var inlineRequirements = _requirements.Where(r => !IsAsyncSource(r)).ToList();
        _allRequirementsMet = inlineRequirements.All(req => _selectedCredentials.ContainsKey(req.Type));

        if (FormContext is not null)
        {
            // Async-source requirements are enforced post-submit by the F111/F127 presentation
            // lifecycle — they never gate the submit button (#1330). Only the inline
            // (SorchaInternal) path needs a pre-submit selection.
            FormContext.CredentialGateSatisfied = _allRequirementsMet || inlineRequirements.Count == 0;
        }
    }
```
4. Ensure `UpdateRequirementStatus()` also runs when match results load with zero inline requirements (it already runs in `MatchCredentialsAsync`'s `finally`). Additionally call it once in `OnParametersSetAsync` right after `_requirements` is assigned, so the unblock does not depend on the match call succeeding:
```csharp
        _requirements = Requirements.ToList();
        UpdateRequirementStatus();
```
5. Amend the big corrected comment in `SelectCredentialAsync` — replace its final sentence (`Until a real SD-JWT VP is built — the wallet service exposes POST /presentations/request then /{requestId}/submit for exactly this — do not thread this value into ActionExecuteRequest.`) with:
```
                // This Select flow now applies ONLY to PresentationSource.SorchaInternal
                // requirements. SorchaWallet/HAIP gates are satisfied post-submit via the F111
                // lifecycle — locally by ISorchaWalletLocalPresenter (request object → export →
                // sign-kb → direct_post; see rehearse.ps1 Complete-SorchaWalletPresentation for
                // the proven recipe) or cross-device by QR. Note: the wallet-service
                // POST /api/v1/presentations/{id}/submit flow VERIFIES a caller-built vpToken —
                // nothing in it builds one; do not point here at it as a builder again.
```

In `SorchaFormRenderer.razor`:

6. Line 553: `_formContext.CredentialGateSatisfied = !_credentialRequirements.Any();` →
```csharp
            // Async-source (SorchaWallet/HAIP) gates are enforced post-submit by the presentation
            // lifecycle; only inline (SorchaInternal) requirements need a pre-submit selection.
            _formContext.CredentialGateSatisfied = _credentialRequirements.All(r =>
                r.PresentationSource != Sorcha.Blueprint.Models.Credentials.PresentationSource.SorchaInternal);
```
7. Line 1153 condition: unchanged logically (it just reads `CredentialGateSatisfied`), but update the error copy at 1155 to `"Select your credential before submitting."` only if a test depends on the old wording; otherwise leave it.

- [ ] **Step 5: Run the tests + the full forms suite**

```
dotnet build tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```
Expected: new tests PASS; update any pre-existing assertions identified in Step 1 that pinned the old blocking behaviour for async-source requirements (keep them for SorchaInternal).

- [ ] **Step 6: Commit** (this commit also carries the pre-existing working-tree comment correction)

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Panels/CredentialGatePanel.razor src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/SorchaFormRenderer.razor tests/Sorcha.UI.Core.Tests/Components/Forms/CredentialGatePanelAsyncSourceTests.cs
git commit -m "fix: [#1330] - async-source credential gates no longer block submit on a discarded selection"
```

---

### Task 6: Documentation corrections (code wins)

**Files:**
- Modify: `.claude/skills/verifiable-credentials/SKILL.md`
- Modify: `docs/superpowers/briefs/2026-07-28-local-credential-presentation.md`

**Interfaces:** none — prose only.

- [ ] **Step 1: Fix the skill's two false claims**

In `.claude/skills/verifiable-credentials/SKILL.md`:

1. Quick Start table row — change
   `| PresentationRequestService | src/Services/Sorcha.Wallet.Service/Services/ | Builds SD-JWT presentations with selective disclosure |`
   to
   `| PresentationRequestService | src/Services/Sorcha.Wallet.Service/Services/ | Legacy OID4VP request lifecycle — creates/matches/VERIFIES a caller-built vpToken (in-memory store; it builds nothing) |`
2. Selective Disclosure section — change the sentence beginning `PresentationRequestService in the Wallet Service is the holder-side orchestrator.` to:
   `Presentations are built CLIENT-side: the PWA's PresentationEngine and the web app's SorchaWalletLocalPresenter (Sorcha.UI.Components.User) assemble jwt~disclosures~kb-jwt, with server-custody KB-JWT signing via POST /api/v1/wallet/presentations/sign-kb. PresentationRequestService only VERIFIES a submitted vpToken (its /submit endpoint) — it has no build path.`

- [ ] **Step 2: Correct the brief**

In `docs/superpowers/briefs/2026-07-28-local-credential-presentation.md`:
1. Change `**Status:** research not started` to `**Status:** superseded — see docs/superpowers/specs/2026-07-28-local-credential-presentation-design.md (§4 unknowns established 2026-07-28; Route A chosen)`.
2. In §7, annotate step 5 with a correction note directly beneath it:
   `> **CORRECTED 2026-07-28:** step 5 presumed the sync inline route. Investigation showed the sync path never checks key binding (CredentialVerifier sets no nonce/audience) and skips the F111 register record, while the async path was already proven completable server-custody with no device (rehearse.ps1 Complete-SorchaWalletPresentation, green on n1). 5c0ce81e stays reverted; the fix is the local completion of the async lifecycle.`
3. In §4, annotate unknown 1 with: `> **ANSWERED:** /presentations/request|submit|result are a legacy in-memory OID4VP mini-flow that VERIFIES a caller-built vpToken — nothing in it builds one. sign-kb signs a client-built KB-JWT input with the slot-108 holder key.`

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/verifiable-credentials/SKILL.md docs/superpowers/briefs/2026-07-28-local-credential-presentation.md
git commit -m "docs: [#1330] - correct PresentationRequestService claims; record route decision in brief"
```

---

### Task 7: Full build + test sweep, push, PR

**Files:** none new.

- [ ] **Step 1: Full solution build**

```
dotnet build
```
Expected: 0 errors. (XML-doc warnings on new public members are build failures in some projects — every new public member in Tasks 1-4 already carries `///` docs; fix any stragglers.)

- [ ] **Step 2: Affected test projects**

```
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```
Expected: PASS (0 failures). If `Sorcha.UI.ContractTests` exists in the repo's test set and exercises `PresentationRequestResponse`/`PresentationRequestInfo`, run it too — nothing in this branch touches those DTOs, so it must stay green:
```
dotnet test tests/Sorcha.UI.ContractTests/Sorcha.UI.ContractTests.csproj
```

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin fix/revert-unsigned-presentation-threading
gh pr create --title "fix: [#1330] - local credential presentation on /app (no QR when the wallet is right here)" --body "$(cat <<'EOF'
## Summary
- A citizen on /app whose server-custody wallet holds a matching credential now satisfies a `presentationSource: SorchaWallet` gate on this device: consent panel -> export -> sign-kb -> direct_post to the F127 callback. The QR path is unchanged and remains available (collapsed) for phone-held credentials.
- Zero server change: this is the browser port of the recipe `rehearse.ps1 Complete-SorchaWalletPresentation` proved live on n1 (all 4 AIAS Cyber paths, 2026-07-28).
- The sync inline route (5c0ce81e revert) STAYS reverted: that path never checks key binding and writes no F111 register record.
- `CredentialGatePanel` no longer blocks submission on a selection nothing consumes (async-source requirements are enforced post-submit by the lifecycle).
- Corrects the false `PresentationRequestService` "builds presentations" claims in the verifiable-credentials skill + brief.

Design: docs/superpowers/specs/2026-07-28-local-credential-presentation-design.md
Closes #1330 after the n1 live gate below.

## Test plan
- [x] Presenter unit tests (probe + present, incl. same-origin refusal, sd_hash composition pin, sign-kb alg-mismatch guard) — mutation-tested
- [x] bUnit: UseThisDevicePanel consent semantics; card local-primary/QR-fallback; gate panel non-blocking
- [ ] n1 live gate (post-merge): Cyber questionnaire on /app with no QR; cross-device QR regression run

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01RboWP2TKvm1nBG22nqsWek
EOF
)"
```

---

### Task 8: n1 live verification + closure (run by the main session, NOT a subagent — needs the deployed fleet and Stuart's browser/phone)

**Files:**
- Modify (after evidence): `C:\Users\stuart\.claude\projects\C--Projects-Sorcha\memory\seam-bugs-nothing-verifies-the-join.md`, `...\memory\f127-presentation-gate-transport.md`

- [ ] **Step 1: Merge the PR on green** (`gh pr merge --squash`), wait for Docker Publish, then deploy `sorcha-ui-web` to n1 per the `n1-deploy` skill (`pull` + `up -d --force-recreate --no-deps sorcha-ui-web`; verify the image digest changed and grep the **uncompressed** `Sorcha.UI.Components.User.wasm` under `/app/wwwroot/_framework` for `UseThisDevicePanel`).
- [ ] **Step 2: The §8 live gate.** Signed in on `https://n1.sorcha.dev/app` as a citizen holding an Assured Identity credential, submit the AIAS Cyber questionnaire. Expected: the gate card shows "Use this device"; Share & continue completes with **no QR interaction**; DevTools shows the request-object GET → `/api/v1/wallet/presentations/sign-kb` → direct_post → `/api/presentations/{id}/status` (and **never** `/api/v1/verifier/requests/`); the submission completes and the Cyber Level credential is delivered.
- [ ] **Step 3: #1327 regression run.** Repeat with the QR route (expand "Or scan with your phone", present from the phone). Expected: unchanged behaviour.
- [ ] **Step 4: Close #1330** with the DevTools/network evidence and the credential-delivery proof — evidence, not assertion.
- [ ] **Step 5: Update memories.** Add the new seam instance to `seam-bugs-nothing-verifies-the-join` (blocking UI gate wired to a selection nothing consumed) and mark `f127-presentation-gate-transport`'s "UI click-through never exercised" item resolved with the date + what was observed.

---

## Self-Review Notes

- Spec coverage: components §2 (Tasks 1-2), §3 card (Tasks 3-4), §4 panel/renderer (Task 5), testing plan (embedded per task + Task 7), corrections §6 (Task 6 + panel comment in Task 5), live gate + memory (Task 8). Out-of-scope items untouched.
- Fixture/API caveats are called out where the plan depends on a shape read from one side (DCQL fixture in Task 1, `IPresentationGateTransport` fake in Task 4, `FormContext` in Task 5): in each case the instruction is to read the authoritative file first and adjust the TEST fixture, never the production contract.
- Type consistency: `LocalPresentationCandidate`/`LocalPresentResult`/`ISorchaWalletLocalPresenter` names and signatures are identical across Tasks 1-4.
