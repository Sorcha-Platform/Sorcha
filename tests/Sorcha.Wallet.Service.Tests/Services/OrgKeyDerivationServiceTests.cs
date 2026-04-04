// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

public class OrgKeyDerivationServiceTests : IDisposable
{
    private readonly WalletDbContext _dbContext;
    private readonly Mock<IOrgKeyProtectionProvider> _protectionProviderMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock;
    private readonly Mock<ILogger<OrgKeyDerivationService>> _loggerMock;
    private readonly OrgKeyDerivationService _sut;

    private static readonly string OrgId = Guid.NewGuid().ToString();
    private static readonly string UserId = Guid.NewGuid().ToString();

    public OrgKeyDerivationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TestRecoveryDbContext(options);

        _protectionProviderMock = new Mock<IOrgKeyProtectionProvider>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _walletUtilitiesMock = new Mock<IWalletUtilities>();
        _loggerMock = new Mock<ILogger<OrgKeyDerivationService>>();

        // Setup default mocks
        _protectionProviderMock
            .Setup(p => p.ProviderName)
            .Returns("Software");

        _protectionProviderMock
            .Setup(p => p.EncryptSeedAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] data, CancellationToken _) => (data, "test-key-id"));

        _protectionProviderMock
            .Setup(p => p.DecryptSeedAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[64]); // 64 bytes seed for BIP32

        var mockPublicKey = new byte[32];
        var mockPrivateKey = new byte[64];
        Array.Fill(mockPublicKey, (byte)0xAA);
        Array.Fill(mockPrivateKey, (byte)0xBB);

        var keySet = new Sorcha.Cryptography.Models.KeySet
        {
            PublicKey = new Sorcha.Cryptography.Models.CryptoKey(WalletNetworks.ED25519, mockPublicKey),
            PrivateKey = new Sorcha.Cryptography.Models.CryptoKey(WalletNetworks.ED25519, mockPrivateKey)
        };

        _cryptoModuleMock
            .Setup(c => c.GenerateKeySetAsync(
                It.IsAny<WalletNetworks>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Cryptography.Models.CryptoResult<Sorcha.Cryptography.Models.KeySet>.Success(keySet));

        _walletUtilitiesMock
            .Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns((byte[] pk, byte net) => $"wallet-{Convert.ToHexString(pk).Substring(0, 8)}");

        _sut = new OrgKeyDerivationService(
            _dbContext,
            _protectionProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region Rotation Tests

    [Fact]
    public async Task RotateKeyAsync_ActiveKey_CreatesNewKeyAtNextIndex()
    {
        // Arrange
        var (masterKey, derivedKey) = await SeedActiveKey();

        // Each call needs a unique wallet address
        var callCount = 0;
        _walletUtilitiesMock
            .Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(() => $"wallet-rotated-{++callCount}");

        // Act
        var result = await _sut.RotateKeyAsync(OrgId, derivedKey.Id);

        // Assert
        result.Should().NotBeNull();
        result.KeyIndex.Should().Be(1); // Original was 0, rotated should be 1
        result.Status.Should().Be(DerivedKeyStatus.Active.ToString());
        result.WalletAddress.Should().StartWith("wallet-rotated-");

        // Verify old record was marked as Rotated
        var oldRecord = await _dbContext.DerivedKeyRecords.FindAsync(derivedKey.Id);
        oldRecord!.Status.Should().Be(DerivedKeyStatus.Rotated);

        // Verify new record was created
        var newRecord = await _dbContext.DerivedKeyRecords.FindAsync(result.DerivedKeyId);
        newRecord.Should().NotBeNull();
        newRecord!.Status.Should().Be(DerivedKeyStatus.Active);
        newRecord.KeyIndex.Should().Be(1);
    }

    [Fact]
    public async Task RotateKeyAsync_AlreadyRotatedKey_ThrowsInvalidOperation()
    {
        // Arrange
        var (_, derivedKey) = await SeedActiveKey();
        derivedKey.Status = DerivedKeyStatus.Rotated;
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _sut.RotateKeyAsync(OrgId, derivedKey.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot rotate key*Rotated*");
    }

    [Fact]
    public async Task RotateKeyAsync_NonExistentKey_ThrowsKeyNotFound()
    {
        // Act
        var act = () => _sut.RotateKeyAsync(OrgId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    #endregion

    #region Revocation Tests

    [Fact]
    public async Task RevokeKeyAsync_ActiveKey_SetsRevokedAndLocksWallet()
    {
        // Arrange
        var (_, derivedKey) = await SeedActiveKey();

        // Act
        await _sut.RevokeKeyAsync(OrgId, derivedKey.Id);

        // Assert
        var record = await _dbContext.DerivedKeyRecords.FindAsync(derivedKey.Id);
        record!.Status.Should().Be(DerivedKeyStatus.Revoked);
        record.RevokedAt.Should().NotBeNull();
        record.RevokedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var wallet = await _dbContext.Wallets.FindAsync(derivedKey.WalletAddress);
        wallet!.Status.Should().Be(WalletStatus.Locked);
    }

    [Fact]
    public async Task RevokeKeyAsync_AlreadyRevoked_ThrowsInvalidOperation()
    {
        // Arrange
        var (_, derivedKey) = await SeedActiveKey();
        derivedKey.Status = DerivedKeyStatus.Revoked;
        derivedKey.RevokedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var act = () => _sut.RevokeKeyAsync(OrgId, derivedKey.Id);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already revoked*");
    }

    [Fact]
    public async Task RevokeKeyAsync_IdentityKey_LogsDIDRevocation()
    {
        // Arrange
        var (_, derivedKey) = await SeedActiveKey(KeyUsage.Identity);

        // Act
        await _sut.RevokeKeyAsync(OrgId, derivedKey.Id);

        // Assert - verify logger was called with DID revocation message
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("DID revocation event should be published")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Purpose-Specific Derivation Tests

    [Fact]
    public async Task DeriveUserKeyAsync_AllKeyUsages_ProduceUniqueWallets()
    {
        // Arrange
        await SeedMasterKey();

        // Make each derivation produce a unique wallet address based on the derivation path
        var addressCounter = 0;
        _walletUtilitiesMock
            .Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns(() => $"wallet-usage-{++addressCounter}");

        var usages = Enum.GetValues<KeyUsage>();
        var results = new List<DerivedKeyResult>();

        // Act
        foreach (var usage in usages)
        {
            var result = await _sut.DeriveUserKeyAsync(OrgId, UserId, 0, usage);
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(5);
        results.Select(r => r.WalletAddress).Distinct().Should().HaveCount(5,
            "each key usage should produce a unique wallet");
        results.Select(r => r.DerivationPath).Distinct().Should().HaveCount(5,
            "each key usage should produce a unique derivation path");
        results.Select(r => r.KeyUsage).Should().BeEquivalentTo(usages);
    }

    #endregion

    #region Helpers

    private async Task<OrgMasterKey> SeedMasterKey()
    {
        var masterKey = new OrgMasterKey
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            EncryptedSeed = new byte[64],
            ProtectionProvider = "Software",
            ProtectionKeyId = "test-key-id",
            Algorithm = "ED25519",
            MasterPublicKey = "test-master-public-key",
            Status = OrgMasterKeyStatus.Active,
            CreatedBy = "test-admin"
        };

        _dbContext.OrgMasterKeys.Add(masterKey);
        await _dbContext.SaveChangesAsync();
        return masterKey;
    }

    private async Task<(OrgMasterKey MasterKey, DerivedKeyRecord DerivedKey)> SeedActiveKey(
        KeyUsage usage = KeyUsage.VCIssuance)
    {
        var masterKey = await SeedMasterKey();

        var walletAddress = $"wallet-seed-{Guid.NewGuid():N}".Substring(0, 30);
        var derivedKey = new DerivedKeyRecord
        {
            Id = Guid.NewGuid(),
            OrgMasterKeyId = masterKey.Id,
            OrganizationId = OrgId,
            UserId = UserId,
            DepartmentId = 0,
            KeyUsage = usage,
            KeyIndex = 0,
            DerivationPath = DerivationPathBuilder.Build(
                Guid.Parse(OrgId), 0, Guid.Parse(UserId), usage, 0),
            WalletAddress = walletAddress,
            Status = DerivedKeyStatus.Active,
            CustodyMode = CustodyMode.Custodial
        };

        var wallet = new WalletEntity
        {
            Address = walletAddress,
            EncryptedPrivateKey = Convert.ToBase64String(new byte[64]),
            EncryptionKeyId = "test-key-id",
            Algorithm = "ED25519",
            Owner = UserId,
            Tenant = OrgId,
            Name = "Test wallet",
            Status = WalletStatus.Active,
            DerivedKeyRecordId = derivedKey.Id,
            CustodyMode = CustodyMode.Custodial
        };

        _dbContext.DerivedKeyRecords.Add(derivedKey);
        _dbContext.Wallets.Add(wallet);
        await _dbContext.SaveChangesAsync();

        return (masterKey, derivedKey);
    }

    #endregion
}
