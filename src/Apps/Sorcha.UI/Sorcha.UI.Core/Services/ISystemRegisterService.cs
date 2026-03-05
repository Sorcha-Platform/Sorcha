// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for viewing the System Register and its disseminated blueprints.
/// </summary>
public interface ISystemRegisterService
{
    Task<SystemRegisterViewModel?> GetStatusAsync(CancellationToken ct = default);
    Task<BlueprintPageResult> GetBlueprintsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<BlueprintDetailViewModel?> GetBlueprintAsync(string blueprintId, CancellationToken ct = default);
    Task<BlueprintDetailViewModel?> GetBlueprintVersionAsync(string blueprintId, long version, CancellationToken ct = default);
}
