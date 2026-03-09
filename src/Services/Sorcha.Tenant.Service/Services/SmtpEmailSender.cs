// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// SMTP-based email sender using MailKit.
/// Sends verification emails, invitation emails, and general-purpose messages.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SmtpEmailSender"/>.
    /// </summary>
    public SmtpEmailSender(IOptions<EmailSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        await SendMessageAsync(message, cancellationToken);
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

    private async Task SendMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new SmtpClient();

            var secureSocketOptions = _settings.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_settings.SmtpHost, _settings.SmtpPort, secureSocketOptions, cancellationToken);

            if (!string.IsNullOrEmpty(_settings.SmtpUsername))
            {
                await client.AuthenticateAsync(_settings.SmtpUsername, _settings.SmtpPassword, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            _logger.LogInformation("Email sent to {Recipient}: {Subject}", message.To, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}: {Subject}", message.To, message.Subject);
            throw;
        }
    }
}
