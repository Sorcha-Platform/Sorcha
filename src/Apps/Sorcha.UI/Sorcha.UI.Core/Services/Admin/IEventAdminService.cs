// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Service for managing system events in the admin interface.
/// </summary>
public interface IEventAdminService
{
    /// <summary>
    /// Retrieves a paginated list of system events, optionally filtered.
    /// </summary>
    Task<EventListResponse> GetEventsAsync(EventFilterModel? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a system event by its identifier.
    /// </summary>
    Task<bool> DeleteEventAsync(string eventId, CancellationToken ct = default);
}
