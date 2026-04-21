// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Invalidates the local relationship cache for a register and publishes a
/// <see cref="RegisterRelationshipChangedEvent"/> to the
/// <c>register:relationship-changed</c> Redis channel — but only when the derived role set
/// actually changed (Feature 108). Called from the docket-seal hook when a docket containing
/// a Control transaction has been written.
/// </summary>
public sealed class RelationshipChangeNotifier
{
    private readonly IRegisterLocalRelationshipService _relationshipService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<RelationshipChangeNotifier> _logger;

    public RelationshipChangeNotifier(
        IRegisterLocalRelationshipService relationshipService,
        IEventPublisher eventPublisher,
        ILogger<RelationshipChangeNotifier> logger)
    {
        _relationshipService = relationshipService ?? throw new ArgumentNullException(nameof(relationshipService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Snapshot the previous relationship, invalidate the cache, re-derive, and publish
    /// an event iff the derived role set changed. Safe to call fire-and-forget from the
    /// docket-seal hook.
    /// </summary>
    public async Task PublishIfChangedAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Snapshot the current (cached) relationship before invalidation.
            var before = await _relationshipService.DeriveAsync(registerId, cancellationToken);

            _relationshipService.Invalidate(registerId);
            var after = await _relationshipService.DeriveAsync(registerId, cancellationToken);

            if (after is null)
            {
                _logger.LogWarning(
                    "RelationshipChangeNotifier: re-derivation returned null for register {RegisterId} — event suppressed",
                    registerId);
                return;
            }

            var beforeRoles = before?.Roles ?? RegisterRoleSet.None;
            if (beforeRoles == after.Roles && before?.ControlRecordVersion == after.ControlRecordVersion)
            {
                _logger.LogDebug(
                    "Relationship for register {RegisterId} unchanged after control-tx seal — no event published",
                    registerId);
                return;
            }

            var added = after.Roles & ~beforeRoles;
            var removed = beforeRoles & ~after.Roles;

            await _eventPublisher.PublishAsync(
                RegisterEventChannels.RegisterRelationshipChanged,
                new RegisterRelationshipChangedEvent
                {
                    RegisterId = registerId,
                    ControlRecordVersion = after.ControlRecordVersion,
                    AddedRoles = added,
                    RemovedRoles = removed,
                    ChangedAt = DateTimeOffset.UtcNow
                },
                cancellationToken);

            _logger.LogInformation(
                "Published relationship change for register {RegisterId}: +{Added} / -{Removed} (controlRecordVersion={Version})",
                registerId, added, removed, after.ControlRecordVersion);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish relationship change for register {RegisterId}", registerId);
        }
    }
}
