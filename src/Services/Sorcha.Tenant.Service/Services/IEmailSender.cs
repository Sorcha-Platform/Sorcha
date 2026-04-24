// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Backend-agnostic transactional email sender. Implementations ship a multipart
/// HTML + plaintext message to the configured SMTP or cloud backend.
/// </summary>
/// <remarks>
/// Application code should not call <see cref="IEmailSender"/> directly — use
/// <see cref="ITransactionalEmailService"/> instead, which renders templates and
/// resolves branding before handing off to this abstraction.
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Sends a multipart email (HTML body + plaintext alternative) to a single recipient.
    /// Both bodies MUST be provided — the plaintext alternative is mandatory (see FR-002
    /// in specs/112-email-sweep/spec.md).
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="htmlBody">HTML body content. Rendered by mail clients that support HTML.</param>
    /// <param name="textBody">Plaintext body content. Rendered by clients without HTML support.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration bound from the <c>Email</c> section in appsettings. Controls both the
/// backend selection (SMTP vs Azure Communication Services) and the Sorcha-default
/// branding applied to emails that are not per-org-branded.
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

    /// <summary>Base URL for constructing verification, invitation, and reset links.</summary>
    public string BaseUrl { get; set; } = "https://sorcha.io";

    /// <summary>
    /// Azure Communication Services connection string for REST API email sending.
    /// When set, the ACS sender is used instead of SMTP. Takes precedence over SMTP settings.
    /// </summary>
    public string? AcsConnectionString { get; set; }

    /// <summary>
    /// Sorcha platform default logo URL for email headers. Absolute https URL.
    /// Null → fall back to rendering the Sorcha name in the primary colour.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Sorcha platform default primary brand colour (hex, e.g. "#2563eb"). Applied to
    /// action buttons and footer links when no per-org colour is in effect.
    /// </summary>
    public string PrimaryColor { get; set; } = "#2563eb";

    /// <summary>
    /// Optional footer tagline rendered above the reply-to line in Sorcha-branded emails.
    /// </summary>
    public string? Tagline { get; set; }

    /// <summary>
    /// Reply-to address shown in the email footer so recipients can easily ask for help.
    /// </summary>
    public string ReplyTo { get; set; } = "help@sorcha.io";
}
