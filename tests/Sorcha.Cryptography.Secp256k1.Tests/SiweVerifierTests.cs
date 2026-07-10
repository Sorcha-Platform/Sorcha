// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Sorcha.Cryptography.Secp256k1.Siwe;

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class SiweVerifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_GenuineProof_Accepts()
    {
        var (priv, address) = NewEthKey();
        var (message, signature) = SignSiwe(priv, address, nonce: "n-1", domain: "app.test");

        var result = SiweVerifier.Verify(message, signature,
            new SiweValidationOptions(ExpectedNonce: "n-1", ExpectedDomain: "app.test", NowUtc: Now));

        result.Valid.Should().BeTrue();
        result.Address.Should().BeEquivalentTo(address);
    }

    [Fact]
    public void Verify_TamperedSignature_Rejects()
    {
        var (priv, address) = NewEthKey();
        var (message, signature) = SignSiwe(priv, address, "n-1", "app.test");
        signature[10] ^= 0xFF;

        SiweVerifier.Verify(message, signature, new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();
    }

    [Fact]
    public void Verify_SignatureByDifferentAddress_Rejects()
    {
        var (priv, _) = NewEthKey();
        var (_, otherAddress) = NewEthKey();
        // Message claims otherAddress, but it is signed by priv → recovered ≠ claimed.
        var (message, signature) = SignSiwe(priv, otherAddress, "n-1", "app.test");

        SiweVerifier.Verify(message, signature, new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongNonceOrDomain_Rejects()
    {
        var (priv, address) = NewEthKey();
        var (message, signature) = SignSiwe(priv, address, "n-1", "app.test");

        SiweVerifier.Verify(message, signature, new SiweValidationOptions(ExpectedNonce: "other", NowUtc: Now)).Valid.Should().BeFalse();
        SiweVerifier.Verify(message, signature, new SiweValidationOptions(ExpectedDomain: "evil.test", NowUtc: Now)).Valid.Should().BeFalse();
    }

    [Fact]
    public void Verify_ExpiredOrNotYetValid_Rejects()
    {
        var (priv, address) = NewEthKey();

        var expired = SignSiweWithWindow(priv, address, expiration: "2020-01-01T00:00:00Z", notBefore: null);
        SiweVerifier.Verify(expired.Message, expired.Signature, new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();

        var future = SignSiweWithWindow(priv, address, expiration: null, notBefore: "2030-01-01T00:00:00Z");
        SiweVerifier.Verify(future.Message, future.Signature, new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedMessageOrBadSignatureLength_Rejects()
    {
        SiweVerifier.Verify("not siwe", new byte[65], new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();
        var (priv, address) = NewEthKey();
        var (message, _) = SignSiwe(priv, address, "n-1", "app.test");
        SiweVerifier.Verify(message, new byte[10], new SiweValidationOptions(NowUtc: Now)).Valid.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (string Message, byte[] Signature) SignSiwe(byte[] priv, string address, string nonce, string domain)
    {
        var msg = new SiweMessage
        {
            Domain = domain,
            Address = address,
            Uri = $"https://{domain}/login",
            Version = "1",
            ChainId = 1,
            Nonce = nonce,
            IssuedAt = "2026-07-10T12:00:00Z"
        };
        return SignMessage(priv, msg);
    }

    private static (string Message, byte[] Signature) SignSiweWithWindow(byte[] priv, string address, string? expiration, string? notBefore)
    {
        var msg = new SiweMessage
        {
            Domain = "app.test",
            Address = address,
            Uri = "https://app.test/login",
            Version = "1",
            ChainId = 1,
            Nonce = "n-1",
            IssuedAt = "2026-07-10T12:00:00Z",
            ExpirationTime = expiration,
            NotBefore = notBefore
        };
        return SignMessage(priv, msg);
    }

    private static (string Message, byte[] Signature) SignMessage(byte[] priv, SiweMessage msg)
    {
        var text = SiweFormatter.Format(msg);
        var digest = Eip191.PersonalSignDigest(Encoding.UTF8.GetBytes(text));
        var sig = Secp256k1Signer.SignRecoverable(digest, priv);
        return (text, sig);
    }

    private static (byte[] Priv, string Address) NewEthKey()
    {
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(Secp256k1PublicKey.Domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var d = ((ECPrivateKeyParameters)pair.Private).D.ToByteArrayUnsigned();
        var priv = new byte[32];
        Array.Copy(d, 0, priv, 32 - d.Length, d.Length);
        var address = EthereumAddress.FromPublicKey(
            Secp256k1PublicKey.FromSec1(((ECPublicKeyParameters)pair.Public).Q.GetEncoded(false)));
        return (priv, address);
    }
}
