// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models.Responses;

/// <summary>
/// Detailed information about an action transaction
/// </summary>
public record ActionDetailsResponse
{
    /// <summary>
    /// The transaction hash
    /// </summary>
    public required string TransactionHash { get; init; }

    /// <summary>
    /// The blueprint ID
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The action ID
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// The workflow instance ID
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// The sender wallet address
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// The register address
    /// </summary>
    public required string RegisterAddress { get; init; }

    /// <summary>
    /// The decrypted payload data (if authorized)
    /// </summary>
    public Dictionary<string, object>? PayloadData { get; init; }

    /// <summary>
    /// File attachment metadata
    /// </summary>
    public List<FileMetadata>? Files { get; init; }

    /// <summary>
    /// Timestamp of the transaction
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Previous transaction hash (if part of a chain)
    /// </summary>
    public string? PreviousTransactionHash { get; init; }
}

/// <summary>
/// Metadata about a file attachment
/// </summary>
public record FileMetadata
{
    /// <summary>
    /// The file ID (transaction hash)
    /// </summary>
    public required string FileId { get; init; }

    /// <summary>
    /// The file name
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The content type
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Optional JSON-encoded chunk metadata (e.g. <c>FileChunkMetadata.ToJson()</c>).
    /// Stored alongside the file record so validators can inspect chunk context.
    /// </summary>
    public string? CustomMetadata { get; init; }
}
