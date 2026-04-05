// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Azure Communication Services email sender using the REST API.
/// Authenticates with the ACS connection string — no Entra ID app needed.
/// </summary>
public class AcsEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly EmailClient _client;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(IOptions<EmailSettings> settings, ILogger<AcsEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.AcsConnectionString))
            throw new InvalidOperationException("Email:AcsConnectionString is required for ACS email sender");

        _client = new EmailClient(_settings.AcsConnectionString);
    }

    /// <inheritdoc />
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.SendAsync(
                Azure.WaitUntil.Completed,
                _settings.FromAddress,
                to,
                subject,
                htmlBody,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Email sent to {Recipient}: {Subject} (ACS OperationId: {OpId})",
                to, subject, result.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email: {Subject}", subject);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SendVerificationEmailAsync(
        string to, string verificationToken, string orgSubdomain, CancellationToken cancellationToken = default)
    {
        var verifyUrl = $"{_settings.BaseUrl}/auth/verify-email?token={Uri.EscapeDataString(verificationToken)}";

        var htmlBody = $"""
            <h2>Verify Your Email Address</h2>
            <p>Welcome to Sorcha! Please verify your email address by clicking the link below:</p>
            <p><a href="{verifyUrl}" style="padding: 12px 24px; background-color: #6366f1; color: white; text-decoration: none; border-radius: 6px;">Verify Email</a></p>
            <p>Or copy this link: <code>{verifyUrl}</code></p>
            <p>This link expires in 24 hours.</p>
            <p style="color: #666; font-size: 12px;">If you didn't create an account, you can safely ignore this email.</p>
            """;

        await SendAsync(to, "Verify your email address — Sorcha", htmlBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendInvitationEmailAsync(
        string to, string invitationToken, string organizationName, string roleName, CancellationToken cancellationToken = default)
    {
        var acceptUrl = $"{_settings.BaseUrl}/invitations/accept?token={Uri.EscapeDataString(invitationToken)}";

        var htmlBody = $"""
            <h2>You're Invited to {organizationName}</h2>
            <p>You've been invited to join <strong>{organizationName}</strong> on Sorcha as a <strong>{roleName}</strong>.</p>
            <p><a href="{acceptUrl}" style="padding: 12px 24px; background-color: #6366f1; color: white; text-decoration: none; border-radius: 6px;">Accept Invitation</a></p>
            <p>Or copy this link: <code>{acceptUrl}</code></p>
            <p>This invitation expires in 7 days.</p>
            <p style="color: #666; font-size: 12px;">If you weren't expecting this invitation, you can safely ignore this email.</p>
            """;

        await SendAsync(to, $"Invitation to join {organizationName} — Sorcha", htmlBody, cancellationToken);
    }
}
