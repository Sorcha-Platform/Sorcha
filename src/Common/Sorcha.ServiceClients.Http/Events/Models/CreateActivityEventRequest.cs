// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Events.Models;

/// <summary>
/// Request model for creating an activity event via the Tenant Service.
/// </summary>
/// <param name="OrganizationId">Identifier of the organization that owns this resource.</param>
/// <param name="UserId">Identifier of the user.</param>
/// <param name="EventType">The event type.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Message">Human-readable message.</param>
/// <param name="SourceService">The source service.</param>
/// <param name="EntityId">Identifier of the entity.</param>
/// <param name="EntityType">The entity type.</param>
public record CreateActivityEventRequest(
    Guid OrganizationId,
    Guid UserId,
    string EventType,
    string Severity,
    string Title,
    string Message,
    string SourceService,
    string? EntityId = null,
    string? EntityType = null);
