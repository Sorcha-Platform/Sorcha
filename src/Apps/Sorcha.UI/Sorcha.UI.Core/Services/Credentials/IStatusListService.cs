// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Service interface for retrieving W3C Bitstring Status Lists.
/// </summary>
public interface IStatusListService
{
    /// <summary>
    /// Gets a status list by ID. Returns null if not found.
    /// </summary>
    Task<StatusListViewModel?> GetStatusListAsync(string listId, CancellationToken ct = default);
}
