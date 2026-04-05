// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.TransactionHandler.Chunking;

/// <summary>
/// Metadata stored in each chunk transaction's <c>Metadata</c> JSON field.
/// Used by the validator to verify chunk integrity and reconstruct the original file.
/// </summary>
public class FileChunkMetadata
{
    /// <summary>
    /// The fixed type discriminator for file-chunk metadata records.
    /// </summary>
    public const string MetadataType = "file-chunk";

    /// <summary>
    /// Always "file-chunk". Identifies this metadata record as a file chunk descriptor.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = MetadataType;

    /// <summary>
    /// Transaction ID of the parent action that initiated the file transfer.
    /// Null at submission time; populated by the validator once the parent action
    /// transaction has been committed to the ledger.
    /// </summary>
    [JsonPropertyName("parentActionId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentActionId { get; set; }

    /// <summary>
    /// SHA-256 hash of the complete original file (before chunking).
    /// Must match the <c>hash</c> field on the parent action's <c>FileReference</c>.
    /// </summary>
    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based position of this chunk within the ordered chunk sequence.
    /// </summary>
    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Total number of chunks that make up this file transfer.
    /// </summary>
    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    /// <summary>
    /// MIME type of the original file (e.g. "application/pdf", "image/png").
    /// </summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Size of this chunk's encrypted payload in bytes.
    /// </summary>
    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; }

    /// <summary>
    /// Serializes this instance to a JSON string using <see cref="System.Text.Json"/>.
    /// </summary>
    /// <returns>A JSON string representation of this metadata record.</returns>
    public string ToJson()
        => JsonSerializer.Serialize(this);

    /// <summary>
    /// Deserializes a <see cref="FileChunkMetadata"/> instance from a JSON string.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>A <see cref="FileChunkMetadata"/> instance populated from the JSON data.</returns>
    public static FileChunkMetadata FromJson(string json)
        => JsonSerializer.Deserialize<FileChunkMetadata>(json)!;
}
