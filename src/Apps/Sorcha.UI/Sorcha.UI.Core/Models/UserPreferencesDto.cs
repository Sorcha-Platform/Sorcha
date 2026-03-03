// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// User preferences response DTO.
/// </summary>
public class UserPreferencesDto
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "en";
    public string TimeFormat { get; set; } = "Local";
    public string? DefaultWalletAddress { get; set; }
    public bool NotificationsEnabled { get; set; }
    public string NotificationMethod { get; set; } = "InApp";
    public string NotificationFrequency { get; set; } = "RealTime";
    public bool TwoFactorEnabled { get; set; }
}

/// <summary>
/// Partial update request — omitted fields are not changed.
/// </summary>
public class UpdateUserPreferencesRequest
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public string? TimeFormat { get; set; }
    public string? DefaultWalletAddress { get; set; }
    public bool? NotificationsEnabled { get; set; }
    public string? NotificationMethod { get; set; }
    public string? NotificationFrequency { get; set; }
}

/// <summary>
/// Lightweight response for default wallet queries.
/// </summary>
public class DefaultWalletResponse
{
    public string? DefaultWalletAddress { get; set; }
}

/// <summary>
/// Request to set default wallet address.
/// </summary>
public class SetDefaultWalletRequest
{
    public string WalletAddress { get; set; } = string.Empty;
}
