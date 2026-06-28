// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Feature 169 — Tenant-side inbox writer for persona (profile) lifecycle events.
/// Writes directly to the local <see cref="IInboxService"/> — no cross-service HTTP
/// hop required because this runs inside Tenant Service.
/// Fail-safe: inbox-write failures are logged as warnings and swallowed; they never
/// block the originating <c>PersonaService</c> operation.
/// </summary>
public interface IPersonaInboxWriter
{
    /// <summary>Write a "profile saved" <see cref="InboxCategory.System"/> inbox entry.</summary>
    Task WritePersonaSavedAsync(Guid platformUserId, string personaName, CancellationToken ct = default);

    /// <summary>Write a "profile deleted" <see cref="InboxCategory.System"/> inbox entry.</summary>
    Task WritePersonaDeletedAsync(Guid platformUserId, string personaName, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PersonaInboxWriter : IPersonaInboxWriter
{
    private readonly IInboxService _inboxService;
    private readonly ILogger<PersonaInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="PersonaInboxWriter"/>.</summary>
    public PersonaInboxWriter(
        IInboxService inboxService,
        ILogger<PersonaInboxWriter> logger)
    {
        _inboxService = inboxService ?? throw new ArgumentNullException(nameof(inboxService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WritePersonaSavedAsync(Guid platformUserId, string personaName, CancellationToken ct = default)
    {
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.System,
                Severity: InboxSeverity.Info,
                CorrelationKey: $"persona:saved:{platformUserId}",
                DetailHref: "/app/profile",
                SourceEventId: DeterministicSourceEventId($"sorcha.inbox.persona.replaced:{platformUserId:N}:{occurredAt.ToUnixTimeSeconds()}"),
                OccurredAt: occurredAt,
                Title: "Profile updated",
                Summary: $"Your profile '{personaName}' was saved.",
                IconKey: "person");

            await _inboxService.WriteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PersonaInboxWriter — failed to write persona-saved inbox entry for {PlatformUserId}",
                platformUserId);
        }
    }

    /// <inheritdoc />
    public async Task WritePersonaDeletedAsync(Guid platformUserId, string personaName, CancellationToken ct = default)
    {
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.System,
                Severity: InboxSeverity.Warning,
                CorrelationKey: $"persona:deleted:{platformUserId}",
                DetailHref: "/app/profile",
                SourceEventId: DeterministicSourceEventId($"sorcha.inbox.persona.deleted:{platformUserId:N}:{occurredAt.ToUnixTimeSeconds()}"),
                OccurredAt: occurredAt,
                Title: "Profile deleted",
                Summary: $"Your profile '{personaName}' was deleted.",
                IconKey: "person");

            await _inboxService.WriteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PersonaInboxWriter — failed to write persona-deleted inbox entry for {PlatformUserId}",
                platformUserId);
        }
    }

    private static Guid DeterministicSourceEventId(string key)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
