// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.Enums;

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// View model for displaying register information in the UI.
/// Wraps the Register model with UI-specific computed properties.
/// </summary>
public record RegisterViewModel
{
    /// <summary>
    /// Unique identifier (32-char GUID without hyphens)
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable register name (1-38 characters)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Purpose and scope of the register
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Current docket height (number of sealed dockets)
    /// </summary>
    public uint Height { get; init; }

    /// <summary>
    /// Register operational status
    /// </summary>
    public RegisterStatus Status { get; init; }

    /// <summary>
    /// Whether register is advertised to network peers (public visibility)
    /// </summary>
    public bool Advertise { get; init; }

    /// <summary>
    /// Whether this node maintains full transaction history
    /// </summary>
    public bool IsFullReplica { get; init; }

    /// <summary>
    /// Register creation timestamp (UTC)
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp (UTC)
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Whether field-level encryption is NOT enforced (development mode).
    /// When true, a warning should be displayed to the user.
    /// </summary>
    public bool DevMode { get; init; }

    /// <summary>
    /// Replication state for remotely-subscribed registers. Null for locally created registers.
    /// </summary>
    public string? SyncState { get; init; }

    /// <summary>
    /// Whether this register is a Feature 142 rehearsal sandbox (derived from the register's
    /// <c>sandbox</c> control-record metadata). Sandbox registers are excluded from the Go-live
    /// target picker and normal register listings — nothing is ever published to them.
    /// </summary>
    public bool Sandbox { get; init; }

    /// <summary>
    /// Computed: Whether the register is currently syncing or in a sync-related state.
    /// Includes both legacy lifecycle values (Subscribing) and Feature 108 sync-state
    /// enum values (Indeterminate, Syncing).
    /// </summary>
    public bool IsInSyncLifecycle => SyncState is "Subscribing" or "Syncing" or "Error" or "Indeterminate";

    /// <summary>
    /// Computed: Human-readable sync state text. Covers the legacy lifecycle strings
    /// (Subscribing / Synced) AND the Feature 108 <c>RegisterSyncState</c> enum
    /// (Indeterminate / Syncing / CaughtUp / Error). "Syncing (N behind)" would
    /// require <c>RegisterSyncStateView.NetworkHeightHighWaterMark - LocalHeight</c>,
    /// which is not currently surfaced on this VM — surfacing it is a follow-up.
    /// </summary>
    public string SyncStateText => SyncState switch
    {
        // Feature 108 RegisterSyncState enum values.
        "Indeterminate" => "Unknown",
        "Syncing" => "Syncing...",
        "CaughtUp" => "Caught up",
        "Error" => "Error",
        // Legacy lifecycle strings (pre-F108) retained for backward compat.
        "Subscribing" => "Subscribing...",
        "Synced" => "Synced",
        _ => ""
    };

    /// <summary>
    /// Computed: Whether the register is currently online
    /// </summary>
    public bool IsOnline => Status == RegisterStatus.Online;

    /// <summary>
    /// Computed: CSS color class based on status
    /// </summary>
    public string StatusColor => Status switch
    {
        RegisterStatus.Online => "success",
        RegisterStatus.Offline => "default",
        RegisterStatus.Checking => "warning",
        RegisterStatus.Recovery => "error",
        _ => "default"
    };

    /// <summary>
    /// Computed: Material icon name based on status
    /// </summary>
    public string StatusIcon => Status switch
    {
        RegisterStatus.Online => "CheckCircle",
        RegisterStatus.Offline => "Cancel",
        RegisterStatus.Checking => "Sync",
        RegisterStatus.Recovery => "Warning",
        _ => "HelpOutline"
    };

    /// <summary>
    /// Computed: Human-readable status text
    /// </summary>
    public string StatusText => Status switch
    {
        RegisterStatus.Online => "Online",
        RegisterStatus.Offline => "Offline",
        RegisterStatus.Checking => "Checking",
        RegisterStatus.Recovery => "Recovery",
        _ => "Unknown"
    };

    /// <summary>
    /// Computed: Relative time since last update (e.g., "5 minutes ago")
    /// </summary>
    public string LastUpdateFormatted => GetRelativeTime(UpdatedAt);

    /// <summary>
    /// Computed: Formatted height with K/M suffix for large numbers
    /// </summary>
    public string HeightFormatted => Height switch
    {
        >= 1_000_000 => $"{Height / 1_000_000.0:F1}M",
        >= 1_000 => $"{Height / 1_000.0:F1}K",
        _ => Height.ToString()
    };

    private static string GetRelativeTime(DateTime dateTime)
    {
        var timeSpan = DateTime.UtcNow - dateTime;

        return timeSpan switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes != 1 ? "s" : "")} ago",
            { TotalHours: < 24 } => $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours != 1 ? "s" : "")} ago",
            { TotalDays: < 30 } => $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays != 1 ? "s" : "")} ago",
            _ => dateTime.ToString("MMM dd, yyyy")
        };
    }
}
