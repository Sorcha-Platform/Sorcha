// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using FluentAssertions;

using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

using Xunit;

namespace Sorcha.Cryptography.Tests.Unit;

/// <summary>
/// <see cref="CryptoModule.GenerateKeySetAsync"/> for <see cref="WalletNetworks.NISTP256"/> must
/// actually derive from its <c>seed</c> parameter — matching the contract
/// <c>GenerateED25519KeySetAsync</c> already honours — rather than ignoring the seed and generating
/// a fresh random key on every call (#1679).
/// </summary>
/// <remarks>
/// A non-deterministic "derivation" silently breaks any caller relying on HD derivation for P-256,
/// e.g. <c>OrgIssuerCertKeyService</c>'s HaipCoKey path (#1687): the SPKI returned by one derivation
/// would never match a signature produced by a later, independent "derivation" of the supposedly
/// same key, because each call was really just handing back a fresh random keypair.
/// </remarks>
public class CryptoModuleNistP256SeedDerivationTests
{
    private readonly CryptoModule _crypto = new();

    [Fact]
    public async Task GenerateKeySetAsync_SameSeedTwice_YieldsIdenticalKeyPair()
    {
        var seed = RandomNumberGenerator.GetBytes(32);

        var result1 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256, (byte[])seed.Clone());
        var result2 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256, (byte[])seed.Clone());

        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();

        result1.Value!.PrivateKey.Key.Should().Equal(
            result2.Value!.PrivateKey.Key,
            "deriving from the same seed twice must yield the same private key, exactly as ED25519 does");
        result1.Value!.PublicKey.Key.Should().Equal(
            result2.Value!.PublicKey.Key,
            "deriving from the same seed twice must yield the same public key");
    }

    [Fact]
    public async Task GenerateKeySetAsync_DifferentSeeds_YieldDifferentKeyPairs()
    {
        var seed1 = RandomNumberGenerator.GetBytes(32);
        var seed2 = RandomNumberGenerator.GetBytes(32);

        var result1 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256, seed1);
        var result2 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256, seed2);

        result1.Value!.PrivateKey.Key.Should().NotBeEquivalentTo(result2.Value!.PrivateKey.Key);
        result1.Value!.PublicKey.Key.Should().NotBeEquivalentTo(result2.Value!.PublicKey.Key);
    }

    [Fact]
    public async Task GenerateKeySetAsync_SeededKeyPair_IsCryptographicallyMatched()
    {
        // The derived private/public pair must actually be a matched EC keypair on P-256, not just
        // deterministic garbage — prove it by signing with the private key and verifying with the
        // public key via the real .NET ECDsa provider.
        var seed = RandomNumberGenerator.GetBytes(32);
        var result = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256, seed);
        result.IsSuccess.Should().BeTrue();

        var privateKey = result.Value!.PrivateKey.Key!;
        var publicKey = result.Value!.PublicKey.Key!;
        privateKey.Should().HaveCount(32);
        publicKey.Should().HaveCount(64);

        using var signer = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKey,
        });
        var digest = SHA256.HashData("determinism-proof"u8.ToArray());
        var signature = signer.SignHash(digest);

        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = publicKey[..32],
                Y = publicKey[32..],
            },
        });
        verifier.VerifyHash(digest, signature).Should().BeTrue(
            "the derived private and public keys must be a matched pair");
    }

    [Fact]
    public async Task GenerateKeySetAsync_NoSeed_StillGeneratesRandomKey()
    {
        // Passing no seed must preserve the pre-existing random-generation behaviour, exactly as
        // GenerateED25519KeySetAsync does when seed is null/empty.
        var result1 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);
        var result2 = await _crypto.GenerateKeySetAsync(WalletNetworks.NISTP256);

        result1.Value!.PrivateKey.Key.Should().NotBeEquivalentTo(result2.Value!.PrivateKey.Key);
    }
}
