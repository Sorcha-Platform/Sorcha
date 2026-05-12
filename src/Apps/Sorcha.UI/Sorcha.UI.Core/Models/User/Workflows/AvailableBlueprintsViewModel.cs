// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// View model for the available blueprints response from the Blueprint Service.
/// Maps to the AvailableBlueprintsResponse from GET /api/actions/{wallet}/{register}/blueprints.
/// </summary>
public record AvailableBlueprintsViewModel
{
    public string WalletAddress { get; init; } = string.Empty;
    public string RegisterAddress { get; init; } = string.Empty;
    public List<BlueprintInfoViewModel> Blueprints { get; init; } = [];
}

/// <summary>
/// Summary information about a published blueprint available in a register.
/// </summary>
public record BlueprintInfoViewModel
{
    public string BlueprintId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public List<ActionInfoViewModel> AvailableActions { get; init; } = [];
}

/// <summary>
/// Summary information about an action within an available blueprint.
/// </summary>
public record ActionInfoViewModel
{
    public string ActionId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsAvailable { get; init; }
}
