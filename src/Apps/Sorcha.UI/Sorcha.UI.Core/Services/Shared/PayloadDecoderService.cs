// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Decodes and inspects transaction payload data, providing Base64 decoding,
/// JSON formatting, and content type detection.
/// </summary>
public class PayloadDecoderService : IPayloadDecoderService
{
    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    /// <inheritdoc />
    public string TrimBase64(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return raw
            .Replace("\n", string.Empty)
            .Replace("\r", string.Empty)
            .Trim();
    }

    /// <inheritdoc />
    public string? DecodeBase64ToUtf8(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public (bool IsJson, string? PrettyPrinted) TryFormatJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var prettyPrinted = JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
            return (true, prettyPrinted);
        }
        catch
        {
            return (false, null);
        }
    }

    /// <inheritdoc />
    public string DetectContentType(string? declaredType, string? decodedText)
    {
        if (!string.IsNullOrWhiteSpace(declaredType))
        {
            return declaredType;
        }

        if (decodedText is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(decodedText);
                return "application/json";
            }
            catch
            {
                // Not JSON — check if it looks like valid UTF-8 text
            }

            // If we got a non-null decoded string, it was valid UTF-8
            return "text/plain";
        }

        return "application/octet-stream";
    }
}
