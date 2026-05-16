// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;

namespace Sorcha.Tenant.Service.Services;

/// <inheritdoc />
public sealed class TransactionalEmailService : ITransactionalEmailService
{
    private const string VerifySubject = "Confirm your email";
    private const string PasswordResetSubject = "Reset your password";
    private const string PairingResumptionSubject = "Set up your Sorcha wallet";

    /// <summary>
    /// Maximum number of characters of an organisation name that may appear in an email
    /// subject line. Defensive mitigation against admin-set org names being used as a
    /// phishing surface through subject previews (e.g. inbox list views). Org-name
    /// validation at creation time is the stronger fix and tracked as a follow-up;
    /// until that lands, longer names are ellipsised here so a pathological subject
    /// ("Urgent: verify your account within 24 hours or…") is visibly truncated rather
    /// than rendered in full.
    /// </summary>
    private const int MaxOrgNameInSubjectChars = 60;

    private readonly IEmailTemplateRenderer _renderer;
    private readonly IEmailBrandingResolver _branding;
    private readonly IEmailSender _sender;
    private readonly EmailSettings _settings;

    /// <summary>
    /// Initializes a new instance of <see cref="TransactionalEmailService"/>.
    /// </summary>
    public TransactionalEmailService(
        IEmailTemplateRenderer renderer,
        IEmailBrandingResolver branding,
        IEmailSender sender,
        IOptions<EmailSettings> settings)
    {
        _renderer = renderer;
        _branding = branding;
        _sender = sender;
        _settings = settings.Value;
    }

    /// <inheritdoc />
    public async Task SendVerificationAsync(VerifyEmailDispatch dispatch, CancellationToken ct = default)
    {
        var model = new VerifyEmailTemplateModel(
            DisplayName: dispatch.DisplayName,
            VerifyUrl: dispatch.VerifyUrl,
            ExpiresInHours: dispatch.ExpiresInHours,
            Branding: _branding.GetDefault());

        var (html, text) = _renderer.Render("verify", model);
        await _sender.SendAsync(dispatch.ToEmail, VerifySubject, html, text, ct);
    }

    /// <inheritdoc />
    public async Task SendInvitationAsync(InviteEmailDispatch dispatch, CancellationToken ct = default)
    {
        var branding = _branding.GetForOrganization(dispatch.InvitingOrganization);
        var model = new InviteEmailTemplateModel(
            InviterName: dispatch.InviterName,
            OrganizationName: dispatch.InvitingOrganization.Name,
            RoleDisplayName: dispatch.RoleDisplayName,
            AcceptUrl: dispatch.AcceptUrl,
            ExpiresInDays: dispatch.ExpiresInDays,
            Branding: branding);

        var (html, text) = _renderer.Render("invite", model);
        var subject = $"You're invited to join {TruncateForSubject(dispatch.InvitingOrganization.Name)}";
        await _sender.SendAsync(dispatch.ToEmail, subject, html, text, ct);
    }

    /// <inheritdoc />
    public async Task SendPasswordResetAsync(ResetPasswordDispatch dispatch, CancellationToken ct = default)
    {
        var model = new ResetPasswordTemplateModel(
            DisplayName: dispatch.DisplayName,
            ResetUrl: dispatch.ResetUrl,
            ExpiresInMinutes: dispatch.ExpiresInMinutes,
            Branding: _branding.GetDefault());

        var (html, text) = _renderer.Render("reset", model);
        await _sender.SendAsync(dispatch.ToEmail, PasswordResetSubject, html, text, ct);
    }

    /// <inheritdoc />
    public Task SendWelcomeAsync(WelcomeDispatchContext context, CancellationToken ct = default)
    {
        return context.Variant switch
        {
            WelcomeVariant.Public => SendWelcomePublicAsync(context, ct),
            WelcomeVariant.Invited => SendWelcomeInvitedAsync(context, ct),
            _ => throw new InvalidOperationException(
                $"Unknown welcome variant: {context.Variant}"),
        };
    }

    private async Task SendWelcomePublicAsync(WelcomeDispatchContext context, CancellationToken ct)
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var model = new WelcomePublicTemplateModel(
            DisplayName: context.User.DisplayName,
            DashboardUrl: $"{baseUrl}/dashboard",
            BrowseRegistersUrl: $"{baseUrl}/registers",
            DemoWorkflowsUrl: $"{baseUrl}/blueprints",
            DocsUrl: "https://docs.sorcha.dev",
            Branding: _branding.GetDefault());

        var (html, text) = _renderer.Render("welcome-public", model);
        await _sender.SendAsync(context.User.Email, "Welcome to Sorcha", html, text, ct);
    }

    private async Task SendWelcomeInvitedAsync(WelcomeDispatchContext context, CancellationToken ct)
    {
        if (context.InvitingOrganization is null)
            throw new InvalidOperationException(
                "Invited welcome requires an inviting organisation in the context.");
        if (string.IsNullOrWhiteSpace(context.InvitedRole))
            throw new InvalidOperationException(
                "Invited welcome requires an InvitedRole in the context — " +
                "WelcomeEmailDispatcher populates this from the earliest standard-org membership.");

        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var branding = _branding.GetForOrganization(context.InvitingOrganization);

        var model = new WelcomeInvitedTemplateModel(
            DisplayName: context.User.DisplayName,
            OrganizationName: context.InvitingOrganization.Name,
            RoleDisplayName: context.InvitedRole,
            DashboardUrl: $"{baseUrl}/dashboard",
            Branding: branding);

        var (html, text) = _renderer.Render("welcome-invited", model);
        var subject = $"You've joined {TruncateForSubject(context.InvitingOrganization.Name)}";
        await _sender.SendAsync(context.User.Email, subject, html, text, ct);
    }

    /// <inheritdoc />
    public async Task SendPairingResumptionAsync(PairingResumptionDispatch dispatch, CancellationToken ct = default)
    {
        var model = new PairingResumptionTemplateModel(
            DisplayName: dispatch.DisplayName,
            ResumptionUrl: dispatch.ResumptionUrl,
            ExpiresInHours: dispatch.ExpiresInHours,
            Branding: _branding.GetDefault());

        var (html, text) = _renderer.Render("pairing-resumption", model);
        await _sender.SendAsync(dispatch.ToEmail, PairingResumptionSubject, html, text, ct);
    }

    /// <summary>
    /// Caps an organisation name at <see cref="MaxOrgNameInSubjectChars"/> characters
    /// with a visible ellipsis when longer. Keeps email subjects readable and bounds
    /// the phishing surface of admin-set org names until platform-wide org-name
    /// validation is implemented as a follow-up.
    /// </summary>
    private static string TruncateForSubject(string orgName)
    {
        if (string.IsNullOrEmpty(orgName) || orgName.Length <= MaxOrgNameInSubjectChars)
            return orgName;
        return orgName.Substring(0, MaxOrgNameInSubjectChars) + "…";
    }
}
