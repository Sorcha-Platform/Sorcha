// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TransactionalEmailService"/>: the facade routes each
/// dispatch to the correct template, builds the view model correctly, and invokes
/// the sender with both HTML and text bodies.
/// </summary>
public class TransactionalEmailServiceTests
{
    private readonly Mock<IEmailTemplateRenderer> _renderer = new();
    private readonly Mock<IEmailBrandingResolver> _branding = new();
    private readonly Mock<IEmailSender> _sender = new();
    private readonly TransactionalEmailService _service;

    private static readonly EmailBranding FakeBranding =
        new("Sorcha", null, "#2563eb", null, "help@sorcha.dev");

    private static readonly EmailBranding FakeOrgBranding =
        new("Acme", "https://acme.example/logo.png", "#FF5722", "Verify with confidence", "help@sorcha.dev");

    public TransactionalEmailServiceTests()
    {
        _branding.Setup(b => b.GetDefault()).Returns(FakeBranding);
        _branding.Setup(b => b.GetForOrganization(It.IsAny<Organization>())).Returns(FakeOrgBranding);

        _renderer
            .Setup(r => r.Render(It.IsAny<string>(), It.IsAny<object>()))
            .Returns(("<html>x</html>", "x"));

        var settings = Options.Create(new EmailSettings { BaseUrl = "https://sorcha.dev" });
        _service = new TransactionalEmailService(
            _renderer.Object, _branding.Object, _sender.Object, settings);
    }

    [Fact]
    public async Task SendVerificationAsync_RendersVerifyTemplateAndSendsMultipart()
    {
        var dispatch = new VerifyEmailDispatch(
            ToEmail: "user@example.com",
            DisplayName: "Stuart Fraser",
            VerifyUrl: "https://sorcha.dev/auth/verify-email?token=T",
            ExpiresInHours: 24);

        await _service.SendVerificationAsync(dispatch);

        _renderer.Verify(r => r.Render("verify", It.Is<VerifyEmailTemplateModel>(m =>
            m.DisplayName == "Stuart Fraser" &&
            m.VerifyUrl == "https://sorcha.dev/auth/verify-email?token=T" &&
            m.ExpiresInHours == 24 &&
            m.Branding == FakeBranding)), Times.Once);

        _sender.Verify(s => s.SendAsync(
            "user@example.com",
            It.IsAny<string>(),
            "<html>x</html>",
            "x",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendInvitationAsync_UsesOrgBrandingAndRendersInviteTemplate()
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Acme Verification Co.",
        };

        var dispatch = new InviteEmailDispatch(
            ToEmail: "invitee@example.com",
            InviterName: "Admin User",
            InvitingOrganization: org,
            RoleDisplayName: "Designer",
            AcceptUrl: "https://sorcha.dev/invitations/accept?token=T",
            ExpiresInDays: 7);

        await _service.SendInvitationAsync(dispatch);

        _branding.Verify(b => b.GetForOrganization(org), Times.Once);
        _renderer.Verify(r => r.Render("invite", It.Is<InviteEmailTemplateModel>(m =>
            m.InviterName == "Admin User" &&
            m.OrganizationName == "Acme Verification Co." &&
            m.RoleDisplayName == "Designer" &&
            m.AcceptUrl == "https://sorcha.dev/invitations/accept?token=T" &&
            m.ExpiresInDays == 7 &&
            m.Branding == FakeOrgBranding)), Times.Once);

        _sender.Verify(s => s.SendAsync(
            "invitee@example.com",
            It.Is<string>(sub => sub.Contains("Acme Verification Co.")),
            "<html>x</html>",
            "x",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPasswordResetAsync_RendersResetTemplateAndSendsMultipart()
    {
        var dispatch = new ResetPasswordDispatch(
            ToEmail: "user@example.com",
            DisplayName: "Stuart Fraser",
            ResetUrl: "https://sorcha.dev/auth/reset-password?token=T",
            ExpiresInMinutes: 60);

        await _service.SendPasswordResetAsync(dispatch);

        _renderer.Verify(r => r.Render("reset", It.Is<ResetPasswordTemplateModel>(m =>
            m.DisplayName == "Stuart Fraser" &&
            m.ResetUrl == "https://sorcha.dev/auth/reset-password?token=T" &&
            m.ExpiresInMinutes == 60 &&
            m.Branding == FakeBranding)), Times.Once);

        _sender.Verify(s => s.SendAsync(
            "user@example.com",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWelcomeAsync_PublicVariant_RendersWelcomePublicTemplate()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "public@example.com",
            DisplayName = "Stuart Fraser",
            EmailVerified = true,
        };
        var ctx = new WelcomeDispatchContext(user, WelcomeVariant.Public, InvitingOrganization: null, InvitedRole: null);

        await _service.SendWelcomeAsync(ctx);

        _renderer.Verify(r => r.Render("welcome-public", It.Is<WelcomePublicTemplateModel>(m =>
            m.DisplayName == "Stuart Fraser" &&
            m.Branding == FakeBranding &&
            m.DashboardUrl.StartsWith("https://sorcha.dev"))), Times.Once);

        _sender.Verify(s => s.SendAsync(
            "public@example.com",
            It.Is<string>(sub => sub.Contains("Welcome")),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWelcomeAsync_InvitedVariant_RendersWelcomeInvitedTemplateWithOrg()
    {
        var orgId = Guid.NewGuid();
        var org = new Organization { Id = orgId, Name = "Acme" };
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "invited@example.com",
            DisplayName = "Stuart Fraser",
            EmailVerified = true,
        };
        // Role now comes explicitly on the context — no more mutating the tracked
        // OrgMemberships navigation (reviewer M-2).
        var ctx = new WelcomeDispatchContext(user, WelcomeVariant.Invited, org, InvitedRole: "Designer");

        await _service.SendWelcomeAsync(ctx);

        _renderer.Verify(r => r.Render("welcome-invited", It.Is<WelcomeInvitedTemplateModel>(m =>
            m.DisplayName == "Stuart Fraser" &&
            m.OrganizationName == "Acme" &&
            m.RoleDisplayName == "Designer" &&
            m.Branding == FakeOrgBranding)), Times.Once);

        _sender.Verify(s => s.SendAsync(
            "invited@example.com",
            It.Is<string>(sub => sub.Contains("Acme")),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWelcomeAsync_InvitedWithoutOrg_Throws()
    {
        var user = new PlatformUser { Email = "x@x.com", DisplayName = "x", EmailVerified = true };
        var ctx = new WelcomeDispatchContext(user, WelcomeVariant.Invited, InvitingOrganization: null, InvitedRole: "Designer");

        Func<Task> act = () => _service.SendWelcomeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inviting organisation*");
    }

    [Fact]
    public async Task SendInvitationAsync_OrgNameExceedsSubjectCap_IsEllipsisedInSubject()
    {
        // Defence against admin-set org names being used as a phishing surface in
        // inbox subject-line previews. Longer than 60 chars → visible ellipsis.
        var longName = new string('A', 100); // 100 × "A"
        var org = new Organization { Id = Guid.NewGuid(), Name = longName };

        var dispatch = new InviteEmailDispatch(
            ToEmail: "invitee@example.com",
            InviterName: "Admin",
            InvitingOrganization: org,
            RoleDisplayName: "Designer",
            AcceptUrl: "https://sorcha.dev/invitations/accept?token=T",
            ExpiresInDays: 7);

        await _service.SendInvitationAsync(dispatch);

        _sender.Verify(s => s.SendAsync(
            "invitee@example.com",
            It.Is<string>(sub =>
                sub.Contains("…") &&
                sub.Length < "You're invited to join ".Length + longName.Length),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendInvitationAsync_OrgNameWithinCap_RendersUntruncated()
    {
        var shortName = "Acme";
        var org = new Organization { Id = Guid.NewGuid(), Name = shortName };

        var dispatch = new InviteEmailDispatch(
            ToEmail: "invitee@example.com",
            InviterName: "Admin",
            InvitingOrganization: org,
            RoleDisplayName: "Designer",
            AcceptUrl: "https://sorcha.dev/invitations/accept?token=T",
            ExpiresInDays: 7);

        await _service.SendInvitationAsync(dispatch);

        _sender.Verify(s => s.SendAsync(
            "invitee@example.com",
            "You're invited to join Acme",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendWelcomeAsync_InvitedWithoutRole_Throws()
    {
        var user = new PlatformUser { Email = "x@x.com", DisplayName = "x", EmailVerified = true };
        var org = new Organization { Id = Guid.NewGuid(), Name = "Acme" };
        var ctx = new WelcomeDispatchContext(user, WelcomeVariant.Invited, org, InvitedRole: null);

        Func<Task> act = () => _service.SendWelcomeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*InvitedRole*");
    }
}
