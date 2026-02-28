// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sodium;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Extensions;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Implements symmetric encryption and decryption operations.
/// </summary>
public class SymmetricCrypto : ISymmetricCrypto
{
    /// <summary>
    /// Encrypts data with symmetric encryption.
    /// </summary>
    public async Task<CryptoResult<SymmetricCiphertext>> EncryptAsync(
        byte[] plaintext,
        EncryptionType encryptionType = EncryptionType.XCHACHA20_POLY1305,
        byte[]? key = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (plaintext == null || plaintext.Length == 0)
                return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.InvalidParameter, "Plaintext cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            // Generate key if not provided
            key ??= GenerateKey(encryptionType);

            // Validate key size
            int expectedKeySize = encryptionType.GetSymmetricKeySize();
            if (key.Length != expectedKeySize)
                return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.InvalidKey,
                    $"Key must be {expectedKeySize} bytes for {encryptionType}");

            return encryptionType switch
            {
#pragma warning disable CS0618 // Obsolete members referenced for runtime guard
                EncryptionType.AES_128 => throw new NotSupportedException("AES-CBC has been deprecated due to lack of authentication. Use AES_GCM or CHACHA20_POLY1305."),
                EncryptionType.AES_256 => throw new NotSupportedException("AES-CBC has been deprecated due to lack of authentication. Use AES_GCM or CHACHA20_POLY1305."),
#pragma warning restore CS0618
                EncryptionType.AES_GCM => await EncryptAesGcmAsync(plaintext, key, cancellationToken),
                EncryptionType.CHACHA20_POLY1305 => await EncryptChaCha20Poly1305Async(plaintext, key, cancellationToken),
                EncryptionType.XCHACHA20_POLY1305 => await EncryptXChaCha20Poly1305Async(plaintext, key, cancellationToken),
                _ => CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.InvalidParameter, $"Unsupported encryption type: {encryptionType}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.EncryptionFailed, $"Encryption failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrypts data with symmetric encryption.
    /// </summary>
    public async Task<CryptoResult<byte[]>> DecryptAsync(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (ciphertext == null)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Ciphertext cannot be null");

            if (ciphertext.Data == null || ciphertext.Data.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Ciphertext data cannot be null or empty");

            if (ciphertext.Key == null || ciphertext.Key.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Key cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            return ciphertext.Type switch
            {
#pragma warning disable CS0618 // Obsolete members referenced for runtime guard
                EncryptionType.AES_128 => throw new NotSupportedException("AES-CBC has been deprecated due to lack of authentication. Use AES_GCM or CHACHA20_POLY1305."),
                EncryptionType.AES_256 => throw new NotSupportedException("AES-CBC has been deprecated due to lack of authentication. Use AES_GCM or CHACHA20_POLY1305."),
#pragma warning restore CS0618
                EncryptionType.AES_GCM => await DecryptAesGcmAsync(ciphertext, cancellationToken),
                EncryptionType.CHACHA20_POLY1305 => await DecryptChaCha20Poly1305Async(ciphertext, cancellationToken),
                EncryptionType.XCHACHA20_POLY1305 => await DecryptXChaCha20Poly1305Async(ciphertext, cancellationToken),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Unsupported encryption type: {ciphertext.Type}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, $"Decryption failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a random encryption key for the specified encryption type.
    /// </summary>
    public byte[] GenerateKey(EncryptionType encryptionType)
    {
        int keySize = encryptionType.GetSymmetricKeySize();
        return RandomNumberGenerator.GetBytes(keySize);
    }

    /// <summary>
    /// Generates a random IV/nonce for the specified encryption type.
    /// </summary>
    public byte[] GenerateIV(EncryptionType encryptionType)
    {
        int ivSize = encryptionType.GetIVSize();
        return RandomNumberGenerator.GetBytes(ivSize);
    }

    private Task<CryptoResult<SymmetricCiphertext>> EncryptAesGcmAsync(
        byte[] plaintext,
        byte[] key,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] nonce = GenerateIV(EncryptionType.AES_GCM);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16]; // 128-bit authentication tag

            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            // Combine ciphertext and tag
            byte[] combined = new byte[ciphertext.Length + tag.Length];
            Array.Copy(ciphertext, 0, combined, 0, ciphertext.Length);
            Array.Copy(tag, 0, combined, ciphertext.Length, tag.Length);

            var result = new SymmetricCiphertext
            {
                Data = combined,
                Key = key,
                IV = nonce,
                Type = EncryptionType.AES_GCM
            };

            return CryptoResult<SymmetricCiphertext>.Success(result);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptAesGcmAsync(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ciphertext.Data.Length < 16)
                return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, "Ciphertext too short");

            // Split ciphertext and tag
            int tagLength = 16;
            byte[] encrypted = new byte[ciphertext.Data.Length - tagLength];
            byte[] tag = new byte[tagLength];
            Array.Copy(ciphertext.Data, 0, encrypted, 0, encrypted.Length);
            Array.Copy(ciphertext.Data, encrypted.Length, tag, 0, tagLength);

            byte[] plaintext = new byte[encrypted.Length];

            using var aesGcm = new AesGcm(ciphertext.Key, 16);
            aesGcm.Decrypt(ciphertext.IV, encrypted, tag, plaintext);

            return CryptoResult<byte[]>.Success(plaintext);
        }, cancellationToken);
    }

    private Task<CryptoResult<SymmetricCiphertext>> EncryptChaCha20Poly1305Async(
        byte[] plaintext,
        byte[] key,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] nonce = GenerateIV(EncryptionType.CHACHA20_POLY1305);

                // Use libsodium's ChaCha20-Poly1305
                // Additional data parameter must be an empty array (not null) when no AAD is needed
                byte[] ciphertext = SecretAeadChaCha20Poly1305.Encrypt(plaintext, nonce, key, Array.Empty<byte>());

                var result = new SymmetricCiphertext
                {
                    Data = ciphertext,
                    Key = key,
                    IV = nonce,
                    Type = EncryptionType.CHACHA20_POLY1305
                };

                return CryptoResult<SymmetricCiphertext>.Success(result);
            }
            catch (Exception ex)
            {
                return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.EncryptionFailed, $"ChaCha20-Poly1305 encryption failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptChaCha20Poly1305Async(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] plaintext = SecretAeadChaCha20Poly1305.Decrypt(ciphertext.Data, ciphertext.IV, ciphertext.Key, Array.Empty<byte>());

                return CryptoResult<byte[]>.Success(plaintext);
            }
            catch (Exception ex)
            {
                return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, $"ChaCha20-Poly1305 decryption failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    private Task<CryptoResult<SymmetricCiphertext>> EncryptXChaCha20Poly1305Async(
        byte[] plaintext,
        byte[] key,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] nonce = GenerateIV(EncryptionType.XCHACHA20_POLY1305);

                // Use libsodium's XChaCha20-Poly1305 (non-obsolete API)
                // Additional data parameter must be an empty array (not null) when no AAD is needed
                byte[] ciphertext = SecretAeadXChaCha20Poly1305.Encrypt(plaintext, nonce, key, Array.Empty<byte>());

                var result = new SymmetricCiphertext
                {
                    Data = ciphertext,
                    Key = key,
                    IV = nonce,
                    Type = EncryptionType.XCHACHA20_POLY1305
                };

                return CryptoResult<SymmetricCiphertext>.Success(result);
            }
            catch (Exception ex)
            {
                return CryptoResult<SymmetricCiphertext>.Failure(CryptoStatus.EncryptionFailed, $"XChaCha20-Poly1305 encryption failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptXChaCha20Poly1305Async(
        SymmetricCiphertext ciphertext,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] plaintext = SecretAeadXChaCha20Poly1305.Decrypt(ciphertext.Data, ciphertext.IV, ciphertext.Key, Array.Empty<byte>());

                return CryptoResult<byte[]>.Success(plaintext);
            }
            catch (Exception ex)
            {
                return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, $"XChaCha20-Poly1305 decryption failed: {ex.Message}");
            }
        }, cancellationToken);
    }

}
