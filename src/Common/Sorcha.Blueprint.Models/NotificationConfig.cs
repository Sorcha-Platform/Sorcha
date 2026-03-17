// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Optional per-action notification configuration for blueprint designers.
/// Controls how notifications appear to recipients when an action becomes pending.
/// </summary>
public class NotificationConfig
{
    /// <summary>
    /// Template string with {{payload.field}} references for extracting business context.
    /// Example: "Inspection requested for Order #{{payload.orderRef}}"
    /// When null, defaults to "{BlueprintTitle} — {ActionTitle}".
    /// </summary>
    [JsonPropertyName("summaryTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SummaryTemplate { get; set; }

    /// <summary>
    /// Urgency rule: "none" (default), "deadline" (calculate from DeadlineField), or "always-urgent".
    /// </summary>
    [JsonPropertyName("urgencyRule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UrgencyRule { get; set; }

    /// <summary>
    /// Payload field path for deadline extraction (e.g., "payload.inspectionDeadline").
    /// Used when UrgencyRule is "deadline".
    /// </summary>
    [JsonPropertyName("deadlineField")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeadlineField { get; set; }

    /// <summary>
    /// Payload field path for grouping related notifications (e.g., "payload.orderRef").
    /// </summary>
    [JsonPropertyName("groupBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupBy { get; set; }
}
