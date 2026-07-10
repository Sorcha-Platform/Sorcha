// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class Secp256k1SignerTests
{
    [Fact]
    public void SignRecoverable_KnownGeneratorKey_RecoversToKnownAddress()
    {
        // Private key = 1 → public key = G → address 0x7E5F…395Bdf.
        var priv = new byte[32];
        priv[31] = 0x01;
        var digest = Sha256("prove-control");

        var sig = Secp256k1Signer.SignRecoverable(digest, priv);

        sig.Length.Should().Be(65);
        var v = sig[64];
        v.Should().BeOneOf((byte)27, (byte)28);

        var recovered = Secp256k1Recovery.RecoverFromDigest(
            digest, new BigInteger(1, sig[..32]), new BigInteger(1, sig[32..64]), v - 27);
        recovered.Should().NotBeNull();
        EthereumAddress.FromPublicKey(recovered!).Should().Be("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf");
    }

    [Fact]
    public void SignRecoverable_RandomKeys_RecoverToTheSigningKey_LowS()
    {
        var n = Secp256k1PublicKey.Domain.N;
        var halfN = n.ShiftRight(1);

        for (var i = 0; i < 10; i++)
        {
            var (priv, pub) = NewKeyPair();
            var digest = Sha256($"msg-{i}");

            var sig = Secp256k1Signer.SignRecoverable(digest, priv);

            var s = new BigInteger(1, sig[32..64]);
            s.CompareTo(halfN).Should().BeLessThanOrEqualTo(0, "signatures must be low-s");
            sig[64].Should().BeOneOf((byte)27, (byte)28);

            var recovered = Secp256k1Recovery.RecoverFromDigest(
                digest, new BigInteger(1, sig[..32]), new BigInteger(1, sig[32..64]), sig[64] - 27);
            Convert.ToHexString(recovered!.ToSec1Uncompressed())
                .Should().Be(Convert.ToHexString(pub.ToSec1Uncompressed()));
        }
    }

    [Fact]
    public void SignRecoverable_IsDeterministic()
    {
        var (priv, _) = NewKeyPair();
        var digest = Sha256("deterministic");

        var a = Secp256k1Signer.SignRecoverable(digest, priv);
        var b = Secp256k1Signer.SignRecoverable(digest, priv);

        Convert.ToHexString(a).Should().Be(Convert.ToHexString(b));
    }

    [Fact]
    public void SignRecoverable_WrongLengths_Throw()
    {
        var priv = new byte[32];
        priv[31] = 1;
        var act1 = () => Secp256k1Signer.SignRecoverable(new byte[31], priv);
        var act2 = () => Secp256k1Signer.SignRecoverable(new byte[32], new byte[31]);
        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Eip191_PersonalSignDigest_MatchesIndependentKeccak()
    {
        var message = "abc"u8.ToArray();

        // 0x19 ‖ ASCII("Ethereum Signed Message:" + LF) ‖ ASCII("3") ‖ "abc"
        var prefixText = "Ethereum Signed Message:" + (char)0x0a + "3abc";
        var preimage = new byte[1 + prefixText.Length];
        preimage[0] = 0x19;
        Encoding.ASCII.GetBytes(prefixText).CopyTo(preimage, 1);
        var expected = Keccak256.ComputeHash(preimage);

        Convert.ToHexString(Eip191.PersonalSignDigest(message))
            .Should().Be(Convert.ToHexString(expected));
    }

    private static byte[] Sha256(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    private static (byte[] Priv, Secp256k1PublicKey Pub) NewKeyPair()
    {
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(Secp256k1PublicKey.Domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var priv = ((ECPrivateKeyParameters)pair.Private).D.ToByteArrayUnsigned();
        var padded = new byte[32];
        Array.Copy(priv, 0, padded, 32 - priv.Length, priv.Length);
        var pub = Secp256k1PublicKey.FromSec1(((ECPublicKeyParameters)pair.Public).Q.GetEncoded(false));
        return (padded, pub);
    }
}
