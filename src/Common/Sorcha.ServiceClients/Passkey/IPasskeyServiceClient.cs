// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Passkey;

/// <summary>
/// HTTP client for retrieving passkey public keys from the Tenant Service.
/// Used by Wallet Service for recovery key wrapping.
/// </summary>
public interface IPasskeyServiceClient
{
    /// <summary>
    /// Gets the primary passkey's public key for recovery key wrapping.
    /// </summary>
    /// <param name="userId">The platform user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Passkey recovery key info, or null if no passkey registered.</returns>
    Task<PasskeyRecoveryKeyInfo?> GetRecoveryPublicKeyAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Passkey public key information for recovery key wrapping.
/// </summary>
public record PasskeyRecoveryKeyInfo
{
    /// <summary>FIDO2 credential ID (Base64).</summary>
    public required string CredentialId { get; init; }

    /// <summary>CBOR-encoded COSE public key (Base64).</summary>
    public required string PublicKeyCose { get; init; }

    /// <summary>Algorithm identifier (e.g., "ES256", "RS256").</summary>
    public required string Algorithm { get; init; }
}
