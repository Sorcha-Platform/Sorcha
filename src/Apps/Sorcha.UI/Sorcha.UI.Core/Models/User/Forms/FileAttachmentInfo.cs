// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Represents a file uploaded through the form renderer.
/// </summary>
public class FileAttachmentInfo
{
    /// <summary>
    /// Original file name
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME content type
    /// </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// File content as bytes
    /// </summary>
    public byte[] Content { get; set; } = [];

    /// <summary>
    /// The scope (JSON Pointer) this file is associated with
    /// </summary>
    public string Scope { get; set; } = string.Empty;
}
