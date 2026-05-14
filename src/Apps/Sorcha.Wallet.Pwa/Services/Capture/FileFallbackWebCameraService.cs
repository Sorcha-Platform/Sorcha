// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Components.User.Services.Capture;

namespace Sorcha.Wallet.Pwa.Services.Capture;

/// <summary>
/// File-upload fallback for <see cref="IWebCameraService"/> (Feature 125, T051).
/// Reports the camera as unsupported and returns null from
/// <see cref="CapturePortraitAsync"/>; consumers (e.g.
/// <c>PortraitCaptureControl</c>) fall back to the &lt;input type="file"&gt;
/// path when this is the registered implementation.
/// </summary>
/// <remarks>
/// The native-camera implementation (<c>WebCameraService</c>) lands behind
/// this same interface in a follow-up alongside <c>webcamera-bridge.js</c>.
/// Until then, file upload + client-side canvas resize is the v1 path and
/// the spec's <c>quickstart.md</c> "common gotchas" already documents the
/// fallback.
/// </remarks>
public sealed class FileFallbackWebCameraService : IWebCameraService
{
    private readonly ILogger<FileFallbackWebCameraService> _logger;
    private readonly IJSRuntime _js;

    /// <summary>Initialise a new fallback service.</summary>
    public FileFallbackWebCameraService(IJSRuntime js, ILogger<FileFallbackWebCameraService> logger)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<bool> IsCameraSupportedAsync(CancellationToken ct = default)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task<string?> CapturePortraitAsync(PortraitCaptureRequest request, CancellationToken ct = default)
    {
        // Mobile-camera capture is wired via webcamera-bridge.js in a follow-
        // up. Returning null causes PortraitCaptureControl to fall back to
        // the file-upload input + canvas-resize path.
        _logger.LogInformation(
            "Camera capture requested but not supported on this device; falling back to file upload " +
            "({Width}x{Height} target, quality {Quality}).",
            request.TargetWidth, request.TargetHeight, request.JpegQuality);
        return Task.FromResult<string?>(null);
    }
}
