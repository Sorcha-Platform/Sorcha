// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Tenant.Service.Configuration;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Covers the invitation resend endpoint added for issue #1256.
/// <para>
/// The admin UI used to show a Resend button whose client method POSTed to a route the Tenant Service
/// had never implemented: the call 404'd, the page discarded the returned <c>false</c>, and the
/// operator saw "Invitation resent" every time. DRIFT-004 removed the button rather than faking
/// success, and <c>OrgInvitationWireContractTests</c> now fails the build if a client operation has no
/// endpoint behind it — so the endpoint had to exist before the button could come back.
/// </para>
/// </summary>
public sealed class InvitationResendTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly Mock<ITransactionalEmailService> _transactional = new();
    private readonly Mock<IInvitationRepository> _invitations = new();
    private readonly Mock<IOrganizationRepository> _orgs = new();
    private readonly Mock<IIdentityRepository> _identities = new();
    private readonly InvitationService _sut;

    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdminId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public InvitationResendTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _orgs.Setup(o => o.GetByIdAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = OrgId, Name = "Acme" });
        _identities.Setup(i => i.GetUserByIdAsync(AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentity { Id = AdminId, DisplayName = "Ada Admin" });

        _sut = new InvitationService(
            _invitations.Object,
            _identities.Object,
            _orgs.Object,
            _transactional.Object,
            Options.Create(new EmailSettings { BaseUrl = "https://n1.sorcha.dev" }),
            _db,
            NullLogger<InvitationService>.Instance);
    }

    private OrgInvitation Pending(string token = "original-token", int windowDays = 7)
    {
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var invitation = new OrgInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrgId,
            Email = "invitee@example.test",
            AssignedRole = UserRole.Consumer,
            Token = token,
            CreatedAt = created,
            ExpiresAt = created.AddDays(windowDays),
            InvitedByUserId = AdminId,
            Status = InvitationStatus.Pending,
        };
        _invitations.Setup(r => r.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);
        return invitation;
    }

    /// <summary>The happy path: a fresh email goes out through the templated facade.</summary>
    [Fact]
    public async Task Resend_PendingInvitation_SendsTheEmailAgain()
    {
        var invitation = Pending();

        var result = await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        result.Should().BeTrue();
        _transactional.Verify(t => t.SendInvitationAsync(
            It.IsAny<InviteEmailDispatch>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Resend ROTATES the token. That is the documented decision: an operator resending is usually
    /// recovering from a mail that never arrived, and rotating bounds how long a leaked token lives.
    /// </summary>
    [Fact]
    public async Task Resend_RotatesTheToken()
    {
        var invitation = Pending(token: "original-token");

        await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        invitation.Token.Should().NotBe("original-token",
            "a resent invitation must not keep a token that may already have leaked (#1256)");
        invitation.Token.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The emailed link must carry the NEW token, or the mail is useless.</summary>
    [Fact]
    public async Task Resend_EmailsTheRotatedToken_NotTheOldOne()
    {
        var invitation = Pending(token: "original-token");
        InviteEmailDispatch? dispatched = null;
        _transactional
            .Setup(t => t.SendInvitationAsync(It.IsAny<InviteEmailDispatch>(), It.IsAny<CancellationToken>()))
            .Callback<InviteEmailDispatch, CancellationToken>((d, _) => dispatched = d);

        await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        dispatched.Should().NotBeNull();
        dispatched!.AcceptUrl.Should().NotContain("original-token");
        dispatched.AcceptUrl.Should().Contain(Uri.EscapeDataString(invitation.Token));
    }

    /// <summary>
    /// Expiry is reset, or the resent link is dead on arrival for an invitation already near expiry —
    /// which is exactly when an operator reaches for Resend.
    /// </summary>
    [Fact]
    public async Task Resend_ResetsTheExpiryWindow()
    {
        var invitation = Pending(windowDays: 7);
        var before = invitation.ExpiresAt;

        await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        invitation.ExpiresAt.Should().BeAfter(before);
        invitation.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The ORIGINAL validity window is preserved, so a deliberately short-lived invitation does not
    /// silently become a long-lived one just because someone resent it.
    /// </summary>
    [Fact]
    public async Task Resend_PreservesTheOriginalValidityWindow_NotADefault()
    {
        var invitation = Pending(windowDays: 2);

        await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        var newWindow = invitation.ExpiresAt - DateTimeOffset.UtcNow;
        newWindow.TotalDays.Should().BeApproximately(2, 0.2,
            "a 2-day invitation must stay a 2-day invitation on resend, not inherit a 7-day default");
    }

    [Theory]
    [InlineData(InvitationStatus.Accepted)]
    [InlineData(InvitationStatus.Revoked)]
    [InlineData(InvitationStatus.Expired)]
    public async Task Resend_NonPendingInvitation_Throws_AndSendsNothing(InvitationStatus status)
    {
        var invitation = Pending();
        invitation.Status = status;

        var act = async () => await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _transactional.Verify(t => t.SendInvitationAsync(
            It.IsAny<InviteEmailDispatch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>An unknown invitation is false, which the endpoint maps to 404.</summary>
    [Fact]
    public async Task Resend_UnknownInvitation_ReturnsFalse()
    {
        _invitations.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrgInvitation?)null);

        (await _sut.ResendInvitationAsync(OrgId, Guid.NewGuid(), AdminId)).Should().BeFalse();
    }

    /// <summary>
    /// Cross-org isolation: an invitation belonging to another organisation must be indistinguishable
    /// from one that does not exist, so this endpoint cannot be used to probe for them.
    /// </summary>
    [Fact]
    public async Task Resend_InvitationFromAnotherOrg_ReturnsFalse_AndSendsNothing()
    {
        var invitation = Pending();
        invitation.OrganizationId = Guid.NewGuid();

        (await _sut.ResendInvitationAsync(OrgId, invitation.Id, AdminId)).Should().BeFalse();
        _transactional.Verify(t => t.SendInvitationAsync(
            It.IsAny<InviteEmailDispatch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
