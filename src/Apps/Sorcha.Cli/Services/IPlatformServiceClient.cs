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
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.PlatformSettingsResponse</c> exactly; the pairing
/// is asserted by <c>CliWireContractTests</c>. This previously declared <c>registrationOpen</c> and
/// <c>socialLoginEnabled</c>, which the server does not send — so both rendered <c>false</c>
/// regardless of the platform's actual configuration — while omitting the public-org fields the
/// server does send.
/// </remarks>
public class PlatformSettingsResponse
{
    /// <summary>Whether the public organisation is enabled for self-registration.</summary>
    public bool PublicOrgEnabled { get; set; }

    /// <summary>Maximum organisations a single user may belong to.</summary>
    public int MaxOrgsPerUser { get; set; }

    /// <summary>The well-known public organisation's id.</summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>The public organisation's current status.</summary>
    public string PublicOrgStatus { get; set; } = string.Empty;

    /// <summary>When these settings were last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
