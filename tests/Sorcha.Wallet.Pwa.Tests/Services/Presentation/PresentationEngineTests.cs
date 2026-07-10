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

    // ────────────────────────── helpers ──────────────────────────

    private static ParsedPresentationRequest MakeRequest(IReadOnlyList<string> required, IReadOnlyList<string> optional)
        => new()
        {
            ClientId = ClientId,
            ResponseUri = "https://verify.test/r/x/response",
            Nonce = "abc",
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
        string? responseMode = "direct_post")
    {
        var payload = new Dictionary<string, object>();
        if (clientId is not null) payload["client_id"] = clientId;
        if (responseUri is not null) payload["response_uri"] = responseUri;
        if (nonce is not null) payload["nonce"] = nonce;
        if (responseMode is not null) payload["response_mode"] = responseMode;
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
