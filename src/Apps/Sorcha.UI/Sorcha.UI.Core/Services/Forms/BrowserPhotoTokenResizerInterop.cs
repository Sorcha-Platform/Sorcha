// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Browser-side implementation of <see cref="IPhotoTokenResizerInterop"/> that
/// delegates to a JS module performing the canvas-based scale + JPEG encode.
/// Feature 107 Assured Identity v1 (T016).
/// </summary>
/// <remarks>
/// The JS module lives at <c>_content/Sorcha.UI.Core/js/photo-token-resizer.js</c>
/// and exports a single <c>resizeAndEncodeJpeg(sourceBytes, width, height, quality)</c>
/// function. The module is lazily imported on first use.
/// </remarks>
public sealed class BrowserPhotoTokenResizerInterop : IPhotoTokenResizerInterop, IAsyncDisposable
{
    private const string ModulePath = "./_content/Sorcha.UI.Core/js/photo-token-resizer.js";

    private readonly Lazy<Task<IJSObjectReference>> _module;

    public BrowserPhotoTokenResizerInterop(IJSRuntime jsRuntime)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);
        _module = new Lazy<Task<IJSObjectReference>>(() =>
            jsRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());
    }

    public async Task<byte[]> ResizeAndEncodeJpegAsync(
        byte[] sourceBytes,
        int width,
        int height,
        double quality,
        CancellationToken cancellationToken)
    {
        var module = await _module.Value.ConfigureAwait(false);
        return await module.InvokeAsync<byte[]>(
            "resizeAndEncodeJpeg",
            cancellationToken,
            sourceBytes, width, height, quality)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module.IsValueCreated)
        {
            try
            {
                var module = await _module.Value.ConfigureAwait(false);
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Circuit already dropped — nothing to release.
            }
        }
    }
}
