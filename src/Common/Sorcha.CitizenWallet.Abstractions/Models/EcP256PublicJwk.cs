// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// JSON Web Key carrying the public half of an EC P-256 keypair, in the shape
/// produced by <c>SubtleCrypto.exportKey("jwk", ...)</c> on a public key.
/// </summary>
/// <remarks>
/// Used to transport device public keys from the citizen wallet PWA to the
/// Wallet Service during enrolment, and to embed device / holder public keys
/// in delegation and KB-JWT <c>cnf</c> claims. Always public — private fields
/// are never accepted on this type and are rejected during validation.
/// </remarks>
public sealed record EcP256PublicJwk
{
    /// <summary>Key type. MUST be <c>EC</c>.</summary>
    [JsonPropertyName("kty")]
    public string Kty { get; init; } = "EC";

    /// <summary>Curve. MUST be <c>P-256</c>.</summary>
    [JsonPropertyName("crv")]
    public string Crv { get; init; } = "P-256";

    /// <summary>X coordinate, base64url-encoded.</summary>
    [JsonPropertyName("x")]
    public string X { get; init; } = string.Empty;

    /// <summary>Y coordinate, base64url-encoded.</summary>
    [JsonPropertyName("y")]
    public string Y { get; init; } = string.Empty;
}
