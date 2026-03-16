// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using NBitcoin;

namespace Sorcha.Wallet.Core.Domain.ValueObjects;

/// <summary>
/// Represents a BIP44 derivation path
/// </summary>
public record DerivationPath
{
    private readonly KeyPath _keyPath;

    /// <summary>
    /// Creates a derivation path from a string
    /// </summary>
    /// <param name="path">The path string (e.g., m/44'/0'/0'/0/0)</param>
    public DerivationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Derivation path cannot be empty", nameof(path));

        try
        {
            _keyPath = new KeyPath(path);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid derivation path", nameof(path), ex);
        }
    }

    /// <summary>
    /// Creates a derivation path from NBitcoin.KeyPath
    /// </summary>
    internal DerivationPath(KeyPath keyPath)
    {
        _keyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
    }

    /// <summary>
    /// Creates a BIP44 derivation path
    /// </summary>
    /// <param name="coinType">Coin type (e.g., 0 for Bitcoin, 60 for Ethereum)</param>
    /// <param name="account">Account index</param>
    /// <param name="change">Change index (0 for receive, 1 for change)</param>
    /// <param name="addressIndex">Address index</param>
    /// <returns>A BIP44 derivation path</returns>
    public static DerivationPath CreateBip44(uint coinType = 0, uint account = 0, uint change = 0, uint addressIndex = 0)
    {
        // BIP44: m / purpose' / coin_type' / account' / change / address_index
        var path = $"m/44'/{coinType}'/{account}'/{change}/{addressIndex}";
        return new DerivationPath(path);
    }

    /// <summary>
    /// Creates a BIP44 derivation path for PQC algorithms.
    /// Uses coin type 1' (reserved for PQC in Sorcha).
    /// </summary>
    public static DerivationPath CreatePqcBip44(uint account = 0, uint change = 0, uint addressIndex = 0)
    {
        // PQC coin type = 1' (distinguishes from classical derivation paths)
        return CreateBip44(coinType: 1, account: account, change: change, addressIndex: addressIndex);
    }

    /// <summary>
    /// Gets the path as a string
    /// </summary>
    public string Path => _keyPath.ToString();

    /// <summary>
    /// Gets the NBitcoin KeyPath
    /// </summary>
    internal KeyPath KeyPath => _keyPath;

    /// <summary>
    /// Parses a BIP44 path and extracts components
    /// </summary>
    /// <param name="path">The path to parse</param>
    /// <param name="coinType">Output coin type</param>
    /// <param name="account">Output account</param>
    /// <param name="change">Output change</param>
    /// <param name="addressIndex">Output address index</param>
    /// <returns>True if successfully parsed as BIP44</returns>
    public static bool TryParseBip44(string path, out uint coinType, out uint account, out uint change, out uint addressIndex)
    {
        coinType = account = change = addressIndex = 0;

        try
        {
            var keyPath = new KeyPath(path);
            var indices = keyPath.Indexes;

            if (indices.Length != 5)
                return false;

            // m/44'/coinType'/account'/change/addressIndex
            const uint hardenedBit = 0x80000000;
            if (indices[0] != 44 + hardenedBit)
                return false;

            coinType = indices[1] & ~hardenedBit;
            account = indices[2] & ~hardenedBit;
            change = indices[3];
            addressIndex = indices[4];

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the BIP44 derivation path as a string representation.
    /// </summary>
    /// <returns>The derivation path string (e.g., "m/44'/0'/0'/0/0").</returns>
    public override string ToString() => Path;
}
