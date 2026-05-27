// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.ServiceClients.Register;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class DashboardServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly DashboardService _service;
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Guid _orgId = Guid.NewGuid();

    public DashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"DashboardTests_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new TenantDbContext(options);
        _service = new DashboardService(
            _dbContext,
            _registerClient.Object,
            NullLogger<DashboardService>.Instance);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsActiveAndSuspendedCounts()
    {
        SeedUsers(active: 5, suspended: 2);

        var result = await _service.GetDashboardAsync(_orgId);

        result.ActiveUserCount.Should().Be(5);
        result.SuspendedUserCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsUsersByRoleBreakdown()
    {
        _dbContext.UserIdentities.AddRange(
            CreateUser(UserRole.Administrator),
            CreateUser(UserRole.Administrator),
            CreateUser(UserRole.Designer),
            CreateUser(UserRole.Consumer),
            CreateUser(UserRole.Consumer),
            CreateUser(UserRole.Consumer));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.UsersByRole.Should().ContainKey("Administrator").WhoseValue.Should().Be(2);
        result.UsersByRole.Should().ContainKey("Designer").WhoseValue.Should().Be(1);
        result.UsersByRole.Should().ContainKey("Consumer").WhoseValue.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsRecentLogins_SortedByMostRecent()
    {
        var users = new[]
        {
            CreateUser(UserRole.Consumer, lastLogin: DateTimeOffset.UtcNow.AddHours(-1)),
            CreateUser(UserRole.Consumer, lastLogin: DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateUser(UserRole.Consumer, lastLogin: DateTimeOffset.UtcNow.AddDays(-2)),
            CreateUser(UserRole.Consumer) // No login
        };
        _dbContext.UserIdentities.AddRange(users);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.RecentLogins.Should().HaveCount(3);
        result.RecentLogins[0].Timestamp.Should().BeAfter(result.RecentLogins[1].Timestamp);
    }

    [Fact]
    public async Task GetDashboardAsync_RecentLogins_LimitedTo10()
    {
        for (int i = 0; i < 15; i++)
        {
            _dbContext.UserIdentities.Add(
                CreateUser(UserRole.Consumer, lastLogin: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.RecentLogins.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsPendingInvitationCount()
    {
        _dbContext.OrgInvitations.AddRange(
            new OrgInvitation
            {
                OrganizationId = _orgId, Email = "a@t.com", Token = "t1",
                Status = InvitationStatus.Pending, ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                InvitedByUserId = Guid.NewGuid()
            },
            new OrgInvitation
            {
                OrganizationId = _orgId, Email = "b@t.com", Token = "t2",
                Status = InvitationStatus.Pending, ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                InvitedByUserId = Guid.NewGuid()
            },
            new OrgInvitation
            {
                OrganizationId = _orgId, Email = "c@t.com", Token = "t3",
                Status = InvitationStatus.Accepted, ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                InvitedByUserId = Guid.NewGuid()
            },
            new OrgInvitation
            {
                OrganizationId = _orgId, Email = "d@t.com", Token = "t4",
                Status = InvitationStatus.Pending, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), // expired
                InvitedByUserId = Guid.NewGuid()
            });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.PendingInvitationCount.Should().Be(2); // Only active pending, not expired or accepted
    }

    [Fact]
    public async Task GetDashboardAsync_NoIdpConfigured_ReturnsNotConfigured()
    {
        SeedUsers(active: 1);

        var result = await _service.GetDashboardAsync(_orgId);

        result.IdpStatus.Configured.Should().BeFalse();
        result.IdpStatus.Enabled.Should().BeFalse();
        result.IdpStatus.ProviderName.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_IdpConfigured_ReturnsStatus()
    {
        SeedUsers(active: 1);
        _dbContext.IdentityProviderConfigurations.Add(new IdentityProviderConfiguration
        {
            OrganizationId = _orgId,
            ProviderPreset = IdentityProviderType.MicrosoftEntra,
            IssuerUrl = "https://login.microsoft.com/tenant/v2.0",
            ClientId = "test-client",
            ClientSecretEncrypted = new byte[32],
            IsEnabled = true,
            DisplayName = "Microsoft Entra"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.IdpStatus.Configured.Should().BeTrue();
        result.IdpStatus.Enabled.Should().BeTrue();
        result.IdpStatus.ProviderName.Should().Be("Microsoft Entra");
    }

    [Fact]
    public async Task GetDashboardAsync_EmptyOrg_ReturnsZeroCounts()
    {
        var result = await _service.GetDashboardAsync(_orgId);

        result.ActiveUserCount.Should().Be(0);
        result.SuspendedUserCount.Should().Be(0);
        result.UsersByRole.Should().BeEmpty();
        result.RecentLogins.Should().BeEmpty();
        result.PendingInvitationCount.Should().Be(0);
    }

    private void SeedUsers(int active = 0, int suspended = 0)
    {
        for (int i = 0; i < active; i++)
            _dbContext.UserIdentities.Add(CreateUser(UserRole.Consumer));
        for (int i = 0; i < suspended; i++)
            _dbContext.UserIdentities.Add(CreateUser(UserRole.Consumer, status: IdentityStatus.Suspended));
        _dbContext.SaveChanges();
    }

    private UserIdentity CreateUser(
        UserRole role = UserRole.Consumer,
        IdentityStatus status = IdentityStatus.Active,
        DateTimeOffset? lastLogin = null,
        ProvisioningMethod provisionedVia = ProvisioningMethod.Local)
    {
        return new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            Email = $"{Guid.NewGuid():N}@test.com",
            DisplayName = $"User {Guid.NewGuid():N}",
            Roles = [role],
            Status = status,
            LastLoginAt = lastLogin,
            ProvisionedVia = provisionedVia,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // ---------------------------------------------------------------------
    // Feature 131 / UX-005 — GetOrgSummaryAsync
    // ---------------------------------------------------------------------

    [Fact]
    public async Task GetOrgSummaryAsync_CountsActiveUsersAndPendingInvitations()
    {
        SeedUsers(active: 4, suspended: 1);
        _dbContext.OrgInvitations.Add(new OrgInvitation
        {
            OrganizationId = _orgId, Email = "p@t.com", Token = "p1",
            Status = InvitationStatus.Pending, ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            InvitedByUserId = Guid.NewGuid()
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetOrgSummaryAsync(_orgId);

        result.OrgId.Should().Be(_orgId);
        result.ActiveUsers.Should().Be(4);
        result.PendingInvitations.Should().Be(1);
        result.SubscribedRegisters.Should().Be(0);
        result.RecentTransactions.Should().Be(0);
    }

    [Fact]
    public async Task GetOrgSummaryAsync_CountsActiveSubscribedRegisters()
    {
        SeedUsers(active: 1);
        _dbContext.OrganizationRegisterSubscriptions.AddRange(
            new OrganizationRegisterSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = _orgId, RegisterId = "reg-a",
                Status = SubscriptionStatus.Active, SubscriptionType = SubscriptionType.Owner,
                SubscribedAt = DateTimeOffset.UtcNow, SubscribedByUserId = Guid.NewGuid()
            },
            new OrganizationRegisterSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = _orgId, RegisterId = "reg-b",
                Status = SubscriptionStatus.Active, SubscriptionType = SubscriptionType.Public,
                SubscribedAt = DateTimeOffset.UtcNow, SubscribedByUserId = Guid.NewGuid()
            },
            new OrganizationRegisterSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = _orgId, RegisterId = "reg-c",
                Status = SubscriptionStatus.Revoked, SubscriptionType = SubscriptionType.Owner,
                SubscribedAt = DateTimeOffset.UtcNow, SubscribedByUserId = Guid.NewGuid()
            });
        await _dbContext.SaveChangesAsync();

        _registerClient
            .Setup(c => c.GetStatsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterStatsResponse { RegisterCount = 2, TransactionCount = 17 });

        var result = await _service.GetOrgSummaryAsync(_orgId);

        result.SubscribedRegisters.Should().Be(2);     // active only
        result.RecentTransactions.Should().Be(17);
        _registerClient.Verify(
            c => c.GetStatsAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 2
                                                  && ids.Contains("reg-a")
                                                  && ids.Contains("reg-b")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrgSummaryAsync_SkipsRegisterCallWhenNoSubscriptions()
    {
        SeedUsers(active: 1);

        var result = await _service.GetOrgSummaryAsync(_orgId);

        result.SubscribedRegisters.Should().Be(0);
        result.RecentTransactions.Should().Be(0);
        _registerClient.Verify(
            c => c.GetStatsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrgSummaryAsync_RegisterClientThrows_DegradesToZeroTransactions()
    {
        SeedUsers(active: 1);
        _dbContext.OrganizationRegisterSubscriptions.Add(new OrganizationRegisterSubscription
        {
            Id = Guid.NewGuid(), OrganizationId = _orgId, RegisterId = "reg-a",
            Status = SubscriptionStatus.Active, SubscriptionType = SubscriptionType.Owner,
            SubscribedAt = DateTimeOffset.UtcNow, SubscribedByUserId = Guid.NewGuid()
        });
        await _dbContext.SaveChangesAsync();

        _registerClient
            .Setup(c => c.GetStatsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register down"));

        var result = await _service.GetOrgSummaryAsync(_orgId);

        result.SubscribedRegisters.Should().Be(1);
        result.RecentTransactions.Should().Be(0); // graceful degrade
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
