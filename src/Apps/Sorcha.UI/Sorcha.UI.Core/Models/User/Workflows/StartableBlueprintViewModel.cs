// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// Display model for a blueprint the user can start within a specific register.
/// </summary>
public record StartableBlueprintViewModel
{
    public string BlueprintId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Version { get; init; }
    public string RegisterId { get; init; } = string.Empty;
    public string StartingActionTitle { get; init; } = string.Empty;
    public string? StartingActionDescription { get; init; }
}
