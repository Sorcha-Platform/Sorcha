// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class Secp256k1RecoveryTests
{
    [Fact]
    public void TryRecover_RecoversTheSigningKey_AcrossManyKeys()
    {
        // Many keypairs across iterations exercise both recovery ids (0 and 1): whichever recid
        // the true key falls under, it MUST appear among the recovered candidates.
        for (var i = 0; i < 12; i++)
        {
            var (pub, priv) = NewKeyPair();
            var message = Encoding.UTF8.GetBytes($"recover-me-{i}");
            var signature = SignEs256k(message, priv);

            var candidates = Secp256k1Recovery.TryRecover(message, signature);

            candidates.Should().NotBeEmpty();
            candidates.Select(c => Convert.ToHexString(c.ToSec1Uncompressed()))
                .Should().Contain(Convert.ToHexString(pub.ToSec1Uncompressed()),
                    "the signing key must be one of the recovery candidates");
        }
    }

    [Fact]
    public void VerifyByAddress_KnownGeneratorVector_ReturnsTrue()
    {
        // Private key = 1 → public key = G → the well-known address 0x7E5F…395Bdf.
        var priv = new ECPrivateKeyParameters(BigInteger.One, Secp256k1PublicKey.Domain);
        var message = Encoding.UTF8.GetBytes("known-vector");
        var signature = SignEs256k(message, priv);

        Secp256k1Verifier.VerifyByAddressStatic(message, signature, "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf")
            .Should().BeTrue();
    }

    [Fact]
    public void VerifyByAddress_LowercaseChecksummedAndCaip10_AllVerify()
    {
        var (pub, priv) = NewKeyPair();
        var address = EthereumAddress.FromPublicKey(pub); // EIP-55 checksummed
        var message = Encoding.UTF8.GetBytes("bind-to-address");
        var signature = SignEs256k(message, priv);

        Secp256k1Verifier.VerifyByAddressStatic(message, signature, address).Should().BeTrue();
        Secp256k1Verifier.VerifyByAddressStatic(message, signature, address.ToLowerInvariant()).Should().BeTrue();
        Secp256k1Verifier.VerifyByAddressStatic(message, signature, $"eip155:1:{address}").Should().BeTrue();
        Secp256k1Verifier.Default.VerifyByAddress(message, signature, address).Should().BeTrue();
    }

    [Fact]
    public void VerifyByAddress_DifferentAddress_ReturnsFalse()
    {
        var (_, priv) = NewKeyPair();
        var (otherPub, _) = NewKeyPair();
        var message = Encoding.UTF8.GetBytes("mismatch");
        var signature = SignEs256k(message, priv);

        Secp256k1Verifier.VerifyByAddressStatic(message, signature, EthereumAddress.FromPublicKey(otherPub))
            .Should().BeFalse();
    }

    [Fact]
    public void VerifyByAddress_TamperedOrMalformed_ReturnsFalseNeverThrows()
    {
        var (pub, priv) = NewKeyPair();
        var address = EthereumAddress.FromPublicKey(pub);
        var message = Encoding.UTF8.GetBytes("x");
        var signature = SignEs256k(message, priv);
        var tampered = (byte[])signature.Clone();
        tampered[5] ^= 0xFF;

        Secp256k1Verifier.VerifyByAddressStatic(message, tampered, address).Should().BeFalse();      // wrong sig
        Secp256k1Verifier.VerifyByAddressStatic(message, new byte[10], address).Should().BeFalse();  // wrong length
        Secp256k1Verifier.VerifyByAddressStatic(message, new byte[64], address).Should().BeFalse();  // zero r,s
        Secp256k1Verifier.VerifyByAddressStatic(message, signature, "not-an-address").Should().BeFalse();
        Secp256k1Verifier.VerifyByAddressStatic(message, signature, "0x1234").Should().BeFalse();    // too short
    }

    [Fact]
    public void TryRecover_MalformedSignature_ReturnsEmptyNeverThrows()
    {
        var message = Encoding.UTF8.GetBytes("x");
        Secp256k1Recovery.TryRecover(message, new byte[10]).Should().BeEmpty();
        Secp256k1Recovery.TryRecover(message, new byte[64]).Should().BeEmpty(); // zero r,s
    }

    private static (Secp256k1PublicKey Pub, ECPrivateKeyParameters Priv) NewKeyPair()
    {
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(Secp256k1PublicKey.Domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var priv = (ECPrivateKeyParameters)pair.Private;
        var pub = (ECPublicKeyParameters)pair.Public;
        return (Secp256k1PublicKey.FromSec1(pub.Q.GetEncoded(false)), priv);
    }

    private static byte[] SignEs256k(byte[] message, ECPrivateKeyParameters priv)
    {
        var hash = SHA256.HashData(message);
        var signer = new ECDsaSigner();
        signer.Init(true, priv);
        var rs = signer.GenerateSignature(hash);
        return [.. ToFixed32(rs[0]), .. ToFixed32(rs[1])];
    }

    private static byte[] ToFixed32(BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length == 32)
        {
            return bytes;
        }

        var result = new byte[32];
        Array.Copy(bytes, 0, result, 32 - bytes.Length, bytes.Length);
        return result;
    }
}
