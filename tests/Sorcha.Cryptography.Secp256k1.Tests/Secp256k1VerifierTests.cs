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

public class Secp256k1VerifierTests
{
    [Fact]
    public void Verify_ValidEs256kSignature_ReturnsTrue()
    {
        var (pub, priv) = NewKeyPair();
        var message = Encoding.UTF8.GetBytes("eyJhbGciOiJFUzI1NksifQ.eyJzdWIiOiJkaWQ6a2V5OnpRM3MifQ");
        var signature = SignEs256k(message, priv);

        Secp256k1Verifier.Default.Verify(message, signature, pub).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsFalse()
    {
        var (pub, priv) = NewKeyPair();
        var message = Encoding.UTF8.GetBytes("hello-sorcha");
        var signature = SignEs256k(message, priv);
        signature[10] ^= 0xFF;

        Secp256k1Verifier.Default.Verify(message, signature, pub).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedMessage_ReturnsFalse()
    {
        var (pub, priv) = NewKeyPair();
        var signature = SignEs256k(Encoding.UTF8.GetBytes("original"), priv);

        Secp256k1Verifier.Default.Verify(Encoding.UTF8.GetBytes("tampered"), signature, pub).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var (_, priv) = NewKeyPair();
        var (otherPub, _) = NewKeyPair();
        var message = Encoding.UTF8.GetBytes("hello");
        var signature = SignEs256k(message, priv);

        Secp256k1Verifier.Default.Verify(message, signature, otherPub).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedSignature_ReturnsFalseNeverThrows()
    {
        var (pub, _) = NewKeyPair();
        var message = Encoding.UTF8.GetBytes("x");

        Secp256k1Verifier.VerifyEs256k(message, new byte[10], pub).Should().BeFalse();     // wrong length
        Secp256k1Verifier.VerifyEs256k(message, new byte[64], pub).Should().BeFalse();     // zero r,s
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
