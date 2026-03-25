// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for organization dashboard statistics.
/// </summary>
public record OrganizationDashboardViewModel
{
    public int UserCount { get; init; }
    public int ParticipantCount { get; init; }
    public int PublishedParticipantCount { get; init; }
    public int ActiveUserCount { get; init; }
    public int ActiveParticipantCount { get; init; }
    public int InvitedUserCount { get; init; }
    public int UnverifiedUserCount { get; init; }
}
