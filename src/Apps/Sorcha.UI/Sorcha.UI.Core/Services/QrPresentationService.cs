// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using QRCoder;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Generates QR codes for OID4VP presentation requests.
/// </summary>
public interface IQrPresentationService
{
    /// <summary>
    /// Generates an SVG QR code for the given presentation request URL and nonce.
    /// </summary>
    string GenerateSvg(string requestUrl, string nonce, int pixelsPerModule = 10);

    /// <summary>
    /// Generates an SVG QR code for a raw URI (e.g. openid-credential-offer:// or openid4vp://).
    /// </summary>
    string GenerateSvgFromUri(string uri, int pixelsPerModule = 10);

    /// <summary>
    /// Generates a PNG QR code as a base64 data URI for the given presentation request URL and nonce.
    /// </summary>
    string GeneratePngDataUri(string requestUrl, string nonce, int pixelsPerModule = 10);

    /// <summary>
    /// Builds the OID4VP authorize URL from a request URL and nonce.
    /// </summary>
    string BuildOid4vpUrl(string requestUrl, string nonce);
}

public class QrPresentationService : IQrPresentationService
{
    public string GenerateSvg(string requestUrl, string nonce, int pixelsPerModule = 10)
    {
        var url = BuildOid4vpUrl(requestUrl, nonce);
        return GenerateSvgFromUri(url, pixelsPerModule);
    }

    public string GenerateSvgFromUri(string uri, int pixelsPerModule = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        using var svgQr = new SvgQRCode(data);

        return svgQr.GetGraphic(pixelsPerModule);
    }

    public string GeneratePngDataUri(string requestUrl, string nonce, int pixelsPerModule = 10)
    {
        var url = BuildOid4vpUrl(requestUrl, nonce);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var pngQr = new PngByteQRCode(data);

        var pngBytes = pngQr.GetGraphic(pixelsPerModule);
        var base64 = Convert.ToBase64String(pngBytes);
        return $"data:image/png;base64,{base64}";
    }

    public string BuildOid4vpUrl(string requestUrl, string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        return $"openid4vp://authorize?request_uri={Uri.EscapeDataString(requestUrl)}&nonce={nonce}";
    }
}
