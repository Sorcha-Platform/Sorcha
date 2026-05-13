// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MudBlazor;

namespace Sorcha.UI.Core.Utilities;

/// <summary>
/// Maps MIME content types to MudBlazor Material icon strings for display in file lists.
/// </summary>
public static class MimeTypeIconHelper
{
    /// <summary>
    /// Returns a MudBlazor icon string for the given MIME content type.
    /// Falls back to a generic file icon for unknown or null types.
    /// </summary>
    /// <param name="contentType">MIME type string, e.g. "image/jpeg" or "application/pdf".</param>
    /// <returns>A MudBlazor <see cref="Icons.Material.Filled"/> icon path string.</returns>
    public static string GetIcon(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        var t when t != null && t.StartsWith("image/") => Icons.Material.Filled.Image,
        "application/pdf" => Icons.Material.Filled.PictureAsPdf,
        // Spreadsheets — checked before generic "document" to avoid false matches on types
        // like "vnd.openxmlformats-officedocument.spreadsheetml.sheet" which contain both
        // "document" and "sheet" substrings.
        var t when t != null && (t.Contains("sheet") || t.Contains("excel") || t.Contains("csv")) => Icons.Material.Filled.TableChart,
        var t when t != null && (t.Contains("word") || t.Contains("document")) => Icons.Material.Filled.Description,
        var t when t != null && (t.Contains("zip") || t.Contains("compressed") || t.Contains("archive") || t.Contains("tar")) => Icons.Material.Filled.FolderZip,
        var t when t != null && t.StartsWith("video/") => Icons.Material.Filled.VideoFile,
        var t when t != null && t.StartsWith("audio/") => Icons.Material.Filled.AudioFile,
        _ => Icons.Material.Filled.InsertDriveFile
    };
}
