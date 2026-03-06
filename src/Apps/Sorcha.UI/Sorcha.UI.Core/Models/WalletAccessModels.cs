// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// View model for a wallet access grant displayed in the Access tab.
/// </summary>
public class WalletAccessGrantViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string AccessRight { get; set; } = string.Empty;
    public string GrantedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Form model for granting wallet access.
/// </summary>
public class GrantAccessFormModel
{
    public string Subject { get; set; } = string.Empty;
    public string AccessRight { get; set; } = "ReadOnly";
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Result of an access check operation.
/// </summary>
public class AccessCheckResult
{
    public string WalletAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RequiredRight { get; set; } = string.Empty;
    public bool HasAccess { get; set; }
}
