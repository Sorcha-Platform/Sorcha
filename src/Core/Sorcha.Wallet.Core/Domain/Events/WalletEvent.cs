// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain.Events;

/// <summary>
/// Base class for wallet domain events
/// </summary>
public abstract record WalletEvent
{
    /// <summary>
    /// Event identifier
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Wallet address this event relates to
    /// </summary>
    public required string WalletAddress { get; init; }

    /// <summary>
    /// Timestamp when event occurred
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Subject who triggered the event
    /// </summary>
    public string? TriggeredBy { get; init; }
}

/// <summary>
/// Event raised when a new wallet is created
/// </summary>
public record WalletCreatedEvent : WalletEvent
{
    /// <summary>
    /// The user or principal who owns this wallet
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// The tenant or organization this wallet belongs to
    /// </summary>
    public required string Tenant { get; init; }

    /// <summary>
    /// The cryptographic algorithm used for this wallet (e.g., ED25519, NIST_P256, RSA_4096)
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// The human-readable name assigned to this wallet
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// Event raised when a wallet is recovered from a mnemonic
/// </summary>
public record WalletRecoveredEvent : WalletEvent
{
    /// <summary>
    /// The user or principal who owns the recovered wallet
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// The tenant or organization this recovered wallet belongs to
    /// </summary>
    public required string Tenant { get; init; }

    /// <summary>
    /// The cryptographic algorithm of the recovered wallet (e.g., ED25519, NIST_P256, RSA_4096)
    /// </summary>
    public required string Algorithm { get; init; }
}

/// <summary>
/// Event raised when a new address is generated
/// </summary>
public record AddressGeneratedEvent : WalletEvent
{
    /// <summary>
    /// The newly generated blockchain address (base58 or hex encoded)
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// The BIP44 derivation path used to generate this address (e.g., m/44'/0'/0'/0/0)
    /// </summary>
    public required string DerivationPath { get; init; }

    /// <summary>
    /// The address index within the derivation path
    /// </summary>
    public int Index { get; init; }
}

/// <summary>
/// Event raised when a transaction is signed
/// </summary>
public record TransactionSignedEvent : WalletEvent
{
    /// <summary>
    /// The unique identifier of the signed transaction (typically a hash)
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// The wallet address or principal that signed the transaction
    /// </summary>
    public required string SignedBy { get; init; }
}

/// <summary>
/// Event raised when access is granted to a wallet
/// </summary>
public record DelegateAddedEvent : WalletEvent
{
    /// <summary>
    /// The user or principal being granted access to the wallet
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The level of access being granted (Owner, ReadWrite, or ReadOnly)
    /// </summary>
    public required AccessRight AccessRight { get; init; }

    /// <summary>
    /// The user or principal who granted this access
    /// </summary>
    public required string GrantedBy { get; init; }
}

/// <summary>
/// Event raised when access is revoked
/// </summary>
public record DelegateRemovedEvent : WalletEvent
{
    /// <summary>
    /// The user or principal whose access is being revoked
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The user or principal who revoked this access
    /// </summary>
    public required string RevokedBy { get; init; }
}

/// <summary>
/// Event raised when wallet status changes
/// </summary>
public record WalletStatusChangedEvent : WalletEvent
{
    /// <summary>
    /// The previous status of the wallet before the change
    /// </summary>
    public required WalletStatus OldStatus { get; init; }

    /// <summary>
    /// The new status of the wallet after the change (Active, Locked, Suspended, or Deleted)
    /// </summary>
    public required WalletStatus NewStatus { get; init; }
}

/// <summary>
/// Event raised when encryption key is rotated
/// </summary>
public record KeyRotatedEvent : WalletEvent
{
    /// <summary>
    /// The identifier of the encryption key being replaced
    /// </summary>
    public required string OldKeyId { get; init; }

    /// <summary>
    /// The identifier of the new encryption key being used
    /// </summary>
    public required string NewKeyId { get; init; }
}
