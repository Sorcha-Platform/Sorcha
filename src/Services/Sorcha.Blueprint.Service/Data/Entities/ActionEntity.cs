// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Represents a submitted action transaction with its payload content,
/// optional idempotency key, and associated file attachments.
/// </summary>
public class ActionEntity
{
    /// <summary>
    /// Transaction hash serving as the primary key.
    /// </summary>
    [Required]
    public string TransactionHash { get; set; } = default!;

    /// <summary>
    /// Wallet address of the submitting participant.
    /// </summary>
    [Required]
    public string WalletAddress { get; set; } = default!;

    /// <summary>
    /// Address of the target register.
    /// </summary>
    [Required]
    public string RegisterAddress { get; set; } = default!;

    /// <summary>
    /// Action payload content stored as JSONB.
    /// </summary>
    [Required]
    public string Content { get; set; } = default!;

    /// <summary>
    /// Optional idempotency key for duplicate submission prevention.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Expiry time for the idempotency key.
    /// </summary>
    public DateTimeOffset? IdempotencyExpiry { get; set; }

    /// <summary>
    /// Timestamp when the action was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// File attachments associated with this action.
    /// </summary>
    public ICollection<FileMetadataEntity> Files { get; set; } = new List<FileMetadataEntity>();
}
