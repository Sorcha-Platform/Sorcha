// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
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

        _sut = CreateSut(allowVerified: false);
    }

    private static IConfiguration Config(bool allowVerified) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Platform:AllowAdminVerifiedUserCreation"] = allowVerified ? "true" : "false"
        }).Build();

    private OrgProvisioningService CreateSut(bool allowVerified = false) =>
        new(_db, _orgService.Object, _settings.Object, _invitations.Object,
            _membershipInbox.Object, NullLogger<OrgProvisioningService>.Instance, Config(allowVerified));

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

    // --- AdminProvisionAsync: direct org-scoped password user (spec 136 follow-up) ---

    [Fact]
    public async Task AdminProvision_NewEmailWithPassword_FlagOn_CreatesVerifiedSingleOrgUser()
    {
        var sut = CreateSut(allowVerified: true);
        var sysAdminId = Guid.NewGuid();

        var result = await sut.AdminProvisionAsync(
            sysAdminId, "Acme Verification", "acme-verif", null,
            "ops@acme-verif.test", UserRole.Administrator, CancellationToken.None,
            adminPassword: "Dev_Pass_2025!", adminDisplayName: "Acme Ops", adminEmailVerified: true);

        result.Success.Should().BeTrue();
        result.AdminDirectlyAdded.Should().BeTrue();
        result.InvitationId.Should().BeNull("a directly-provisioned admin needs no invitation");

        var pu = _db.PlatformUsers.Single(u => u.Email == "ops@acme-verif.test");
        pu.EmailVerified.Should().BeTrue();
        pu.PasswordHash.Should().NotBeNullOrEmpty();
        pu.Status.Should().Be(PlatformUserStatus.Active);

        // Org-scoped: a membership in the new org and nowhere else (never the public org).
        var memberships = _db.PlatformUserOrgMemberships.Where(m => m.PlatformUserId == pu.Id).ToList();
        memberships.Should().ContainSingle()
            .Which.OrganizationId.Should().Be(result.OrganizationId!.Value);
        _db.UserIdentities.Should().Contain(i => i.PlatformUserId == pu.Id && i.OrganizationId == result.OrganizationId!.Value);
    }

    [Fact]
    public async Task AdminProvision_VerifiedRequested_FlagOff_Rejected_NoUserCreated()
    {
        var sut = CreateSut(allowVerified: false);

        var result = await sut.AdminProvisionAsync(
            Guid.NewGuid(), "Acme", "acme-off", null,
            "ops@acme-off.test", UserRole.Administrator, CancellationToken.None,
            adminPassword: "Dev_Pass_2025!", adminEmailVerified: true);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("verified_creation_disabled");
        _db.PlatformUsers.Any(u => u.Email == "ops@acme-off.test").Should().BeFalse("the request must be refused before any write");
    }

    [Fact]
    public async Task AdminProvision_NewEmailWithPassword_NotVerified_FlagIrrelevant_CreatesUnverifiedUser()
    {
        var sut = CreateSut(allowVerified: false);

        var result = await sut.AdminProvisionAsync(
            Guid.NewGuid(), "Acme Licensing", "acme-lic", null,
            "ops@acme-lic.test", UserRole.Administrator, CancellationToken.None,
            adminPassword: "Dev_Pass_2025!", adminEmailVerified: false);

        result.Success.Should().BeTrue("creating an unverified password user does not need the flag");
        result.AdminDirectlyAdded.Should().BeTrue();
        _db.PlatformUsers.Single(u => u.Email == "ops@acme-lic.test").EmailVerified.Should().BeFalse();
    }
}
