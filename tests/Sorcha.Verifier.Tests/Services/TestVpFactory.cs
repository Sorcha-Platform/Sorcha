// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Test helper that mints golden Citizen Wallet PWA presentation samples.
/// Mirrors the on-the-wire shape produced by <c>Sorcha.Wallet.Pwa</c>'s
/// presentation engine (T094): SD-JWT VC (signed by issuer, cnf=holder JWK),
/// device delegation credential (signed by holder, cnf=device JWK, status_list ref),
/// KB-JWT (signed by device, audience+nonce binding to the verifier session).
/// </summary>
internal static class TestVpFactory
{
    public sealed record Bundle(
        string VpToken,
        string Delegation,
        ECDsa IssuerKey,
        ECDsa HolderKey,
        ECDsa DeviceKey,
        string StatusListUri,
        int StatusListIndex);

    public static Bundle Mint(
        string vct,
        Dictionary<string, JsonElement> disclosedClaims,
        string verifierClientId,
        string verifierNonce,
        DateTimeOffset? delegationExpiresAt = null,
        string statusListUri = "https://verify.test/status/00000000000000000000000000000000/citizen-devices/0.statuslist+jwt",
        int statusListIndex = 7)
    {
        var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holder = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var holderJwk = ToJwk(holder);
        var deviceJwk = ToJwk(device);
        var holderThumbprint = Thumbprint(holderJwk);
        var deviceThumbprint = Thumbprint(deviceJwk);

        // Build SD-JWT VC payload — disclosable claims live in disclosures, not the body.
        // We embed the SHA-256 hashes in `_sd` per RFC 9901 so a strict consumer would
        // accept it; the v1 verifier we test only checks disclosure name presence.
        var disclosures = new List<string>();
        var sdHashes = new List<string>();
        foreach (var (name, value) in disclosedClaims)
        {
            var (segment, hash) = MintDisclosure(name, value);
            disclosures.Add(segment);
            sdHashes.Add(hash);
        }

        var credentialPayload = new Dictionary<string, object>
        {
            ["iss"] = $"did:sorcha:org:test",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["vct"] = vct,
            ["_sd"] = sdHashes,
            ["_sd_alg"] = "sha-256",
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = JsonDocument.Parse(holderJwk).RootElement },
        };
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "vc+sd-jwt" },
            credentialPayload, issuer);

        // Delegation credential — signed by holder key, cnf=device JWK
        var exp = (delegationExpiresAt ?? DateTimeOffset.UtcNow.AddDays(365)).ToUnixTimeSeconds();
        var delegationPayload = new Dictionary<string, object>
        {
            ["iss"] = $"did:sorcha:holder:{holderThumbprint}",
            ["sub"] = $"did:sorcha:device:{deviceThumbprint}",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = exp,
            ["vct"] = "https://sorcha.dev/vc/citizen-device-delegation/v1",
            ["delegated_capabilities"] = new[] { "presentation.holder-key-binding" },
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = JsonDocument.Parse(deviceJwk).RootElement },
            ["status"] = new Dictionary<string, object>
            {
                ["status_list"] = new Dictionary<string, object>
                {
                    ["uri"] = statusListUri,
                    ["idx"] = statusListIndex,
                },
            },
        };
        var delegation = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "vc+sd-jwt" },
            delegationPayload, holder);

        // KB-JWT — signed by device key, audience+nonce binding
        var kbHeader = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt",
            ["kid"] = deviceThumbprint,
        };
        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["aud"] = verifierClientId,
            ["nonce"] = verifierNonce,
            ["sd_hash"] = "placeholder",
        };
        var kbJwt = SignEs256(kbHeader, kbPayload, device);

        var disclosureSegments = string.Concat(disclosures.Select(d => "~" + d));
        var vpToken = $"{credentialJwt}{disclosureSegments}~{kbJwt}";

        return new Bundle(vpToken, delegation, issuer, holder, device, statusListUri, statusListIndex);
    }

    /// <summary>Mint a single SD-JWT disclosure segment for a name/value pair, returning (segment, sha256-hash).</summary>
    public static (string Segment, string Hash) MintDisclosure(string name, JsonElement value)
    {
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);
        var array = JsonSerializer.SerializeToUtf8Bytes(new object[]
        {
            Base64Url.EncodeToString(salt), name, value,
        });
        var segment = Base64Url.EncodeToString(array);
        var hashBytes = SHA256.HashData(Encoding.ASCII.GetBytes(segment));
        return (segment, Base64Url.EncodeToString(hashBytes));
    }

    public static string SignEs256(Dictionary<string, object> header, Dictionary<string, object> payload, ECDsa signer)
    {
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var sig = signer.SignData(signingInput, HashAlgorithmName.SHA256);
        return $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(sig)}";
    }

    public static string ToJwk(ECDsa ecdsa)
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

    private static string Thumbprint(string jwkJson)
    {
        using var doc = JsonDocument.Parse(jwkJson);
        var root = doc.RootElement;
        // RFC 7638 canonical form: members in lexicographic order, no whitespace
        var canonical =
            $"{{\"crv\":\"{root.GetProperty("crv").GetString()}\"," +
            $"\"kty\":\"{root.GetProperty("kty").GetString()}\"," +
            $"\"x\":\"{root.GetProperty("x").GetString()}\"," +
            $"\"y\":\"{root.GetProperty("y").GetString()}\"}}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64Url.EncodeToString(hash);
    }
}
