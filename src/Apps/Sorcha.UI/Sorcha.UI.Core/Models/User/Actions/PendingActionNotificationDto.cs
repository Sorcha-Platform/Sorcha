// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// UI model for pending action notifications received via SignalR.
/// Enriched with business context from the blueprint and participant registry.
/// </summary>
public class PendingActionNotificationDto
{
    /// <summary>Unique event identifier.</summary>
    public Guid EventId { get; set; }

    /// <summary>Rendered notification summary (template-derived or default).</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Urgency level: "normal", "warning", or "urgent".</summary>
    public string Urgency { get; set; } = "normal";

    /// <summary>Deadline timestamp, if configured via NotificationConfig.</summary>
    public DateTimeOffset? Deadline { get; set; }

    /// <summary>Deep link path to the action page.</summary>
    public string? NavigationPath { get; set; }

    /// <summary>Resolved sender display name.</summary>
    public string SenderDisplayName { get; set; } = string.Empty;

    /// <summary>Blueprint display name.</summary>
    public string BlueprintTitle { get; set; } = string.Empty;

    /// <summary>Action display title.</summary>
    public string ActionTitle { get; set; } = string.Empty;

    /// <summary>Grouping key for related notifications.</summary>
    public string? GroupKey { get; set; }

    /// <summary>64-char hex SHA-256 transaction hash.</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>Register the transaction belongs to.</summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>Recipient wallet address.</summary>
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>When the event was detected.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Whether this was detected during recovery mode.</summary>
    public bool IsRecoveryEvent { get; set; }
}
