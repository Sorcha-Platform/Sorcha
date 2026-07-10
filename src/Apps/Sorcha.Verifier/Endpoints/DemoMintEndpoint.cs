// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Sorcha.Verifier.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;using Sorcha.Verifier.Engine;


namespace Sorcha.Verifier.Endpoints;

/// <summary>
/// Demo-only credential minter. The wallet posts its device public JWK; the
/// endpoint mints a fresh holder key + issuer key, builds an SD-JWT VC bound
/// to the holder, builds a holder→device delegation credential, and returns
/// everything for the wallet to seed its cache. Lets the MVP demo run without
/// the real enrolment + issuance pipelines (US2).
///
/// <para><strong>NOT for production.</strong> The minted material is not anchored
/// to any real Sorcha trust root and is regenerated per call.</para>
/// </summary>
public static class DemoMintEndpoint
{
    private const string DemoVct = "https://sorcha.dev/vc/demo-id-card/v1";

    /// <summary>Maps the demo mint endpoint at <c>POST /verify/demo/mint</c>.</summary>
    public static IEndpointRouteBuilder MapDemoMintEndpoint(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/demo/mint", Handle)
            .WithName("CitizenVerifierDemoMint")
            .WithSummary("Demo-only — mint a credential + delegation bound to a wallet's device key.")
            .WithDescription(
                "Returns a freshly-signed SD-JWT VC + holder→device delegation credential " +
                "with random demo keys. Used by the wallet PWA to seed its cache for the MVP demo. " +
                "Registers the freshly-generated issuer JWK with the verifier's in-memory key " +
                "registry so subsequent presentations of this credential pass full issuer-signature " +
                "verification end-to-end.")
            .Accepts<DemoMintRequest>("application/json")
            .Produces<DemoMintResponse>();
        return routes;
    }

    private static IResult Handle(DemoMintRequest body, JwkRegistryIssuerKeyResolver issuerKeys)
    {
        if (body.DeviceJwk.ValueKind != JsonValueKind.Object)
        {
            return Results.BadRequest(new { error = "deviceJwk must be a JWK object." });
        }

        using var holder = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var holderJwk = JwkOf(holder);
        var holderJwkEl = JsonSerializer.Deserialize<JsonElement>(holderJwk);
        var holderThumbprint = Thumbprint(holderJwkEl);
        var deviceThumbprint = Thumbprint(body.DeviceJwk);

        // Demo claims — selectively-disclosable.
        var givenName = ("givenName", "Stuart");
        var familyName = ("familyName", "Fraser");
        var dateOfBirth = ("dateOfBirth", "1980-01-01");

        var (givenSeg, _) = MintDisclosure(givenName.Item1, givenName.Item2);
        var (familySeg, _) = MintDisclosure(familyName.Item1, familyName.Item2);
        var (dobSeg, _) = MintDisclosure(dateOfBirth.Item1, dateOfBirth.Item2);

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var credentialPayload = new Dictionary<string, object>
        {
            ["iss"] = "did:sorcha:org:demo",
            ["iat"] = nowUnix,
            ["vct"] = DemoVct,
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = holderJwkEl },
        };
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            credentialPayload, issuer);

        // Register the freshly-generated issuer JWK so the validator can verify
        // this credential's signature on subsequent /response presentations.
        // NOTE: production hardening replaces this with a DID-resolver-based
        // resolver — the demo mints the issuer key here only because there is no
        // real issuer infrastructure yet (US4 / Phase 6).
        var issuerJwk = JwkOf(issuer);
        var issuerJwkEl = JsonSerializer.Deserialize<JsonElement>(issuerJwk);
        issuerKeys.Register("did:sorcha:org:demo", issuerJwkEl);

        var rawSdJwt = $"{credentialJwt}~{givenSeg}~{familySeg}~{dobSeg}";

        var delegationPayload = new Dictionary<string, object>
        {
            ["iss"] = $"did:sorcha:holder:{holderThumbprint}",
            ["sub"] = $"did:sorcha:device:{deviceThumbprint}",
            ["iat"] = nowUnix,
            ["exp"] = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeSeconds(),
            ["vct"] = "https://sorcha.dev/vc/citizen-device-delegation/v1",
            ["delegated_capabilities"] = new[] { "presentation.holder-key-binding" },
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = body.DeviceJwk },
            // Status-list bit deliberately omitted in demo mode — wallet's NoopStatusListService
            // returns false anyway, and the validator skips status checks when uri/idx absent.
        };
        var delegationJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            delegationPayload, holder);

        var response = new DemoMintResponse(
            CredentialId: Guid.NewGuid(),
            Vct: DemoVct,
            DisplayLabel: "Demo identity card",
            RawSdJwt: rawSdJwt,
            AvailableClaimNames: ["givenName", "familyName", "dateOfBirth"],
            DelegationJwt: delegationJwt,
            HolderPublicJwk: holderJwkEl);

        return Results.Ok(response);
    }

    private static (string Segment, string Hash) MintDisclosure(string name, string value)
    {
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        var array = JsonSerializer.SerializeToUtf8Bytes(new object[]
        {
            Base64Url.EncodeToString(salt), name, value,
        });
        var segment = Base64Url.EncodeToString(array);
        var hash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(segment)));
        return (segment, hash);
    }

    private static string SignEs256(Dictionary<string, object> header, Dictionary<string, object> payload, ECDsa signer)
    {
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var sig = signer.SignData(signingInput, HashAlgorithmName.SHA256);
        return $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(sig)}";
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

    private static string Thumbprint(JsonElement jwk)
    {
        var canonical =
            $"{{\"crv\":\"{jwk.GetProperty("crv").GetString()}\"," +
            $"\"kty\":\"{jwk.GetProperty("kty").GetString()}\"," +
            $"\"x\":\"{jwk.GetProperty("x").GetString()}\"," +
            $"\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

/// <summary>Body for <c>POST /verify/demo/mint</c>.</summary>
public sealed record DemoMintRequest(JsonElement DeviceJwk);

/// <summary>Response for <c>POST /verify/demo/mint</c>.</summary>
public sealed record DemoMintResponse(
    Guid CredentialId,
    string Vct,
    string DisplayLabel,
    string RawSdJwt,
    IReadOnlyList<string> AvailableClaimNames,
    string DelegationJwt,
    JsonElement HolderPublicJwk);
