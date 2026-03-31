// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OrgWalletProvisioningTests : IDisposable
{
    private readonly string _dbName;
    private readonly Mock<IWalletServiceClient> _walletClientMock;
    private readonly Mock<ILogger<OrgWalletReconciliationService>> _loggerMock;
    private readonly ServiceProvider _serviceProvider;

    public OrgWalletProvisioningTests()
    {
        _dbName = Guid.NewGuid().ToString();
        _walletClientMock = new Mock<IWalletServiceClient>();
        _loggerMock = new Mock<ILogger<OrgWalletReconciliationService>>();

        var services = new ServiceCollection();
        services.AddDbContext<TenantDbContext>(options =>
            Microsoft.EntityFrameworkCore.InMemoryDbContextOptionsExtensions
                .UseInMemoryDatabase(options, _dbName));
        services.AddSingleton(_walletClientMock.Object);
        services.AddSingleton(_loggerMock.Object);

        _serviceProvider = services.BuildServiceProvider();

        // Ensure database is created
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private OrgWalletReconciliationService CreateService()
    {
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var dbReady = new DatabaseReadySignal();
        dbReady.Signal(); // Tests don't need to wait for migrations
        return new OrgWalletReconciliationService(scopeFactory, dbReady, _loggerMock.Object)
        {
            ScanInterval = TimeSpan.FromMilliseconds(50),
            MaxRetries = 5,
            BaseBackoffSeconds = 0.01
        };
    }

    private void SeedOrg(Guid orgId, string subdomain, string? walletAddress = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = $"Org {subdomain}",
            Subdomain = subdomain,
            Status = OrganizationStatus.Active,
            WalletAddress = walletAddress,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ExecuteAsync_OrgWithoutWallet_ProvisionsWallet()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        SeedOrg(orgId, "acme");

        var walletInfo = new WalletInfo
        {
            Address = "wallet-addr-123",
            Name = "org-acme-signing",
            PublicKey = "cHVibGljLWtleQ==",
            Algorithm = "ED25519",
            Status = "Active",
            Owner = orgId.ToString(),
            Tenant = orgId.ToString()
        };

        _walletClientMock
            .Setup(w => w.CreateWalletAsync(
                "org-acme-signing",
                "ED25519",
                orgId.ToString(),
                orgId.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(walletInfo);

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — run one reconciliation cycle directly
        await service.ReconcileAsync(cts.Token);

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var org = await db.Organizations.FindAsync(orgId);
        org.Should().NotBeNull();
        org!.WalletAddress.Should().Be("wallet-addr-123");
        org.PublicKey.Should().Be("cHVibGljLWtleQ==");
        org.SigningAlgorithm.Should().Be("ED25519");

        _walletClientMock.Verify(
            w => w.CreateWalletAsync(
                "org-acme-signing", "ED25519",
                orgId.ToString(), orgId.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WalletServiceFails_RetriesWithBackoff()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        SeedOrg(orgId, "failcorp");

        _walletClientMock
            .Setup(w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wallet service unavailable"));

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — run two reconciliation cycles
        await service.ReconcileAsync(cts.Token);
        await service.ReconcileAsync(cts.Token);

        // Assert — retry count should be 2
        service.RetryCounts.Should().ContainKey(orgId);
        service.RetryCounts[orgId].Should().Be(2);

        // Wallet should still be null
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var org = await db.Organizations.FindAsync(orgId);
        org!.WalletAddress.Should().BeNull();

        _walletClientMock.Verify(
            w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_MaxRetriesExceeded_StopsRetrying()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        SeedOrg(orgId, "giveupcorp");

        _walletClientMock
            .Setup(w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wallet service unavailable"));

        var service = CreateService();
        service.MaxRetries = 3; // Lower for faster test
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — run 5 reconciliation cycles (more than max retries)
        for (var i = 0; i < 5; i++)
        {
            await service.ReconcileAsync(cts.Token);
        }

        // Assert — should have tried exactly 3 times (max retries), then stopped
        _walletClientMock.Verify(
            w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        service.RetryCounts[orgId].Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_AllOrgsHaveWallets_NoAction()
    {
        // Arrange — seed org that already has a wallet
        var orgId = Guid.NewGuid();
        SeedOrg(orgId, "walletcorp", walletAddress: "existing-wallet-addr");

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await service.ReconcileAsync(cts.Token);

        // Assert — wallet client should never be called
        _walletClientMock.Verify(
            w => w.CreateWalletAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
