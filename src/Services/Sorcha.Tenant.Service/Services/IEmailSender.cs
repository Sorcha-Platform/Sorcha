// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Abstraction for sending emails from the Tenant Service.
/// Implementations may use SMTP (MailKit), SendGrid, or other providers.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email to a single recipient.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">HTML body content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an email verification message with a tokenized link.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="verificationToken">URL-safe verification token.</param>
    /// <param name="orgSubdomain">Organization subdomain for link construction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendVerificationEmailAsync(string to, string verificationToken, string orgSubdomain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an organization invitation email.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="invitationToken">URL-safe invitation token.</param>
    /// <param name="organizationName">Name of the inviting organization.</param>
    /// <param name="roleName">Role the user will be assigned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendInvitationEmailAsync(string to, string invitationToken, string organizationName, string roleName, CancellationToken cancellationToken = default);
}

/// <summary>
/// SMTP configuration settings for MailKit email sending.
/// Bound from configuration section "Email".
/// </summary>
public class EmailSettings
{
    /// <summary>SMTP server hostname.</summary>
    public string SmtpHost { get; set; } = "localhost";

    /// <summary>SMTP server port (587 for STARTTLS, 465 for SSL).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>SMTP authentication username.</summary>
    public string? SmtpUsername { get; set; }

    /// <summary>SMTP authentication password.</summary>
    public string? SmtpPassword { get; set; }

    /// <summary>Whether to use SSL/TLS.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Sender email address (From header).</summary>
    public string FromAddress { get; set; } = "noreply@sorcha.io";

    /// <summary>Sender display name.</summary>
    public string FromName { get; set; } = "Sorcha Platform";

    /// <summary>Base URL for constructing verification and invitation links.</summary>
    public string BaseUrl { get; set; } = "https://sorcha.io";
}
