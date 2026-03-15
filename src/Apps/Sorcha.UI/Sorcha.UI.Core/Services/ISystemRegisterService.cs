// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for querying the platform system register and its blueprint catalog.
/// </summary>
public interface ISystemRegisterService
{
    /// <summary>
    /// Gets the current system register status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>System register status or null if unavailable.</returns>
    Task<SystemRegisterViewModel?> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a paginated list of blueprints published to the system register.
    /// </summary>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated blueprint results.</returns>
    Task<BlueprintPageResult> GetBlueprintsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Gets detailed information about a specific blueprint.
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Blueprint details or null if not found.</returns>
    Task<BlueprintDetailViewModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default);

    /// <summary>
    /// Initializes the system register with default blueprints.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if initialization was performed, false if already initialized.</returns>
    Task<bool> InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets detailed information about a specific version of a blueprint.
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier.</param>
    /// <param name="version">Version number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Blueprint version details or null if not found.</returns>
    Task<BlueprintDetailViewModel?> GetBlueprintVersionAsync(string blueprintId, long version, CancellationToken ct = default);
}
