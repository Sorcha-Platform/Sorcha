// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.UI.Components.User.Services.Capture;

/// <summary>
/// getUserMedia-backed <see cref="IWebCameraService"/> (Feature 174 follow-up). Bridges to the
/// <c>window.SorchaPortrait</c> JS module (loaded from <c>js/sorcha-portrait.js</c>) which opens the
/// device camera in a capture overlay and returns a resized base64 JPEG. Works on desktop and mobile
/// (unlike the native <c>&lt;input capture&gt;</c> path, which desktop browsers ignore).
/// </summary>
public sealed class WebCameraService(IJSRuntime js) : IWebCameraService
{
    private readonly IJSRuntime _js = js;

    /// <inheritdoc />
    public async Task<bool> IsCameraSupportedAsync(CancellationToken ct = default)
    {
        try
        {
            return await _js.InvokeAsync<bool>("SorchaPortrait.isCameraSupported", ct);
        }
        catch (JSException)
        {
            // Module not loaded (e.g. PWA host without the script) — treat as unsupported so the
            // control falls back to file upload rather than showing a dead camera button.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> CapturePortraitAsync(PortraitCaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await _js.InvokeAsync<string?>(
                "SorchaPortrait.capturePortrait",
                ct,
                request.TargetWidth,
                request.TargetHeight,
                request.JpegQuality);
        }
        catch (JSException ex) when (ex.Message.Contains("camera-permission-denied", StringComparison.Ordinal))
        {
            // Surface permission denial distinctly so the control shows the "check camera permission"
            // recovery message rather than a generic failure.
            throw new UnauthorizedAccessException("camera-permission-denied", ex);
        }
    }
}
