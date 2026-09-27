// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// User preferences for UI customization and application behavior.
/// Lazily created on first access.
/// </summary>
/// <remarks>
/// #1694 — preferences belong to the PERSON, not to one of their organisation identities. They were
/// keyed by <c>UserIdentity.Id</c> (the per-org JWT <c>sub</c>), so someone in two orgs had two
/// unrelated sets, and the Wallet — which resolves a notification's recipient to a
/// <c>PlatformUser</c> — could never find either. Keyed by <see cref="PlatformUserId"/> now.
/// </remarks>
public class UserPreferences
{
    /// <summary>
    /// Unique preferences record identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The person (<c>PlatformUser.Id</c>) who owns these preferences — one set per person,
    /// shared across every organisation they belong to. Stored in the pre-existing <c>UserId</c>
    /// column (see <c>TenantDbContext.ConfigureUserPreferences</c>) so no schema change was needed.
    /// </summary>
    public Guid PlatformUserId { get; set; }

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
    /// Whether notifications are enabled. Defaults to ON — the documented default, and the Wallet's
    /// own default. It was <c>false</c>, and <c>GET /api/preferences</c> creates a row on first read,
    /// so merely opening the settings page would have switched notifications off once preferences
    /// were actually honoured (#1694).
    /// </summary>
    public bool NotificationsEnabled { get; set; } = true;

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
