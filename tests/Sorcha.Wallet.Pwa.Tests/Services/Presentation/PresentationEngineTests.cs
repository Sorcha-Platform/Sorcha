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
using Sorcha.Wallet.Pwa.Services.Presentation;
using Sorcha.UI.Core.Models.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Presentation;

/// <summary>
/// Tests for <see cref="PresentationEngine"/> (Feature 114, T095). Covers:
/// parse happy path + error paths, match (success / wrong vct / missing claim),
/// build (KB-JWT signature + sd_hash + only-approved-disclosures invariant).
/// </summary>
public sealed class PresentationEngineTests
{
    private readonly PresentationEngine _engine = new(TimeProvider.System,
        NullLogger<PresentationEngine>.Instance);

    private const string Vct = "https://sorcha.dev/vc/test/v1";
    private const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    // ────────────────────────── Parse ──────────────────────────

    [Fact]
    public void Parse_ValidDeepLink_ReturnsPopulatedRequest()
    {
        var pd = MakePresentationDefinition("sess-1", Vct,
            required: ["givenName"], optional: ["familyName"]);
        var link = MakeDeepLink(ClientId, "https://verify.test/r/sess-1/response", "n0nce", pd);

        var parsed = _engine.Parse(link);

        parsed.ClientId.Should().Be(ClientId);
        parsed.Nonce.Should().Be("n0nce");
        parsed.RequiredVct.Should().Be(Vct);
        parsed.RequiredClaims.Should().ContainSingle().Which.Should().Be("givenName");
        parsed.OptionalClaims.Should().ContainSingle().Which.Should().Be("familyName");
        parsed.ResponseUri.Should().Be("https://verify.test/r/sess-1/response");
    }

    [Fact]
    public void Parse_NotOpenid4VpScheme_Throws()
    {
        Action act = () => _engine.Parse("https://verify.test/foo");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingNonce_Throws()
    {
        var link = "openid4vp://?client_id=did:test&response_uri=https://x/y" +
                   "&presentation_definition=" + Uri.EscapeDataString("{}");
        Action act = () => _engine.Parse(link);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_PresentationDefinitionWithoutVct_Throws()
    {
        var pd = JsonSerializer.Serialize(new
        {
            id = "x",
            input_descriptors = new[] { new { id = "p", constraints = new { fields = Array.Empty<object>() } } }
        });
        var link = MakeDeepLink(ClientId, "https://verify.test/r/x/response", "n", pd);
        Action act = () => _engine.Parse(link);
        act.Should().Throw<FormatException>();
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
            typ = "vc+sd-jwt"
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
        foreach (var (name, value) in claims)
        {
            Span<byte> salt = stackalloc byte[16];
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

    private static string MakePresentationDefinition(string id, string vct,
        IReadOnlyList<string> required, IReadOnlyList<string> optional)
    {
        var fields = new List<object>
        {
            new { path = new[] { "$.vct" }, filter = new { type = "string", @const = vct } }
        };
        foreach (var c in required) fields.Add(new { path = new[] { "$." + c }, optional = false });
        foreach (var c in optional) fields.Add(new { path = new[] { "$." + c }, optional = true });

        return JsonSerializer.Serialize(new
        {
            id,
            input_descriptors = new[]
            {
                new
                {
                    id = "primary",
                    name = vct,
                    purpose = "test",
                    constraints = new
                    {
                        limit_disclosure = "required",
                        fields = fields.ToArray()
                    }
                }
            }
        });
    }

    private static string MakeDeepLink(string clientId, string responseUri, string nonce, string presentationDefinitionJson)
        => "openid4vp://?" +
           $"client_id={Uri.EscapeDataString(clientId)}" +
           "&response_mode=direct_post" +
           $"&response_uri={Uri.EscapeDataString(responseUri)}" +
           $"&nonce={Uri.EscapeDataString(nonce)}" +
           $"&presentation_definition={Uri.EscapeDataString(presentationDefinitionJson)}";

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
