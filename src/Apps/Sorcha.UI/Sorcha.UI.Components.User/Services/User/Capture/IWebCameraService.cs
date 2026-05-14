// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Capture;

/// <summary>
/// Captures a portrait image from the device camera (Feature 125, T050).
/// Used by <c>PortraitCaptureControl</c> when an action schema field
/// carries <c>x-file.capture: "user"</c> + <c>embedAs: "image-token-jpeg-240x320"</c>.
/// </summary>
/// <remarks>
/// <para>
/// v1 ships the interface and a desktop-fallback implementation that uses
/// the standard HTML file-upload picker. The mobile-camera-first
/// implementation lands behind the same interface once
/// <c>webcamera-bridge.js</c> is added; consumers don't change.
/// </para>
/// <para>
/// The resulting JPEG token is the 240×320 wallet-standard format from
/// Feature 107's <c>x-file.embedAs</c> contract — a base64 string the
/// caller embeds into the action payload at the field's pointer.
/// </para>
/// </remarks>
public interface IWebCameraService
{
    /// <summary>True when this device exposes a usable camera.</summary>
    Task<bool> IsCameraSupportedAsync(CancellationToken ct = default);

    /// <summary>
    /// Capture a portrait and return a base64-encoded JPEG sized to the
    /// platform's wallet-standard format. Returns null on user cancel.
    /// Throws on permission denial — callers map this to the
    /// camera-permission-denied recovery scaffold.
    /// </summary>
    Task<string?> CapturePortraitAsync(PortraitCaptureRequest request, CancellationToken ct = default);
}

/// <summary>Input to <see cref="IWebCameraService.CapturePortraitAsync"/>.</summary>
/// <param name="TargetWidth">Output image width in pixels.</param>
/// <param name="TargetHeight">Output image height in pixels.</param>
/// <param name="JpegQuality">Encoder quality 0..1; default 0.85 balances size + clarity.</param>
public sealed record PortraitCaptureRequest(
    int TargetWidth = 240,
    int TargetHeight = 320,
    double JpegQuality = 0.85);
