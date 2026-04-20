// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Thin wrapper over the browser-side <c>&lt;canvas&gt;</c> + JPEG export
/// path used by <see cref="PhotoTokenResizer"/>. Wrapping the JS interop
/// behind an interface lets unit tests exercise the quality-stepping logic
/// without a browser runtime. Feature 107 Assured Identity v1 (T016).
/// </summary>
public interface IPhotoTokenResizerInterop
{
    /// <summary>
    /// Loads <paramref name="sourceBytes"/> onto a canvas, scales + centre-crops
    /// to <paramref name="width"/>×<paramref name="height"/>, and exports a
    /// JPEG at the requested quality. Returns the raw JPEG bytes.
    /// </summary>
    Task<byte[]> ResizeAndEncodeJpegAsync(
        byte[] sourceBytes,
        int width,
        int height,
        double quality,
        CancellationToken cancellationToken);
}
