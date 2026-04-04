// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Events.Models;

/// <summary>
/// Request model for creating an activity event via the Tenant Service.
/// </summary>
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
