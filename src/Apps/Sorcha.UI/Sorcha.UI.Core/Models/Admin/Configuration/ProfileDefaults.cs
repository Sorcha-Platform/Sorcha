// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Configuration;

/// <summary>
/// Factory class for creating default profile configurations.
/// Provides 3 standardized environments: local (Aspire), docker, and production.
/// </summary>
public static class ProfileDefaults
{
    /// <summary>
    /// Default active profile name ("docker" for Docker Compose deployments).
    /// </summary>
    public static string DefaultActiveProfile => "docker";

    /// <summary>
    /// Gets the default set of profiles for Sorcha UI.
    /// </summary>
    /// <returns>List of default profiles.</returns>
    public static List<Profile> GetDefaultProfiles()
    {
        var now = DateTime.UtcNow;

        return new List<Profile>
        {
            // Local development with .NET Aspire/AppHost
            // Uses individual service URLs on HTTPS ports (7xxx series)
            new Profile
            {
                Name = "local",
                Description = "Local .NET Aspire development (individual service ports)",
                SorchaServiceUrl = string.Empty, // Not used - all services have specific URLs
                TenantServiceUrl = "https://localhost:7110",
                RegisterServiceUrl = "https://localhost:7290",
                BlueprintServiceUrl = "https://localhost:7000",
                WalletServiceUrl = "https://localhost:7001",
                PeerServiceUrl = "https://localhost:7002",
                AuthTokenUrl = "https://localhost:7110/api/service-auth/token",
                DefaultClientId = "sorcha-ui",
                VerifySsl = false, // Self-signed certs in development
                TimeoutSeconds = 30,
                IsSystemProfile = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // Docker Compose environment
            // Uses API Gateway for all routing (same-origin relative URLs)
            new Profile
            {
                Name = "docker",
                Description = "Docker Compose (all services via API Gateway)",
                SorchaServiceUrl = string.Empty, // Empty = relative URLs (same origin via gateway)
                TenantServiceUrl = null, // Use base URL + /api/tenant
                RegisterServiceUrl = null, // Use base URL + /api/register
                BlueprintServiceUrl = null, // Use base URL + /api/blueprint
                WalletServiceUrl = null, // Use base URL + /api/wallet
                PeerServiceUrl = null, // Use base URL + /api/peer
                AuthTokenUrl = null, // Derived from tenant service
                DefaultClientId = "sorcha-ui",
                VerifySsl = false,
                TimeoutSeconds = 30,
                IsSystemProfile = true,
                CreatedAt = now,
                UpdatedAt = now
            },

            // Production environment
            // Uses dedicated domain with HTTPS
            new Profile
            {
                Name = "production",
                Description = "Production environment (sorcha.io)",
                SorchaServiceUrl = "https://api.sorcha.io",
                TenantServiceUrl = null, // Use base URL + /api/tenant
                RegisterServiceUrl = null, // Use base URL + /api/register
                BlueprintServiceUrl = null, // Use base URL + /api/blueprint
                WalletServiceUrl = null, // Use base URL + /api/wallet
                PeerServiceUrl = null, // Use base URL + /api/peer
                AuthTokenUrl = null, // Derived from tenant service
                DefaultClientId = "sorcha-ui",
                VerifySsl = true, // Require valid SSL in production
                TimeoutSeconds = 30,
                IsSystemProfile = true,
                CreatedAt = now,
                UpdatedAt = now
            }
        };
    }

    /// <summary>
    /// Gets a specific default profile by name.
    /// </summary>
    /// <param name="name">Profile name (local, docker, or production).</param>
    /// <returns>The profile, or null if not found.</returns>
    public static Profile? GetDefaultProfile(string name)
    {
        return GetDefaultProfiles()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
