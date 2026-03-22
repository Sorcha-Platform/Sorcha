// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// Tracks the last used sequence number for a wallet on a register (SEC-AUDIT 4.2).
/// Used for transaction replay protection — each wallet maintains a monotonically
/// increasing counter per register.
/// </summary>
public record WalletSequence
{
    /// <summary>Register scope</summary>
    public required string RegisterId { get; init; }

    /// <summary>Sender wallet address</summary>
    public required string WalletAddress { get; init; }

    /// <summary>Last accepted sequence number for this wallet on this register</summary>
    public ulong LastSequenceNumber { get; set; }

    /// <summary>When the sequence was last updated</summary>
    public DateTimeOffset LastUpdatedAt { get; set; }
}
