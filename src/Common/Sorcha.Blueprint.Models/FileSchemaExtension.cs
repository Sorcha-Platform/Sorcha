// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Represents the <c>x-file</c> extension from a JSON Schema property definition.
/// Carries file-upload constraints used by the UI for client-side validation
/// and by the validator service for server-side enforcement.
/// </summary>
public class FileSchemaExtension
{
    /// <summary>
    /// Maximum total upload size enforced by the platform: 40 MB.
    /// </summary>
    public const long PlatformMaxSizeBytes = 40L * 1024 * 1024;

    /// <summary>
    /// Maximum number of chunks the platform will accept per file: 10.
    /// </summary>
    public const int PlatformMaxChunks = 10;

    /// <summary>
    /// Default chunk size used when splitting a file for upload: 4 MB.
    /// </summary>
    public const long DefaultChunkSizeBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Allowed MIME types for the file field (e.g. <c>["image/jpeg", "image/png"]</c>).
    /// An empty array means all MIME types are permitted.
    /// </summary>
    [JsonPropertyName("accept")]
    public string[] Accept { get; set; } = [];

    /// <summary>
    /// Human-readable maximum file size string (e.g. <c>"16MB"</c>, <c>"512KB"</c>).
    /// Parse the numeric byte limit with <see cref="ParseMaxSizeBytes"/>.
    /// </summary>
    [JsonPropertyName("maxSizePerFile")]
    public string MaxSizePerFile { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of chunks allowed per file upload.
    /// Defaults to <see cref="PlatformMaxChunks"/> (10) and may not exceed it.
    /// </summary>
    [JsonPropertyName("maxChunks")]
    public int MaxChunks { get; set; } = PlatformMaxChunks;

    /// <summary>
    /// Recognised values for the <see cref="Capture"/> hint.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidCaptureValues =
        new HashSet<string>(StringComparer.Ordinal) { "user", "environment" };

    /// <summary>
    /// Recognised values for the <see cref="EmbedAs"/> hint. v1 supports the
    /// ISO/IEC 19794-5 token-image dimensions (240×320) only.
    /// </summary>
    public static readonly IReadOnlySet<string> ValidEmbedAsValues =
        new HashSet<string>(StringComparer.Ordinal) { "image-token-jpeg-240x320" };

    /// <summary>
    /// Camera-capture hint for mobile browsers. <c>"user"</c> requests the
    /// front-facing camera; <c>"environment"</c> requests the rear-facing
    /// camera; <see langword="null"/> disables the attribute and the renderer
    /// falls back to a plain file picker (legacy behaviour).
    /// </summary>
    /// <remarks>
    /// Feature 107 Assured Identity v1. See
    /// <c>specs/107-assured-identity-v1/contracts/x-file-capture-extension.md</c>.
    /// Unknown values are preserved as-is so the UI can log a warning rather
    /// than silently coercing; see <see cref="IsValidCapture"/>.
    /// </remarks>
    [JsonPropertyName("capture")]
    public string? Capture { get; set; }

    /// <summary>
    /// Client-side embedding hint. <c>"image-token-jpeg-240x320"</c> advises
    /// the renderer to produce a resized 240×320 JPEG token alongside the
    /// chunked full-resolution upload, carried in the action payload as a
    /// base64 string at <c>{fieldPointer}/tokenImageBase64</c>.
    /// <see langword="null"/> disables resize (legacy behaviour).
    /// </summary>
    /// <remarks>
    /// Feature 107 Assured Identity v1. See
    /// <c>specs/107-assured-identity-v1/contracts/portrait-claim-format.md</c>.
    /// </remarks>
    [JsonPropertyName("embedAs")]
    public string? EmbedAs { get; set; }

    /// <summary>
    /// True when <paramref name="value"/> is a recognised <see cref="Capture"/>
    /// value (<c>"user"</c> or <c>"environment"</c>). <see langword="null"/>
    /// is considered valid (attribute absent = legacy behaviour).
    /// </summary>
    public static bool IsValidCapture(string? value) =>
        value is null || ValidCaptureValues.Contains(value);

    /// <summary>
    /// True when <paramref name="value"/> is a recognised <see cref="EmbedAs"/>
    /// value. <see langword="null"/> is considered valid (no resize).
    /// </summary>
    public static bool IsValidEmbedAs(string? value) =>
        value is null || ValidEmbedAsValues.Contains(value);

    /// <summary>
    /// Parses a human-readable size string such as <c>"16MB"</c> or <c>"512KB"</c>
    /// into the equivalent number of bytes.
    /// </summary>
    /// <param name="sizeStr">
    /// The size string to parse. Must end with <c>MB</c> or <c>KB</c> (case-insensitive)
    /// preceded by a positive integer or decimal number.
    /// </param>
    /// <returns>The size expressed as a <see cref="long"/> number of bytes.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sizeStr"/> is null, empty, or does not match a
    /// recognised format.
    /// </exception>
    public static long ParseMaxSizeBytes(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr))
            throw new ArgumentException("Size string must not be null or empty.", nameof(sizeStr));

        var trimmed = sizeStr.Trim();

        if (trimmed.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
        {
            var numeric = trimmed[..^2].Trim();
            if (double.TryParse(numeric, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mb))
                return (long)(mb * 1024 * 1024);
        }
        else if (trimmed.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
        {
            var numeric = trimmed[..^2].Trim();
            if (double.TryParse(numeric, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var kb))
                return (long)(kb * 1024);
        }

        throw new ArgumentException(
            $"Unrecognised size format '{sizeStr}'. Expected a value ending with MB or KB, e.g. \"16MB\" or \"512KB\".",
            nameof(sizeStr));
    }

    /// <summary>
    /// Attempts to read the <c>x-file</c> extension property from a JSON Schema element
    /// and deserialise it into a <see cref="FileSchemaExtension"/> instance.
    /// </summary>
    /// <param name="schema">The JSON Schema element to inspect.</param>
    /// <param name="extension">
    /// When this method returns <see langword="true"/>, contains the parsed
    /// <see cref="FileSchemaExtension"/>; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the <c>x-file</c> property was present and could be
    /// deserialised; <see langword="false"/> otherwise.
    /// </returns>
    public static bool TryParseFromSchema(JsonElement schema, out FileSchemaExtension? extension)
    {
        extension = null;

        if (schema.ValueKind != JsonValueKind.Object)
            return false;

        if (!schema.TryGetProperty("x-file", out var xFileElement))
            return false;

        if (xFileElement.ValueKind != JsonValueKind.Object)
            return false;

        try
        {
            extension = JsonSerializer.Deserialize<FileSchemaExtension>(xFileElement.GetRawText());
            return extension is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
