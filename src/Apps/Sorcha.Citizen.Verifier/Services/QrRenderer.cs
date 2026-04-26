// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using QRCoder;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Server-side QR rendering — produces a base64 PNG data URL for any payload.
/// QRCoder is the existing Sorcha-blessed encoder (see <c>Sorcha.UI.Core/Services/QrPresentationService.cs</c>).
/// </summary>
public sealed class QrRenderer
{
    /// <summary>Render a payload to a data URL suitable for an <c>&lt;img src&gt;</c>.</summary>
    public string RenderDataUrl(string payload, int pixelsPerModule = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }
}
