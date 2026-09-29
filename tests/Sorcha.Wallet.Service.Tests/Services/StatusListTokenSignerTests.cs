// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

using Sorcha.Cryptography.Core;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// TODO(095) / #1759 — an IETF Token Status List must be signed by the key a verifier can resolve
/// from the issuing organisation's DID: the org's VC-issuance key, under the SAME <c>kid</c> its
/// credentials carry (RFC 9972 / draft-ietf-oauth-status-list §11.3, "the same key … referenced by
/// kid"). Keys come from the real <see cref="CryptoModule"/> — the format wallets actually store —
/// and every signature is checked by an INDEPENDENT verifier, never by the code that produced it.
/// </summary>
public sealed class StatusListTokenSignerTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string IssuerDid = "did:sorcha:org:ws11qexampleorgwallet";
    private const string Kid = IssuerDid + "#vc-issuance-0";
    private const string Subject = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/list-1";

    // The IETF draft's own 1-bit example, decoded (§4.1: statuses 1,0,0,1,1,1,0,1,1,1,0,0,0,1,0,1).
    private static readonly byte[] SpecEntries = [0xb9, 0xa3];

    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-09-29T10:00:00Z"));

    [Fact]
    public async Task SignAsync_Ed25519IssuanceKey_ProducesStatusListTokenVerifiableWithTheOrgKey()
    {
        var keys = await GenerateAsync(WalletNetworks.ED25519);
        var signer = CreateSigner(Material(keys.PrivateKey.Key!, "ED25519"));

        var token = await signer.SignAsync(Request(bits: 1), CancellationToken.None);

        token.Should().NotBeNull();
        var (header, payload, signingInput, signature) = Decode(token!.Jwt);
        header.GetProperty("typ").GetString().Should().Be("statuslist+jwt");
        header.GetProperty("alg").GetString().Should().Be("EdDSA");
        header.GetProperty("kid").GetString().Should().Be(Kid);

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(keys.PublicKey.Key!, 0));
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        verifier.VerifySignature(signature).Should().BeTrue("the list must verify against the org's published key");

        AssertClaims(payload, bits: 1);
        token.IssuerDid.Should().Be(IssuerDid);
        token.Kid.Should().Be(Kid);
    }

    [Fact]
    public async Task SignAsync_P256IssuanceKey_ProducesEs256TokenVerifiableWithTheOrgKey()
    {
        var keys = await GenerateAsync(WalletNetworks.NISTP256);
        // P-256 wallets record their algorithm as NISTP256, and store the private key as the raw
        // 32-byte scalar — the signer must accept exactly that, not only the JOSE name or a DER blob.
        var signer = CreateSigner(Material(keys.PrivateKey.Key!, "NISTP256"));

        var token = await signer.SignAsync(Request(bits: 2), CancellationToken.None);

        token.Should().NotBeNull();
        var (header, payload, signingInput, signature) = Decode(token!.Jwt);
        header.GetProperty("alg").GetString().Should().Be("ES256");
        signature.Should().HaveCount(64, "JWS ES256 signatures are the fixed-width r‖s form (RFC 7518 §3.4)");

        var pub = keys.PublicKey.Key!;
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = pub[..32], Y = pub[32..64] },
        });
        ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation).Should().BeTrue();

        AssertClaims(payload, bits: 2);
    }

    [Fact]
    public async Task SignAsync_OrgHasNoIssuanceKey_ReturnsNullRatherThanSigningWithAnyOtherKey()
    {
        var signer = CreateSigner(material: null);

        var token = await signer.SignAsync(Request(bits: 1), CancellationToken.None);

        token.Should().BeNull("an unverifiable list is worse than none — there is no fallback key");
    }

    [Fact]
    public async Task SignAsync_WipesTheDecryptedPrivateKeyAfterSigning()
    {
        var keys = await GenerateAsync(WalletNetworks.ED25519);
        var material = Material(keys.PrivateKey.Key!, "ED25519");
        var signer = CreateSigner(material);

        await signer.SignAsync(Request(bits: 1), CancellationToken.None);

        material.PrivateKey.Should().OnlyContain(b => b == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(16)]
    public async Task SignAsync_BitsOutsideTheSpecWidths_IsRefused(int bits)
    {
        var keys = await GenerateAsync(WalletNetworks.ED25519);
        var signer = CreateSigner(Material(keys.PrivateKey.Key!, "ED25519"));

        var act = () => signer.SignAsync(Request(bits), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("list-1")]
    [InlineData("ftp://example.org/list")]
    public async Task SignAsync_SubjectThatIsNotAnHttpsOrHttpUri_IsRefused(string subject)
    {
        var keys = await GenerateAsync(WalletNetworks.ED25519);
        var signer = CreateSigner(Material(keys.PrivateKey.Key!, "ED25519"));

        var act = () => signer.SignAsync(Request(1) with { Subject = subject }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void AssertClaims(JsonElement payload, int bits)
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        payload.GetProperty("iss").GetString().Should().Be(IssuerDid);
        payload.GetProperty("sub").GetString().Should().Be(Subject);
        payload.GetProperty("iat").GetInt64().Should().Be(now);
        payload.GetProperty("ttl").GetInt32().Should().Be(300);
        payload.GetProperty("exp").GetInt64().Should().Be(now + 300);

        var statusList = payload.GetProperty("status_list");
        statusList.GetProperty("bits").GetInt32().Should().Be(bits);
        Inflate(Base64Url.DecodeFromChars(statusList.GetProperty("lst").GetString()))
            .Should().Equal(SpecEntries, "lst is the ZLIB-compressed entry bytes, unaltered (§4.2)");
    }

    private StatusListTokenSignRequest Request(int bits) =>
        new(OrgId, Subject, bits, SpecEntries, TtlSeconds: 300);

    private StatusListTokenSigner CreateSigner(IssuanceSigningMaterial? material)
    {
        var keys = new Mock<IIssuanceKeyService>();
        keys.Setup(k => k.GetActiveSigningMaterialAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(material);
        return new StatusListTokenSigner(keys.Object, _clock, NullLogger<StatusListTokenSigner>.Instance);
    }

    private static IssuanceSigningMaterial Material(byte[] privateKey, string algorithm) =>
        new(OrgId, IssuerDid, Kid, (byte[])privateKey.Clone(), algorithm, RotationIndex: 0);

    private static async Task<KeySet> GenerateAsync(WalletNetworks network)
    {
        var result = await new CryptoModule().GenerateKeySetAsync(network);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private static (JsonElement Header, JsonElement Payload, byte[] SigningInput, byte[] Signature) Decode(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);
        return (
            JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0])),
            JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1])),
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]));
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
