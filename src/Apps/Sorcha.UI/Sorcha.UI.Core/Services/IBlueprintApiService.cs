// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Cloud-backed blueprint storage service replacing LocalStorage persistence.
/// </summary>
public interface IBlueprintApiService
{
    Task<PaginatedList<BlueprintListItemViewModel>> GetBlueprintsAsync(int page = 1, int pageSize = 20, string? search = null, string? status = null, CancellationToken cancellationToken = default);
    Task<BlueprintListItemViewModel?> GetBlueprintAsync(string id, CancellationToken cancellationToken = default);
    Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintDetailAsync(string id, CancellationToken cancellationToken = default);
    Task<BlueprintListItemViewModel?> SaveBlueprintAsync(object blueprint, CancellationToken cancellationToken = default);
    Task<BlueprintListItemViewModel?> UpdateBlueprintAsync(string id, object blueprint, CancellationToken cancellationToken = default);
    Task<bool> DeleteBlueprintAsync(string id, CancellationToken cancellationToken = default);
    Task<PublishReviewViewModel?> PublishBlueprintAsync(string id, CancellationToken cancellationToken = default);
    Task<BlueprintValidationResponse?> ValidateBlueprintAsync(string id, CancellationToken cancellationToken = default);
    Task<PublishReviewViewModel?> PublishBlueprintToRegisterAsync(string id, string registerId, CancellationToken cancellationToken = default);
    Task<List<BlueprintVersionViewModel>> GetVersionsAsync(string id, CancellationToken cancellationToken = default);
    Task<BlueprintListItemViewModel?> GetVersionAsync(string id, string version, CancellationToken cancellationToken = default);
    Task<AvailableBlueprintsViewModel?> GetAvailableBlueprintsAsync(string walletAddress, string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single published blueprint (with full action schemas) from the
    /// published blueprint store. Works on any node with the blueprint published
    /// — unlike <see cref="GetBlueprintDetailAsync"/> which requires the authoring
    /// node's draft store. Use this for the New Submissions workspace.
    /// </summary>
    Task<Sorcha.Blueprint.Models.Blueprint?> GetPublishedBlueprintDetailAsync(string walletAddress, string registerId, string blueprintId, CancellationToken cancellationToken = default);
}
