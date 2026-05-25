// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sorcha.ServiceClients.Register;

/// <summary>
/// Canonical content hash of a blueprint JSON (Feature 138 US4). Producer (the register's
/// blueprint-publish path) and consumer (the Blueprint Service recovery path) MUST hash the
/// identical canonical form, so this single helper defines it: parse the JSON, re-serialize with
/// no indentation and relaxed escaping (the same canonical options the publish path already uses to
/// compute the sealed payload hash), then SHA-256 to lowercase hex.
/// </summary>
public static class BlueprintContentHash
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Computes the canonical SHA-256 (lowercase hex) of <paramref name="blueprintJson"/>.
    /// Throws <see cref="JsonException"/> if the input is not valid JSON.
    /// </summary>
    public static string Compute(string blueprintJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintJson);
        using var doc = JsonDocument.Parse(blueprintJson);
        var canonical = JsonSerializer.Serialize(doc.RootElement, CanonicalOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
