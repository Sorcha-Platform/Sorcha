// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for platform management endpoints via the API Gateway.
/// </summary>
public interface IPlatformServiceClient
{
    /// <summary>
    /// Lists all platform organizations (system admin only).
    /// </summary>
    [Get("/api/platform/organizations")]
    Task<List<PlatformOrganizationResponse>> ListPlatformOrganizationsAsync(
        [Query] string? status,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets platform settings (system admin only).
    /// </summary>
    [Get("/api/platform/settings")]
    Task<PlatformSettingsResponse> GetPlatformSettingsAsync(
        [Header("Authorization")] string authorization);
}

// --- Response DTOs ---

/// <summary>
/// Platform organization response.
/// </summary>
public class PlatformOrganizationResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Platform settings response.
/// </summary>
public class PlatformSettingsResponse
{
    public bool PublicOrgEnabled { get; set; }
    public int MaxOrgsPerUser { get; set; }
    public bool RegistrationOpen { get; set; }
    public bool SocialLoginEnabled { get; set; }
}
