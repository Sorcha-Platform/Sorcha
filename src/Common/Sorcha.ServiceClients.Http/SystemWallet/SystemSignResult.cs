// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// Result of a system wallet signing operation
/// </summary>
public record SystemSignResult
{
    /// <summary>
    /// Raw cryptographic signature bytes
    /// </summary>
    public required byte[] Signature { get; init; }

    /// <summary>
    /// Public key bytes of the signing wallet
    /// </summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>
    /// Signing algorithm used (e.g. "ED25519")
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Address of the wallet that produced the signature
    /// </summary>
    public required string WalletAddress { get; init; }
}
