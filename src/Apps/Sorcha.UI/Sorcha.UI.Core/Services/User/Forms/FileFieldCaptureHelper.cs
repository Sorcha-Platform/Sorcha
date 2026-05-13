// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Static helpers that translate a <see cref="FileSchemaExtension"/> into the
/// decisions the <c>FileRenderer.razor</c> needs at render time: what HTML
/// <c>capture</c> attribute to emit, and whether to run the portrait
/// token-image resizer after the citizen picks a file. Feature 107 Assured
/// Identity v1 (T015 / FR-003, FR-004).
/// </summary>
public static class FileFieldCaptureHelper
{
    /// <summary>
    /// Returns the HTML <c>capture</c> attribute value to emit on the
    /// <c>&lt;InputFile&gt;</c> element, or <see langword="null"/> when the
    /// attribute should be omitted (legacy file-picker behaviour).
    /// </summary>
    /// <remarks>
    /// Desktop browsers ignore <c>capture</c> by HTML spec; on mobile it
    /// opens the device camera with the requested facing direction. Unknown
    /// values are NOT propagated — we refuse to trust an arbitrary string
    /// into the DOM and emit no attribute instead.
    /// </remarks>
    public static string? GetCaptureAttribute(FileSchemaExtension? extension)
    {
        if (extension?.Capture is null) return null;
        return FileSchemaExtension.IsValidCapture(extension.Capture) ? extension.Capture : null;
    }

    /// <summary>
    /// True when the file field declares <c>embedAs</c> with a recognised
    /// token-image value, so the renderer should invoke the client-side
    /// token-image resizer after upload.
    /// </summary>
    public static bool ShouldResizeForEmbed(FileSchemaExtension? extension)
    {
        if (extension?.EmbedAs is null) return false;
        return FileSchemaExtension.IsValidEmbedAs(extension.EmbedAs);
    }
}
