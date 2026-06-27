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
            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.System,
                Severity: InboxSeverity.Info,
                CorrelationKey: $"persona:saved:{platformUserId}",
                DetailHref: "/app/profile",
                SourceEventId: Guid.NewGuid(),
                OccurredAt: DateTimeOffset.UtcNow,
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
            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.System,
                Severity: InboxSeverity.Info,
                CorrelationKey: $"persona:deleted:{platformUserId}",
                DetailHref: "/app/profile",
                SourceEventId: Guid.NewGuid(),
                OccurredAt: DateTimeOffset.UtcNow,
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
}
