// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class DashboardServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly DashboardService _service;
    private readonly Guid _orgId = Guid.NewGuid();

    public DashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"DashboardTests_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new TenantDbContext(options);
        _service = new DashboardService(_dbContext);
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
            CreateUser(UserRole.Member),
            CreateUser(UserRole.Member),
            CreateUser(UserRole.Member));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(_orgId);

        result.UsersByRole.Should().ContainKey("Administrator").WhoseValue.Should().Be(2);
        result.UsersByRole.Should().ContainKey("Designer").WhoseValue.Should().Be(1);
        result.UsersByRole.Should().ContainKey("Member").WhoseValue.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsRecentLogins_SortedByMostRecent()
    {
        var users = new[]
        {
            CreateUser(UserRole.Member, lastLogin: DateTimeOffset.UtcNow.AddHours(-1)),
            CreateUser(UserRole.Member, lastLogin: DateTimeOffset.UtcNow.AddMinutes(-5)),
            CreateUser(UserRole.Member, lastLogin: DateTimeOffset.UtcNow.AddDays(-2)),
            CreateUser(UserRole.Member) // No login
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
                CreateUser(UserRole.Member, lastLogin: DateTimeOffset.UtcNow.AddMinutes(-i)));
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
            _dbContext.UserIdentities.Add(CreateUser(UserRole.Member));
        for (int i = 0; i < suspended; i++)
            _dbContext.UserIdentities.Add(CreateUser(UserRole.Member, status: IdentityStatus.Suspended));
        _dbContext.SaveChanges();
    }

    private UserIdentity CreateUser(
        UserRole role = UserRole.Member,
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

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
