// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.UI.Testing;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Models.Device;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Device;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;
using PresentPage = Sorcha.Wallet.Pwa.Pages.Present;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// #1195 Phase 2 (Task 7, fix round 1) — the Present page's remote/web flow routes through
/// <see cref="IPresentationEngine.Select"/> and honours the outcome: the holder-<c>cnf</c> root
/// presents via the SERVER-CUSTODY signing leg (Task 6a endpoint), a this-device copy via the
/// device-sign leg, <c>BindDeviceFirst</c> renders an explicit bind CTA (never a doomed present),
/// and <c>NoMatch</c> keeps the existing no-matching-credential UX.
/// </summary>
public sealed class PresentSelectionWiringTests : ComponentTestFixture
{
    private const string Vct = "https://sorcha.dev/vc/test/v1";

    private readonly Mock<IPresentationEngine> _engine = new();
    private readonly Mock<ICredentialCache> _credentials = new();
    private readonly Mock<IDeviceKeyService> _deviceKey = new();
    private readonly Mock<IStatusListService> _statusList = new();
    private readonly Mock<IPresentationLog> _log = new();
    private readonly Mock<ICitizenWalletClient> _walletClient = new();
    private readonly Mock<IHolderKeyClient> _holderKeys = new();
    private readonly StubHttpHandler _http = new();

    private static readonly JsonElement HolderJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"OKP","crv":"Ed25519","x":"holderX"}""");
    private static readonly string HolderThumbprint = PresentationEngine.ComputeJwkThumbprint(HolderJwk);
    private static readonly JsonElement DeviceJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"EC","crv":"P-256","x":"dx","y":"dy"}""");

    public PresentSelectionWiringTests()
    {
        Services.AddSingleton(_engine.Object);
        Services.AddSingleton(_credentials.Object);
        Services.AddSingleton(_deviceKey.Object);
        Services.AddSingleton(_statusList.Object);
        Services.AddSingleton(_log.Object);
        Services.AddSingleton(_walletClient.Object);
        Services.AddSingleton(_holderKeys.Object);
        Services.AddScoped(_ => new HttpClient(_http));
        // #1310/#1311 — ConfirmAsync/ConfirmMulti now post via the bearer-carrying typed client,
        // not the ambient HttpClient. Backed by the SAME always-200 stub handler as above so the
        // direct_post assertions in this file keep working unchanged.
        Services.AddScoped<IPresentationDirectPostClient>(_ => new PresentationDirectPostClient(new HttpClient(_http)));

        // PasteOnly intake — the paste field renders immediately, no camera interop.
        var probe = new Mock<IDeviceProfileProbe>();
        probe.Setup(p => p.GetProfileAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new DeviceProfile(DeviceFormFactor.Desktop, CameraAvailability.Unavailable));
        Services.AddScoped<IDeviceProfileProbe>(_ => probe.Object);

        _deviceKey.Setup(d => d.GetThumbprintAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync("device-thumbprint");
        _deviceKey.Setup(d => d.GetPublicJwkAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DeviceJwk);
        _holderKeys.Setup(h => h.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HolderKeysView { HolderJwk = HolderJwk });
        _credentials.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<CachedCredential> { MakeCredential(), MakeCredential() });
        _engine.Setup(e => e.ParseAsync(
                It.IsAny<string>(),
                It.IsAny<Func<string, CancellationToken, Task<string>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRequest());
    }

    // ────────────────────────── outcomes ──────────────────────────

    [Fact]
    public async Task Continue_RemoteFlow_RoutesThroughSelectWithRemoteSurfaceAndBothThumbprints()
    {
        var rootMatch = MakeMatch();
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(Selected(rootMatch, PresentationSigningMode.ServerCustody));

        var cut = await ContinueAsync();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=consent-sheet]").Should().ContainSingle(
                "a Selected outcome lands on the consent surface"));

        _engine.Verify(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                "device-thumbprint", HolderThumbprint, PresentationSurface.Remote),
            Times.Once,
            "the web present page is the REMOTE surface and must hand Select both key thumbprints");
    }

    [Fact]
    public async Task Confirm_ServerCustodySelection_SignsViaWalletServiceWithHolderJwk_NeverDeviceKey()
    {
        var rootMatch = MakeMatch();
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(Selected(rootMatch, PresentationSigningMode.ServerCustody));

        JsonElement? jwkPassedToBuild = null;
        SetupBuildVpToken(jwk => jwkPassedToBuild = jwk);
        _walletClient.Setup(w => w.SignKbJwtAsync(It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KbJwtSignResponse
            {
                Signature = Base64Url.EncodeToString(new byte[] { 9, 9 }),
                Algorithm = "EdDSA",
            });

        var cut = await ContinueAsync();
        await cut.InvokeAsync(() => cut.Find("[data-testid=consent-confirm]").Click());

        cut.WaitForAssertion(() =>
        {
            // The KB-JWT is signed by the WALLET SERVICE under the holder key — never this device.
            _walletClient.Verify(w => w.SignKbJwtAsync(
                It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _deviceKey.Verify(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never,
                "device-signing the holder-cnf root produces a KB-JWT that fails verification downstream");
            jwkPassedToBuild.Should().NotBeNull();
            jwkPassedToBuild!.Value.GetRawText().Should().Be(HolderJwk.GetRawText(),
                "the KB-JWT header must carry the HOLDER key's thumbprint, not the device key's");
        });
    }

    [Fact]
    public async Task Confirm_DeviceSelection_SignsWithDeviceKey_NeverWalletService()
    {
        var copyMatch = MakeMatch();
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(Selected(copyMatch, PresentationSigningMode.Device));

        SetupBuildVpToken(_ => { });
        _deviceKey.Setup(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new byte[] { 7 });

        var cut = await ContinueAsync();
        await cut.InvokeAsync(() => cut.Find("[data-testid=consent-confirm]").Click());

        cut.WaitForAssertion(() =>
        {
            _deviceKey.Verify(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
            _walletClient.Verify(w => w.SignKbJwtAsync(
                It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    [Fact]
    public async Task Continue_BindDeviceFirst_RendersBindCtaAndNeverAttemptsPresent()
    {
        var rootMatch = MakeMatch();
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(BindFirst(rootMatch));

        var cut = await ContinueAsync();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid=present-bind-first]").Should().ContainSingle(
                "a bind-first outcome renders explicit copy, never silence");
            cut.FindAll("[data-testid=present-bind-cta]").Should().ContainSingle(
                "the citizen is routed to the bind action, not left at a dead end");
            cut.FindAll("[data-testid=consent-sheet]").Should().BeEmpty(
                "a doomed present must never be offered");
        });
        _engine.Verify(e => e.BuildVpTokenAsync(
                It.IsAny<CredentialMatch>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<JsonElement>(),
                It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The CTA deep-links the root's credential card — the Task 6 "Bind to device" surface.
        await cut.InvokeAsync(() => cut.Find("[data-testid=present-bind-cta]").Click());
        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"credentials/{rootMatch.Credential.Id}");
    }

    [Fact]
    public async Task Continue_HolderKeyUnavailable_ShowsNamedInlineError_NoConsent()
    {
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(HolderKeyUnavailable());

        var cut = await ContinueAsync();

        cut.WaitForAssertion(() =>
        {
            var error = cut.FindAll("[data-testid=present-error]");
            error.Should().ContainSingle("fail-closed selection must surface a named error, never silence");
            error[0].TextContent.Should().Contain("holder key",
                "the citizen must be told WHAT is unavailable so a retry makes sense");
            cut.FindAll("[data-testid=consent-sheet]").Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Continue_NoMatch_KeepsExistingNoMatchingCredentialUx()
    {
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(NoMatch());

        var cut = await ContinueAsync();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("None of your credentials match",
                "a NoMatch outcome keeps the existing no-matching-credential dialog"));
    }

    [Fact]
    public async Task Continue_ChoiceRequired_ShowsPickerAndChosenCandidateKeepsItsSigningMode()
    {
        // Fix round 2 — genuinely distinct candidates restore the picker, and the chosen
        // credential keeps the signing mode its candidate carried (never re-derived).
        var rootMatch = MakeMatch();
        var legacyMatch = MakeMatch();
        _engine.Setup(e => e.Select(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PresentationSurface>()))
            .Returns(Choice(
            [
                new PresentationCandidate(rootMatch, PresentationSigningMode.ServerCustody),
                new PresentationCandidate(legacyMatch, PresentationSigningMode.Device),
            ]));

        JsonElement? jwkPassedToBuild = null;
        SetupBuildVpToken(jwk => jwkPassedToBuild = jwk);
        _walletClient.Setup(w => w.SignKbJwtAsync(It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KbJwtSignResponse
            {
                Signature = Base64Url.EncodeToString(new byte[] { 9, 9 }),
                Algorithm = "EdDSA",
            });

        var cut = await ContinueAsync();

        // The picker is offered with both distinct candidates.
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=credential-picker-candidate]").Should().HaveCount(2,
                "distinct candidates must offer the citizen a pick, exactly as before Task 7"));

        // Choose the FIRST candidate — the server-custody root.
        await cut.InvokeAsync(() =>
            cut.FindAll("[data-testid=credential-picker-candidate]")[0].Click());
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=consent-sheet]").Should().ContainSingle());

        await cut.InvokeAsync(() => cut.Find("[data-testid=consent-confirm]").Click());

        cut.WaitForAssertion(() =>
        {
            // The chosen ROOT candidate carried ServerCustody — the wallet service signs, never the device.
            _walletClient.Verify(w => w.SignKbJwtAsync(
                It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            _deviceKey.Verify(d => d.SignAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
            jwkPassedToBuild!.Value.GetRawText().Should().Be(HolderJwk.GetRawText());
        });
    }

    [Fact]
    public async Task ConfirmMulti_RootQueryIsServerCustodySigned_CopyQueryDeviceSigned_NeverDeviceSignedRoot()
    {
        // Fix round 2 — the multi-credential (F181 US2) flow classifies EACH consented query by cnf
        // thumbprint: the holder-cnf root goes server-custody, the device-cnf copy device-signs.
        // A device-signed root is the recorded silent-verification-failure trap.
        const string AddressVct = "https://sorcha.dev/vc/address/v1";
        var deviceThumbprint = PresentationEngine.ComputeJwkThumbprint(DeviceJwk);
        _deviceKey.Setup(d => d.GetThumbprintAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(deviceThumbprint);

        var rootCred = MakeCnfCredential(Vct, HolderJwk);           // cnf = holder key → root
        var copyCred = MakeCnfCredential(AddressVct, DeviceJwk);    // cnf = THIS device → copy

        var request = MakeMultiRequest(("identity", Vct), ("address", AddressVct));
        _engine.Setup(e => e.ParseAsync(
                It.IsAny<string>(), It.IsAny<Func<string, CancellationToken, Task<string>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        _engine.Setup(e => e.MatchQuery(
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<IReadOnlyList<CachedCredential>>()))
            .Returns(new DcqlMatchResult
            {
                Satisfiable = true,
                PerQuery =
                [
                    MakeQueryMatch("identity", Vct, rootCred),
                    MakeQueryMatch("address", AddressVct, copyCred),
                ],
                UnsatisfiedRequiredQueryIds = [],
                SetChoices = [],
            });

        IReadOnlyList<ConsentedQuery>? capturedConsented = null;
        JsonElement? capturedHolderJwk = null;
        Func<byte[], CancellationToken, Task<byte[]>>? capturedHolderSigner = null;
        _engine.Setup(e => e.BuildVpTokenEnvelopeAsync(
                It.IsAny<IReadOnlyList<ConsentedQuery>>(), It.IsAny<ParsedPresentationRequest>(),
                It.IsAny<JsonElement>(), It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>>(),
                It.IsAny<JsonElement?>(), It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>?>(),
                It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<ConsentedQuery> consented, ParsedPresentationRequest _,
                JsonElement _, Func<byte[], CancellationToken, Task<byte[]>> _,
                JsonElement? holderJwk, Func<byte[], CancellationToken, Task<byte[]>>? holderSigner,
                CancellationToken _) =>
            {
                capturedConsented = consented;
                capturedHolderJwk = holderJwk;
                capturedHolderSigner = holderSigner;
            })
            .ReturnsAsync("""{"identity":["vp"],"address":["vp"]}""");
        _walletClient.Setup(w => w.SignKbJwtAsync(It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KbJwtSignResponse
            {
                Signature = Base64Url.EncodeToString(new byte[] { 9 }),
                Algorithm = "EdDSA",
            });

        var cut = await ContinueAsync();
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid=consent-sheet]").Should().ContainSingle());
        await cut.InvokeAsync(() => cut.Find("[data-testid=consent-confirm]").Click());

        cut.WaitForAssertion(() => capturedConsented.Should().NotBeNull());

        // The root's query is server-custody; the copy's is device-signed. NEVER a device-signed root.
        capturedConsented!.Single(c => c.QueryId == "identity").SigningMode
            .Should().Be(PresentationSigningMode.ServerCustody,
                "device-signing the holder-cnf root fails verification downstream with no local error");
        capturedConsented.Single(c => c.QueryId == "address").SigningMode
            .Should().Be(PresentationSigningMode.Device);

        // The page supplied the holder signing leg for the server-custody entry.
        capturedHolderJwk.Should().NotBeNull();
        capturedHolderJwk!.Value.GetRawText().Should().Be(HolderJwk.GetRawText());
        capturedHolderSigner.Should().NotBeNull();
        await capturedHolderSigner!(new byte[] { 1, 2 }, CancellationToken.None);
        _walletClient.Verify(w => w.SignKbJwtAsync(
            It.IsAny<KbJwtSignRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "the holder signer must round-trip the Task 6a sign-kb endpoint");
    }

    // ────────────────────────── helpers ──────────────────────────

    /// <summary>Render the page, paste a deep link, and press Continue.</summary>
    private async Task<IRenderedComponent<PresentPage>> ContinueAsync()
    {
        var cut = Render<PresentPage>();
        await cut.InvokeAsync(() =>
            cut.Find("[data-testid=present-paste-field]").Change("openid4vp://request"));
        await cut.InvokeAsync(() => cut.Find("[data-testid=present-continue]").Click());
        return cut;
    }

    /// <summary>Mock BuildVpTokenAsync to capture the signer JWK and drive the signer delegate once.</summary>
    private void SetupBuildVpToken(Action<JsonElement> onJwk)
    {
        _engine.Setup(e => e.BuildVpTokenAsync(
                It.IsAny<CredentialMatch>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<ParsedPresentationRequest>(), It.IsAny<JsonElement>(),
                It.IsAny<Func<byte[], CancellationToken, Task<byte[]>>>(), It.IsAny<CancellationToken>()))
            .Returns(async (CredentialMatch _, IReadOnlyList<string> _, ParsedPresentationRequest _,
                JsonElement jwk, Func<byte[], CancellationToken, Task<byte[]>> signer, CancellationToken ct) =>
            {
                onJwk(jwk);
                await signer(new byte[] { 1, 2, 3 }, ct);
                return "vp";
            });
    }

    private static ParsedPresentationRequest MakeRequest() => new()
    {
        ClientId = "did:sorcha:verifier:1",
        ResponseUri = "https://verify.test/r/x/response",
        Nonce = "n",
        State = "s",
        Query = new DcqlQuery
        {
            Credentials = [new DcqlCredentialQuery
            {
                Id = "credential",
                Format = DcqlFormats.SdJwtVc,
                Meta = new DcqlCredentialMeta { VctValues = [Vct] },
            }],
        },
        RequiredVct = Vct,
        RequiredClaims = ["givenName"],
        OptionalClaims = [],
    };

    private static CachedCredential MakeCredential() => new()
    {
        Id = Guid.NewGuid(),
        Vct = Vct,
        RawSdJwt = "h.p.s",
        AvailableClaimNames = ["givenName"],
    };

    private static CredentialMatch MakeMatch() => new()
    {
        Credential = MakeCredential(),
        SatisfiedRequired = ["givenName"],
        AvailableOptional = [],
    };

    /// <summary>A two-query DCQL request so the page takes the multi-credential (F181 US2) path.</summary>
    private static ParsedPresentationRequest MakeMultiRequest(params (string Id, string Vct)[] queries) => new()
    {
        ClientId = "did:sorcha:verifier:1",
        ResponseUri = "https://verify.test/r/x/response",
        Nonce = "n",
        State = "s",
        Query = new DcqlQuery
        {
            Credentials = queries.Select(q => new DcqlCredentialQuery
            {
                Id = q.Id,
                Format = DcqlFormats.SdJwtVc,
                Meta = new DcqlCredentialMeta { VctValues = [q.Vct] },
            }).ToList(),
        },
        RequiredVct = queries[0].Vct,
        RequiredClaims = [],
        OptionalClaims = [],
    };

    private static DcqlQueryMatch MakeQueryMatch(string queryId, string vct, CachedCredential credential) => new()
    {
        QueryId = queryId,
        Vct = vct,
        RequiredClaims = [],
        OptionalClaims = [],
        Candidates =
        [
            new CredentialMatch { Credential = credential, SatisfiedRequired = [], AvailableOptional = [] },
        ],
    };

    /// <summary>A cached SD-JWT whose payload carries the given cnf.jwk — classified by REAL thumbprint logic.</summary>
    private static CachedCredential MakeCnfCredential(string vct, JsonElement cnfJwk)
    {
        var headerSeg = Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "dc+sd-jwt" }));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object> { ["vct"] = vct, ["cnf"] = new Dictionary<string, object> { ["jwk"] = cnfJwk } }));
        return new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = vct,
            RawSdJwt = $"{headerSeg}.{payloadSeg}.sig",
            AvailableClaimNames = [],
        };
    }

    // PresentationSelection factories are internal to the PWA assembly; build results directly.
    private static PresentationSelection Selected(CredentialMatch match, PresentationSigningMode mode) =>
        new() { Outcome = PresentationSelectionOutcome.Selected, Match = match, SigningMode = mode };

    private static PresentationSelection Choice(IReadOnlyList<PresentationCandidate> candidates) =>
        new() { Outcome = PresentationSelectionOutcome.ChoiceRequired, Candidates = candidates };

    private static PresentationSelection BindFirst(CredentialMatch root) =>
        new() { Outcome = PresentationSelectionOutcome.BindDeviceFirst, RootToBind = root };

    private static PresentationSelection HolderKeyUnavailable() =>
        new() { Outcome = PresentationSelectionOutcome.HolderKeyUnavailable };

    private static PresentationSelection NoMatch() =>
        new() { Outcome = PresentationSelectionOutcome.NoMatch };

    /// <summary>Always-200 handler so ConfirmAsync's direct_post never touches the network.</summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
