// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Azure Communication Services email sender using the REST API.
/// Authenticates with the ACS connection string — no Entra ID app needed.
/// Emits multipart HTML + plaintext content.
/// </summary>
public class AcsEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly EmailClient _client;
    private readonly ILogger<AcsEmailSender> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AcsEmailSender"/>.
    /// </summary>
    public AcsEmailSender(IOptions<EmailSettings> settings, ILogger<AcsEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.AcsConnectionString))
            throw new InvalidOperationException("Email:AcsConnectionString is required for ACS email sender");

        _client = new EmailClient(_settings.AcsConnectionString);
    }

    /// <inheritdoc />
    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new EmailMessage(
                senderAddress: _settings.FromAddress,
                recipientAddress: to,
                content: new EmailContent(subject)
                {
                    Html = htmlBody,
                    PlainText = textBody,
                });

            var result = await _client.SendAsync(
                Azure.WaitUntil.Completed,
                message,
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
}
