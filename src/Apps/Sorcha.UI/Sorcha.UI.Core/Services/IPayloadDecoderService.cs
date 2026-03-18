// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for decoding and inspecting transaction payload data.
/// </summary>
public interface IPayloadDecoderService
{
    /// <summary>
    /// Strips leading/trailing whitespace, LF, and CR from raw Base64 strings.
    /// </summary>
    /// <param name="raw">Raw Base64 string potentially containing whitespace.</param>
    /// <returns>Trimmed Base64 string.</returns>
    string TrimBase64(string raw);

    /// <summary>
    /// Decodes a Base64 string to a UTF-8 text string.
    /// </summary>
    /// <param name="base64">Base64-encoded string.</param>
    /// <returns>Decoded UTF-8 string, or null if decoding fails.</returns>
    string? DecodeBase64ToUtf8(string base64);

    /// <summary>
    /// Attempts to parse text as JSON and pretty-print it.
    /// </summary>
    /// <param name="text">Text to attempt JSON parsing on.</param>
    /// <returns>Tuple indicating whether text is valid JSON and the pretty-printed result.</returns>
    (bool IsJson, string? PrettyPrinted) TryFormatJson(string text);

    /// <summary>
    /// Detects the MIME content type of decoded payload data.
    /// </summary>
    /// <param name="declaredType">Explicitly declared content type, if any.</param>
    /// <param name="decodedText">Decoded text content for inference.</param>
    /// <returns>Detected MIME type string.</returns>
    string DetectContentType(string? declaredType, string? decodedText);
}
