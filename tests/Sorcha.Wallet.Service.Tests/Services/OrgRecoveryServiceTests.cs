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

public class OrgRecoveryServiceTests : IDisposable
{
    private readonly WalletDbContext _dbContext;
    private readonly Mock<IRecoveryKeyService> _recoveryKeyServiceMock;
    private readonly Mock<IKeyManagementService> _keyManagementServiceMock;
    private readonly OrgRecoveryService _sut;

    private const string AdminUserId = "admin-001";
    private const string TargetUserId = "user-123";
    private const string TenantId = "tenant-456";

    public OrgRecoveryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TestRecoveryDbContext(options);

        _recoveryKeyServiceMock = new Mock<IRecoveryKeyService>();
        _keyManagementServiceMock = new Mock<IKeyManagementService>();

        _keyManagementServiceMock
            .Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        _keyManagementServiceMock
            .Setup(k => k.EncryptPrivateKeyAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(("new-encrypted", "new-key-id"));

        _sut = new OrgRecoveryService(
            _dbContext,
            _recoveryKeyServiceMock.Object,
            _keyManagementServiceMock.Object,
            NullLogger<OrgRecoveryService>.Instance);
    }

    [Fact]
    public async Task RecoverAsync_AdminRecoversUserWallets_Succeeds()
    {
        await SeedWalletWithOrgWrap();

        var result = await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        result.WalletsRecovered.Should().Be(1);
        result.WalletAddresses.Should().Contain("ws1-org-wallet");
    }

    [Fact]
    public async Task RecoverAsync_NoWallets_ReturnsEmpty()
    {
        var result = await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        result.WalletsRecovered.Should().Be(0);
    }

    [Fact]
    public async Task RecoverAsync_RevokesDelegationsByDefault()
    {
        var wallet = await SeedWalletWithOrgWrap();
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "delegate-user",
            AccessRight = AccessRight.ReadWrite,
            GrantedBy = TargetUserId
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        result.DelegationsRevoked.Should().Be(1);
        result.DelegationsPendingReview.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecoverAsync_SkipDelegationRevocation_PreservesDelegations()
    {
        var wallet = await SeedWalletWithOrgWrap();
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "delegate-user",
            AccessRight = AccessRight.ReadWrite,
            GrantedBy = TargetUserId
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(
            AdminUserId, TargetUserId, TenantId, "signature",
            skipDelegationRevocation: true);

        result.DelegationsRevoked.Should().Be(0);
        result.DelegationsPendingReview.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverAsync_CreatesAuditLogWithAdminAsInitiator()
    {
        await SeedWalletWithOrgWrap();

        await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature", ipAddress: "10.0.0.1");

        var auditLogs = await _dbContext.RecoveryAuditLogs.ToListAsync();
        auditLogs.Should().HaveCount(1);
        auditLogs[0].UserId.Should().Be(TargetUserId);
        auditLogs[0].InitiatedBy.Should().Be(AdminUserId);
        auditLogs[0].RecoveryPath.Should().Be(RecoveryPathType.OrgManaged);
        auditLogs[0].IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task RecoverAsync_OnlyRecoversTenantWallets()
    {
        await SeedWalletWithOrgWrap("ws1-same-tenant", TenantId);
        // Seed a wallet in a different tenant
        var otherWallet = new WalletEntity
        {
            Address = "ws1-other-tenant",
            EncryptedPrivateKey = "enc",
            EncryptionKeyId = "key",
            Algorithm = "ED25519",
            Owner = TargetUserId,
            Tenant = "other-tenant",
            Name = "Other Tenant Wallet",
            RecoveryEnabled = true,
            EncryptedMasterKeyBlob = "blob"
        };
        _dbContext.Wallets.Add(otherWallet);
        _dbContext.RecoveryKeyWraps.Add(new RecoveryKeyWrap
        {
            WalletAddress = "ws1-other-tenant",
            RecoveryPath = RecoveryPathType.OrgManaged,
            EncryptedRecoveryKey = "wrapped",
            RecipientKeyId = "org-key",
            Algorithm = "ED25519"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        result.WalletsRecovered.Should().Be(1);
        result.WalletAddresses.Should().OnlyContain(a => a == "ws1-same-tenant");
    }

    private async Task<WalletEntity> SeedWalletWithOrgWrap(string address = "ws1-org-wallet", string tenantId = TenantId)
    {
        var wallet = new WalletEntity
        {
            Address = address,
            EncryptedPrivateKey = "old-encrypted",
            EncryptionKeyId = "old-key-id",
            Algorithm = "ED25519",
            Owner = TargetUserId,
            Tenant = tenantId,
            Name = "Org Wallet",
            RecoveryEnabled = true,
            EncryptedMasterKeyBlob = "encrypted-blob"
        };
        _dbContext.Wallets.Add(wallet);

        _dbContext.RecoveryKeyWraps.Add(new RecoveryKeyWrap
        {
            WalletAddress = address,
            RecoveryPath = RecoveryPathType.OrgManaged,
            EncryptedRecoveryKey = "wrapped-key",
            RecipientKeyId = "org-recovery-key",
            Algorithm = "ED25519"
        });

        await _dbContext.SaveChangesAsync();
        return wallet;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
