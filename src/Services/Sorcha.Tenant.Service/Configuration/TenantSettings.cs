// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Configuration;

/// <summary>
/// Tenant Service settings. Bound from <c>Tenant</c> section of configuration.
/// </summary>
public sealed class TenantSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Tenant";

    /// <summary>
    /// Platform domain used as the federated <c>did:web</c> base. Default <c>sorcha.dev</c>.
    /// Maps to the federated DID form <c>did:web:{PlatformDomain}:orgs:{orgId}</c>.
    /// </summary>
    public string PlatformDomain { get; set; } = "sorcha.dev";
}
