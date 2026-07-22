// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Cryptography.Utilities;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Implements key management operations including BIP39 mnemonic support.
/// </summary>
public class KeyManager : IKeyManager
{
    private readonly ICryptoModule _cryptoModule;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyManager"/> class.
    /// </summary>
    /// <param name="cryptoModule">The crypto module for key operations. If null, creates a new instance.</param>
    public KeyManager(ICryptoModule? cryptoModule = null)
    {
        _cryptoModule = cryptoModule ?? new CryptoModule();
    }

    /// <summary>
    /// Creates a master key ring with mnemonic recovery phrase.
    /// </summary>
    public async Task<CryptoResult<KeyRing>> CreateMasterKeyRingAsync(
        WalletNetworks network,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate a 24-word mnemonic (only for ED25519 and NIST P-256)
            string? mnemonic = null;
            byte[]? seed = null;

            if (network == WalletNetworks.ED25519 || network == WalletNetworks.NISTP256)
            {
                var mnemonicResult = GenerateMnemonic(24);
                if (!mnemonicResult.IsSuccess || mnemonicResult.Value == null)
                    return CryptoResult<KeyRing>.Failure(mnemonicResult.Status, mnemonicResult.ErrorMessage);

                mnemonic = mnemonicResult.Value;

                // Convert mnemonic to seed
                var seedResult = MnemonicToSeed(mnemonic, password);
                if (!seedResult.IsSuccess || seedResult.Value == null)
                    return CryptoResult<KeyRing>.Failure(seedResult.Status, seedResult.ErrorMessage);

                seed = seedResult.Value;
            }

            // Generate key set (RSA doesn't use seed)
            var keySetResult = await _cryptoModule.GenerateKeySetAsync(network, seed, cancellationToken);
            if (!keySetResult.IsSuccess)
                return CryptoResult<KeyRing>.Failure(keySetResult.Status, keySetResult.ErrorMessage);

            var keyRing = new KeyRing
            {
                Network = network,
                Mnemonic = mnemonic,
                MasterKeySet = keySetResult.Value,
                PasswordHint = string.IsNullOrEmpty(password) ? null : "Password protected"
            };

            return CryptoResult<KeyRing>.Success(keyRing);
        }
        catch (Exception ex)
        {
            return CryptoResult<KeyRing>.Failure(CryptoStatus.KeyGenerationFailed, $"Failed to create key ring: {ex.Message}");
        }
    }

    /// <summary>
    /// Recovers a key ring from a mnemonic recovery phrase.
    /// </summary>
    public async Task<CryptoResult<KeyRing>> RecoverMasterKeyRingAsync(
        string mnemonic,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
                return CryptoResult<KeyRing>.Failure(CryptoStatus.InvalidParameter, "Mnemonic cannot be null or empty");

            // Validate mnemonic
            if (!ValidateMnemonic(mnemonic))
                return CryptoResult<KeyRing>.Failure(CryptoStatus.InvalidMnemonic, "Invalid mnemonic phrase");

            // Convert mnemonic to seed
            var seedResult = MnemonicToSeed(mnemonic, password);
            if (!seedResult.IsSuccess || seedResult.Value == null)
                return CryptoResult<KeyRing>.Failure(seedResult.Status, seedResult.ErrorMessage);

            var seed = seedResult.Value;

            // Try to determine network type (default to ED25519)
            // In a real implementation, this might be stored with the mnemonic or provided by the user
            var network = WalletNetworks.ED25519;

            // Generate key set from seed
            var keySetResult = await _cryptoModule.GenerateKeySetAsync(network, seed, cancellationToken);
            if (!keySetResult.IsSuccess)
                return CryptoResult<KeyRing>.Failure(keySetResult.Status, keySetResult.ErrorMessage);

            var keyRing = new KeyRing
            {
                Network = network,
                Mnemonic = mnemonic,
                MasterKeySet = keySetResult.Value,
                PasswordHint = string.IsNullOrEmpty(password) ? null : "Password protected"
            };

            return CryptoResult<KeyRing>.Success(keyRing);
        }
        catch (Exception ex)
        {
            return CryptoResult<KeyRing>.Failure(CryptoStatus.KeyGenerationFailed, $"Failed to recover key ring: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a random mnemonic phrase.
    /// </summary>
    public CryptoResult<string> GenerateMnemonic(int wordCount = 24)
    {
        try
        {
            // Validate word count (must be 12, 15, 18, 21, or 24)
            if (wordCount != 12 && wordCount != 15 && wordCount != 18 && wordCount != 21 && wordCount != 24)
                return CryptoResult<string>.Failure(CryptoStatus.InvalidParameter,
                    "Word count must be 12, 15, 18, 21, or 24");

            // Calculate entropy size
            // 12 words = 128 bits, 15 words = 160 bits, 18 words = 192 bits, 21 words = 224 bits, 24 words = 256 bits
            int entropyBits = (wordCount * 11) - (wordCount / 3);
            int entropyBytes = entropyBits / 8;

            // Generate random entropy
            byte[] entropy = RandomNumberGenerator.GetBytes(entropyBytes);

            // Calculate checksum
            byte[] hash = SHA256.HashData(entropy);
            int checksumBits = entropyBits / 32;

            // Combine entropy and checksum
            var bits = new List<bool>();

            // Add entropy bits
            foreach (byte b in entropy)
            {
                for (int i = 7; i >= 0; i--)
                {
                    bits.Add((b & (1 << i)) != 0);
                }
            }

            // Add checksum bits
            for (int i = 0; i < checksumBits; i++)
            {
                bits.Add((hash[0] & (1 << (7 - i))) != 0);
            }

            // Convert bits to words
            var words = new List<string>();
            for (int i = 0; i < wordCount; i++)
            {
                int wordIndex = 0;
                for (int j = 0; j < 11; j++)
                {
                    if (bits[i * 11 + j])
                        wordIndex |= 1 << (10 - j);
                }
                words.Add(Bip39WordList.GetWord(wordIndex));
            }

            return CryptoResult<string>.Success(string.Join(" ", words));
        }
        catch (Exception ex)
        {
            return CryptoResult<string>.Failure(CryptoStatus.UnexpectedError, $"Failed to generate mnemonic: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a mnemonic phrase.
    /// </summary>
    public bool ValidateMnemonic(string mnemonic)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
                return false;

            string[] words = mnemonic.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Validate word count
            if (words.Length != 12 && words.Length != 15 && words.Length != 18 && words.Length != 21 && words.Length != 24)
                return false;

            // Validate all words are in the BIP39 word list
            var wordIndices = new List<int>();
            foreach (var word in words)
            {
                int index = Bip39WordList.GetWordIndex(word);
                if (index == -1)
                    return false;
                wordIndices.Add(index);
            }

            // Convert word indices to bits
            var bits = new List<bool>();
            foreach (int index in wordIndices)
            {
                for (int i = 10; i >= 0; i--)
                {
                    bits.Add((index & (1 << i)) != 0);
                }
            }

            // Calculate checksum length
            int checksumBits = words.Length / 3;
            int entropyBits = bits.Count - checksumBits;

            // Extract entropy
            byte[] entropy = new byte[entropyBits / 8];
            for (int i = 0; i < entropy.Length; i++)
            {
                byte b = 0;
                for (int j = 0; j < 8; j++)
                {
                    if (bits[i * 8 + j])
                        b |= (byte)(1 << (7 - j));
                }
                entropy[i] = b;
            }

            // Calculate expected checksum
            byte[] hash = SHA256.HashData(entropy);

            // Verify checksum
            for (int i = 0; i < checksumBits; i++)
            {
                bool expectedBit = (hash[0] & (1 << (7 - i))) != 0;
                bool actualBit = bits[entropyBits + i];
                if (expectedBit != actualBit)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a mnemonic to a seed for key derivation.
    /// </summary>
    public CryptoResult<byte[]> MnemonicToSeed(string mnemonic, string? password = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Mnemonic cannot be null or empty");

            if (!ValidateMnemonic(mnemonic))
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidMnemonic, "Invalid mnemonic phrase");

            // Normalize mnemonic
            string normalizedMnemonic = string.Join(" ",
                mnemonic.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            // BIP39 specifies PBKDF2-HMAC-SHA512 with 2048 iterations
            // Salt is "mnemonic" + passphrase
            string salt = "mnemonic" + (password ?? "");

            byte[] mnemonicBytes = Encoding.UTF8.GetBytes(normalizedMnemonic);
            byte[] saltBytes = Encoding.UTF8.GetBytes(salt);

            // Use PBKDF2 to derive seed (BIP39 specifies 512-bit seed)
            byte[] seed = Rfc2898DeriveBytes.Pbkdf2(
                mnemonicBytes,
                saltBytes,
                2048,
                HashAlgorithmName.SHA512,
                64); // 512 bits / 64 bytes

            return CryptoResult<byte[]>.Success(seed);
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.UnexpectedError, $"Failed to convert mnemonic to seed: {ex.Message}");
        }
    }
}
