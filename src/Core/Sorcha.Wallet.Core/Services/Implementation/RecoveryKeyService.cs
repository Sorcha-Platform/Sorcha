// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Manages recovery key lifecycle using Sorcha.Cryptography for symmetric and asymmetric operations.
/// </summary>
public class RecoveryKeyService : IRecoveryKeyService
{
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ICryptoModule _cryptoModule;
    private readonly ILogger<RecoveryKeyService> _logger;

    public RecoveryKeyService(
        ISymmetricCrypto symmetricCrypto,
        ICryptoModule cryptoModule,
        ILogger<RecoveryKeyService> logger)
    {
        _symmetricCrypto = symmetricCrypto;
        _cryptoModule = cryptoModule;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<byte[]> GenerateRecoveryKeyAsync(CancellationToken cancellationToken = default)
    {
        var key = RandomNumberGenerator.GetBytes(32); // AES-256 = 32 bytes
        _logger.LogDebug("Generated new AES-256 recovery key");
        return Task.FromResult(key);
    }

    /// <inheritdoc/>
    public async Task<string> WrapRecoveryKeyAsync(
        byte[] recoveryKey, byte[] recipientPublicKey, string algorithm,
        CancellationToken cancellationToken = default)
    {
        var network = MapAlgorithmToNetwork(algorithm);
        var result = await _cryptoModule.EncryptAsync(recoveryKey, (byte)network, recipientPublicKey, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            throw new CryptographicException(
                $"Failed to wrap recovery key with {algorithm}: {result.ErrorMessage}");
        }

        _logger.LogDebug("Wrapped recovery key using {Algorithm}", algorithm);
        return Convert.ToBase64String(result.Value);
    }

    /// <inheritdoc/>
    public async Task<byte[]> UnwrapRecoveryKeyAsync(
        string encryptedRecoveryKey, byte[] recipientPrivateKey, string algorithm,
        CancellationToken cancellationToken = default)
    {
        var ciphertext = Convert.FromBase64String(encryptedRecoveryKey);
        var network = MapAlgorithmToNetwork(algorithm);
        var result = await _cryptoModule.DecryptAsync(ciphertext, (byte)network, recipientPrivateKey, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            throw new CryptographicException(
                $"Failed to unwrap recovery key with {algorithm}: {result.ErrorMessage}");
        }

        _logger.LogDebug("Unwrapped recovery key using {Algorithm}", algorithm);
        return result.Value;
    }

    /// <inheritdoc/>
    public async Task<string> EncryptMasterKeyAsync(
        byte[] masterKey, byte[] recoveryKey,
        CancellationToken cancellationToken = default)
    {
        var result = await _symmetricCrypto.EncryptAsync(
            masterKey, EncryptionType.AES_GCM, recoveryKey, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            throw new CryptographicException(
                $"Failed to encrypt master key: {result.ErrorMessage}");
        }

        // Serialize IV + ciphertext as a single blob
        using var ciphertext = result.Value;
        var blob = SerializeCiphertext(ciphertext.IV, ciphertext.Data);
        _logger.LogDebug("Encrypted master key with recovery key (AES-256-GCM)");
        return Convert.ToBase64String(blob);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DecryptMasterKeyAsync(
        string encryptedBlob, byte[] recoveryKey,
        CancellationToken cancellationToken = default)
    {
        var blob = Convert.FromBase64String(encryptedBlob);
        var (iv, data) = DeserializeCiphertext(blob);

        var ciphertext = new Sorcha.Cryptography.Models.SymmetricCiphertext
        {
            Data = data,
            Key = recoveryKey,
            IV = iv,
            Type = EncryptionType.AES_GCM
        };

        var result = await _symmetricCrypto.DecryptAsync(ciphertext, cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            throw new CryptographicException(
                $"Failed to decrypt master key: {result.ErrorMessage}");
        }

        _logger.LogDebug("Decrypted master key with recovery key");
        return result.Value;
    }

    private static WalletNetworks MapAlgorithmToNetwork(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "ED25519" => WalletNetworks.ED25519,
            "NISTP256" or "ES256" or "P-256" => WalletNetworks.NISTP256,
            "RSA4096" or "RS256" => WalletNetworks.RSA4096,
            _ => throw new ArgumentException($"Unsupported algorithm for key wrapping: {algorithm}")
        };

    /// <summary>
    /// Serializes IV + ciphertext into a single byte array: [4-byte IV length][IV][ciphertext].
    /// </summary>
    private static byte[] SerializeCiphertext(byte[] iv, byte[] data)
    {
        var result = new byte[4 + iv.Length + data.Length];
        BitConverter.GetBytes(iv.Length).CopyTo(result, 0);
        iv.CopyTo(result, 4);
        data.CopyTo(result, 4 + iv.Length);
        return result;
    }

    /// <summary>
    /// Deserializes a blob back into IV + ciphertext.
    /// </summary>
    private static (byte[] IV, byte[] Data) DeserializeCiphertext(byte[] blob)
    {
        var ivLength = BitConverter.ToInt32(blob, 0);
        var iv = new byte[ivLength];
        Array.Copy(blob, 4, iv, 0, ivLength);
        var data = new byte[blob.Length - 4 - ivLength];
        Array.Copy(blob, 4 + ivLength, data, 0, data.Length);
        return (iv, data);
    }
}
