// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Result of a wallet recovery operation.
/// </summary>
public class RecoveryResult
{
    /// <summary>Number of wallets successfully restored.</summary>
    public int WalletsRecovered { get; set; }

    /// <summary>Addresses of all restored wallets.</summary>
    public string[] WalletAddresses { get; set; } = [];

    /// <summary>Number of delegations revoked.</summary>
    public int DelegationsRevoked { get; set; }

    /// <summary>Delegations pending user review for selective preservation.</summary>
    public List<DelegationReviewItem> DelegationsPendingReview { get; set; } = [];
}

/// <summary>
/// A delegation that was revoked during recovery and can be selectively preserved.
/// </summary>
public class DelegationReviewItem
{
    /// <summary>WalletAccess ID.</summary>
    public Guid DelegationId { get; set; }

    /// <summary>Wallet address the delegation was on.</summary>
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>Subject (user/service) that had access.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Access level that was granted.</summary>
    public string AccessRight { get; set; } = string.Empty;

    /// <summary>When the delegation was originally granted.</summary>
    public DateTime GrantedAt { get; set; }

    /// <summary>Reason for the original grant.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response for GET /api/v1/wallets/recovery-status.
/// </summary>
public class RecoveryStatusResponse
{
    /// <summary>Mnemonic recovery is always available.</summary>
    public bool MnemonicRecoveryAvailable { get; set; } = true;

    /// <summary>Whether the user has a passkey with recovery wraps.</summary>
    public bool PasskeyRecoveryAvailable { get; set; }

    /// <summary>Whether the user's org has recovery config.</summary>
    public bool OrgRecoveryAvailable { get; set; }

    /// <summary>Wallets with EncryptedMasterKeyBlob (recovery enabled).</summary>
    public int WalletsWithRecovery { get; set; }

    /// <summary>Wallets without recovery (mnemonic only).</summary>
    public int WalletsWithoutRecovery { get; set; }
}
