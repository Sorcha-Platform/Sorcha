// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Core.Events;

/// <summary>
/// Event raised when a register is created
/// </summary>
public class RegisterCreatedEvent
{
    public required string RegisterId { get; set; }
    public required string Name { get; set; }
    public RegisterPurpose Purpose { get; set; } = RegisterPurpose.General;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Event raised when a register is deleted
/// </summary>
public class RegisterDeletedEvent
{
    public required string RegisterId { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// Event raised when a transaction is confirmed and stored
/// </summary>
public class TransactionConfirmedEvent
{
    public required string TransactionId { get; set; }
    public required string RegisterId { get; set; }
    public List<string> ToWallets { get; set; } = new();
    public required string SenderWallet { get; set; }
    public required string PreviousTransactionId { get; set; }
    public TransactionMetaData? MetaData { get; set; }
    public DateTime ConfirmedAt { get; set; }
}

/// <summary>
/// Event raised when a docket is confirmed and sealed
/// </summary>
public class DocketConfirmedEvent
{
    public required string RegisterId { get; set; }
    public ulong DocketId { get; set; }
    public List<string> TransactionIds { get; set; } = new();
    public required string Hash { get; set; }
    public DateTime TimeStamp { get; set; }
}

/// <summary>
/// Event raised when a register's status changes
/// </summary>
public class RegisterStatusChangedEvent
{
    public required string RegisterId { get; set; }
    public required string OldStatus { get; set; }
    public required string NewStatus { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// Event raised when a register's height is updated
/// </summary>
public class RegisterHeightUpdatedEvent
{
    public required string RegisterId { get; set; }
    public uint OldHeight { get; set; }
    public uint NewHeight { get; set; }
    public DateTime UpdatedAt { get; set; }
}
