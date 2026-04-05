// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Tracks the upload state and metadata for a single file attached via a file-reference field.
/// </summary>
public class FileReferenceInfo
{
    /// <summary>Original file name as provided by the browser.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type reported by the browser.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Lowercase hex-encoded SHA-256 hash of the entire file, computed client-side before upload.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Salt or envelope nonce returned by the server on first chunk acceptance.
    /// Populated after the server acknowledges the upload session.
    /// </summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Transaction IDs returned by the Blueprint Service for each uploaded chunk,
    /// in order. Used to reconstruct or reference the file on the server side.
    /// </summary>
    public List<string> ChunkTransactionIds { get; set; } = [];

    /// <summary>Current upload status.</summary>
    public UploadStatus Status { get; set; } = UploadStatus.Pending;

    /// <summary>Upload progress as a percentage (0–100).</summary>
    public double Progress { get; set; }

    /// <summary>Human-readable error message when <see cref="Status"/> is <see cref="UploadStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Lifecycle states for a file being attached via <c>FileReferenceField</c>.
/// </summary>
public enum UploadStatus
{
    /// <summary>File has been selected and validated but upload has not started.</summary>
    Pending,

    /// <summary>Upload is in progress; chunks are being sent to the server.</summary>
    Uploading,

    /// <summary>All chunks were accepted and the file reference is ready for form submission.</summary>
    Complete,

    /// <summary>Upload failed; see <see cref="FileReferenceInfo.ErrorMessage"/> for details.</summary>
    Failed
}
