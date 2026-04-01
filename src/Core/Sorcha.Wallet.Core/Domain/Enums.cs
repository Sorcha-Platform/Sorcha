// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain;

/// <summary>
/// Represents the status of a wallet
/// </summary>
public enum WalletStatus
{
    /// <summary>
    /// Wallet is active and can be used for transactions
    /// </summary>
    Active,

    /// <summary>
    /// Wallet has been archived but can be reactivated
    /// </summary>
    Archived,

    /// <summary>
    /// Wallet has been soft-deleted
    /// </summary>
    Deleted,

    /// <summary>
    /// Wallet is temporarily locked for security reasons
    /// </summary>
    Locked
}

/// <summary>
/// Represents the type of access a delegate has to a wallet
/// </summary>
public enum AccessRight
{
    /// <summary>
    /// Full ownership rights - can modify wallet, grant access, delete
    /// </summary>
    Owner,

    /// <summary>
    /// Can read and sign transactions
    /// </summary>
    ReadWrite,

    /// <summary>
    /// Can only read wallet information and addresses
    /// </summary>
    ReadOnly
}

/// <summary>
/// Represents the recovery path used for wallet recovery
/// </summary>
public enum RecoveryPathType
{
    /// <summary>
    /// Existing mnemonic-based recovery (Path 1)
    /// </summary>
    Mnemonic,

    /// <summary>
    /// Organization-managed recovery via org admin (Path 3)
    /// </summary>
    OrgManaged,

    /// <summary>
    /// Passkey-bound recovery via FIDO2/WebAuthn (Path 4)
    /// </summary>
    Passkey
}

/// <summary>
/// Represents the state of a wallet transaction
/// </summary>
public enum TransactionState
{
    /// <summary>
    /// Transaction is pending submission
    /// </summary>
    Pending,

    /// <summary>
    /// Transaction has been submitted to the network
    /// </summary>
    Submitted,

    /// <summary>
    /// Transaction has been confirmed
    /// </summary>
    Confirmed,

    /// <summary>
    /// Transaction outputs have been spent
    /// </summary>
    Spent,

    /// <summary>
    /// Transaction failed
    /// </summary>
    Failed,

    /// <summary>
    /// Transaction has a signed receipt (cryptographic proof of finality)
    /// </summary>
    Receipted
}

/// <summary>
/// Represents the direction of a wallet transaction
/// </summary>
public enum TransactionDirection
{
    /// <summary>
    /// Transaction was signed by the wallet (outgoing)
    /// </summary>
    Outbound,

    /// <summary>
    /// Transaction was received by the wallet (incoming)
    /// </summary>
    Inbound
}
