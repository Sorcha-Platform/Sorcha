// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Represents file metadata and content attached to an action transaction.
/// </summary>
public class FileMetadataEntity
{
    /// <summary>
    /// Unique identifier for the file.
    /// </summary>
    [Required]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Foreign key to the parent action entity.
    /// </summary>
    [Required]
    public string TransactionHash { get; set; } = default!;

    /// <summary>
    /// Original file name.
    /// </summary>
    [Required]
    public string FileName { get; set; } = default!;

    /// <summary>
    /// MIME content type of the file.
    /// </summary>
    [Required]
    public string ContentType { get; set; } = default!;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Raw file content.
    /// </summary>
    [Required]
    public byte[] Content { get; set; } = default!;

    /// <summary>
    /// Navigation property to the parent action.
    /// </summary>
    public ActionEntity? Action { get; set; }
}
