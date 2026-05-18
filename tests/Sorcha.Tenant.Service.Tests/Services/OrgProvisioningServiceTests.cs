// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Task #14 — verifies <see cref="OrgProvisioningService.ProvisionAsync"/> fires the
/// membership-inbox writer for the founding owner after the org row commits.
/// </summary>
public sealed class OrgProvisioningServiceTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly Mock<IOrganizationService> _orgService = new();
    private readonly Mock<IPlatformSettingsService> _settings = new();
    private readonly Mock<IInvitationService> _invitations = new();
    private readonly Mock<ITenantMembershipInboxWriter> _membershipInbox = new();
    private readonly OrgProvisioningService _sut;

    public OrgProvisioningServiceTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _orgService.Setup(s => s.ValidateSubdomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (string?)null));
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformSettings { MaxOrgsPerUser = 10 });

        _sut = new OrgProvisioningService(
            _db,
            _orgService.Object,
            _settings.Object,
            _invitations.Object,
            _membershipInbox.Object,
            NullLogger<OrgProvisioningService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ProvisionAsync_HappyPath_FiresMembershipInboxWriterForFoundingOwner()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _db.PlatformUsers.Add(new PlatformUser
        {
            Id = userId,
            Email = "owner@example.com",
            DisplayName = "Founding Owner",
            EmailVerified = true,
            EmailVerifiedAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var request = new ProvisionOrgRequest
        {
            Name = "Founding Org",
            Subdomain = "founding",
            Description = null
        };

        // Act
        var result = await _sut.ProvisionAsync(userId, request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _membershipInbox.Verify(
            w => w.WriteOrgMembershipAddedAsync(
                userId,
                result.OrganizationId!.Value,
                UserRole.Administrator.ToString(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
