// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service client for platform-level settings management.
/// </summary>
public interface IPlatformSettingsAdminService
{
    /// <summary>
    /// Gets the current platform settings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Platform settings, or null if the request fails.</returns>
    Task<PlatformSettingsDto?> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the public organization enabled setting.
    /// </summary>
    /// <param name="enabled">Whether public organization is enabled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated platform settings, or null if the request fails.</returns>
    Task<PlatformSettingsDto?> UpdatePublicOrgAsync(bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Updates the maximum organizations per user setting.
    /// </summary>
    /// <param name="maxOrgs">Maximum number of organizations per user.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated platform settings, or null if the request fails.</returns>
    Task<PlatformSettingsDto?> UpdateMaxOrgsPerUserAsync(int maxOrgs, CancellationToken ct = default);
}

/// <summary>
/// Platform settings data transfer object.
/// </summary>
public class PlatformSettingsDto
{
    /// <summary>
    /// Whether the public organization feature is enabled.
    /// </summary>
    public bool PublicOrgEnabled { get; set; }

    /// <summary>
    /// Maximum number of organizations a user can belong to.
    /// </summary>
    public int MaxOrgsPerUser { get; set; }

    /// <summary>
    /// The ID of the public organization.
    /// </summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>
    /// Status of the public organization.
    /// </summary>
    public string PublicOrgStatus { get; set; } = string.Empty;

    /// <summary>
    /// When the settings were last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
