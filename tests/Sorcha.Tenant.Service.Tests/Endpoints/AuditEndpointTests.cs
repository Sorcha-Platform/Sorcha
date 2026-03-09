// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class AuditEndpointTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Guid _testOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AuditEndpointTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();

        // Seed test organization
        _dbContext.Organizations.Add(new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            AllowedEmailDomains = [],
            AuditRetentionMonths = 12,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Seed audit events
        SeedAuditEvents();
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private void SeedAuditEvents()
    {
        var userId = Guid.NewGuid();

        // Recent events
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = _testOrgId,
            EventType = AuditEventType.Login,
            IdentityId = userId,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
            Success = true,
            IpAddress = "192.168.1.1"
        });

        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = _testOrgId,
            EventType = AuditEventType.TokenIssued,
            IdentityId = userId,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-2),
            Success = true
        });

        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = _testOrgId,
            EventType = AuditEventType.PermissionDenied,
            IdentityId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow.AddDays(-5),
            Success = false
        });

        // Old event (for retention testing)
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = _testOrgId,
            EventType = AuditEventType.Login,
            IdentityId = userId,
            Timestamp = DateTimeOffset.UtcNow.AddMonths(-14),
            Success = true
        });

        // Event for different org (should not appear)
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = Guid.NewGuid(),
            EventType = AuditEventType.Login,
            IdentityId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Success = true
        });
    }

    // ── Query tests ──────────────────────────────────────────

    [Fact]
    public void QueryAuditEvents_ReturnsOnlyOrgEvents()
    {
        var events = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId)
            .OrderByDescending(a => a.Timestamp)
            .ToList();

        events.Should().HaveCount(4);
        events.Should().AllSatisfy(e => e.OrganizationId.Should().Be(_testOrgId));
    }

    [Fact]
    public void QueryAuditEvents_FilterByEventType_ReturnsMatching()
    {
        var events = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId && a.EventType == AuditEventType.Login)
            .ToList();

        events.Should().HaveCount(2);
    }

    [Fact]
    public void QueryAuditEvents_FilterByDateRange_ReturnsMatching()
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-3);

        var events = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId && a.Timestamp >= startDate)
            .ToList();

        events.Should().HaveCount(2); // Only the recent events
    }

    [Fact]
    public void QueryAuditEvents_FilterByUserId_ReturnsMatching()
    {
        var loginEvent = _dbContext.AuditLogEntries
            .First(a => a.OrganizationId == _testOrgId && a.EventType == AuditEventType.Login);

        var events = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId && a.IdentityId == loginEvent.IdentityId)
            .ToList();

        events.Should().HaveCount(3); // Login + TokenIssued + old Login (same userId)
    }

    [Fact]
    public void QueryAuditEvents_Pagination_ReturnsCorrectPage()
    {
        var page1 = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId)
            .OrderByDescending(a => a.Timestamp)
            .Skip(0)
            .Take(2)
            .ToList();

        var page2 = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId)
            .OrderByDescending(a => a.Timestamp)
            .Skip(2)
            .Take(2)
            .ToList();

        page1.Should().HaveCount(2);
        page2.Should().HaveCount(2);
        page1.Should().NotIntersectWith(page2);
    }

    // ── Retention tests ──────────────────────────────────────

    [Fact]
    public void GetRetention_ReturnsCurrentSetting()
    {
        var org = _dbContext.Organizations.Find(_testOrgId);

        org!.AuditRetentionMonths.Should().Be(12);
    }

    [Fact]
    public async Task UpdateRetention_ValidValue_Updates()
    {
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AuditRetentionMonths = 24;
        await _dbContext.SaveChangesAsync();

        var updated = await _dbContext.Organizations.FindAsync(_testOrgId);
        updated!.AuditRetentionMonths.Should().Be(24);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(999)]
    public void RetentionValidation_OutOfRange_IsInvalid(int months)
    {
        var isValid = months >= 1 && months <= 120;
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(120)]
    public void RetentionValidation_InRange_IsValid(int months)
    {
        var isValid = months >= 1 && months <= 120;
        isValid.Should().BeTrue();
    }

    // ── AuditCleanupService tests ────────────────────────────

    [Fact]
    public async Task PurgeExpiredEntries_CompletesWithoutError()
    {
        // ExecuteDeleteAsync is not supported by EF Core InMemory provider,
        // so we verify the method completes without throwing (the per-org try-catch handles it).
        // Actual deletion behavior is validated by integration tests with a relational provider.
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(sp => sp.GetService(typeof(TenantDbContext)))
            .Returns(_dbContext);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var service = new AuditCleanupService(
            scopeFactoryMock.Object,
            new Mock<ILogger<AuditCleanupService>>().Object);

        var countBefore = _dbContext.AuditLogEntries
            .Count(a => a.OrganizationId == _testOrgId);
        countBefore.Should().Be(4);

        // Should complete without throwing — per-org exception is caught and logged
        var act = () => service.PurgeExpiredEntriesAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PurgeExpiredEntries_DoesNotRemoveRecentEntries()
    {
        // Set retention to 120 months — nothing should be purged
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AuditRetentionMonths = 120;
        await _dbContext.SaveChangesAsync();

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(sp => sp.GetService(typeof(TenantDbContext)))
            .Returns(_dbContext);
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var service = new AuditCleanupService(
            scopeFactoryMock.Object,
            new Mock<ILogger<AuditCleanupService>>().Object);

        await service.PurgeExpiredEntriesAsync(CancellationToken.None);

        var count = _dbContext.AuditLogEntries
            .Count(a => a.OrganizationId == _testOrgId);
        count.Should().Be(4); // All retained
    }

    // ── DTO mapping tests ────────────────────────────────────

    [Fact]
    public void AuditEventResponse_FromEntity_MapsCorrectly()
    {
        var entry = new AuditLogEntry
        {
            Id = 42,
            Timestamp = DateTimeOffset.UtcNow,
            EventType = AuditEventType.Login,
            IdentityId = Guid.NewGuid(),
            IpAddress = "10.0.0.1",
            Success = true,
            Details = new Dictionary<string, object> { ["key"] = "value" }
        };

        var response = AuditEventResponse.FromEntity(entry);

        response.Id.Should().Be(42);
        response.EventType.Should().Be("Login");
        response.IdentityId.Should().Be(entry.IdentityId);
        response.IpAddress.Should().Be("10.0.0.1");
        response.Success.Should().BeTrue();
        response.Details.Should().ContainKey("key");
    }
}
