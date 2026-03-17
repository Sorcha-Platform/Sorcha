// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Calculates notification urgency based on NotificationConfig rules and payload deadlines.
/// </summary>
public static class UrgencyCalculator
{
    /// <summary>
    /// Calculates the urgency level for a notification.
    /// </summary>
    /// <param name="config">Notification configuration from the blueprint action. May be null.</param>
    /// <param name="payload">JSON payload to extract deadline values from. May be null.</param>
    /// <returns>Urgency string: "normal", "warning", or "urgent".</returns>
    public static string Calculate(NotificationConfig? config, JsonElement? payload)
    {
        if (config is null || string.IsNullOrEmpty(config.UrgencyRule))
            return "normal";

        return config.UrgencyRule.ToLowerInvariant() switch
        {
            "always-urgent" => "urgent",
            "deadline" => CalculateFromDeadline(config.DeadlineField, payload),
            _ => "normal"
        };
    }

    /// <summary>
    /// Extracts the deadline DateTimeOffset from the payload using the configured field path.
    /// </summary>
    /// <param name="config">Notification configuration.</param>
    /// <param name="payload">JSON payload.</param>
    /// <returns>The deadline value, or null if not found or not parseable.</returns>
    public static DateTimeOffset? ExtractDeadline(NotificationConfig? config, JsonElement? payload)
    {
        if (config is null || string.IsNullOrEmpty(config.DeadlineField) || payload is null)
            return null;

        var fieldPath = config.DeadlineField;
        // Strip "payload." prefix if present
        if (fieldPath.StartsWith("payload.", StringComparison.OrdinalIgnoreCase))
            fieldPath = fieldPath["payload.".Length..];

        return ResolveDeadline(payload.Value, fieldPath);
    }

    private static string CalculateFromDeadline(string? deadlineField, JsonElement? payload)
    {
        if (string.IsNullOrEmpty(deadlineField) || payload is null)
            return "normal";

        var fieldPath = deadlineField;
        // Strip "payload." prefix if present
        if (fieldPath.StartsWith("payload.", StringComparison.OrdinalIgnoreCase))
            fieldPath = fieldPath["payload.".Length..];

        var deadline = ResolveDeadline(payload.Value, fieldPath);
        if (deadline is null)
            return "normal";

        var remaining = deadline.Value - DateTimeOffset.UtcNow;

        if (remaining.TotalHours <= 0) return "urgent"; // Past deadline
        if (remaining.TotalHours < 4) return "urgent";
        if (remaining.TotalHours < 24) return "warning";
        return "normal";
    }

    private static DateTimeOffset? ResolveDeadline(JsonElement element, string path)
    {
        var segments = path.Split('.');
        var current = element;

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetProperty(segment, out var next))
                return null;

            current = next;
        }

        if (current.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(current.GetString(), out var dto))
                return dto;
        }

        return null;
    }
}
