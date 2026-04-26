// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="CitizenStatusListPublisher"/> (Feature 114).
/// Uses an EF Core in-memory provider for the WalletDbContext slice and stubs
/// the wallet repository / key management to provide a real ECDSA P-256 key
/// pair so signed JWTs round-trip-verify against the derived public key.
/// </summary>
public sealed class CitizenStatusListPublisherTests : IDisposable
{
    private readonly TestCitizenWalletDbContext _db;
    private readonly Mock<IWalletRepository> _repoMock = new();
    private readonly Mock<IKeyManagementService> _keyMgmtMock = new();
    private readonly IConfiguration _configuration;
    private readonly CitizenStatusListPublisher _publisher;

    private readonly byte[] _signingPrivate;
    private readonly byte[] _signingPublic;
    private const string SigningWalletAddress = "ws1qstatuslist1";

    public CitizenStatusListPublisherTests()
    {
        var dbOptions = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"CitizenStatusListPublisherTests-{Guid.NewGuid():N}")
            .Options;
        _db = new TestCitizenWalletDbContext(dbOptions);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CitizenWallet:PublicBaseUrl"] = "https://verify.test"
            })
            .Build();

        // Real ECDSA P-256 keys so signatures verify in tests
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _signingPrivate = ecdsa.ExportECPrivateKey();
        _signingPublic = ecdsa.ExportSubjectPublicKeyInfo();

        _repoMock
            .Setup(r => r.GetByAddressAsync(SigningWalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = SigningWalletAddress,
                EncryptedPrivateKey = "encrypted-master",
                EncryptionKeyId = "k",
                Algorithm = "ES256",
                Owner = "org-1",
                Tenant = "tenant-1",
                Name = "Org Status Signer"
            });

        _keyMgmtMock
            .Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new byte[64]);

        // Return a fresh copy per call — the publisher zeroises the private key after
        // signing, so a shared array reference would be wiped between invocations.
        _keyMgmtMock
            .Setup(k => k.DeriveKeyAtPathAsync(It.IsAny<byte[]>(), It.IsAny<DerivationPath>(), "ES256"))
            .ReturnsAsync(() => ((byte[])_signingPrivate.Clone(), _signingPublic));

        _publisher = new CitizenStatusListPublisher(
            _db,
            _repoMock.Object,
            _keyMgmtMock.Object,
            _configuration,
            Mock.Of<ILogger<CitizenStatusListPublisher>>());
    }

    [Fact]
    public async Task AllocateIndexAsync_FirstAllocation_CreatesListZero_AndReturnsIndexZero()
    {
        var orgId = Guid.NewGuid();

        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        listId.Should().Be(0);
        idx.Should().Be(0);

        var lists = await _db.CitizenDeviceStatusLists.Where(l => l.OrganizationId == orgId).ToListAsync();
        lists.Should().HaveCount(1);
        lists[0].LastAllocatedIndex.Should().Be(0);
        lists[0].SignedJwt.Should().NotBeNullOrEmpty(because: "list is signed on creation");
    }

    [Fact]
    public async Task AllocateIndexAsync_Sequential_IncrementsIndex()
    {
        var orgId = Guid.NewGuid();

        var (l1, i1) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var (l2, i2) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var (l3, i3) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        l1.Should().Be(0); l2.Should().Be(0); l3.Should().Be(0);
        i1.Should().Be(0); i2.Should().Be(1); i3.Should().Be(2);
    }

    [Fact]
    public async Task FlipAsync_SetsBit_IncrementsRevokedCount_ResignsList()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        var firstJwt = (await _publisher.GetSignedListAsync(orgId, listId))!;

        // Add a tiny delay so iat changes
        await Task.Delay(1100);

        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var list = await _db.CitizenDeviceStatusLists.FirstAsync(l => l.OrganizationId == orgId);
        list.RevokedCount.Should().Be(1);
        (list.Bitstring[idx / 8] & (1 << (idx % 8))).Should().NotBe(0);
        list.SignedJwt.Should().NotBe(firstJwt, because: "list was re-signed after flip");
    }

    [Fact]
    public async Task FlipAsync_Idempotent_DoesNotDoubleCountRevocations()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);
        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var list = await _db.CitizenDeviceStatusLists.FirstAsync(l => l.OrganizationId == orgId);
        list.RevokedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSignedListAsync_Unknown_ReturnsNull()
    {
        var jwt = await _publisher.GetSignedListAsync(Guid.NewGuid(), 0);
        jwt.Should().BeNull();
    }

    [Fact]
    public async Task BuildStatusListUri_UsesConfiguredBaseUrl()
    {
        var orgId = Guid.NewGuid();
        var uri = _publisher.BuildStatusListUri(orgId, 7);

        uri.Should().Be($"https://verify.test/api/v1/wallet/status/{orgId}/citizen-devices/7.statuslist+jwt");
    }

    [Fact]
    public async Task SignedJwt_HasExpectedHeader_AndStatusListClaim()
    {
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var jwt = (await _publisher.GetSignedListAsync(orgId, 0))!;

        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);

        var header = JsonDocument.Parse(Decode(parts[0])).RootElement;
        header.GetProperty("alg").GetString().Should().Be("ES256");
        header.GetProperty("typ").GetString().Should().Be("statuslist+jwt");

        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;
        payload.GetProperty("iss").GetString().Should().StartWith("did:sorcha:org:");
        payload.GetProperty("sub").GetString().Should().StartWith("https://verify.test/api/v1/wallet/status/");
        payload.GetProperty("status_list").GetProperty("bits").GetInt32().Should().Be(1);
        payload.GetProperty("status_list").GetProperty("lst").GetString().Should().NotBeNullOrEmpty();
        payload.GetProperty("exp").GetInt64()
            .Should().BeGreaterThan(payload.GetProperty("iat").GetInt64());
    }

    [Fact]
    public async Task SignedJwt_SignatureVerifies_AgainstSigningPublicKey()
    {
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var jwt = (await _publisher.GetSignedListAsync(orgId, 0))!;

        var parts = jwt.Split('.');
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);

        using var verifier = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        verifier.ImportSubjectPublicKeyInfo(_signingPublic, out _);

        verifier.VerifyData(signingInput, signature, HashAlgorithmName.SHA256)
            .Should().BeTrue();
    }

    [Fact]
    public async Task SignedJwt_StatusListBitsRoundTripDeflate_AndShowsRevokedBit()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var jwt = (await _publisher.GetSignedListAsync(orgId, listId))!;
        var payload = JsonDocument.Parse(Decode(jwt.Split('.')[1])).RootElement;
        var lstB64 = payload.GetProperty("status_list").GetProperty("lst").GetString()!;

        var compressed = Base64Url.DecodeFromChars(lstB64);
        using var ms = new MemoryStream(compressed);
        using var inflater = new ZLibStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await inflater.CopyToAsync(output);
        var bits = output.ToArray();

        bits.Length.Should().Be(32_768 / 8);
        (bits[idx / 8] & (1 << (idx % 8))).Should().NotBe(0);
    }

    [Fact]
    public async Task FlipAsync_OutOfRangeIndex_ThrowsArgumentOutOfRange()
    {
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        var act = () => _publisher.FlipAsync(orgId, 0, 999_999, SigningWalletAddress);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task FlipAsync_UnknownList_ThrowsKeyNotFound()
    {
        var act = () => _publisher.FlipAsync(Guid.NewGuid(), 0, 0, SigningWalletAddress);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private static string Decode(string base64Url)
    {
        var bytes = Base64Url.DecodeFromChars(base64Url);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
