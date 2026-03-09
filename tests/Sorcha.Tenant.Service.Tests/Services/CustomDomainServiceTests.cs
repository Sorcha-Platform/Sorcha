// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class CustomDomainServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly CustomDomainRepository _domainRepository;
    private readonly Mock<IOrganizationRepository> _orgRepoMock;
    private readonly Mock<IDnsResolver> _dnsResolverMock;
    private readonly CustomDomainService _service;

    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Organization _testOrg;

    public CustomDomainServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"CustomDomainTests_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new TenantDbContext(options);
        _domainRepository = new CustomDomainRepository(_dbContext);
        _orgRepoMock = new Mock<IOrganizationRepository>();
        _dnsResolverMock = new Mock<IDnsResolver>();

        _testOrg = new Organization
        {
            Id = _orgId,
            Name = "Acme Corp",
            Subdomain = "acme"
        };

        _orgRepoMock
            .Setup(r => r.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testOrg);

        _service = new CustomDomainService(
            _domainRepository,
            _orgRepoMock.Object,
            _dbContext,
            _dnsResolverMock.Object,
            Mock.Of<ILogger<CustomDomainService>>());
    }

    [Fact]
    public async Task GetCustomDomainAsync_NoDomainConfigured_ReturnsNoneStatus()
    {
        var result = await _service.GetCustomDomainAsync(_orgId);

        result.Status.Should().Be("None");
        result.Domain.Should().BeNull();
        result.CnameTarget.Should().Be("acme.sorcha.io");
    }

    [Fact]
    public async Task GetCustomDomainAsync_DomainConfigured_ReturnsCurrentStatus()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "login.acme.com",
            Status = CustomDomainStatus.Verified,
            VerifiedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetCustomDomainAsync(_orgId);

        result.Status.Should().Be("Verified");
        result.Domain.Should().Be("login.acme.com");
        result.VerifiedAt.Should().NotBeNull();
        result.CnameTarget.Should().Be("acme.sorcha.io");
    }

    [Fact]
    public async Task ConfigureCustomDomainAsync_NewDomain_SetsPendingStatusAndReturnsCnameInstructions()
    {
        var request = new ConfigureCustomDomainRequest { Domain = "login.acme.com" };

        var result = await _service.ConfigureCustomDomainAsync(_orgId, request, _adminUserId);

        result.Domain.Should().Be("login.acme.com");
        result.CnameTarget.Should().Be("acme.sorcha.io");
        result.Instructions.Should().Contain("CNAME");

        var mapping = await _dbContext.CustomDomainMappings.FirstAsync();
        mapping.Status.Should().Be(CustomDomainStatus.Pending);
        mapping.Domain.Should().Be("login.acme.com");
    }

    [Fact]
    public async Task ConfigureCustomDomainAsync_UpdateExistingDomain_ResetsToPending()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "old.acme.com",
            Status = CustomDomainStatus.Verified,
            VerifiedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var request = new ConfigureCustomDomainRequest { Domain = "new.acme.com" };
        var result = await _service.ConfigureCustomDomainAsync(_orgId, request, _adminUserId);

        result.Domain.Should().Be("new.acme.com");
        var mapping = await _dbContext.CustomDomainMappings.FirstAsync();
        mapping.Status.Should().Be(CustomDomainStatus.Pending);
        mapping.VerifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task ConfigureCustomDomainAsync_DomainUsedByAnotherOrg_ThrowsInvalidOperationException()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = Guid.NewGuid(), // Different org
            Domain = "taken.acme.com",
            Status = CustomDomainStatus.Verified
        });
        await _dbContext.SaveChangesAsync();

        var request = new ConfigureCustomDomainRequest { Domain = "taken.acme.com" };

        var act = () => _service.ConfigureCustomDomainAsync(_orgId, request, _adminUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already configured*");
    }

    [Fact]
    public async Task ConfigureCustomDomainAsync_WritesAuditEvent()
    {
        var request = new ConfigureCustomDomainRequest { Domain = "audit.acme.com" };

        await _service.ConfigureCustomDomainAsync(_orgId, request, _adminUserId);

        var auditEntry = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EventType == AuditEventType.CustomDomainConfigured);
        auditEntry.Should().NotBeNull();
        auditEntry!.OrganizationId.Should().Be(_orgId);
        auditEntry.Details!["domain"].ToString().Should().Be("audit.acme.com");
    }

    [Fact]
    public async Task VerifyCustomDomainAsync_CnameMatches_SetsVerifiedStatus()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "verify.acme.com",
            Status = CustomDomainStatus.Pending
        });
        await _dbContext.SaveChangesAsync();

        _dnsResolverMock
            .Setup(d => d.VerifyCnameAsync("verify.acme.com", "acme.sorcha.io", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _service.VerifyCustomDomainAsync(_orgId, _adminUserId);

        result.Verified.Should().BeTrue();
        result.Message.Should().Contain("verified successfully");

        var mapping = await _dbContext.CustomDomainMappings.FirstAsync();
        mapping.Status.Should().Be(CustomDomainStatus.Verified);
        mapping.VerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCustomDomainAsync_CnameDoesNotMatch_SetsFailedStatus()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "fail.acme.com",
            Status = CustomDomainStatus.Pending
        });
        await _dbContext.SaveChangesAsync();

        _dnsResolverMock
            .Setup(d => d.VerifyCnameAsync("fail.acme.com", "acme.sorcha.io", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.VerifyCustomDomainAsync(_orgId, _adminUserId);

        result.Verified.Should().BeFalse();
        result.Message.Should().Contain("Verification failed");

        var mapping = await _dbContext.CustomDomainMappings.FirstAsync();
        mapping.Status.Should().Be(CustomDomainStatus.Failed);
    }

    [Fact]
    public async Task VerifyCustomDomainAsync_NoDomainConfigured_ThrowsInvalidOperationException()
    {
        var act = () => _service.VerifyCustomDomainAsync(_orgId, _adminUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No custom domain configured*");
    }

    [Fact]
    public async Task VerifyCustomDomainAsync_WritesAuditEvent_OnSuccess()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "audit-verify.acme.com",
            Status = CustomDomainStatus.Pending
        });
        await _dbContext.SaveChangesAsync();

        _dnsResolverMock
            .Setup(d => d.VerifyCnameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _service.VerifyCustomDomainAsync(_orgId, _adminUserId);

        var auditEntry = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EventType == AuditEventType.CustomDomainVerified);
        auditEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCustomDomainAsync_WritesAuditEvent_OnFailure()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "audit-fail.acme.com",
            Status = CustomDomainStatus.Pending
        });
        await _dbContext.SaveChangesAsync();

        _dnsResolverMock
            .Setup(d => d.VerifyCnameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _service.VerifyCustomDomainAsync(_orgId, _adminUserId);

        var auditEntry = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EventType == AuditEventType.CustomDomainFailed);
        auditEntry.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveCustomDomainAsync_ExistingDomain_DeletesMappingAndResetsOrg()
    {
        _dbContext.CustomDomainMappings.Add(new CustomDomainMapping
        {
            OrganizationId = _orgId,
            Domain = "remove.acme.com",
            Status = CustomDomainStatus.Verified
        });
        // Seed org directly in DB for the update path
        _dbContext.Organizations.Add(_testOrg);
        _testOrg.CustomDomain = "remove.acme.com";
        _testOrg.CustomDomainStatus = CustomDomainStatus.Verified;
        await _dbContext.SaveChangesAsync();

        // Override mock to use DB org
        _orgRepoMock
            .Setup(r => r.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _dbContext.Organizations.First(o => o.Id == _orgId));

        await _service.RemoveCustomDomainAsync(_orgId, _adminUserId);

        var mappings = await _dbContext.CustomDomainMappings.CountAsync();
        mappings.Should().Be(0);
    }

    [Fact]
    public async Task RemoveCustomDomainAsync_NoDomain_DoesNotThrow()
    {
        var act = () => _service.RemoveCustomDomainAsync(_orgId, _adminUserId);

        await act.Should().NotThrowAsync();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
