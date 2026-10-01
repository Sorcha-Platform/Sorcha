// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// The one list of platform-owned system blueprints (Feature 197, #1466). The order is the seed
/// order; every id has a shipped template at <c>blueprints/templates/{id}.json</c>.
/// </summary>
public static class SystemBlueprintCatalog
{
    /// <summary>System blueprint ids in seed order.</summary>
    public static IReadOnlyList<string> Ids { get; } = new[]
    {
        "register-creation-v1",
        "register-governance-v1",
        "create-organisation-v1",
        "join-private-register-v1",
    };

    /// <summary>Id of the governance workflow, which other components pin to.</summary>
    public static string GovernanceBlueprintId => GovernanceBlueprint.BlueprintId;
}
