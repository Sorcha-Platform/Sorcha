// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class InvitationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly InvitationRepository _invitationRepository;
    private readonly Mock<IIdentityRepository> _identityRepoMock;
    private readonly Mock<IOrganizationRepository> _orgRepoMock;
    private readonly Mock<ITransactionalEmailService> _transactionalMock;
    private readonly InvitationService _service;

    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    public InvitationServiceTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"InvitationTests_{Guid.NewGuid():N}")
            .Options;

        _dbContext = new TenantDbContext(options);
        _invitationRepository = new InvitationRepository(_dbContext);
        _identityRepoMock = new Mock<IIdentityRepository>();
        _orgRepoMock = new Mock<IOrganizationRepository>();
        _transactionalMock = new Mock<ITransactionalEmailService>();

        _identityRepoMock
            .Setup(r => r.GetUserByIdAsync(_adminUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentity
            {
                Id = _adminUserId,
                DisplayName = "Admin User",
                Email = "admin@test.com",
                OrganizationId = _orgId
            });

        _orgRepoMock
            .Setup(r => r.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization
            {
                Id = _orgId,
                Name = "Acme Verification Co.",
                Subdomain = "acme",
            });

        _transactionalMock
            .Setup(t => t.SendInvitationAsync(It.IsAny<InviteEmailDispatch>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var emailSettings = Options.Create(new EmailSettings { BaseUrl = "https://sorcha.io" });

        _service = new InvitationService(
            _invitationRepository,
            _identityRepoMock.Object,
            _orgRepoMock.Object,
            _transactionalMock.Object,
            emailSettings,
            _dbContext,
            Mock.Of<ILogger<InvitationService>>());
    }

    [Fact]
    public async Task CreateInvitationAsync_ValidRequest_CreatesInvitationAndSendsEmail()
    {
        var request = new CreateOrgInvitationRequest
        {
            Email = "user@example.com",
            Role = UserRole.Designer,
            ExpiryDays = 7
        };

        var result = await _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        result.Email.Should().Be("user@example.com");
        result.AssignedRole.Should().Be("Designer");
        result.Status.Should().Be("Pending");
        result.InvitedBy.Should().Be("Admin User");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        _transactionalMock.Verify(t => t.SendInvitationAsync(
            It.Is<InviteEmailDispatch>(d =>
                d.ToEmail == "user@example.com" &&
                d.InviterName == "Admin User" &&
                d.InvitingOrganization.Id == _orgId &&
                d.RoleDisplayName == "Designer" &&
                d.AcceptUrl.Contains("/invitations/accept?token=") &&
                d.ExpiresInDays == 7),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateInvitationAsync_GeneratesUrlSafe32ByteToken()
    {
        var request = new CreateOrgInvitationRequest { Email = "token@test.com" };

        var result = await _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        // Token should be URL-safe base64 (no +, /, or =)
        var invitation = await _dbContext.OrgInvitations.FirstAsync();
        invitation.Token.Should().NotContain("+");
        invitation.Token.Should().NotContain("/");
        invitation.Token.Should().NotContain("=");
        invitation.Token.Length.Should().BeGreaterThanOrEqualTo(40); // 32 bytes → ~43 base64 chars
    }

    [Fact]
    public async Task CreateInvitationAsync_7DayExpiry_SetsCorrectExpiresAt()
    {
        var request = new CreateOrgInvitationRequest
        {
            Email = "expiry@test.com",
            ExpiryDays = 14
        };

        var result = await _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        result.ExpiresAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateInvitationAsync_SystemAdminRole_ThrowsArgumentException()
    {
        var request = new CreateOrgInvitationRequest
        {
            Email = "sysadmin@test.com",
            Role = UserRole.SystemAdmin
        };

        var act = () => _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*SystemAdmin*");
    }

    [Fact]
    public async Task CreateInvitationAsync_DuplicateActiveInvitation_ThrowsInvalidOperationException()
    {
        var request = new CreateOrgInvitationRequest { Email = "dupe@test.com" };
        await _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        var act = () => _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateInvitationAsync_WritesAuditEvent()
    {
        var request = new CreateOrgInvitationRequest { Email = "audit@test.com", Role = UserRole.Auditor };

        await _service.CreateInvitationAsync(_orgId, request, _adminUserId);

        var auditEntry = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EventType == AuditEventType.InvitationSent);
        auditEntry.Should().NotBeNull();
        auditEntry!.OrganizationId.Should().Be(_orgId);
        auditEntry.Details!["email"].ToString().Should().Be("audit@test.com");
    }

    [Fact]
    public async Task ListInvitationsAsync_ReturnsAllInvitations()
    {
        await _service.CreateInvitationAsync(_orgId, new CreateOrgInvitationRequest { Email = "a@test.com" }, _adminUserId);
        await _service.CreateInvitationAsync(_orgId, new CreateOrgInvitationRequest { Email = "b@test.com" }, _adminUserId);

        var result = await _service.ListInvitationsAsync(_orgId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListInvitationsAsync_ExpiredInvitation_TransitionsToExpiredStatus()
    {
        // Seed an invitation that's already expired
        var expired = new OrgInvitation
        {
            OrganizationId = _orgId,
            Email = "expired@test.com",
            Token = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            Status = InvitationStatus.Pending,
            InvitedByUserId = _adminUserId
        };
        _dbContext.OrgInvitations.Add(expired);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListInvitationsAsync(_orgId);

        result.Should().ContainSingle(i => i.Email == "expired@test.com");
        result.First(i => i.Email == "expired@test.com").Status.Should().Be("Expired");
    }

    [Fact]
    public async Task RevokeInvitationAsync_PendingInvitation_SetsRevokedStatus()
    {
        var created = await _service.CreateInvitationAsync(
            _orgId, new CreateOrgInvitationRequest { Email = "revoke@test.com" }, _adminUserId);

        var success = await _service.RevokeInvitationAsync(_orgId, created.Id, _adminUserId);

        success.Should().BeTrue();
        var invitation = await _dbContext.OrgInvitations.FirstAsync(i => i.Id == created.Id);
        invitation.Status.Should().Be(InvitationStatus.Revoked);
        invitation.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeInvitationAsync_NonExistentInvitation_ReturnsFalse()
    {
        var result = await _service.RevokeInvitationAsync(_orgId, Guid.NewGuid(), _adminUserId);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeInvitationAsync_AlreadyRevoked_ThrowsInvalidOperationException()
    {
        var created = await _service.CreateInvitationAsync(
            _orgId, new CreateOrgInvitationRequest { Email = "double-revoke@test.com" }, _adminUserId);
        await _service.RevokeInvitationAsync(_orgId, created.Id, _adminUserId);

        var act = () => _service.RevokeInvitationAsync(_orgId, created.Id, _adminUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Revoked*");
    }

    [Fact]
    public async Task RevokeInvitationAsync_WritesAuditEvent()
    {
        var created = await _service.CreateInvitationAsync(
            _orgId, new CreateOrgInvitationRequest { Email = "audit-revoke@test.com" }, _adminUserId);

        await _service.RevokeInvitationAsync(_orgId, created.Id, _adminUserId);

        var auditEntry = await _dbContext.AuditLogEntries
            .FirstOrDefaultAsync(a => a.EventType == AuditEventType.InvitationRevoked);
        auditEntry.Should().NotBeNull();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
