// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Runtime value stored in an action payload for a file field.
/// Contains metadata and chunk transaction references for a file attachment.
/// </summary>
public class FileReference
{
    /// <summary>
    /// Original filename of the attached file.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MaxLength(255)]
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the file (e.g. "application/pdf", "image/png").
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Total file size in bytes, measured before encryption.
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// SHA-256 hash of the plaintext file content, prefixed with "sha256:".
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded random salt used for HKDF master key derivation.
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Ordered array of chunk transaction IDs (1–10 items).
    /// Each ID references a register transaction holding one encrypted chunk.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(10)]
    [JsonPropertyName("chunkTransactionIds")]
    public string[] ChunkTransactionIds { get; set; } = [];

    /// <summary>
    /// Transaction ID of the parent action whose payload carries the wrapped master key
    /// for this file's encryption envelope.
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("masterKeyId")]
    public string MasterKeyId { get; set; } = string.Empty;
}
