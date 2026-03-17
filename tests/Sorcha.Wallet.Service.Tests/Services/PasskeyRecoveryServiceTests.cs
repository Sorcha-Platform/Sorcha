// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

public class PasskeyRecoveryServiceTests : IDisposable
{
    private readonly WalletDbContext _dbContext;
    private readonly Mock<IRecoveryKeyService> _recoveryKeyServiceMock;
    private readonly Mock<IKeyManagementService> _keyManagementServiceMock;
    private readonly PasskeyRecoveryService _sut;

    private const string UserId = "user-123";
    private const string TenantId = "tenant-456";
    private const string CredentialId = "cred-abc";

    public PasskeyRecoveryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TestRecoveryDbContext(options);

        _recoveryKeyServiceMock = new Mock<IRecoveryKeyService>();
        _keyManagementServiceMock = new Mock<IKeyManagementService>();

        // Default: decrypt returns a fake key, encrypt returns encrypted + keyId
        _keyManagementServiceMock
            .Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        _keyManagementServiceMock
            .Setup(k => k.EncryptPrivateKeyAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(("new-encrypted", "new-key-id"));

        _sut = new PasskeyRecoveryService(
            _dbContext,
            _recoveryKeyServiceMock.Object,
            _keyManagementServiceMock.Object,
            NullLogger<PasskeyRecoveryService>.Instance);
    }

    [Fact]
    public async Task RecoverAsync_WithValidPasskey_RestoresWallets()
    {
        await SeedWalletWithPasskeyWrap();

        var result = await _sut.RecoverAsync(UserId, TenantId, CredentialId);

        result.WalletsRecovered.Should().Be(1);
        result.WalletAddresses.Should().Contain("ws1-test-address");
    }

    [Fact]
    public async Task RecoverAsync_NoRecoverableWallets_ReturnsEmpty()
    {
        var result = await _sut.RecoverAsync(UserId, TenantId, CredentialId);

        result.WalletsRecovered.Should().Be(0);
        result.WalletAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverAsync_WrongCredentialId_SkipsWallet()
    {
        await SeedWalletWithPasskeyWrap();

        var result = await _sut.RecoverAsync(UserId, TenantId, "wrong-credential-id");

        result.WalletsRecovered.Should().Be(0);
    }

    [Fact]
    public async Task RecoverAsync_RevokesAllDelegations()
    {
        var wallet = await SeedWalletWithPasskeyWrap();
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "delegate-user",
            AccessRight = AccessRight.ReadWrite,
            GrantedBy = UserId
        });
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "another-delegate",
            AccessRight = AccessRight.ReadOnly,
            GrantedBy = UserId
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(UserId, TenantId, CredentialId);

        result.DelegationsRevoked.Should().Be(2);
        result.DelegationsPendingReview.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecoverAsync_CreatesAuditLog()
    {
        await SeedWalletWithPasskeyWrap();

        await _sut.RecoverAsync(UserId, TenantId, CredentialId, "192.168.1.1");

        var auditLogs = await _dbContext.RecoveryAuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        auditLogs[0].UserId.Should().Be(UserId);
        auditLogs[0].RecoveryPath.Should().Be(RecoveryPathType.Passkey);
        auditLogs[0].IpAddress.Should().Be("192.168.1.1");
        auditLogs[0].WalletsRecovered.Should().Be(1);
    }

    [Fact]
    public async Task RecoverAsync_MultipleWallets_RestoresAll()
    {
        await SeedWalletWithPasskeyWrap("ws1-wallet-1");
        await SeedWalletWithPasskeyWrap("ws1-wallet-2");

        var result = await _sut.RecoverAsync(UserId, TenantId, CredentialId);

        result.WalletsRecovered.Should().Be(2);
        result.WalletAddresses.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecoverAsync_RevokedWrap_IsSkipped()
    {
        var wallet = await SeedWalletWithPasskeyWrap();
        // Revoke the wrap
        var wrap = await _dbContext.RecoveryKeyWraps.FirstAsync();
        wrap.RevokedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(UserId, TenantId, CredentialId);

        result.WalletsRecovered.Should().Be(0);
    }

    private async Task<WalletEntity> SeedWalletWithPasskeyWrap(string address = "ws1-test-address")
    {
        var wallet = new WalletEntity
        {
            Address = address,
            EncryptedPrivateKey = "old-encrypted",
            EncryptionKeyId = "old-key-id",
            Algorithm = "ED25519",
            Owner = UserId,
            Tenant = TenantId,
            Name = "Test Wallet",
            RecoveryEnabled = true,
            EncryptedMasterKeyBlob = "encrypted-blob"
        };
        _dbContext.Wallets.Add(wallet);

        _dbContext.RecoveryKeyWraps.Add(new RecoveryKeyWrap
        {
            WalletAddress = address,
            RecoveryPath = RecoveryPathType.Passkey,
            EncryptedRecoveryKey = "wrapped-key",
            RecipientKeyId = CredentialId,
            Algorithm = "ES256"
        });

        await _dbContext.SaveChangesAsync();
        return wallet;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
