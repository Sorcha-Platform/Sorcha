// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Reads identity claims out of a JWT access token client-side. This does NOT validate the signature
/// — it is used only to attribute the per-device cache to a user (the "who owns this data" marker),
/// never to make an authorization decision. The server remains the sole authority on token validity.
/// </summary>
internal static class JwtIdentity
{
    /// <summary>
    /// Extracts the <c>sub</c> (subject — the caller's stable platform user id) claim from a JWT.
    /// Returns null if the token is missing, malformed, or carries no <c>sub</c>.
    /// </summary>
    public static string? ExtractSubject(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;

        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return doc.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String
                ? sub.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
