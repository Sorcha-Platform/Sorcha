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

/// <summary>
/// Tests for delegation revocation and selective preservation during wallet recovery.
/// </summary>
public class DelegationRecoveryTests : IDisposable
{
    private readonly WalletDbContext _dbContext;
    private readonly PasskeyRecoveryService _passkeyRecovery;
    private readonly OrgRecoveryService _orgRecovery;

    private const string UserId = "user-123";
    private const string AdminId = "admin-001";
    private const string TenantId = "tenant-456";
    private const string CredentialId = "cred-abc";

    public DelegationRecoveryTests()
    {
        var options = new DbContextOptionsBuilder<TestRecoveryDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TestRecoveryDbContext(options);

        var recoveryKeyMock = new Mock<IRecoveryKeyService>();
        var keyMgmtMock = new Mock<IKeyManagementService>();
        keyMgmtMock.Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        keyMgmtMock.Setup(k => k.EncryptPrivateKeyAsync(It.IsAny<byte[]>(), It.IsAny<string>()))
            .ReturnsAsync(("new-enc", "new-key"));

        _passkeyRecovery = new PasskeyRecoveryService(
            _dbContext, recoveryKeyMock.Object, keyMgmtMock.Object,
            NullLogger<PasskeyRecoveryService>.Instance);

        _orgRecovery = new OrgRecoveryService(
            _dbContext, recoveryKeyMock.Object, keyMgmtMock.Object,
            NullLogger<OrgRecoveryService>.Instance);
    }

    [Fact]
    public async Task PasskeyRecovery_RevokesAllActiveDelegations()
    {
        await SeedWalletWithDelegations(RecoveryPathType.Passkey, 3);

        var result = await _passkeyRecovery.RecoverAsync(UserId, TenantId, CredentialId);

        result.DelegationsRevoked.Should().Be(3);
        var dbDelegations = await _dbContext.WalletAccess.ToListAsync();
        dbDelegations.Should().AllSatisfy(d => d.RevokedAt.Should().NotBeNull());
    }

    [Fact]
    public async Task PasskeyRecovery_SkipsAlreadyRevokedDelegations()
    {
        var wallet = await SeedWalletWithDelegations(RecoveryPathType.Passkey, 2);
        // Revoke one beforehand
        var first = await _dbContext.WalletAccess.FirstAsync();
        first.RevokedAt = DateTime.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync();

        var result = await _passkeyRecovery.RecoverAsync(UserId, TenantId, CredentialId);

        result.DelegationsRevoked.Should().Be(1); // only the still-active one
    }

    [Fact]
    public async Task PasskeyRecovery_SkipsExpiredDelegations()
    {
        var wallet = await SeedWalletWithDelegations(RecoveryPathType.Passkey, 0);
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "expired-delegate",
            AccessRight = AccessRight.ReadOnly,
            GrantedBy = UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(-1) // Already expired
        });
        await _dbContext.SaveChangesAsync();

        var result = await _passkeyRecovery.RecoverAsync(UserId, TenantId, CredentialId);

        result.DelegationsRevoked.Should().Be(0);
    }

    [Fact]
    public async Task OrgRecovery_SkipRevocation_PreservesAllDelegations()
    {
        await SeedWalletWithDelegations(RecoveryPathType.OrgManaged, 3);

        var result = await _orgRecovery.RecoverAsync(
            AdminId, UserId, TenantId, "sig", skipDelegationRevocation: true);

        result.DelegationsRevoked.Should().Be(0);
        var dbDelegations = await _dbContext.WalletAccess.ToListAsync();
        dbDelegations.Should().AllSatisfy(d => d.RevokedAt.Should().BeNull());
    }

    [Fact]
    public async Task PasskeyRecovery_AuditLogCountsAccurate()
    {
        await SeedWalletWithDelegations(RecoveryPathType.Passkey, 5);

        await _passkeyRecovery.RecoverAsync(UserId, TenantId, CredentialId);

        var log = await _dbContext.RecoveryAuditLogs.FirstAsync();
        log.DelegationsRevoked.Should().Be(5);
        log.DelegationsPreserved.Should().Be(0);
    }

    [Fact]
    public async Task DelegationReviewItems_ContainCorrectDetails()
    {
        var wallet = await SeedWalletWithDelegations(RecoveryPathType.Passkey, 0);
        _dbContext.WalletAccess.Add(new WalletAccess
        {
            ParentWalletAddress = wallet.Address,
            Subject = "specific-delegate",
            AccessRight = AccessRight.ReadWrite,
            GrantedBy = UserId,
            Reason = "Collaboration on project"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _passkeyRecovery.RecoverAsync(UserId, TenantId, CredentialId);

        result.DelegationsPendingReview.Should().HaveCount(1);
        var item = result.DelegationsPendingReview[0];
        item.Subject.Should().Be("specific-delegate");
        item.AccessRight.Should().Be("ReadWrite");
        item.Reason.Should().Be("Collaboration on project");
        item.WalletAddress.Should().Be(wallet.Address);
    }

    private async Task<WalletEntity> SeedWalletWithDelegations(RecoveryPathType pathType, int delegationCount)
    {
        var wallet = new WalletEntity
        {
            Address = $"ws1-recovery-test-{Guid.NewGuid():N}",
            EncryptedPrivateKey = "enc",
            EncryptionKeyId = "key",
            Algorithm = "ED25519",
            Owner = UserId,
            Tenant = TenantId,
            Name = "Test",
            RecoveryEnabled = true,
            EncryptedMasterKeyBlob = "blob"
        };
        _dbContext.Wallets.Add(wallet);

        _dbContext.RecoveryKeyWraps.Add(new RecoveryKeyWrap
        {
            WalletAddress = wallet.Address,
            RecoveryPath = pathType,
            EncryptedRecoveryKey = "wrapped",
            RecipientKeyId = pathType == RecoveryPathType.Passkey ? CredentialId : "org-key",
            Algorithm = "ED25519"
        });

        for (var i = 0; i < delegationCount; i++)
        {
            _dbContext.WalletAccess.Add(new WalletAccess
            {
                ParentWalletAddress = wallet.Address,
                Subject = $"delegate-{i}",
                AccessRight = AccessRight.ReadOnly,
                GrantedBy = UserId
            });
        }

        await _dbContext.SaveChangesAsync();
        return wallet;
    }

    public void Dispose() => _dbContext.Dispose();
}
