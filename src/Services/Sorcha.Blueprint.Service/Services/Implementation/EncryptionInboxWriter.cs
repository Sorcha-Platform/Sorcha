// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Inbox;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 169 — Blueprint Service inbox writer for register encryption lifecycle
/// events. Calls <see cref="IPlatformInboxClient"/> (cross-service HTTP) to write
/// into the durable user inbox owned by Tenant Service.
/// Fail-safe: inbox-write failures are logged as warnings and swallowed; they never
/// block the originating <c>EncryptionBackgroundService</c> operation.
/// </summary>
public interface IEncryptionInboxWriter
{
    /// <summary>Write an "encryption complete" inbox entry for the given platform user.</summary>
    Task WriteEncryptionCompleteAsync(Guid platformUserId, string registerTitle, CancellationToken ct = default);

    /// <summary>Write an "encryption failed" inbox entry for the given platform user.</summary>
    Task WriteEncryptionFailedAsync(Guid platformUserId, string registerTitle, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class EncryptionInboxWriter : IEncryptionInboxWriter
{
    private readonly IPlatformInboxClient _inboxClient;
    private readonly ILogger<EncryptionInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="EncryptionInboxWriter"/>.</summary>
    public EncryptionInboxWriter(
        IPlatformInboxClient inboxClient,
        ILogger<EncryptionInboxWriter> logger)
    {
        _inboxClient = inboxClient ?? throw new ArgumentNullException(nameof(inboxClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteEncryptionCompleteAsync(Guid platformUserId, string registerTitle, CancellationToken ct = default)
    {
        try
        {
            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId,
                Category: "Workflow",
                Severity: "Info",
                CorrelationKey: $"encryption:complete:{platformUserId}:{registerTitle}",
                DetailHref: "/app/registers",
                SourceEventId: Guid.NewGuid(),
                OccurredAt: DateTimeOffset.UtcNow,
                Title: "Register encrypted",
                Summary: $"Encryption of '{registerTitle}' completed successfully.",
                IconKey: "lock",
                ChannelHints: null);

            await _inboxClient.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EncryptionInboxWriter — failed to write encryption-complete inbox entry for {PlatformUserId}",
                platformUserId);
        }
    }

    /// <inheritdoc />
    public async Task WriteEncryptionFailedAsync(Guid platformUserId, string registerTitle, CancellationToken ct = default)
    {
        try
        {
            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId,
                Category: "Workflow",
                Severity: "Warning",
                CorrelationKey: $"encryption:failed:{platformUserId}:{registerTitle}",
                DetailHref: "/app/registers",
                SourceEventId: Guid.NewGuid(),
                OccurredAt: DateTimeOffset.UtcNow,
                Title: "Register encryption failed",
                Summary: $"Encryption of '{registerTitle}' failed. Please try again.",
                IconKey: "lock",
                ChannelHints: null);

            await _inboxClient.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EncryptionInboxWriter — failed to write encryption-failed inbox entry for {PlatformUserId}",
                platformUserId);
        }
    }
}
