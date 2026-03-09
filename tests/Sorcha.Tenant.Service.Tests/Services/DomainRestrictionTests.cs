// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class DomainRestrictionTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IOrganizationRepository> _orgRepoMock;
    private readonly Mock<IIdentityRepository> _identityRepoMock;
    private readonly Mock<ILogger<OrganizationService>> _loggerMock;
    private readonly Guid _testOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public DomainRestrictionTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _orgRepoMock = new Mock<IOrganizationRepository>();
        _identityRepoMock = new Mock<IIdentityRepository>();
        _loggerMock = new Mock<ILogger<OrganizationService>>();

        // Seed test organization
        var org = new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            AllowedEmailDomains = [],
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OrganizationService CreateService()
    {
        return new OrganizationService(
            _orgRepoMock.Object,
            _identityRepoMock.Object,
            _dbContext,
            _loggerMock.Object);
    }

    // ── GetDomainRestrictionsAsync ────────────────────────────

    [Fact]
    public async Task GetDomainRestrictionsAsync_OrgNotFound_ReturnsNull()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        var service = CreateService();

        var result = await service.GetDomainRestrictionsAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDomainRestrictionsAsync_NoDomains_ReturnsInactive()
    {
        var org = new Organization
        {
            Id = _testOrgId,
            Name = "Test Org",
            Subdomain = "testorg",
            AllowedEmailDomains = []
        };
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);

        var service = CreateService();

        var result = await service.GetDomainRestrictionsAsync(_testOrgId);

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEmpty();
        result.RestrictionsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetDomainRestrictionsAsync_WithDomains_ReturnsActive()
    {
        var org = new Organization
        {
            Id = _testOrgId,
            Name = "Test Org",
            Subdomain = "testorg",
            AllowedEmailDomains = ["acme.com", "example.org"]
        };
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(org);

        var service = CreateService();

        var result = await service.GetDomainRestrictionsAsync(_testOrgId);

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEquivalentTo(["acme.com", "example.org"]);
        result.RestrictionsActive.Should().BeTrue();
    }

    // ── UpdateDomainRestrictionsAsync ─────────────────────────

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_OrgNotFound_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            Guid.NewGuid(), ["acme.com"], Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_SetDomains_UpdatesOrg()
    {
        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com", "example.org"], Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEquivalentTo(["acme.com", "example.org"]);
        result.RestrictionsActive.Should().BeTrue();

        // Verify persisted
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AllowedEmailDomains.Should().BeEquivalentTo(["acme.com", "example.org"]);
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_EmptyArray_DisablesRestrictions()
    {
        // First set some domains
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AllowedEmailDomains = ["acme.com"];
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            _testOrgId, [], Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEmpty();
        result.RestrictionsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_NormalizesToLowercase()
    {
        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["ACME.COM", " Example.Org "], Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEquivalentTo(["acme.com", "example.org"]);
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_RemovesDuplicates()
    {
        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com", "ACME.COM", "acme.com"], Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().HaveCount(1);
        result.AllowedDomains.Should().Contain("acme.com");
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_SkipsWhitespaceEntries()
    {
        var service = CreateService();

        var result = await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com", "", "  ", "example.org"], Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AllowedDomains.Should().BeEquivalentTo(["acme.com", "example.org"]);
    }

    // ── Audit event wiring ───────────────────────────────────

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_WritesAuditEvent()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();

        await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com"], userId);

        var audit = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId && a.EventType == AuditEventType.DomainRestrictionUpdated)
            .ToList();

        audit.Should().HaveCount(1);
        audit[0].IdentityId.Should().Be(userId);
        audit[0].Details.Should().ContainKey("previousDomains");
        audit[0].Details.Should().ContainKey("newDomains");
    }

    [Fact]
    public async Task UpdateDomainRestrictionsAsync_AuditContainsPreviousAndNewDomains()
    {
        // Set initial domains
        var org = await _dbContext.Organizations.FindAsync(_testOrgId);
        org!.AllowedEmailDomains = ["old.com"];
        await _dbContext.SaveChangesAsync();

        var service = CreateService();

        await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["new.com", "another.com"], Guid.NewGuid());

        var audit = _dbContext.AuditLogEntries
            .First(a => a.EventType == AuditEventType.DomainRestrictionUpdated);

        audit.Details!["previousDomains"].Should().NotBeNull();
        audit.Details["newDomains"].Should().NotBeNull();
    }

    // ── Integration with CheckDomainRestrictionsAsync ─────────

    [Fact]
    public async Task CheckDomainRestrictions_AfterSettingDomains_EnforcesRestriction()
    {
        var service = CreateService();
        var provisioningService = new OidcProvisioningService(
            _dbContext, new Mock<ILogger<OidcProvisioningService>>().Object);

        // Set domain restrictions
        await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com"], Guid.NewGuid());

        // Check matching domain
        var allowed = await provisioningService.CheckDomainRestrictionsAsync(
            _testOrgId, "user@acme.com", CancellationToken.None);
        allowed.Should().BeTrue();

        // Check non-matching domain
        var denied = await provisioningService.CheckDomainRestrictionsAsync(
            _testOrgId, "user@other.com", CancellationToken.None);
        denied.Should().BeFalse();
    }

    [Fact]
    public async Task CheckDomainRestrictions_AfterClearingDomains_AllowsAll()
    {
        var service = CreateService();
        var provisioningService = new OidcProvisioningService(
            _dbContext, new Mock<ILogger<OidcProvisioningService>>().Object);

        // Set then clear domain restrictions
        await service.UpdateDomainRestrictionsAsync(
            _testOrgId, ["acme.com"], Guid.NewGuid());
        await service.UpdateDomainRestrictionsAsync(
            _testOrgId, [], Guid.NewGuid());

        // Any domain should now be allowed
        var allowed = await provisioningService.CheckDomainRestrictionsAsync(
            _testOrgId, "user@anything.com", CancellationToken.None);
        allowed.Should().BeTrue();
    }
}
