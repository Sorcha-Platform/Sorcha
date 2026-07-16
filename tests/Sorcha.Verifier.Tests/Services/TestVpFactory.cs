// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

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
        int statusListIndex = 7,
        DateTimeOffset? kbJwtIssuedAt = null,
        DateTimeOffset? kbJwtExpiresAt = null,
        bool omitKbJwtExp = false,
        string credentialTyp = "dc+sd-jwt",
        Dictionary<string, object>? extraPayloadClaims = null,
        IEnumerable<string>? extraDisclosureSegments = null)
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
        // Fix round 2 (anchoring completeness) — lets tests add payload claims (e.g. an
        // array carrying {"...": digest} markers) or OVERRIDE defaults (e.g. _sd_alg).
        if (extraPayloadClaims is not null)
        {
            foreach (var (name, value) in extraPayloadClaims)
            {
                credentialPayload[name] = value;
            }
        }
        if (extraDisclosureSegments is not null)
        {
            disclosures.AddRange(extraDisclosureSegments);
        }
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = credentialTyp },
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
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            delegationPayload, holder);

        // KB-JWT — signed by device key, audience+nonce binding
        var kbHeader = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt",
            ["kid"] = deviceThumbprint,
        };
        // Feature 138 US5 — the KB-JWT carries its own short exp (default now+120s, within the
        // 120s max-lifetime bound). Negative tests override iat/exp or omit exp entirely.
        var kbIat = (kbJwtIssuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = kbIat,
            ["aud"] = verifierClientId,
            ["nonce"] = verifierNonce,
            ["sd_hash"] = "placeholder",
        };
        if (!omitKbJwtExp)
        {
            kbPayload["exp"] = (kbJwtExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(120)).ToUnixTimeSeconds();
        }
        var kbJwt = SignEs256(kbHeader, kbPayload, device);

        var disclosureSegments = string.Concat(disclosures.Select(d => "~" + d));
        var vpToken = $"{credentialJwt}{disclosureSegments}~{kbJwt}";

        return new Bundle(vpToken, delegation, issuer, holder, device, statusListUri, statusListIndex);
    }

    /// <summary>
    /// As <see cref="Mint"/>, but the holder key is an <b>Ed25519</b> (OKP) key — the default
    /// Sorcha wallet algorithm. The credential's <c>cnf.jwk</c> is therefore an OKP JWK and the
    /// device delegation credential is signed by the holder with an honest <c>alg:"EdDSA"</c>
    /// header. The issuer and device keys stay EC P-256 (issuers vary; the device key is always
    /// a WebCrypto non-extractable P-256 key). Mirrors the real on-the-wire shape an Ed25519
    /// citizen wallet produces.
    /// </summary>
    public static Ed25519Bundle MintEd25519Holder(
        string vct,
        Dictionary<string, JsonElement> disclosedClaims,
        string verifierClientId,
        string verifierNonce,
        DateTimeOffset? delegationExpiresAt = null,
        string statusListUri = "https://verify.test/status/00000000000000000000000000000000/citizen-devices/0.statuslist+jwt",
        int statusListIndex = 7)
    {
        var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var holder = new Ed25519KeyPairGenerator();
        holder.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var holderPair = holder.GenerateKeyPair();
        var holderPriv = (Ed25519PrivateKeyParameters)holderPair.Private;
        var holderPub = (Ed25519PublicKeyParameters)holderPair.Public;

        var holderJwk = ToOkpJwk(holderPub);
        var deviceJwk = ToJwk(device);
        var holderThumbprint = ThumbprintOkp(holderJwk);
        var deviceThumbprint = Thumbprint(deviceJwk);

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
            ["iss"] = "did:sorcha:org:test",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["vct"] = vct,
            ["_sd"] = sdHashes,
            ["_sd_alg"] = "sha-256",
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = JsonDocument.Parse(holderJwk).RootElement },
        };
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            credentialPayload, issuer);

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
        // Honest EdDSA header — the holder key is Ed25519, so the delegation JWS is an EdDSA signature.
        var delegation = SignEdDsa(
            new Dictionary<string, object> { ["alg"] = "EdDSA", ["typ"] = "dc+sd-jwt" },
            delegationPayload, holderPriv);

        var kbHeader = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt",
            ["kid"] = deviceThumbprint,
        };
        var kbPayload = new Dictionary<string, object>
        {
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeSeconds(),
            ["aud"] = verifierClientId,
            ["nonce"] = verifierNonce,
            ["sd_hash"] = "placeholder",
        };
        var kbJwt = SignEs256(kbHeader, kbPayload, device);

        var disclosureSegments = string.Concat(disclosures.Select(d => "~" + d));
        var vpToken = $"{credentialJwt}{disclosureSegments}~{kbJwt}";

        return new Ed25519Bundle(vpToken, delegation, issuer, statusListUri, statusListIndex);
    }

    public sealed record Ed25519Bundle(
        string VpToken,
        string Delegation,
        ECDsa IssuerKey,
        string StatusListUri,
        int StatusListIndex);

    /// <summary>
    /// A delegation-free SERVER-CUSTODY presentation (#1195 Phase 2). The vp_token carries an
    /// SD-JWT VC with <c>cnf</c> = the holder key and a KB-JWT the holder key itself signed —
    /// there is NO delegation credential. Exposes the issuer key (for issuer-signature tests)
    /// and the holder's public JWK.
    /// </summary>
    public sealed record ServerCustodyBundle(
        string VpToken,
        ECDsa IssuerKey,
        JsonElement HolderJwk,
        string Issuer);

    private const string TestIssuer = "did:sorcha:org:test";

    private static (string CredentialJwt, List<string> Disclosures) BuildCredential(
        string vct, Dictionary<string, JsonElement> disclosedClaims, string holderJwkJson, ECDsa issuer)
    {
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
            ["iss"] = TestIssuer,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["vct"] = vct,
            ["_sd"] = sdHashes,
            ["_sd_alg"] = "sha-256",
            ["cnf"] = new Dictionary<string, object> { ["jwk"] = JsonDocument.Parse(holderJwkJson).RootElement },
        };
        var credentialJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "dc+sd-jwt" },
            credentialPayload, issuer);
        return (credentialJwt, disclosures);
    }

    private static Dictionary<string, object> KbPayload(string verifierClientId, string verifierNonce) => new()
    {
        ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["exp"] = DateTimeOffset.UtcNow.AddSeconds(120).ToUnixTimeSeconds(),
        ["aud"] = verifierClientId,
        ["nonce"] = verifierNonce,
        ["sd_hash"] = "placeholder",
    };

    /// <summary>
    /// Mint a server-custody presentation with a P-256 (EC) holder key. When
    /// <paramref name="kbSignedByHolder"/> is true the KB-JWT is signed by the holder key (the
    /// legitimate server-custody case); when false it is signed by an UNRELATED P-256 device key
    /// while <c>cnf</c> stays the holder — the wrong-key negative that must reject as a key-binding
    /// mismatch.
    /// </summary>
    public static ServerCustodyBundle MintServerCustodyP256(
        string vct,
        Dictionary<string, JsonElement> disclosedClaims,
        string verifierClientId,
        string verifierNonce,
        bool kbSignedByHolder = true)
    {
        var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holder = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var holderJwkJson = ToJwk(holder);

        var (credentialJwt, disclosures) = BuildCredential(vct, disclosedClaims, holderJwkJson, issuer);

        var kbSigner = kbSignedByHolder ? holder : ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kbJwt = SignEs256(
            new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "kb+jwt" },
            KbPayload(verifierClientId, verifierNonce), kbSigner);

        var vpToken = $"{credentialJwt}{string.Concat(disclosures.Select(d => "~" + d))}~{kbJwt}";
        return new ServerCustodyBundle(
            vpToken, issuer, JsonDocument.Parse(holderJwkJson).RootElement.Clone(), TestIssuer);
    }

    /// <summary>
    /// Mint a server-custody presentation with an <b>Ed25519</b> (OKP) holder key — the default
    /// Sorcha wallet algorithm. When <paramref name="kbSignedByHolder"/> is true the KB-JWT is an
    /// honest EdDSA signature by the holder key; when false it is signed by an unrelated P-256
    /// device key (the wrong-key negative on the EdDSA-holder path).
    /// </summary>
    public static ServerCustodyBundle MintServerCustodyEd25519(
        string vct,
        Dictionary<string, JsonElement> disclosedClaims,
        string verifierClientId,
        string verifierNonce,
        bool kbSignedByHolder = true)
    {
        var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;
        var holderJwkJson = ToOkpJwk(pub);

        var (credentialJwt, disclosures) = BuildCredential(vct, disclosedClaims, holderJwkJson, issuer);

        string kbJwt;
        if (kbSignedByHolder)
        {
            kbJwt = SignEdDsa(
                new Dictionary<string, object> { ["alg"] = "EdDSA", ["typ"] = "kb+jwt" },
                KbPayload(verifierClientId, verifierNonce), priv);
        }
        else
        {
            kbJwt = SignEs256(
                new Dictionary<string, object> { ["alg"] = "ES256", ["typ"] = "kb+jwt" },
                KbPayload(verifierClientId, verifierNonce), ECDsa.Create(ECCurve.NamedCurves.nistP256));
        }

        var vpToken = $"{credentialJwt}{string.Concat(disclosures.Select(d => "~" + d))}~{kbJwt}";
        return new ServerCustodyBundle(
            vpToken, issuer, JsonDocument.Parse(holderJwkJson).RootElement.Clone(), TestIssuer);
    }

    public static string SignEdDsa(Dictionary<string, object> header, Dictionary<string, object> payload, Ed25519PrivateKeyParameters signer)
    {
        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var ed = new Ed25519Signer();
        ed.Init(true, signer);
        ed.BlockUpdate(signingInput, 0, signingInput.Length);
        var sig = ed.GenerateSignature();
        return $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(sig)}";
    }

    public static string ToOkpJwk(Ed25519PublicKeyParameters pub) =>
        JsonSerializer.Serialize(new
        {
            kty = "OKP",
            crv = "Ed25519",
            x = Base64Url.EncodeToString(pub.GetEncoded()),
        });

    private static string ThumbprintOkp(string jwkJson)
    {
        using var doc = JsonDocument.Parse(jwkJson);
        var root = doc.RootElement;
        var canonical =
            $"{{\"crv\":\"{root.GetProperty("crv").GetString()}\"," +
            $"\"kty\":\"{root.GetProperty("kty").GetString()}\"," +
            $"\"x\":\"{root.GetProperty("x").GetString()}\"}}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64Url.EncodeToString(hash);
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
