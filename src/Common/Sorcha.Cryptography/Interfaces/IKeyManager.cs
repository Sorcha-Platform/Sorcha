// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Threading;
using System.Threading.Tasks;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides key management operations including mnemonic generation and recovery.
/// </summary>
public interface IKeyManager
{
    /// <summary>
    /// Creates a master key ring with mnemonic recovery phrase.
    /// </summary>
    /// <param name="network">The wallet network/algorithm type.</param>
    /// <param name="password">Optional password for additional security.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the key ring with mnemonic or error status.</returns>
    Task<CryptoResult<KeyRing>> CreateMasterKeyRingAsync(
        WalletNetworks network,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers a key ring from a mnemonic recovery phrase.
    /// </summary>
    /// <param name="mnemonic">The mnemonic recovery phrase (12, 15, 18, 21, or 24 words).</param>
    /// <param name="password">Optional password if one was used during creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the recovered key ring or error status.</returns>
    Task<CryptoResult<KeyRing>> RecoverMasterKeyRingAsync(
        string mnemonic,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a random mnemonic phrase.
    /// </summary>
    /// <param name="wordCount">The number of words (12, 15, 18, 21, or 24). Defaults to 24.</param>
    /// <returns>A result containing the mnemonic phrase or error status.</returns>
    CryptoResult<string> GenerateMnemonic(int wordCount = 24);

    /// <summary>
    /// Validates a mnemonic phrase.
    /// </summary>
    /// <param name="mnemonic">The mnemonic phrase to validate.</param>
    /// <returns>True if the mnemonic is valid.</returns>
    bool ValidateMnemonic(string mnemonic);

    /// <summary>
    /// Converts a mnemonic to a seed for key derivation.
    /// </summary>
    /// <param name="mnemonic">The mnemonic phrase.</param>
    /// <param name="password">Optional password.</param>
    /// <returns>A result containing the seed bytes or error status.</returns>
    CryptoResult<byte[]> MnemonicToSeed(string mnemonic, string? password = null);
}
