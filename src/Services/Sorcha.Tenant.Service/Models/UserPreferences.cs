// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// User preferences for UI customization and application behavior.
/// Stored in per-organization schema (org_{organization_id}).
/// One-to-one relationship with UserIdentity — lazily created on first access.
/// </summary>
public class UserPreferences
{
    /// <summary>
    /// Unique preferences record identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User who owns these preferences. One-to-one with UserIdentity.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// UI theme preference (Light, Dark, or System).
    /// </summary>
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Preferred language (ISO 639-1 code: en, fr, de, es).
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// Time display format preference (UTC or Local).
    /// </summary>
    public TimeFormatPreference TimeFormat { get; set; } = TimeFormatPreference.Local;

    /// <summary>
    /// Address of the user's default wallet for signing operations.
    /// Null if no default wallet is set.
    /// </summary>
    public string? DefaultWalletAddress { get; set; }

    /// <summary>
    /// Whether push notifications are enabled.
    /// </summary>
    public bool NotificationsEnabled { get; set; }

    /// <summary>
    /// Delivery channel for inbound action notifications.
    /// Default: InApp (in-app notifications only).
    /// </summary>
    public NotificationMethod NotificationMethod { get; set; } = NotificationMethod.InApp;

    /// <summary>
    /// Delivery timing for inbound action notifications.
    /// Default: RealTime (immediate per-transaction delivery).
    /// </summary>
    public NotificationFrequency NotificationFrequency { get; set; } = NotificationFrequency.RealTime;

    /// <summary>
    /// Whether two-factor authentication is active.
    /// Read-only via preferences API — managed exclusively by the TOTP enrollment flow.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    /// Last modification timestamp (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// UI theme preference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemePreference
{
    /// <summary>
    /// Light theme.
    /// </summary>
    Light = 0,

    /// <summary>
    /// Dark theme.
    /// </summary>
    Dark = 1,

    /// <summary>
    /// Follow system/browser theme setting.
    /// </summary>
    System = 2
}

/// <summary>
/// Time display format preference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimeFormatPreference
{
    /// <summary>
    /// Display times in UTC.
    /// </summary>
    UTC = 0,

    /// <summary>
    /// Display times in the user's local timezone.
    /// </summary>
    Local = 1
}
