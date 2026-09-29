// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using SimpleBase;

using Sorcha.ServiceClients.Did;
using Sorcha.Verifier.Engine;

using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// #1759 / #1768 — the ONE implementation that decides whether an IETF Token Status List may be
/// believed, shared by the engine's cache and HAIP. Keys are resolved through the REAL DID resolver
/// registry (did:key via the real <see cref="KeyDidResolver"/>; did:sorcha:org via a document shaped
/// like the one the Tenant Service publishes), never a stub that ignores <c>kid</c>. Entry reading is
/// anchored to the IETF draft's own worked examples.
/// </summary>
public sealed class StatusListTokenVerifierTests
{
    private const string Uri = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/list-1";
    private const string OrgDid = "did:sorcha:org:ws11qexampleorg";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T10:00:00Z");

    // draft-ietf-oauth-status-list §4.1 examples — the lst values and the statuses they encode.
    private const string SpecLst1Bit = "eNrbuRgAAhcBXQ";
    private static readonly int[] SpecStatuses1Bit = [1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1];
    private const string SpecLst2Bit = "eNo76fITAAPfAgc";
    private static readonly int[] SpecStatuses2Bit = [1, 2, 0, 3, 0, 1, 0, 1, 1, 2, 3, 3];

    [Fact]
    public async Task Verify_Ed25519DidKeyList_IsVerifiedAndReadsTheSpecStatuses()
    {
        var key = Ed25519Key.New();
        var jwt = Build(key.Sign, alg: "EdDSA", kid: key.Kid, iss: key.Did, lst: SpecLst1Bit, bits: 1);

        var result = await Verifier().VerifyAsync(jwt, Uri, key.Did);

        result.Rejection.Should().BeNull();
        result.Bits.Should().Be(1);
        Enumerable.Range(0, SpecStatuses1Bit.Length)
            .Select(i => StatusListTokenVerifier.ReadEntry(result.Entries, result.Bits, i))
            .Should().Equal(SpecStatuses1Bit.Select(v => (int?)v));
    }

    [Fact]
    public async Task Verify_OrgDidListUnderVcIssuanceKid_IsVerifiedAndReadsTheTwoBitSpecStatuses()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = Build(Es256Signer(ecdsa), alg: "ES256", kid: OrgDid + "#vc-issuance-0", iss: OrgDid,
            lst: SpecLst2Bit, bits: 2);

        var result = await Verifier(OrgDocument(ecdsa)).VerifyAsync(jwt, Uri, OrgDid);

        result.Rejection.Should().BeNull();
        Enumerable.Range(0, SpecStatuses2Bit.Length)
            .Select(i => StatusListTokenVerifier.ReadEntry(result.Entries, result.Bits, i))
            .Should().Equal(SpecStatuses2Bit.Select(v => (int?)v));
    }

    [Fact]
    public async Task Verify_ListSignedByAnotherKeyUnderTheRightKid_IsRejected()
    {
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var forger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = Build(Es256Signer(forger), alg: "ES256", kid: OrgDid + "#vc-issuance-0", iss: OrgDid);

        var result = await Verifier(OrgDocument(published)).VerifyAsync(jwt, Uri, OrgDid);

        result.Rejection.Should().Be(StatusListTokenRejection.SignatureInvalid);
    }

    [Fact]
    public async Task Verify_ListCarryingItsOwnKeyInTheHeader_IsStillCheckedAgainstTheIssuersKey()
    {
        // #1768 — the self-certifying shape: a forger embeds the key they signed with. That key must
        // count for nothing; only the key resolved from the issuer's DID may verify the list.
        using var published = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var forger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = Build(Es256Signer(forger), alg: "ES256", kid: OrgDid + "#vc-issuance-0", iss: OrgDid,
            extraHeader: ("jwk", Es256Jwk(forger)));

        var result = await Verifier(OrgDocument(published)).VerifyAsync(jwt, Uri, OrgDid);

        result.Rejection.Should().Be(StatusListTokenRejection.SignatureInvalid);
    }

    [Fact]
    public async Task Verify_IssuerNotTheExpectedOne_IsRejectedEvenWhenItsOwnKeyVerifies()
    {
        var attacker = Ed25519Key.New();
        var jwt = Build(attacker.Sign, alg: "EdDSA", kid: attacker.Kid, iss: attacker.Did);

        var result = await Verifier().VerifyAsync(jwt, Uri, expectedIssuer: OrgDid);

        result.Rejection.Should().Be(StatusListTokenRejection.IssuerMismatch);
    }

    [Fact]
    public async Task Verify_SubjectNotTheCredentialsStatusUri_IsRejected()
    {
        // RFC 9972 §5.1 / §8.3: sub MUST equal the status_list.uri of the referenced token — otherwise
        // a genuine list for SOME OTHER set of credentials could be served in place of this one.
        var key = Ed25519Key.New();
        var jwt = Build(key.Sign, alg: "EdDSA", kid: key.Kid, iss: key.Did, sub: Uri + "-other");

        var result = await Verifier().VerifyAsync(jwt, Uri, key.Did);

        result.Rejection.Should().Be(StatusListTokenRejection.SubjectMismatch);
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData(null)]
    public async Task Verify_TypeNotStatusListJwt_IsRejected(string? typ)
    {
        var key = Ed25519Key.New();
        var jwt = Build(key.Sign, alg: "EdDSA", kid: key.Kid, iss: key.Did, typ: typ);

        var result = await Verifier().VerifyAsync(jwt, Uri, key.Did);

        result.Rejection.Should().Be(StatusListTokenRejection.WrongType);
    }

    [Fact]
    public async Task Verify_KeyNoLongerAnAssertionMethod_IsRejected()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwt = Build(Es256Signer(ecdsa), alg: "ES256", kid: OrgDid + "#vc-issuance-0", iss: OrgDid);
        var revoked = OrgDocument(ecdsa);
        revoked.AssertionMethod = [OrgDid + "#vc-issuance-1"];

        var result = await Verifier(revoked).VerifyAsync(jwt, Uri, OrgDid);

        result.Rejection.Should().Be(StatusListTokenRejection.KeyUnresolved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_MissingOrPastExpiry_IsRejected(bool expired)
    {
        var key = Ed25519Key.New();
        var jwt = Build(key.Sign, alg: "EdDSA", kid: key.Kid, iss: key.Did,
            exp: Now.AddMinutes(-5), omitExp: !expired);

        var result = await Verifier().VerifyAsync(jwt, Uri, key.Did);

        result.Rejection.Should().Be(StatusListTokenRejection.Expired);
    }

    [Fact]
    public async Task Verify_BitsOutsideTheSpecWidths_IsRejected()
    {
        var key = Ed25519Key.New();
        var jwt = Build(key.Sign, alg: "EdDSA", kid: key.Kid, iss: key.Did, bits: 3);

        var result = await Verifier().VerifyAsync(jwt, Uri, key.Did);

        result.Rejection.Should().Be(StatusListTokenRejection.UnsupportedBits);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public async Task Verify_Malformed_IsRejected(string jwt)
    {
        var result = await Verifier().VerifyAsync(jwt, Uri, OrgDid);

        result.Rejection.Should().Be(StatusListTokenRejection.Malformed);
    }

    [Fact]
    public void ReadEntry_OutsideTheList_ReturnsNull()
    {
        StatusListTokenVerifier.ReadEntry([0xff], bits: 2, index: 4).Should().BeNull();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static StatusListTokenVerifier Verifier(DidDocument? orgDocument = null)
    {
        var registry = new DidResolverRegistry(NullLogger<DidResolverRegistry>.Instance);
        registry.Register(new KeyDidResolver(NullLogger<KeyDidResolver>.Instance));
        if (orgDocument is not null) registry.Register(new FixedDocumentResolver("sorcha", orgDocument));

        var keys = new DidResolverBackedIssuerKeyResolver(
            registry, new TestMeterFactory(), NullLogger<DidResolverBackedIssuerKeyResolver>.Instance);
        return new StatusListTokenVerifier(keys, new FixedTimeProvider(Now),
            NullLogger<StatusListTokenVerifier>.Instance);
    }

    /// <summary>The shape OrgDidDocumentService publishes: #vc-issuance-n + thumbprint VMs, both asserting.</summary>
    private static DidDocument OrgDocument(ECDsa ecdsa) => new()
    {
        Id = OrgDid,
        VerificationMethod =
        [
            new VerificationMethod
            {
                Id = OrgDid + "#vc-issuance-0",
                Type = "JsonWebKey2020",
                Controller = OrgDid,
                PublicKeyJwk = Es256Jwk(ecdsa),
            },
        ],
        AssertionMethod = [OrgDid + "#vc-issuance-0"],
        Authentication = [OrgDid + "#vc-issuance-0"],
    };

    private static string Build(
        Func<byte[], byte[]> sign, string alg, string kid, string iss,
        string lst = SpecLst1Bit, int bits = 1, string sub = Uri, string? typ = "statuslist+jwt",
        DateTimeOffset? exp = null, (string Name, object Value)? extraHeader = null, bool omitExp = false)
    {
        var header = new Dictionary<string, object> { ["alg"] = alg, ["kid"] = kid };
        if (typ is not null) header["typ"] = typ;
        if (extraHeader is { } h) header[h.Name] = h.Value;

        var payload = new Dictionary<string, object>
        {
            ["iss"] = iss,
            ["sub"] = sub,
            ["iat"] = Now.ToUnixTimeSeconds(),
            ["status_list"] = new Dictionary<string, object> { ["bits"] = bits, ["lst"] = lst },
        };
        if (!omitExp) payload["exp"] = (exp ?? Now.AddMinutes(5)).ToUnixTimeSeconds();

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var sig = sign(Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}"));
        return $"{headerB64}.{payloadB64}.{Base64Url.EncodeToString(sig)}";
    }

    private static Func<byte[], byte[]> Es256Signer(ECDsa ecdsa) =>
        input => ecdsa.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static JsonElement Es256Jwk(ECDsa ecdsa)
    {
        var q = ecdsa.ExportParameters(false).Q;
        return JsonSerializer.SerializeToElement(new
        {
            kty = "EC", crv = "P-256", x = Base64Url.EncodeToString(q.X!), y = Base64Url.EncodeToString(q.Y!),
        });
    }

    private sealed record Ed25519Key(Ed25519PrivateKeyParameters Private, string Did, string Kid)
    {
        public static Ed25519Key New()
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            var pub = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
            var multibase = "z" + Base58.Bitcoin.Encode(new byte[] { 0xed, 0x01 }.Concat(pub).ToArray());
            var did = "did:key:" + multibase;
            return new Ed25519Key((Ed25519PrivateKeyParameters)pair.Private, did, did + "#" + multibase);
        }

        public byte[] Sign(byte[] input)
        {
            var signer = new Ed25519Signer();
            signer.Init(true, Private);
            signer.BlockUpdate(input, 0, input.Length);
            return signer.GenerateSignature();
        }
    }

    private sealed class FixedDocumentResolver(string method, DidDocument document) : IDidResolver
    {
        public bool CanResolve(string didMethod) => string.Equals(didMethod, method, StringComparison.OrdinalIgnoreCase);

        public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(did, document.Id, StringComparison.Ordinal) ? document : null);
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
