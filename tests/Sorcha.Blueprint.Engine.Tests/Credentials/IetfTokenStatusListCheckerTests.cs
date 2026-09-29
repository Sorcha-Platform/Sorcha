// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using SimpleBase;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Did;
using Sorcha.Verifier.Engine;

using Xunit;

using CredentialStatusValue = Sorcha.Blueprint.Engine.Credentials.CredentialStatusValue;
using StatusReference = Sorcha.Blueprint.Engine.Credentials.StatusReference;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// #1768 — HAIP's IETF status checker verified a list against the JWK embedded in THAT LIST's own
/// header, so a forger could sign an all-VALID list with any key and embed it; and it never pinned the
/// list to the credential's issuer. It now delegates to the engine's shared
/// <see cref="StatusListTokenVerifier"/>: key resolved from the credential issuer's DID by kid, iss and
/// sub pinned. Keys resolve through the REAL did:key resolver; entries are the IETF draft's own example.
/// </summary>
public sealed class IetfTokenStatusListCheckerTests
{
    private const string Uri = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/list-1";

    // draft-ietf-oauth-status-list §4.1, bits=2: statuses 1,2,0,3,0,1,0,1,1,2,3,3.
    private const string SpecLst2Bit = "eNo76fITAAPfAgc";

    [Theory]
    [InlineData(0, CredentialStatusValue.Invalid)]
    [InlineData(1, CredentialStatusValue.Suspended)]
    [InlineData(2, CredentialStatusValue.Valid)]
    [InlineData(3, CredentialStatusValue.Unresolved)] // 0x03 is application-specific: not a status
    public async Task CheckAsync_ListSignedByTheIssuersResolvableKey_ReadsTheEntry(int idx, CredentialStatusValue expected)
    {
        var issuer = Key.New();
        var checker = Checker(Serve(Build(issuer, issuer)));

        var status = await checker.CheckAsync(Ref(idx, issuer.Did));

        status.Should().Be(expected);
    }

    [Fact]
    public async Task CheckAsync_ForgedListCarryingItsOwnJwk_IsUnresolved()
    {
        // The #1768 attack: claim to be the issuer, sign with your own key, and embed that key.
        var issuer = Key.New();
        var forger = Key.New();
        var forged = Build(signer: forger, claimedIssuer: issuer, embedJwkOf: forger);

        var status = await Checker(Serve(forged)).CheckAsync(Ref(2, issuer.Did));

        status.Should().Be(CredentialStatusValue.Unresolved, "only the key resolved from the issuer's DID counts");
    }

    [Fact]
    public async Task CheckAsync_GenuineListFromAnotherIssuer_IsUnresolved()
    {
        var other = Key.New();
        var credentialIssuer = Key.New();

        var status = await Checker(Serve(Build(other, other))).CheckAsync(Ref(2, credentialIssuer.Did));

        status.Should().Be(CredentialStatusValue.Unresolved);
    }

    [Fact]
    public async Task CheckAsync_ReferenceWithNoExpectedIssuer_IsUnresolvedWithoutFetching()
    {
        var fetched = false;
        var checker = Checker(Serve("unused", onRequest: () => fetched = true));

        var status = await checker.CheckAsync(new StatusReference { Uri = Uri, Index = 0 });

        status.Should().Be(CredentialStatusValue.Unresolved, "an unpinned list cannot be authenticated");
        fetched.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_FetchFails_IsUnresolved()
    {
        var issuer = Key.New();

        var status = await Checker(Serve("", HttpStatusCode.ServiceUnavailable)).CheckAsync(Ref(0, issuer.Did));

        status.Should().Be(CredentialStatusValue.Unresolved);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static StatusReference Ref(int idx, string issuer) =>
        new() { Uri = Uri, Index = idx, ExpectedIssuer = issuer };

    private static IetfTokenStatusListChecker Checker(HttpClient http)
    {
        var registry = new DidResolverRegistry(NullLogger<DidResolverRegistry>.Instance);
        registry.Register(new KeyDidResolver(NullLogger<KeyDidResolver>.Instance));
        var keys = new DidResolverBackedIssuerKeyResolver(
            registry, new TestMeterFactory(), NullLogger<DidResolverBackedIssuerKeyResolver>.Instance);
        var verifier = new StatusListTokenVerifier(keys, TimeProvider.System, NullLogger<StatusListTokenVerifier>.Instance);
        return new IetfTokenStatusListChecker(http, verifier, NullLogger<IetfTokenStatusListChecker>.Instance);
    }

    private static HttpClient Serve(string body, HttpStatusCode status = HttpStatusCode.OK, System.Action? onRequest = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                onRequest?.Invoke();
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/statuslist+jwt"),
                });
            });
        return new HttpClient(handler.Object);
    }

    private static string Build(Key signer, Key claimedIssuer, Key? embedJwkOf = null)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "EdDSA",
            ["kid"] = claimedIssuer.Kid,
            ["typ"] = "statuslist+jwt",
        };
        if (embedJwkOf is not null)
            header["jwk"] = new { kty = "OKP", crv = "Ed25519", x = Base64Url.EncodeToString(embedJwkOf.Public) };

        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["iss"] = claimedIssuer.Did,
            ["sub"] = Uri,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["status_list"] = new Dictionary<string, object> { ["bits"] = 2, ["lst"] = SpecLst2Bit },
        };

        var h = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var p = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var input = Encoding.ASCII.GetBytes($"{h}.{p}");
        var ed = new Ed25519Signer();
        ed.Init(true, signer.Private);
        ed.BlockUpdate(input, 0, input.Length);
        return $"{h}.{p}.{Base64Url.EncodeToString(ed.GenerateSignature())}";
    }

    private sealed record Key(Ed25519PrivateKeyParameters Private, byte[] Public, string Did, string Kid)
    {
        public static Key New()
        {
            var gen = new Ed25519KeyPairGenerator();
            gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            var pair = gen.GenerateKeyPair();
            var pub = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
            var multibase = "z" + Base58.Bitcoin.Encode(new byte[] { 0xed, 0x01 }.Concat(pub).ToArray());
            return new Key((Ed25519PrivateKeyParameters)pair.Private, pub, "did:key:" + multibase,
                "did:key:" + multibase + "#" + multibase);
        }
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
