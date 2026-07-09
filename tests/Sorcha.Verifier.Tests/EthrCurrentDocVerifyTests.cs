// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.ServiceClients.Did;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Services;
using Xunit;

namespace Sorcha.Verifier.Tests;

/// <summary>
/// Feature 179 (US1 headline / SC-001) — end-to-end: a rotated <c>did:ethr</c> whose current document
/// (from on-chain resolution) names the CURRENT owner verifies a credential signed by that owner, and
/// rejects one signed by the rotated-away former owner. Ties the resolved current document →
/// <see cref="DidResolverBackedIssuerKeyResolver"/> (recovery-JWK) → ES256K recover-then-match.
/// </summary>
public class EthrCurrentDocVerifyTests
{
    private const string Did = "did:ethr:0x0000000000000000000000000000000000000abc";

    [Fact]
    public async Task RotatedDid_CurrentOwnerSignatureVerifies_FormerOwnerRejected()
    {
        var (currentPriv, currentAddress) = NewSecp256k1Address();
        var (formerPriv, _) = NewSecp256k1Address();

        // The current on-chain document: #controller commits to the CURRENT owner's address.
        var vmId = $"{Did}#controller";
        var doc = new DidDocument
        {
            Id = Did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = vmId,
                    Type = "EcdsaSecp256k1RecoveryMethod2020",
                    Controller = Did,
                    BlockchainAccountId = $"eip155:1:{currentAddress}"
                }
            ],
            AssertionMethod = [vmId]
        };

        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveWithAlsoKnownAsAsync(Did, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        var resolver = new DidResolverBackedIssuerKeyResolver(registry.Object, new TestMeterFactory(), NullLogger<DidResolverBackedIssuerKeyResolver>.Instance);

        var jwk = await resolver.ResolveAsync(Did, vmId);
        jwk.Should().NotBeNull("the issuer-key resolver surfaces a recovery-JWK for the address VM");

        // Signed by the current owner → verifies; by the former (rotated-away) owner → rejected.
        var currentJws = BuildEs256kJws(currentPriv, $$"""{"iss":"{{Did}}"}""");
        VerifiablePresentationValidator.VerifyJwsSignature(currentJws, jwk!.Value, out _).Should().BeTrue();

        var formerJws = BuildEs256kJws(formerPriv, $$"""{"iss":"{{Did}}"}""");
        VerifiablePresentationValidator.VerifyJwsSignature(formerJws, jwk.Value, out _).Should().BeFalse();
    }

    private static (ECPrivateKeyParameters Priv, string Address) NewSecp256k1Address()
    {
        var curve = SecNamedCurves.GetByName("secp256k1");
        var domain = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var pub = (ECPublicKeyParameters)pair.Public;
        var address = EthereumAddress.FromPublicKey(Secp256k1PublicKey.FromSec1(pub.Q.GetEncoded(false)));
        return ((ECPrivateKeyParameters)pair.Private, address);
    }

    private static string BuildEs256kJws(ECPrivateKeyParameters priv, string payloadJson)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"ES256K"}"""));
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{header}.{payload}";
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(signingInput));
        var signer = new ECDsaSigner();
        signer.Init(true, priv);
        var rs = signer.GenerateSignature(hash);
        var sig = new byte[64];
        Fixed32(rs[0]).CopyTo(sig, 0);
        Fixed32(rs[1]).CopyTo(sig, 32);
        return $"{signingInput}.{Base64Url.EncodeToString(sig)}";
    }

    private static byte[] Fixed32(BigInteger value)
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

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var m = new Meter(options);
            _meters.Add(m);
            return m;
        }

        public void Dispose()
        {
            foreach (var m in _meters)
            {
                m.Dispose();
            }
        }
    }
}
