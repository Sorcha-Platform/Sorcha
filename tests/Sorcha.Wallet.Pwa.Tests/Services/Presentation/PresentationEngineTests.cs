// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Sorcha.UI.Core.Models.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Presentation;

/// <summary>
/// Tests for <see cref="PresentationEngine"/> (Feature 114 T095, Feature 181 US1). Covers:
/// ParseAsync happy path + error paths (request_uri + DCQL request-object form, legacy
/// inline presentation_definition refusal), match (success / wrong vct / missing claim),
/// build (KB-JWT signature + sd_hash + only-approved-disclosures invariant).
/// </summary>
public sealed class PresentationEngineTests
{
    private readonly PresentationEngine _engine = new(TimeProvider.System,
        NullLogger<PresentationEngine>.Instance);

    private const string Vct = "https://sorcha.dev/vc/test/v1";
    private const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    // ────────────────────────── ParseAsync ──────────────────────────

    [Fact]
    public async Task ParseAsync_ValidRequestUriDeepLink_ReturnsPopulatedRequest()
    {
        var query = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"], ["familyName"])],
            purpose: "prove your name");
        var jwt = MakeRequestObjectJwt(query);

        var parsed = await _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));

        parsed.ClientId.Should().Be(ClientId);
        parsed.Nonce.Should().Be("n0nce");
        parsed.State.Should().Be("state-1");
        parsed.RequiredVct.Should().Be(Vct);
        parsed.RequiredClaims.Should().ContainSingle().Which.Should().Be("givenName");
        parsed.OptionalClaims.Should().ContainSingle().Which.Should().Be("familyName");
        parsed.ResponseUri.Should().Be("https://verify.test/r/sess-1/response");
        parsed.Purpose.Should().Be("prove your name");
        parsed.ResponseMode.Should().Be("direct_post");
    }

    [Fact]
    public async Task ParseAsync_NotOpenid4VpScheme_ThrowsFormatException()
    {
        Func<Task> act = () => _engine.ParseAsync("https://verify.test/foo", Fetch("h.p."));
        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task ParseAsync_MissingRequestUri_ThrowsFormatException()
    {
        var link = $"openid4vp://?client_id={Uri.EscapeDataString(ClientId)}&nonce=n";
        Func<Task> act = () => _engine.ParseAsync(link, Fetch("h.p."));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*request_uri*");
    }

    [Fact]
    public async Task ParseAsync_InlinePresentationDefinition_ThrowsLegacyDialect()
    {
        var link = $"openid4vp://?client_id={Uri.EscapeDataString(ClientId)}" +
                   "&presentation_definition=" + Uri.EscapeDataString("{}");
        Func<Task> act = () => _engine.ParseAsync(link, Fetch("h.p."));
        (await act.Should().ThrowAsync<DcqlParseException>())
            .Which.Code.Should().Be(DcqlErrorCodes.LegacyDialect);
    }

    [Fact]
    public async Task ParseAsync_FetcherReturnsNonJwt_ThrowsFormatException()
    {
        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink(), Fetch("not-a-jwt"));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*not a JWT*");
    }

    [Fact]
    public async Task ParseAsync_MissingClientId_ThrowsFormatException()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query, clientId: null);
        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*client_id*");
    }

    [Fact]
    public async Task ParseAsync_MissingNonce_ThrowsFormatException()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query, nonce: null);
        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*nonce*");
    }

    [Fact]
    public async Task ParseAsync_MissingResponseUri_ThrowsFormatException()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query, responseUri: null);
        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*response_uri*");
    }

    [Fact]
    public async Task ParseAsync_MissingState_ThrowsFormatException()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query, state: null);
        Func<Task> act = () => _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));
        (await act.Should().ThrowAsync<FormatException>())
            .WithMessage("*state*");
    }

    [Fact]
    public async Task ParseAsync_StatePresent_IsCapturedVerbatim()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query, state: "8f14e45f-ceea-4f0a-9a0c-example");

        var parsed = await _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));

        parsed.State.Should().Be("8f14e45f-ceea-4f0a-9a0c-example");
    }

    [Fact]
    public async Task ParseAsync_NestedClaimPath_UsesSlashPathConvention()
    {
        var query = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("cred1", Vct, ["/address/street"])]);
        var jwt = MakeRequestObjectJwt(query);

        var parsed = await _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));

        parsed.RequiredClaims.Should().ContainSingle().Which.Should().Be("/address/street");
        parsed.OptionalClaims.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_NoPurpose_PurposeIsNull()
    {
        var query = DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"])]);
        var jwt = MakeRequestObjectJwt(query);

        var parsed = await _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));

        parsed.Purpose.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_MultiCredentialQuery_FirstCredentialIsUsed()
    {
        // US1 consumes the first credential query only (multi-query consent is US2).
        var query = DcqlRequestBuilder.Build(
        [
            DcqlCredentialAsk.SdJwt("cred1", Vct, ["givenName"]),
            DcqlCredentialAsk.SdJwt("cred2", "https://sorcha.dev/vc/other/v1", ["licenceNumber"]),
        ]);
        var jwt = MakeRequestObjectJwt(query);

        var parsed = await _engine.ParseAsync(MakeDeepLink(), Fetch(jwt));

        parsed.RequiredVct.Should().Be(Vct);
        parsed.RequiredClaims.Should().ContainSingle().Which.Should().Be("givenName");
    }

    // ────────────────────────── Match ──────────────────────────

    [Fact]
    public void Match_VctAndAllRequired_Satisfied_ReturnsMatch()
    {
        var req = MakeRequest(["givenName"], ["familyName"]);
        var cred = MakeCredential(Vct, ["givenName", "familyName", "dateOfBirth"]);

        var matches = _engine.Match(req, [cred]);

        matches.Should().HaveCount(1);
        matches[0].SatisfiedRequired.Should().ContainSingle().Which.Should().Be("givenName");
        matches[0].AvailableOptional.Should().ContainSingle().Which.Should().Be("familyName");
    }

    [Fact]
    public void Match_WrongVct_NoMatch()
    {
        var req = MakeRequest(["givenName"], []);
        var cred = MakeCredential("https://wrong/vct", ["givenName"]);
        _engine.Match(req, [cred]).Should().BeEmpty();
    }

    [Fact]
    public void Match_RequiredClaimMissing_NoMatch()
    {
        var req = MakeRequest(["givenName", "ssn"], []);
        var cred = MakeCredential(Vct, ["givenName"]);
        _engine.Match(req, [cred]).Should().BeEmpty();
    }

    // ────────────────────────── BuildVpTokenAsync ──────────────────────────

    [Fact]
    public async Task BuildVpTokenAsync_HappyPath_KbJwtVerifiesAgainstDeviceKey()
    {
        var (cred, allDisclosures) = MakeRealCredential(Vct,
            ("givenName", "Stuart"), ("familyName", "Fraser"));

        var req = MakeRequest(["givenName"], ["familyName"]);
        var match = _engine.Match(req, [cred]).Should().ContainSingle().Subject;

        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));

        Func<byte[], CancellationToken, Task<byte[]>> signer =
            (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256));

        var vp = await _engine.BuildVpTokenAsync(
            match,
            ["givenName", "familyName"],
            req,
            deviceJwk,
            signer);

        // Structural assertions
        var (credJwt, disclosures, kbJwt) = PresentationEngine.SplitSdJwt(vp);
        credJwt.Should().NotBeNullOrEmpty();
        disclosures.Should().HaveCount(2);
        kbJwt.Should().NotBeNullOrEmpty();

        // KB-JWT signature must verify against the device key
        var parts = kbJwt!.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        deviceEcdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();

        // Payload binds the right nonce + audience
        var kbPayload = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
        kbPayload.GetProperty("nonce").GetString().Should().Be(req.Nonce);
        kbPayload.GetProperty("aud").GetString().Should().Be(req.ClientId);
        kbPayload.GetProperty("sd_hash").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task BuildVpTokenAsync_OnlyApprovedDisclosuresAreIncluded()
    {
        var (cred, _) = MakeRealCredential(Vct,
            ("givenName", "Stuart"), ("familyName", "Fraser"), ("dateOfBirth", "1980-01-01"));
        var req = MakeRequest(["givenName"], ["familyName", "dateOfBirth"]);
        var match = _engine.Match(req, [cred]).Single();

        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));

        // Only givenName approved — familyName and dateOfBirth withheld
        var vp = await _engine.BuildVpTokenAsync(
            match, ["givenName"], req, deviceJwk,
            (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256)));

        var (_, disclosures, _) = PresentationEngine.SplitSdJwt(vp);
        disclosures.Should().HaveCount(1);
        var name = PresentationEngine.ReadDisclosureName(disclosures[0]);
        name.Should().Be("givenName");
    }

    [Fact]
    public async Task BuildVpTokenAsync_OkpHolderJwk_EmitsEdDsaHeaderAndOkpThumbprint()
    {
        // #1195 Phase 2 — a holder-cnf ROOT presented server-custody signs under the
        // citizen's Ed25519 holder key. The KB-JWT header must say EdDSA (hardcoded
        // ES256 was the mirror of the verifier-side "ES256-only rejected Ed25519-holder
        // presentations" bug) and kid must be the RFC 7638 OKP thumbprint (crv, kty, x).
        var (cred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var req = MakeRequest(["givenName"], []);
        var match = _engine.Match(req, [cred]).Single();

        var holderJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"OKP","crv":"Ed25519","x":"holderX"}""");

        var vp = await _engine.BuildVpTokenAsync(
            match, ["givenName"], req, holderJwk,
            (_, _) => Task.FromResult(new byte[] { 1, 2, 3 }));

        var (_, _, kbJwt) = PresentationEngine.SplitSdJwt(vp);
        var header = JsonSerializer.Deserialize<JsonElement>(
            Base64Url.DecodeFromChars(kbJwt!.Split('.')[0]));

        header.GetProperty("alg").GetString().Should().Be("EdDSA");

        var expectedThumbprint = Base64Url.EncodeToString(SHA256.HashData(
            Encoding.UTF8.GetBytes("{\"crv\":\"Ed25519\",\"kty\":\"OKP\",\"x\":\"holderX\"}")));
        header.GetProperty("kid").GetString().Should().Be(expectedThumbprint);
    }

    [Fact]
    public async Task BuildVpTokenAsync_ApprovedClaimsMissingRequired_Throws()
    {
        var (cred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var req = MakeRequest(["givenName"], []);
        var match = _engine.Match(req, [cred]).Single();

        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));

        Func<Task> act = async () => await _engine.BuildVpTokenAsync(
            match, [], req, deviceJwk,
            (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256)));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─────────────────── BuildVpTokenEnvelopeAsync (Feature 181 US2) ───────────────────

    [Fact]
    public async Task BuildVpTokenEnvelopeAsync_MultipleQueries_ProducesObjectKeyedEnvelope()
    {
        const string AddressVct = "https://sorcha.dev/vc/address/v1";
        var (idCred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var (addrCred, _) = MakeRealCredential(AddressVct, ("postcode", "EH1 1AA"));

        var req = MakeRequest(["givenName"], []);
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));
        Func<byte[], CancellationToken, Task<byte[]>> signer =
            (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256));

        var idMatch = new CredentialMatch { Credential = idCred, SatisfiedRequired = ["givenName"], AvailableOptional = [] };
        var addrMatch = new CredentialMatch { Credential = addrCred, SatisfiedRequired = ["postcode"], AvailableOptional = [] };

        var consented = new List<ConsentedQuery>
        {
            new("identity", idMatch, ["givenName"], ["givenName"]),
            new("address", addrMatch, ["postcode"], ["postcode"]),
        };

        var json = await _engine.BuildVpTokenEnvelopeAsync(consented, req, deviceJwk, signer);

        // One object-keyed entry per query, each a single SD-JWT presentation.
        var envelope = DcqlVpToken.Parse(json);
        envelope.Presentations.Keys.Should().BeEquivalentTo(new[] { "identity", "address" });
        envelope.Presentations["identity"].Should().ContainSingle();
        envelope.Presentations["address"].Should().ContainSingle();

        // Each presentation carries a device-signed KB-JWT binding the request nonce + audience.
        foreach (var vp in envelope.Presentations.Values.Select(v => v[0]))
        {
            var (_, _, kbJwt) = PresentationEngine.SplitSdJwt(vp);
            kbJwt.Should().NotBeNullOrEmpty();
            var parts = kbJwt!.Split('.');
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = Base64Url.DecodeFromChars(parts[2]);
            deviceEcdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();
            var kbPayload = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
            kbPayload.GetProperty("aud").GetString().Should().Be(req.ClientId);
            kbPayload.GetProperty("nonce").GetString().Should().Be(req.Nonce);
        }
    }

    [Fact]
    public async Task BuildVpTokenEnvelopeAsync_OnlyApprovedDisclosuresPerQuery()
    {
        var (idCred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"), ("familyName", "Fraser"));
        var req = MakeRequest(["givenName"], ["familyName"]);
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));
        Func<byte[], CancellationToken, Task<byte[]>> signer =
            (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256));

        var match = new CredentialMatch
        {
            Credential = idCred,
            SatisfiedRequired = ["givenName"],
            AvailableOptional = ["familyName"],
        };
        // familyName is optional and NOT approved — only givenName should be disclosed.
        var consented = new List<ConsentedQuery> { new("identity", match, ["givenName"], ["givenName"]) };

        var json = await _engine.BuildVpTokenEnvelopeAsync(consented, req, deviceJwk, signer);

        var vp = DcqlVpToken.Parse(json).Presentations["identity"][0];
        var (_, disclosures, _) = PresentationEngine.SplitSdJwt(vp);
        disclosures.Should().ContainSingle();
        PresentationEngine.ReadDisclosureName(disclosures[0]).Should().Be("givenName");
    }

    [Fact]
    public async Task BuildVpTokenEnvelopeAsync_MixedModes_RootServerCustodySigned_CopyDeviceSigned()
    {
        // #1195 Phase 2 (Task 7 fix round 2) — mixed signing modes within ONE envelope: each query's
        // presentation is an independent SD-JWT with its own KB-JWT, verified per-query against its own
        // credential's cnf. A ServerCustody entry must be signed by the HOLDER signer, a Device entry by
        // the DEVICE signer — a device-signed root is the recorded silent-verification-failure trap.
        const string AddressVct = "https://sorcha.dev/vc/address/v1";
        var (rootCred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var (copyCred, _) = MakeRealCredential(AddressVct, ("postcode", "EH1 1AA"));

        var req = MakeRequest(["givenName"], []);
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));
        var holderJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(holderEcdsa));

        var deviceSigned = 0;
        var holderSigned = 0;
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner = (data, _) =>
        {
            deviceSigned++;
            return Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256));
        };
        Func<byte[], CancellationToken, Task<byte[]>> holderSigner = (data, _) =>
        {
            holderSigned++;
            return Task.FromResult(holderEcdsa.SignData(data, HashAlgorithmName.SHA256));
        };

        var rootMatch = new CredentialMatch { Credential = rootCred, SatisfiedRequired = ["givenName"], AvailableOptional = [] };
        var copyMatch = new CredentialMatch { Credential = copyCred, SatisfiedRequired = ["postcode"], AvailableOptional = [] };
        var consented = new List<ConsentedQuery>
        {
            new("identity", rootMatch, ["givenName"], ["givenName"], PresentationSigningMode.ServerCustody),
            new("address", copyMatch, ["postcode"], ["postcode"], PresentationSigningMode.Device),
        };

        var json = await _engine.BuildVpTokenEnvelopeAsync(
            consented, req, deviceJwk, deviceSigner, holderJwk, holderSigner);

        deviceSigned.Should().Be(1);
        holderSigned.Should().Be(1);

        var envelope = DcqlVpToken.Parse(json);

        // The ServerCustody presentation's KB-JWT verifies against the HOLDER key and carries its kid.
        AssertKbJwtSignedBy(envelope.Presentations["identity"][0], holderEcdsa, holderJwk);
        // The Device presentation's KB-JWT verifies against the DEVICE key and carries its kid.
        AssertKbJwtSignedBy(envelope.Presentations["address"][0], deviceEcdsa, deviceJwk);
    }

    [Fact]
    public async Task BuildVpTokenEnvelopeAsync_ServerCustodyEntryWithoutHolderSigner_ThrowsLoudly()
    {
        // Never a silent fallback to device-signing the root — that KB-JWT fails verification
        // downstream with no local error.
        var (rootCred, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var req = MakeRequest(["givenName"], []);
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));

        var rootMatch = new CredentialMatch { Credential = rootCred, SatisfiedRequired = ["givenName"], AvailableOptional = [] };
        var consented = new List<ConsentedQuery>
        {
            new("identity", rootMatch, ["givenName"], ["givenName"], PresentationSigningMode.ServerCustody),
        };

        var deviceSignerCalls = 0;
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner = (data, _) =>
        {
            deviceSignerCalls++;
            return Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256));
        };

        Func<Task> act = async () => await _engine.BuildVpTokenEnvelopeAsync(
            consented, req, deviceJwk, deviceSigner);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*server-custody*");
        deviceSignerCalls.Should().Be(0, "the device signer must NEVER sign a server-custody entry");
    }

    /// <summary>Assert a presentation's KB-JWT verifies against <paramref name="key"/> and its kid is the JWK's thumbprint.</summary>
    private static void AssertKbJwtSignedBy(string vp, ECDsa key, JsonElement jwk)
    {
        var (_, _, kbJwt) = PresentationEngine.SplitSdJwt(vp);
        kbJwt.Should().NotBeNullOrEmpty();
        var parts = kbJwt!.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);
        key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256).Should().BeTrue();

        var header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
        header.GetProperty("kid").GetString().Should().Be(PresentationEngine.ComputeJwkThumbprint(jwk));
    }

    [Fact]
    public async Task BuildVpTokenEnvelopeAsync_Empty_Throws()
    {
        var req = MakeRequest(["givenName"], []);
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JwkOf(deviceEcdsa));

        Func<Task> act = async () => await _engine.BuildVpTokenEnvelopeAsync(
            [], req, deviceJwk, (data, _) => Task.FromResult(deviceEcdsa.SignData(data, HashAlgorithmName.SHA256)));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─────────────── Select — per-surface credential + signing choice (Task 7) ───────────────

    [Fact]
    public void Select_InPerson_RootAndThisDeviceCopyCached_ReturnsDeviceCopyDeviceSign()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JwkOf(deviceKey);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var selection = _engine.Select(
            req, [root, deviceCopy], deviceThumbprint, HolderThumbprint, PresentationSurface.InPerson);

        selection.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        selection.Match!.Credential.Id.Should().Be(deviceCopy.Id);
        selection.SigningMode.Should().Be(PresentationSigningMode.Device);
    }

    [Fact]
    public void Select_Remote_RootAndThisDeviceCopyCached_ReturnsRootServerCustody()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JwkOf(deviceKey);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var selection = _engine.Select(
            req, [root, deviceCopy], deviceThumbprint, HolderThumbprint, PresentationSurface.Remote);

        selection.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        selection.Match!.Credential.Id.Should().Be(root.Id);
        // The root is NEVER device-signed — that KB-JWT fails verification with no local error.
        selection.SigningMode.Should().Be(PresentationSigningMode.ServerCustody);
    }

    [Fact]
    public void Select_InPerson_OnlyRootCached_ReturnsBindDeviceFirst()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var selection = _engine.Select(
            req, [root], deviceThumbprint, HolderThumbprint, PresentationSurface.InPerson);

        // NOT a doomed present, and NOT the root (device-signing the root cannot verify).
        selection.Outcome.Should().Be(PresentationSelectionOutcome.BindDeviceFirst);
        selection.Match.Should().BeNull();
        // The outcome carries the root so the UI can deep-link its credential card (the bind surface).
        selection.RootToBind!.Credential.Id.Should().Be(root.Id);
    }

    [Fact]
    public void Select_DifferentDeviceCopy_IsNeverSelectedForDeviceSigning()
    {
        // A copy bound to ANOTHER device (its cnf thumbprint ≠ this device's AND ≠ the holder's) can
        // neither be device-signed here nor server-custody signed. Never selected on any surface.
        using var thisDevice = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherDevice = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var thisThumbprint = ThumbprintOf(JwkOf(thisDevice));

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var otherDeviceCopy = MakeCnfCredential(Vct, JwkOf(otherDevice), "givenName");
        var req = MakeRequest(["givenName"], []);

        // In person: no copy for THIS device → bind first, and the other-device copy is not device-signed.
        var inPerson = _engine.Select(
            req, [root, otherDeviceCopy], thisThumbprint, HolderThumbprint, PresentationSurface.InPerson);
        inPerson.Outcome.Should().Be(PresentationSelectionOutcome.BindDeviceFirst);
        inPerson.Match.Should().BeNull();

        // Auto / remote fall back to the ROOT (server custody), never the other-device copy.
        foreach (var surface in new[] { PresentationSurface.Auto, PresentationSurface.Remote })
        {
            var s = _engine.Select(req, [root, otherDeviceCopy], thisThumbprint, HolderThumbprint, surface);
            s.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
            s.Match!.Credential.Id.Should().Be(root.Id);
            s.Match.Credential.Id.Should().NotBe(otherDeviceCopy.Id);
            s.SigningMode.Should().Be(PresentationSigningMode.ServerCustody);
        }
    }

    [Fact]
    public void Select_OnlyDifferentDeviceCopy_NoRoot_ReturnsNoMatch()
    {
        // Nothing usable on this device: an other-device copy alone can't be presented AND can't be
        // bound (binding needs the root). Not a bind-first prompt — a plain no-match.
        using var thisDevice = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherDevice = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var thisThumbprint = ThumbprintOf(JwkOf(thisDevice));

        var otherDeviceCopy = MakeCnfCredential(Vct, JwkOf(otherDevice), "givenName");
        var req = MakeRequest(["givenName"], []);

        _engine.Select(req, [otherDeviceCopy], thisThumbprint, HolderThumbprint, PresentationSurface.InPerson)
            .Outcome.Should().Be(PresentationSelectionOutcome.NoMatch);
        _engine.Select(req, [otherDeviceCopy], thisThumbprint, HolderThumbprint, PresentationSurface.Auto)
            .Outcome.Should().Be(PresentationSelectionOutcome.NoMatch);
    }

    [Fact]
    public void Select_Auto_PrefersThisDeviceCopyOverRoot()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JwkOf(deviceKey);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var selection = _engine.Select(
            req, [root, deviceCopy], deviceThumbprint, HolderThumbprint, PresentationSurface.Auto);

        selection.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        selection.Match!.Credential.Id.Should().Be(deviceCopy.Id);
        selection.SigningMode.Should().Be(PresentationSigningMode.Device);
    }

    [Fact]
    public void Select_NullDeviceThumbprint_NoDeviceKey_SelectsRootServerCustody()
    {
        // A host with no usable device key (non-PWA / bridge absent) can never device-sign, so even a
        // device copy in the cache is not signable here. Auto/Remote fall to the root; in person, with a
        // copy present but unsignable and the root bindable, the answer is bind-first.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceCopy = MakeCnfCredential(Vct, JwkOf(deviceKey), "givenName");
        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var remote = _engine.Select(
            req, [root, deviceCopy], deviceThumbprint: null, HolderThumbprint, PresentationSurface.Remote);
        remote.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        remote.Match!.Credential.Id.Should().Be(root.Id);
        remote.SigningMode.Should().Be(PresentationSigningMode.ServerCustody);

        _engine.Select(req, [root, deviceCopy], deviceThumbprint: null, HolderThumbprint, PresentationSurface.InPerson)
            .Outcome.Should().Be(PresentationSelectionOutcome.BindDeviceFirst);
    }

    [Fact]
    public void Select_RequestVctNotHeld_ReturnsNoMatch()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var root = MakeCnfCredential("https://sorcha.dev/vc/other/v1", OkpHolderJwk, "givenName");
        var req = MakeRequest(["givenName"], []);   // asks for Vct, which is not held

        _engine.Select(req, [root], deviceThumbprint, HolderThumbprint, PresentationSurface.Remote)
            .Outcome.Should().Be(PresentationSelectionOutcome.NoMatch);
    }

    [Fact]
    public void Select_P256HolderWallet_RootAndCopyDiscriminatedByThumbprintNotKeyType()
    {
        // Fix round 1 — a P-256 wallet's holder key is EC, so its root's cnf is EC too: by KEY TYPE the
        // root and a device copy are indistinguishable. Discrimination MUST be by RFC 7638 thumbprint
        // against the holder key (the Task 5 server-side rule), or every P-256 wallet's root would be
        // misclassified and selection would break for those users.
        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holderJwk = JwkOf(holderKey);
        var deviceJwk = JwkOf(deviceKey);
        var holderThumbprint = ThumbprintOf(holderJwk);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var ecRoot = MakeCnfCredential(Vct, holderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var remote = _engine.Select(
            req, [ecRoot, deviceCopy], deviceThumbprint, holderThumbprint, PresentationSurface.Remote);
        remote.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        remote.Match!.Credential.Id.Should().Be(ecRoot.Id);
        remote.SigningMode.Should().Be(PresentationSigningMode.ServerCustody);

        var inPerson = _engine.Select(
            req, [ecRoot, deviceCopy], deviceThumbprint, holderThumbprint, PresentationSurface.InPerson);
        inPerson.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        inPerson.Match!.Credential.Id.Should().Be(deviceCopy.Id);
        inPerson.SigningMode.Should().Be(PresentationSigningMode.Device);

        // And with only the EC root cached, in person still answers bind-first — not a doomed present.
        var bindFirst = _engine.Select(
            req, [ecRoot], deviceThumbprint, holderThumbprint, PresentationSurface.InPerson);
        bindFirst.Outcome.Should().Be(PresentationSelectionOutcome.BindDeviceFirst);
        bindFirst.RootToBind!.Credential.Id.Should().Be(ecRoot.Id);
    }

    [Fact]
    public void Select_HolderThumbprintUnavailable_BoundCandidateOnly_FailsClosedWithNamedOutcome()
    {
        // Fix round 1 — without the holder thumbprint, a bound credential that is not THIS device's
        // copy could be the root OR a foreign device's copy. Selection must fail CLOSED with the named
        // outcome, never guess (a guessed server-custody sign over a foreign copy cannot verify).
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        foreach (var surface in new[]
                 { PresentationSurface.Auto, PresentationSurface.Remote, PresentationSurface.InPerson })
        {
            var s = _engine.Select(req, [root], deviceThumbprint, holderThumbprint: null, surface);
            s.Outcome.Should().Be(PresentationSelectionOutcome.HolderKeyUnavailable,
                $"an unclassifiable bound credential must fail closed on {surface}");
            s.Match.Should().BeNull();
        }
    }

    [Fact]
    public void Select_HolderThumbprintUnavailable_ThisDeviceCopyPresent_StillSelectsCopy()
    {
        // The this-device copy is DEFINITIVELY classifiable (its cnf thumbprint is ours) — no holder
        // thumbprint needed. Selecting it is not a guess; only unclassifiable-only caches fail closed.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JwkOf(deviceKey);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        var s = _engine.Select(
            req, [root, deviceCopy], deviceThumbprint, holderThumbprint: null, PresentationSurface.Remote);

        s.Outcome.Should().Be(PresentationSelectionOutcome.Selected);
        s.Match!.Credential.Id.Should().Be(deviceCopy.Id);
        s.SigningMode.Should().Be(PresentationSigningMode.Device);
    }

    [Fact]
    public void Select_TwoDistinctCredentials_ReturnsChoiceRequiredWithBoth()
    {
        // Fix round 2 — two GENUINELY distinct credentials (not a root/copy pair) matching the ask
        // must offer the citizen a pick, exactly as before Task 7.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var (legacyA, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var (legacyB, _) = MakeRealCredential(Vct, ("givenName", "Stiubhart"));
        var req = MakeRequest(["givenName"], []);

        var s = _engine.Select(
            req, [legacyA, legacyB], deviceThumbprint, HolderThumbprint, PresentationSurface.Remote);

        s.Outcome.Should().Be(PresentationSelectionOutcome.ChoiceRequired);
        s.Match.Should().BeNull();
        s.Candidates.Should().HaveCount(2);
        s.Candidates!.Select(c => c.Match.Credential.Id)
            .Should().BeEquivalentTo([legacyA.Id, legacyB.Id]);
        s.Candidates.Should().OnlyContain(c => c.SigningMode == PresentationSigningMode.Device);
    }

    [Fact]
    public void Select_RootPlusCopyPairOnly_AutoSelectsNoPicker()
    {
        // Fix round 2 — the root + this-device copy are ONE credential family (one identity, two
        // bindings): they collapse to the per-surface representative and never offer a picker.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceJwk = JwkOf(deviceKey);
        var deviceThumbprint = ThumbprintOf(deviceJwk);

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var deviceCopy = MakeCnfCredential(Vct, deviceJwk, "givenName");
        var req = MakeRequest(["givenName"], []);

        foreach (var surface in new[]
                 { PresentationSurface.Auto, PresentationSurface.Remote, PresentationSurface.InPerson })
        {
            var s = _engine.Select(req, [root, deviceCopy], deviceThumbprint, HolderThumbprint, surface);
            s.Outcome.Should().Be(PresentationSelectionOutcome.Selected,
                $"a root+copy pair is one identity and must auto-select on {surface}, no picker");
        }
    }

    [Fact]
    public void Select_BoundFamilyPlusDistinctLegacy_OffersPickerWithPerCandidateSigningModes()
    {
        // Fix round 2 — the bound family collapses to ONE representative, but a distinct legacy
        // credential is a real alternative: the citizen picks, and each candidate carries ITS OWN
        // signing mode (the picker must never re-derive the pairing).
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var root = MakeCnfCredential(Vct, OkpHolderJwk, "givenName");
        var (legacy, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var req = MakeRequest(["givenName"], []);

        var s = _engine.Select(
            req, [root, legacy], deviceThumbprint, HolderThumbprint, PresentationSurface.Remote);

        s.Outcome.Should().Be(PresentationSelectionOutcome.ChoiceRequired);
        s.Candidates.Should().HaveCount(2);
        var rootCandidate = s.Candidates!.Single(c => c.Match.Credential.Id == root.Id);
        rootCandidate.SigningMode.Should().Be(PresentationSigningMode.ServerCustody,
            "the root candidate must stay paired with server-custody signing even inside a picker");
        s.Candidates!.Single(c => c.Match.Credential.Id == legacy.Id)
            .SigningMode.Should().Be(PresentationSigningMode.Device);
    }

    [Fact]
    public void Select_LegacyCredentialWithoutCnf_KeepsPhase1DeviceSignedBehaviour()
    {
        // A pre-Phase-2 credential carries no cnf — there is no binding for a verifier to check, so the
        // Phase-1 device-signed present still verifies. Routing Present.razor through Select must NOT
        // regress these: they stay selectable (device-signed) on every surface.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceThumbprint = ThumbprintOf(JwkOf(deviceKey));

        var (legacy, _) = MakeRealCredential(Vct, ("givenName", "Stuart"));
        var req = MakeRequest(["givenName"], []);

        foreach (var surface in new[]
                 { PresentationSurface.Auto, PresentationSurface.Remote, PresentationSurface.InPerson })
        {
            var s = _engine.Select(req, [legacy], deviceThumbprint, HolderThumbprint, surface);
            s.Outcome.Should().Be(PresentationSelectionOutcome.Selected,
                $"a legacy no-cnf credential must stay presentable on {surface}");
            s.Match!.Credential.Id.Should().Be(legacy.Id);
            s.SigningMode.Should().Be(PresentationSigningMode.Device);
        }
    }

    // ────────────────────────── helpers ──────────────────────────

    /// <summary>A holder-cnf root's confirmation key is the citizen's Ed25519 (OKP) holder key.</summary>
    private const string OkpHolderJwk = """{"kty":"OKP","crv":"Ed25519","x":"holderRootPublicKeyX"}""";

    /// <summary>RFC 7638 thumbprint of <see cref="OkpHolderJwk"/> — the default holder thumbprint input.</summary>
    private static readonly string HolderThumbprint = ThumbprintOf(OkpHolderJwk);

    private static string ThumbprintOf(string jwkJson)
        => PresentationEngine.ComputeJwkThumbprint(JsonSerializer.Deserialize<JsonElement>(jwkJson));

    /// <summary>
    /// Build a cached SD-JWT credential carrying a <c>cnf.jwk</c> in its payload (so the selection
    /// layer can read its key binding) plus real disclosures for <paramref name="claimNames"/>.
    /// </summary>
    private static CachedCredential MakeCnfCredential(string vct, string cnfJwkJson, params string[] claimNames)
    {
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            typ = "dc+sd-jwt",
        }));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = "did:sorcha:org:test",
            ["vct"] = vct,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["cnf"] = new Dictionary<string, object>
            {
                ["jwk"] = JsonSerializer.Deserialize<JsonElement>(cnfJwkJson),
            },
        }));
        var credentialJwt = $"{headerSeg}.{payloadSeg}.sig";

        var disclosures = new List<string>();
        Span<byte> salt = stackalloc byte[16];
        foreach (var name in claimNames)
        {
            RandomNumberGenerator.Fill(salt);
            disclosures.Add(Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
                new object[] { Base64Url.EncodeToString(salt), name, "value" })));
        }

        return new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = vct,
            RawSdJwt = credentialJwt + string.Concat(disclosures.Select(d => "~" + d)),
            AvailableClaimNames = claimNames,
        };
    }


    private static ParsedPresentationRequest MakeRequest(IReadOnlyList<string> required, IReadOnlyList<string> optional)
        => new()
        {
            ClientId = ClientId,
            ResponseUri = "https://verify.test/r/x/response",
            Nonce = "abc",
            State = "state-1",
            Query = new DcqlQuery
            {
                Credentials = [new DcqlCredentialQuery
                {
                    Id = "credential",
                    Format = DcqlFormats.SdJwtVc,
                    Meta = new DcqlCredentialMeta { VctValues = [Vct] },
                    Claims = required.Concat(optional).Select(c => new DcqlClaimQuery { Path = [c] }).ToList(),
                }],
            },
            RequiredVct = Vct,
            RequiredClaims = required,
            OptionalClaims = optional,
        };

    private static CachedCredential MakeCredential(string vct, IReadOnlyList<string> claimNames)
        => new()
        {
            Id = Guid.NewGuid(),
            Vct = vct,
            RawSdJwt = "header.payload.sig",
            AvailableClaimNames = claimNames,
        };

    /// <summary>Build a credential whose RawSdJwt is well-formed and includes real disclosures.</summary>
    private static (CachedCredential Credential, List<string> AllDisclosures) MakeRealCredential(
        string vct, params (string Name, string Value)[] claims)
    {
        // Issuer JWT (signature is irrelevant for engine tests — only structure matters)
        using var fakeIssuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            typ = "dc+sd-jwt"
        }));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = "did:sorcha:org:test",
            vct,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        }));
        var sigInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var sig = Base64Url.EncodeToString(fakeIssuer.SignData(sigInput, HashAlgorithmName.SHA256));
        var credentialJwt = $"{headerSeg}.{payloadSeg}.{sig}";

        var allDisclosures = new List<string>();
        Span<byte> salt = stackalloc byte[16]; // reused per iteration (refilled below) — avoids stackalloc-in-loop (CA2014)
        foreach (var (name, value) in claims)
        {
            RandomNumberGenerator.Fill(salt);
            var disc = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                Base64Url.EncodeToString(salt),
                name,
                value,
            });
            allDisclosures.Add(Base64Url.EncodeToString(disc));
        }

        var raw = credentialJwt + string.Concat(allDisclosures.Select(d => "~" + d));
        return (new CachedCredential
        {
            Id = Guid.NewGuid(),
            Vct = vct,
            RawSdJwt = raw,
            AvailableClaimNames = claims.Select(c => c.Name).ToList(),
        }, allDisclosures);
    }

    /// <summary>Feature 181 deep link — carries request_uri only; the payload carries the rest.</summary>
    private static string MakeDeepLink(string requestUri = "https://verify.test/request/sess-1")
        => $"openid4vp://?client_id={Uri.EscapeDataString(ClientId)}" +
           $"&request_uri={Uri.EscapeDataString(requestUri)}";

    /// <summary>Fake request-object fetcher returning a fixed JWT (no IO).</summary>
    private static Func<string, CancellationToken, Task<string>> Fetch(string requestObjectJwt)
        => (_, _) => Task.FromResult(requestObjectJwt);

    /// <summary>
    /// Wrap a <see cref="DcqlQuery"/> in an unsigned request-object JWT
    /// (header <c>{"alg":"none","typ":"oauth-authz-req+jwt"}</c>, base64url payload).
    /// Pass <c>null</c> for a field to omit it from the payload.
    /// </summary>
    private static string MakeRequestObjectJwt(
        DcqlQuery query,
        string? clientId = ClientId,
        string? responseUri = "https://verify.test/r/sess-1/response",
        string? nonce = "n0nce",
        string? responseMode = "direct_post",
        string? state = "state-1")
    {
        var payload = new Dictionary<string, object>();
        if (clientId is not null) payload["client_id"] = clientId;
        if (responseUri is not null) payload["response_uri"] = responseUri;
        if (nonce is not null) payload["nonce"] = nonce;
        if (responseMode is not null) payload["response_mode"] = responseMode;
        if (state is not null) payload["state"] = state;
        payload["dcql_query"] = JsonSerializer.Deserialize<JsonElement>(DcqlRequestBuilder.ToJson(query));

        var headerSeg = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"oauth-authz-req+jwt\"}"));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{headerSeg}.{payloadSeg}.";
    }

    private static string JwkOf(ECDsa ecdsa)
    {
        var p = ecdsa.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.EncodeToString(p.Q.X!),
            y = Base64Url.EncodeToString(p.Q.Y!),
        });
    }
}
