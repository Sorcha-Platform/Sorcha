// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;

namespace Sorcha.Blueprint.Service.Authorization;

/// <summary>
/// Authorization requirement for blueprint / schema / credential / status-list authoring
/// (the <c>CanManageBlueprints</c> policy). Satisfied by <see cref="BlueprintManagementAuthorizationHandler"/>
/// for either a service-tier caller or a platform-tier organization member. Marker type — carries no state.
/// </summary>
public sealed class BlueprintManagementRequirement : IAuthorizationRequirement
{
}
