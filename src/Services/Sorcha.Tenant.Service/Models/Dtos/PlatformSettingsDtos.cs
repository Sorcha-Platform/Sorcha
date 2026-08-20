// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Response containing current platform settings.
/// </summary>
public class PlatformSettingsResponse
{
    /// <summary>
    /// Whether the public organisation is enabled for self-registration.
    /// </summary>
    public bool PublicOrgEnabled { get; set; }

    /// <summary>
    /// Maximum number of private organisations a single user can create.
    /// </summary>
    public int MaxOrgsPerUser { get; set; }

    /// <summary>
    /// ID of the public organisation.
    /// </summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>
    /// Current status of the public organisation.
    /// </summary>
    public string PublicOrgStatus { get; set; } = string.Empty;

    /// <summary>
    /// The public organisation's signing wallet address, once it has one (#1530).
    /// </summary>
    public string? PublicOrgWalletAddress { get; set; }

    /// <summary>
    /// The public organisation's BIP39 recovery phrase — present ONLY on the response to the enable
    /// that first created its wallet, and never again.
    /// </summary>
    /// <remarks>
    /// It is shown once, stored nowhere, and cannot be reissued by anyone including Sorcha. Every
    /// other organisation's phrase goes to an administrator of that organisation; the public
    /// organisation has none, so it goes to the system administrator who enabled it. If they do not
    /// record it, the public organisation can never be recovered.
    /// </remarks>
    public string[]? PublicOrgRecoveryPhrase { get; set; }

    /// <summary>
    /// Timestamp of the last settings modification (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Request to enable or disable the public organisation.
/// </summary>
public class UpdatePublicOrgRequest
{
    /// <summary>
    /// Whether the public organisation should be enabled.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Request to update the maximum organisations per user setting.
/// </summary>
public class UpdateMaxOrgsRequest
{
    /// <summary>
    /// Maximum number of private organisations a single user can create. Range: 1-100.
    /// </summary>
    public int MaxOrgsPerUser { get; set; }
}
