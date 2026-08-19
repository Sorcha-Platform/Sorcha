// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OrganizationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IOrganizationRepository> _orgRepoMock;
    private readonly Mock<IIdentityRepository> _identityRepoMock;
    private readonly Mock<IWalletServiceClient> _walletClientMock;
    private readonly Mock<ITenantMembershipInboxWriter> _membershipInboxMock;
    private readonly Mock<ILogger<OrganizationService>> _loggerMock;
    private readonly Guid _testOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public OrganizationServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _orgRepoMock = new Mock<IOrganizationRepository>();
        _identityRepoMock = new Mock<IIdentityRepository>();
        _walletClientMock = new Mock<IWalletServiceClient>();
        _membershipInboxMock = new Mock<ITenantMembershipInboxWriter>();
        _loggerMock = new Mock<ILogger<OrganizationService>>();

        // Seed test organization
        var org = new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "testorg",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.Organizations.Add(org);
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private OrganizationService CreateService(bool allowAdminVerifiedUserCreation = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:AllowAdminVerifiedUserCreation"] = allowAdminVerifiedUserCreation ? "true" : "false"
            }).Build();
        return new OrganizationService(
            _orgRepoMock.Object,
            _identityRepoMock.Object,
            _dbContext,
            _walletClientMock.Object,
            _membershipInboxMock.Object,
            _loggerMock.Object,
            config);
    }

    // ── #1525: the org admin creates the organisation's wallet ──

    [Fact]
    public async Task CreateOrganizationAsync_DoesNotProvisionAWallet()
    {
        // The whole point of #1525. Creating the wallet server-side generates a BIP39 recovery
        // phrase with no human present to receive it — it is shown once and never stored, so the
        // organisation becomes unrecoverable. A null WalletAddress is the "awaiting its wallet"
        // state, and the org admin fills it deliberately.
        var service = CreateService();
        Organization? saved = null;
        _orgRepoMock.Setup(r => r.CreateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization o, CancellationToken _) => { saved = o; return o; });

        await service.CreateOrganizationAsync(
            new Sorcha.Tenant.Service.Models.Dtos.CreateOrganizationRequest { Name = "Acme", Subdomain = "acme" },
            Guid.NewGuid());

        saved!.WalletAddress.Should().BeNull();
        _walletClientMock.Verify(w => w.CreateWalletAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never,
            "the platform must never mint an organisation's wallet — its recovery phrase belongs to the org admin");
    }

    [Fact]
    public async Task LinkOrganizationWalletAsync_WalletOwnedByTheOrg_IsRecorded()
    {
        var service = CreateService();
        var org = new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" };
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        GivenWallet("ws11qacme", owner: _testOrgId.ToString());

        var result = await service.LinkOrganizationWalletAsync(_testOrgId, "ws11qacme");

        result.Should().NotBeNull();
        org.WalletAddress.Should().Be("ws11qacme");
        org.PublicKey.Should().Be("pk-ws11qacme");
        org.SigningAlgorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task LinkOrganizationWalletAsync_WalletOwnedByAnotherOrg_IsRefused()
    {
        // Addresses are public, so without an ownership check an admin could adopt any wallet whose
        // address they happen to know — and the org's issuer DID would then anchor on a key they do
        // not control.
        var service = CreateService();
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" });
        GivenWallet("ws11qsomeoneelse", owner: Guid.NewGuid().ToString());

        var act = () => service.LinkOrganizationWalletAsync(_testOrgId, "ws11qsomeoneelse");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not owned by organisation*");
    }

    [Fact]
    public async Task LinkOrganizationWalletAsync_UnknownWallet_IsRefused()
    {
        var service = CreateService();
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" });
        _walletClientMock.Setup(w => w.GetWalletAsync("ws11qnope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        var act = () => service.LinkOrganizationWalletAsync(_testOrgId, "ws11qnope");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    }

    [Fact]
    public async Task LinkOrganizationWalletAsync_OrgAlreadyHasAWallet_IsRefused()
    {
        // Replacing the canonical wallet orphans every credential issued under the old one and
        // every governance roster entry matched against it. That is a deliberate migration, never
        // a side effect of calling this twice.
        var service = CreateService();
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization
            {
                Id = _testOrgId, Name = "Acme", Subdomain = "acme", WalletAddress = "ws11qexisting"
            });

        var act = () => service.LinkOrganizationWalletAsync(_testOrgId, "ws11qnew");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has a wallet*");
        _walletClientMock.Verify(w => w.GetWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "it should refuse before looking anything up");
    }

    [Fact]
    public async Task LinkOrganizationWalletAsync_UnknownOrg_ReturnsNull()
    {
        var service = CreateService();
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        var result = await service.LinkOrganizationWalletAsync(_testOrgId, "ws11qwhatever");

        result.Should().BeNull();
    }

    private void GivenWallet(string address, string owner) =>
        _walletClientMock.Setup(w => w.GetWalletAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = address,
                Name = $"org-{owner}-signing",
                PublicKey = $"pk-{address}",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = owner,
                Tenant = owner
            });

    // ── Helper methods ─────────────────────────────────────────

    private PlatformUser SeedPlatformUser(Guid id, string email, bool emailVerified = false,
        string? verificationToken = null)
    {
        var platformUser = new PlatformUser
        {
            Id = id,
            Email = email,
            DisplayName = email.Split('@')[0],
            EmailVerified = emailVerified,
            EmailVerifiedAt = emailVerified ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            VerificationToken = verificationToken,
            VerificationTokenExpiresAt = verificationToken != null
                ? DateTimeOffset.UtcNow.AddHours(24)
                : null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.PlatformUsers.Add(platformUser);
        _dbContext.SaveChanges();
        return platformUser;
    }

    private UserIdentity CreateUserIdentity(Guid orgId, Guid platformUserId, string email,
        ProvisioningMethod provisionedVia = ProvisioningMethod.Local,
        IdentityStatus status = IdentityStatus.Active)
    {
        return new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            PlatformUserId = platformUserId,
            Email = email,
            DisplayName = email.Split('@')[0],
            Roles = [UserRole.Consumer],
            Status = status,
            ProvisionedVia = provisionedVia,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private OrgInvitation SeedOrgInvitation(Guid orgId, string email,
        InvitationStatus status = InvitationStatus.Pending,
        Guid? invitedByUserId = null)
    {
        var invitation = new OrgInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Email = email,
            AssignedRole = UserRole.Consumer,
            Token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = status,
            InvitedByUserId = invitedByUserId ?? Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.OrgInvitations.Add(invitation);
        _dbContext.SaveChanges();
        return invitation;
    }

    // ══════════════════════════════════════════════════════════════
    // T013 - GetOrganizationUsersAsync enhanced filtering
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOrganizationUsersAsync_NoFilters_ReturnsAllUsers()
    {
        // Arrange
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "alice@test.com", emailVerified: true);
        var pu2 = SeedPlatformUser(Guid.NewGuid(), "bob@test.com", emailVerified: false);

        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "alice@test.com");
        var user2 = CreateUserIdentity(_testOrgId, pu2.Id, "bob@test.com");
        var users = new List<UserIdentity> { user1, user2 };

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId);

        // Assert
        result.Users.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.PendingInvitations.Should().BeEmpty();
        result.PendingInvitationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_EmailVerifiedTrue_ReturnsOnlyVerifiedUsers()
    {
        // Arrange
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "verified@test.com", emailVerified: true);
        var pu2 = SeedPlatformUser(Guid.NewGuid(), "unverified@test.com", emailVerified: false);

        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "verified@test.com");
        var user2 = CreateUserIdentity(_testOrgId, pu2.Id, "unverified@test.com");
        var users = new List<UserIdentity> { user1, user2 };

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, emailVerified: true);

        // Assert
        result.Users.Should().HaveCount(1);
        result.Users[0].Email.Should().Be("verified@test.com");
        result.Users[0].EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_EmailVerifiedFalse_ReturnsOnlyUnverifiedUsers()
    {
        // Arrange
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "verified@test.com", emailVerified: true);
        var pu2 = SeedPlatformUser(Guid.NewGuid(), "unverified@test.com", emailVerified: false);

        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "verified@test.com");
        var user2 = CreateUserIdentity(_testOrgId, pu2.Id, "unverified@test.com");
        var users = new List<UserIdentity> { user1, user2 };

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, emailVerified: false);

        // Assert
        result.Users.Should().HaveCount(1);
        result.Users[0].Email.Should().Be("unverified@test.com");
        result.Users[0].EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_ProvisionedViaInvitation_PassesFilterToRepository()
    {
        // Arrange
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "invited@test.com");

        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "invited@test.com",
            provisionedVia: ProvisioningMethod.Invitation);
        var users = new List<UserIdentity> { user1 };

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, "Invitation", It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, provisionedVia: "Invitation");

        // Assert
        result.Users.Should().HaveCount(1);
        result.Users[0].Email.Should().Be("invited@test.com");
        result.Users[0].ProvisionedVia.Should().Be("Invitation");

        _identityRepoMock.Verify(
            r => r.GetUsersWithFiltersAsync(_testOrgId, false, "Invitation", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_IncludePendingInvitations_ReturnsPendingInvitations()
    {
        // Arrange — no existing users, but a pending invitation exists
        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserIdentity>());

        SeedOrgInvitation(_testOrgId, "pending@test.com", InvitationStatus.Pending);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, includePending: true);

        // Assert
        result.Users.Should().BeEmpty();
        result.PendingInvitations.Should().HaveCount(1);
        result.PendingInvitations[0].Email.Should().Be("pending@test.com");
        result.PendingInvitations[0].InvitationStatus.Should().Be("Pending");
        result.PendingInvitationCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_IncludePendingInvitations_ExcludesExistingUsers()
    {
        // Arrange — user already exists AND has a pending invitation
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "existing@test.com");
        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "existing@test.com");

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserIdentity> { user1 });

        // Invitation for user who already has a UserIdentity
        SeedOrgInvitation(_testOrgId, "existing@test.com", InvitationStatus.Pending);
        // Invitation for user who does NOT have a UserIdentity
        SeedOrgInvitation(_testOrgId, "newuser@test.com", InvitationStatus.Pending);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, includePending: true);

        // Assert — only the invitation for the non-existing user should appear
        result.PendingInvitations.Should().HaveCount(1);
        result.PendingInvitations[0].Email.Should().Be("newuser@test.com");
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_IncludePendingInvitationsFalse_ReturnsEmptyPendingList()
    {
        // Arrange
        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserIdentity>());

        SeedOrgInvitation(_testOrgId, "pending@test.com", InvitationStatus.Pending);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId, includePending: false);

        // Assert
        result.PendingInvitations.Should().BeEmpty();
        result.PendingInvitationCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_CombinedFilters_AppliesAll()
    {
        // Arrange — two invitation-provisioned users: one verified, one not
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "verified-invited@test.com", emailVerified: true);
        var pu2 = SeedPlatformUser(Guid.NewGuid(), "unverified-invited@test.com", emailVerified: false);

        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "verified-invited@test.com",
            provisionedVia: ProvisioningMethod.Invitation);
        var user2 = CreateUserIdentity(_testOrgId, pu2.Id, "unverified-invited@test.com",
            provisionedVia: ProvisioningMethod.Invitation);

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, true, "Invitation", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserIdentity> { user1, user2 });

        // Also add a pending invitation for a new user
        SeedOrgInvitation(_testOrgId, "newpending@test.com", InvitationStatus.Pending);

        var service = CreateService();

        // Act — provisionedVia=Invitation, emailVerified=true, includeInactive=true, includePending=true
        var result = await service.GetOrganizationUsersAsync(
            _testOrgId,
            includeInactive: true,
            emailVerified: true,
            provisionedVia: "Invitation",
            includePending: true);

        // Assert — only the verified invited user should appear
        result.Users.Should().HaveCount(1);
        result.Users[0].Email.Should().Be("verified-invited@test.com");
        result.Users[0].EmailVerified.Should().BeTrue();

        // Pending invitations should still be included
        result.PendingInvitations.Should().HaveCount(1);
        result.PendingInvitations[0].Email.Should().Be("newpending@test.com");
    }

    [Fact]
    public async Task GetOrganizationUsersAsync_IncludesInvitationStatusFromLatestInvitation()
    {
        // Arrange — user with a matching OrgInvitation
        var pu1 = SeedPlatformUser(Guid.NewGuid(), "user@test.com");
        var user1 = CreateUserIdentity(_testOrgId, pu1.Id, "user@test.com",
            provisionedVia: ProvisioningMethod.Invitation);

        _identityRepoMock
            .Setup(r => r.GetUsersWithFiltersAsync(_testOrgId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserIdentity> { user1 });

        SeedOrgInvitation(_testOrgId, "user@test.com", InvitationStatus.Accepted);

        var service = CreateService();

        // Act
        var result = await service.GetOrganizationUsersAsync(_testOrgId);

        // Assert
        result.Users.Should().HaveCount(1);
        result.Users[0].InvitationStatus.Should().Be("Accepted");
    }

    // ══════════════════════════════════════════════════════════════
    // T014 - AdminVerifyEmailAsync
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminVerifyEmailAsync_UnverifiedUser_SetsVerifiedAndReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        var platformUser = SeedPlatformUser(platformUserId, "unverified@test.com",
            emailVerified: false, verificationToken: "some-token-abc");

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = _testOrgId,
            PlatformUserId = platformUserId,
            Email = "unverified@test.com",
            DisplayName = "Unverified User",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        var result = await service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);

        // Assert
        result.Should().BeTrue();

        // Verify PlatformUser was updated
        var updatedPlatformUser = await _dbContext.PlatformUsers.FindAsync(platformUserId);
        updatedPlatformUser!.EmailVerified.Should().BeTrue();
        updatedPlatformUser.EmailVerifiedAt.Should().NotBeNull();
        updatedPlatformUser.EmailVerifiedAt!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_AlreadyVerified_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        SeedPlatformUser(platformUserId, "already-verified@test.com", emailVerified: true);

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = _testOrgId,
            PlatformUserId = platformUserId,
            Email = "already-verified@test.com",
            DisplayName = "Already Verified",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        var result = await service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_UserNotInOrg_ThrowsKeyNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var differentOrgId = Guid.NewGuid();

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = differentOrgId, // Different org
            PlatformUserId = Guid.NewGuid(),
            Email = "wrong-org@test.com",
            DisplayName = "Wrong Org User",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act & Assert
        var act = () => service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{userId}*{_testOrgId}*");
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity?)null);

        var service = CreateService();

        // Act & Assert
        var act = () => service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_Success_RecordsAuditEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        SeedPlatformUser(platformUserId, "audit-test@test.com", emailVerified: false);

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = _testOrgId,
            PlatformUserId = platformUserId,
            Email = "audit-test@test.com",
            DisplayName = "Audit Test",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        await service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);

        // Assert — audit log should contain the EmailVerifiedByAdmin event
        var auditEntries = _dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == _testOrgId &&
                        a.EventType == AuditEventType.EmailVerifiedByAdmin)
            .ToList();

        auditEntries.Should().HaveCount(1);
        auditEntries[0].IdentityId.Should().Be(adminUserId);
        auditEntries[0].Success.Should().BeTrue();
        auditEntries[0].Details.Should().ContainKey("targetUserId");
        auditEntries[0].Details!["targetUserId"].Should().Be(userId.ToString());
        auditEntries[0].Details.Should().ContainKey("targetEmail");
        auditEntries[0].Details!["targetEmail"].Should().Be("audit-test@test.com");
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_Success_ClearsVerificationToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        SeedPlatformUser(platformUserId, "token-clear@test.com",
            emailVerified: false, verificationToken: "pending-verification-token");

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = _testOrgId,
            PlatformUserId = platformUserId,
            Email = "token-clear@test.com",
            DisplayName = "Token Clear",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        await service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);

        // Assert — verification token and expiry should be cleared
        var updatedPlatformUser = await _dbContext.PlatformUsers.FindAsync(platformUserId);
        updatedPlatformUser!.VerificationToken.Should().BeNull();
        updatedPlatformUser.VerificationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task AdminVerifyEmailAsync_AlreadyVerified_DoesNotRecordAuditEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();

        SeedPlatformUser(platformUserId, "already-verified2@test.com", emailVerified: true);

        var user = new UserIdentity
        {
            Id = userId,
            OrganizationId = _testOrgId,
            PlatformUserId = platformUserId,
            Email = "already-verified2@test.com",
            DisplayName = "Already Verified 2",
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var service = CreateService();

        // Act
        await service.AdminVerifyEmailAsync(_testOrgId, userId, adminUserId);

        // Assert — no audit event should be written for a no-op
        var auditEntries = _dbContext.AuditLogEntries
            .Where(a => a.EventType == AuditEventType.EmailVerifiedByAdmin)
            .ToList();

        auditEntries.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════
    // Task #14 — AddUserToOrganizationAsync fires the membership-inbox writer
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task AddUserToOrganizationAsync_ExistingPlatformUser_FiresMembershipInboxWriter()
    {
        // Arrange
        var platformUser = SeedPlatformUser(Guid.NewGuid(), "newmember@test.com", emailVerified: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Test Organization", Subdomain = "testorg" });

        _identityRepoMock.Setup(r => r.CreateUserAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity u, CancellationToken _) => u);

        var service = CreateService();
        var request = new Sorcha.Tenant.Service.Models.Dtos.AddUserToOrganizationRequest
        {
            Email = "newmember@test.com",
            DisplayName = "New Member",
            ExternalIdpSubject = "ext-sub-123",
            Roles = [UserRole.Consumer]
        };

        // Act
        await service.AddUserToOrganizationAsync(_testOrgId, request);

        // Assert — inbox writer fired for the linked platform user
        _membershipInboxMock.Verify(
            w => w.WriteOrgMembershipAddedAsync(
                platformUser.Id,
                _testOrgId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddUserToOrganizationAsync_NoLinkedPlatformUser_DoesNotFireInboxWriter()
    {
        // Arrange — adding an identity for an email that has no PlatformUser yet
        // (pre-registration). No membership row is created, so no inbox entry should fire.
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Test Organization", Subdomain = "testorg" });

        _identityRepoMock.Setup(r => r.CreateUserAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity u, CancellationToken _) => u);

        var service = CreateService();
        var request = new Sorcha.Tenant.Service.Models.Dtos.AddUserToOrganizationRequest
        {
            Email = "noone@test.com",
            DisplayName = "Nobody",
            ExternalIdpSubject = "ext-sub-none",
            Roles = [UserRole.Consumer]
        };

        // Act
        await service.AddUserToOrganizationAsync(_testOrgId, request);

        // Assert
        _membershipInboxMock.Verify(
            w => w.WriteOrgMembershipAddedAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ══════════════════════════════════════════════════════════════
    // ProvisionOrgUserAsync — org-scoped password user (spec 136 follow-up)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProvisionOrgUserAsync_NewEmail_FlagOn_Verified_CreatesSingleOrgUser()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" });
        _identityRepoMock.Setup(r => r.CreateUserAsync(It.IsAny<UserIdentity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentity u, CancellationToken _) => u);

        var service = CreateService(allowAdminVerifiedUserCreation: true);
        var request = new Sorcha.Tenant.Service.Models.Dtos.ProvisionOrgUserRequest
        {
            Email = "analyst@acme.test", DisplayName = "Acme Analyst",
            Password = "Dev_Pass_2025!", Roles = [UserRole.Auditor], EmailVerified = true
        };

        await service.ProvisionOrgUserAsync(_testOrgId, request);

        var pu = _dbContext.PlatformUsers.Single(u => u.Email == "analyst@acme.test");
        pu.EmailVerified.Should().BeTrue();
        pu.PasswordHash.Should().NotBeNullOrEmpty();
        var memberships = _dbContext.PlatformUserOrgMemberships.Where(m => m.PlatformUserId == pu.Id).ToList();
        memberships.Should().ContainSingle().Which.OrganizationId.Should().Be(_testOrgId);
    }

    [Fact]
    public async Task ProvisionOrgUserAsync_VerifiedRequested_FlagOff_Throws_NoUserCreated()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" });

        var service = CreateService(allowAdminVerifiedUserCreation: false);
        var request = new Sorcha.Tenant.Service.Models.Dtos.ProvisionOrgUserRequest
        {
            Email = "blocked@acme.test", DisplayName = "Blocked",
            Password = "Dev_Pass_2025!", EmailVerified = true
        };

        var act = () => service.ProvisionOrgUserAsync(_testOrgId, request);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _dbContext.PlatformUsers.Any(u => u.Email == "blocked@acme.test").Should().BeFalse();
    }

    [Fact]
    public async Task ProvisionOrgUserAsync_ExistingPlatformUserEmail_Throws()
    {
        SeedPlatformUser(Guid.NewGuid(), "dupe@acme.test", emailVerified: true);
        _orgRepoMock.Setup(r => r.GetByIdAsync(_testOrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _testOrgId, Name = "Acme", Subdomain = "acme" });

        var service = CreateService(allowAdminVerifiedUserCreation: true);
        var request = new Sorcha.Tenant.Service.Models.Dtos.ProvisionOrgUserRequest
        {
            Email = "dupe@acme.test", DisplayName = "Dupe", Password = "Dev_Pass_2025!"
        };

        var act = () => service.ProvisionOrgUserAsync(_testOrgId, request);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "provision creates a NEW org-scoped user; an existing platform user must use AddUserToOrganization");
    }
}
