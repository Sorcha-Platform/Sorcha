// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="DeviceDelegationIssuer"/> (Feature 114). Pure composition
/// over <see cref="IHolderKeyService"/> + <see cref="ICitizenStatusListPublisher"/>,
/// so all dependencies are mocked. Real ECDSA P-256 keys back the holder mock so
/// signatures verify against the JWK that the issuer claims is the holder's.
/// </summary>
public sealed class DeviceDelegationIssuerTests
{
    private const string CitizenWallet = "ws1qcitizen1";
    private const string OrgWallet = "ws1qorgstatus1";
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly Mock<IHolderKeyService> _holderKeys = new();
    private readonly Mock<ICitizenStatusListPublisher> _statusList = new();
    private readonly DeviceDelegationIssuer _issuer;

    private readonly ECDsa _holderEcdsa;
    private readonly JsonElement _holderPublicJwk;
    private readonly string _holderThumbprint;

    public DeviceDelegationIssuerTests()
    {
        _holderEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = _holderEcdsa.ExportParameters(includePrivateParameters: false);
        var jwk = new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.EncodeToString(p.Q.X!),
            y = Base64Url.EncodeToString(p.Q.Y!)
        };
        _holderPublicJwk = JsonSerializer.SerializeToElement(jwk);
        _holderThumbprint = ComputeThumbprint(jwk.kty, jwk.crv, jwk.x, jwk.y);

        _holderKeys.Setup(s => s.GetHolderPublicJwkAsync(CitizenWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_holderPublicJwk);
        _holderKeys.Setup(s => s.GetHolderJwkThumbprintAsync(CitizenWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_holderThumbprint);
        _holderKeys.Setup(s => s.SignAsync(CitizenWallet, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] data, CancellationToken _) =>
                (_holderEcdsa.SignData(data, HashAlgorithmName.SHA256), "ES256"));

        _statusList.Setup(s => s.AllocateIndexAsync(OrgId, OrgWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((42, 1337));
        _statusList.Setup(s => s.BuildStatusListUri(OrgId, 42))
            .Returns($"https://verify.test/api/v1/wallet/status/{OrgId}/citizen-devices/42.statuslist+jwt");

        _issuer = new DeviceDelegationIssuer(
            _holderKeys.Object,
            _statusList.Object,
            Mock.Of<ILogger<DeviceDelegationIssuer>>());
    }

    private static EcP256PublicJwk MakeDeviceJwk()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        return new EcP256PublicJwk
        {
            X = Base64Url.EncodeToString(p.Q.X!),
            Y = Base64Url.EncodeToString(p.Q.Y!)
        };
    }

    private async Task<DeviceDelegationResult> IssueAsync()
        => await _issuer.IssueAsync(
            PlatformUserId, CitizenWallet, OrgId, OrgWallet,
            MakeDeviceJwk(), "Stuart's iPhone 16", "iOS 19 / Safari 19");

    [Fact]
    public async Task IssueAsync_ReturnsCompactThreePartJwt()
    {
        var result = await IssueAsync();

        result.CompactJwt.Split('.').Should().HaveCount(3);
        result.Jti.Should().NotBeNullOrEmpty();
        result.StatusListId.Should().Be(42);
        result.StatusListIndex.Should().Be(1337);
        result.StatusListUri.Should().StartWith($"https://verify.test/api/v1/wallet/status/{OrgId}/");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(364));
    }

    [Fact]
    public async Task IssueAsync_HeaderHasExpectedJoseClaims()
    {
        var result = await IssueAsync();
        var header = JsonDocument.Parse(Decode(result.CompactJwt.Split('.')[0])).RootElement;

        header.GetProperty("alg").GetString().Should().Be("ES256");
        header.GetProperty("typ").GetString().Should().Be("vc+sd-jwt");
        header.GetProperty("kid").GetString().Should().StartWith("did:sorcha:holder:");
    }

    [Fact]
    public async Task IssueAsync_Ed25519Holder_HeaderAlgIsEdDsa()
    {
        // The default Sorcha wallet is Ed25519, so the citizen-holder key (and therefore the
        // delegation signature) is EdDSA. The JWS header MUST advertise the real algorithm — a
        // hardcoded "ES256" over an Ed25519 signature is unverifiable by any conformant verifier.
        var holderKeys = new Mock<IHolderKeyService>();
        var okpJwk = JsonSerializer.SerializeToElement(new
        {
            kty = "OKP",
            crv = "Ed25519",
            x = Base64Url.EncodeToString(new byte[32])
        });
        holderKeys.Setup(s => s.GetHolderPublicJwkAsync(CitizenWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(okpJwk);
        holderKeys.Setup(s => s.GetHolderJwkThumbprintAsync(CitizenWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync("holder-thumb-okp");
        holderKeys.Setup(s => s.SignAsync(CitizenWallet, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] _, CancellationToken _) => (new byte[64], "ED25519"));

        var issuer = new DeviceDelegationIssuer(
            holderKeys.Object, _statusList.Object, Mock.Of<ILogger<DeviceDelegationIssuer>>());

        var result = await issuer.IssueAsync(
            PlatformUserId, CitizenWallet, OrgId, OrgWallet,
            MakeDeviceJwk(), "Stuart's iPhone 16", "iOS 19 / Safari 19");

        var header = JsonDocument.Parse(Decode(result.CompactJwt.Split('.')[0])).RootElement;
        header.GetProperty("alg").GetString().Should().Be("EdDSA");
        header.GetProperty("typ").GetString().Should().Be("vc+sd-jwt");
        header.GetProperty("kid").GetString().Should().StartWith("did:sorcha:holder:");
    }

    [Fact]
    public async Task IssueAsync_PayloadShapesMatchSchema()
    {
        var result = await IssueAsync();
        var payload = JsonDocument.Parse(Decode(result.CompactJwt.Split('.')[1])).RootElement;

        payload.GetProperty("iss").GetString().Should().Be($"did:sorcha:holder:{_holderThumbprint}");
        payload.GetProperty("sub").GetString().Should().StartWith("did:sorcha:device:");
        payload.GetProperty("vct").GetString().Should().Be(VctUris.CitizenDeviceDelegationV1);
        payload.GetProperty("jti").GetString().Should().Be(result.Jti);
        payload.GetProperty("delegated_capabilities").EnumerateArray()
            .Select(e => e.GetString())
            .Should().ContainSingle().Which.Should().Be(DelegatedCapabilities.PresentationHolderKeyBinding);

        var device = payload.GetProperty("device");
        device.GetProperty("label").GetString().Should().Be("Stuart's iPhone 16");
        device.GetProperty("platform").GetString().Should().Be("iOS 19 / Safari 19");
        device.GetProperty("enrolled_at").GetInt64().Should().BeGreaterThan(0);

        payload.GetProperty("cnf").GetProperty("jwk").GetProperty("crv").GetString().Should().Be("P-256");

        var statusList = payload.GetProperty("status").GetProperty("status_list");
        statusList.GetProperty("uri").GetString().Should().Be(result.StatusListUri);
        statusList.GetProperty("idx").GetInt32().Should().Be(1337);

        payload.GetProperty("exp").GetInt64()
            .Should().BeGreaterThan(payload.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task IssueAsync_SignatureVerifiesAgainstHolderKey()
    {
        var result = await IssueAsync();
        var parts = result.CompactJwt.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);

        _holderEcdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256)
            .Should().BeTrue();
    }

    [Fact]
    public async Task IssueAsync_AllocatesStatusListBeforeSigning()
    {
        await IssueAsync();
        // Allocation must be called exactly once with the org's signing wallet, not the citizen's.
        _statusList.Verify(
            s => s.AllocateIndexAsync(OrgId, OrgWallet, It.IsAny<CancellationToken>()),
            Times.Once);
        _statusList.Verify(
            s => s.AllocateIndexAsync(OrgId, CitizenWallet, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IssueAsync_DistinctCallsProduceDistinctJtiAndDeviceThumbprint()
    {
        // Each call gets a fresh device key + new jti.
        var first = await IssueAsync();
        // Re-arm allocation for the second call (same return is fine).
        _statusList.Setup(s => s.AllocateIndexAsync(OrgId, OrgWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((42, 1338));
        var second = await IssueAsync();

        first.Jti.Should().NotBe(second.Jti);
        first.StatusListIndex.Should().NotBe(second.StatusListIndex);
    }

    private static string Decode(string base64Url)
        => Encoding.UTF8.GetString(Base64Url.DecodeFromChars(base64Url));

    private static string ComputeThumbprint(string kty, string crv, string x, string y)
    {
        var canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64Url.EncodeToString(hash);
    }
}
