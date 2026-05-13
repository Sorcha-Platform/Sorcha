// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// The type of lifecycle action to perform on a credential.
/// </summary>
public enum CredentialLifecycleAction
{
    Suspend,
    Reinstate,
    Revoke,
    Refresh
}

/// <summary>
/// Request model for credential lifecycle operations (suspend/reinstate/revoke/refresh).
/// </summary>
public class CredentialLifecycleRequest
{
    public string CredentialId { get; set; } = string.Empty;
    public string IssuerWallet { get; set; } = string.Empty;
    public CredentialLifecycleAction Action { get; set; }
    public string? Reason { get; set; }
    public string? NewExpiryDuration { get; set; }
}

/// <summary>
/// Result returned after a successful credential lifecycle operation.
/// </summary>
public class CredentialLifecycleResult
{
    public string CredentialId { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public DateTimeOffset PerformedAt { get; set; }
    public string? Reason { get; set; }
    public bool StatusListUpdated { get; set; }
    public string? NewCredentialId { get; set; }
}
