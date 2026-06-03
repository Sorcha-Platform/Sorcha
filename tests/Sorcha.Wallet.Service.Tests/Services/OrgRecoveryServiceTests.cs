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
    public async Task RecoverAsync_WithOrgWrap_ThrowsNotSupported_AndDoesNotRekey()
    {
        // Review M3b: an org-managed wrap is the point recovery would be authorised — but org
        // recovery-key signature verification is not implemented, so the service must fail loud rather
        // than re-key the wallet on the admin's session alone.
        await SeedWalletWithOrgWrap();

        var act = () => _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*signature*");
        var wallet = await _dbContext.Wallets.FirstAsync();
        wallet.EncryptedPrivateKey.Should().Be("old-encrypted", "the wallet must not be re-keyed without verified proof");
    }

    [Fact]
    public async Task RecoverAsync_NoWallets_ReturnsEmpty()
    {
        var result = await _sut.RecoverAsync(AdminUserId, TargetUserId, TenantId, "signature");

        result.WalletsRecovered.Should().Be(0);
    }

    // Note (review M3b): the prior success-path tests (RevokesDelegationsByDefault /
    // SkipDelegationRevocation / CreatesAuditLog / OnlyRecoversTenantWallets) asserted that recovery
    // COMPLETED a re-key on the admin session alone — the exact behaviour now removed. They are dropped
    // until org recovery-key signature verification is built (the deferred wallet-recovery feature).

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
