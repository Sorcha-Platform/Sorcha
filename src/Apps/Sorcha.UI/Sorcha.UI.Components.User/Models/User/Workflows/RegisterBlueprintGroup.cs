// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// Groups startable blueprints by register for display on the New Submission page.
/// Each register represents an authority/jurisdiction with available services.
/// </summary>
public record RegisterBlueprintGroup
{
    public required RegisterViewModel Register { get; init; }
    public List<StartableBlueprintViewModel> Blueprints { get; init; } = [];
}
