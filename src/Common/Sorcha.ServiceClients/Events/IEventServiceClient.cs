// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Events.Models;

namespace Sorcha.ServiceClients.Events;

/// <summary>
/// HTTP client for creating activity events on the Tenant Service.
/// </summary>
public interface IEventServiceClient
{
    /// <summary>
    /// Creates an activity event on the Tenant Service.
    /// Best-effort: returns false on failure without throwing.
    /// </summary>
    /// <param name="request">The event creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the event was created successfully; false otherwise.</returns>
    Task<bool> CreateEventAsync(CreateActivityEventRequest request, CancellationToken ct = default);
}
