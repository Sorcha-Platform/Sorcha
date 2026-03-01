// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sodium;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Cryptography.Utilities;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Concrete implementation of cryptographic operations for ED25519, NIST P-256, RSA-4096,
/// ML-DSA-65 (FIPS 204), SLH-DSA-128s (FIPS 205), and ML-KEM-768 (FIPS 203).
/// </summary>
public class CryptoModule : ICryptoModule
{
    private readonly PqcSignatureProvider _pqcSignatureProvider = new();
    private readonly PqcEncapsulationProvider _pqcEncapsulationProvider = new();
    /// <summary>
    /// Generates a new cryptographic key pair.
    /// </summary>
    public async Task<CryptoResult<KeySet>> GenerateKeySetAsync(
        WalletNetworks network,
        byte[]? seed = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return network switch
            {
                WalletNetworks.ED25519 => await GenerateED25519KeySetAsync(seed, cancellationToken),
                WalletNetworks.NISTP256 => await GenerateNISTP256KeySetAsync(seed, cancellationToken),
                WalletNetworks.RSA4096 => await GenerateRSA4096KeySetAsync(cancellationToken),
                WalletNetworks.ML_DSA_65 => await Task.Run(() => _pqcSignatureProvider.GenerateMlDsa65KeyPair(), cancellationToken),
                WalletNetworks.SLH_DSA_128s => await Task.Run(() => _pqcSignatureProvider.GenerateSlhDsa128sKeyPair(), cancellationToken),
                WalletNetworks.SLH_DSA_192s => await Task.Run(() => _pqcSignatureProvider.GenerateSlhDsa192sKeyPair(), cancellationToken),
                WalletNetworks.ML_KEM_768 => await Task.Run(() => _pqcEncapsulationProvider.GenerateMlKem768KeyPair(), cancellationToken),
                _ => CryptoResult<KeySet>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed, $"Key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Recovers a key set from key data (private key bytes).
    /// For ED25519: expects 64-byte private key (or 32-byte seed).
    /// For NIST P-256: expects 32-byte private key (D parameter).
    /// For RSA-4096: expects DER-encoded RSA private key.
    /// </summary>
    public async Task<CryptoResult<KeySet>> RecoverKeySetAsync(
        WalletNetworks network,
        byte[] keyData,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (keyData == null || keyData.Length == 0)
                return CryptoResult<KeySet>.Failure(CryptoStatus.InvalidParameter, "Key data cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            // If password is provided, treat keyData as encrypted: decrypt first
            byte[] rawKeyData = keyData;
            if (!string.IsNullOrEmpty(password))
            {
                var decryptResult = DecryptKeyDataWithPassword(keyData, password);
                if (!decryptResult.IsSuccess)
                    return CryptoResult<KeySet>.Failure(decryptResult.Status, decryptResult.ErrorMessage ?? "Decryption failed");
                rawKeyData = decryptResult.Value!;
            }

            return network switch
            {
                WalletNetworks.ED25519 => await RecoverED25519KeySetAsync(rawKeyData, cancellationToken),
                WalletNetworks.NISTP256 => await RecoverNISTP256KeySetAsync(rawKeyData, cancellationToken),
                WalletNetworks.RSA4096 => await RecoverRSA4096KeySetAsync(rawKeyData, cancellationToken),
                WalletNetworks.ML_DSA_65 => await Task.Run(() => RecoverPqcKeySet(WalletNetworks.ML_DSA_65, rawKeyData), cancellationToken),
                WalletNetworks.SLH_DSA_128s => await Task.Run(() => RecoverPqcKeySet(WalletNetworks.SLH_DSA_128s, rawKeyData), cancellationToken),
                WalletNetworks.SLH_DSA_192s => await Task.Run(() => RecoverPqcKeySet(WalletNetworks.SLH_DSA_192s, rawKeyData), cancellationToken),
                WalletNetworks.ML_KEM_768 => await Task.Run(() => RecoverPqcKeySet(WalletNetworks.ML_KEM_768, rawKeyData), cancellationToken),
                _ => CryptoResult<KeySet>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed, $"Key recovery failed: {ex.Message}");
        }
    }

    private Task<CryptoResult<KeySet>> RecoverED25519KeySetAsync(byte[] keyData, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            KeyPair keyPair;
            if (keyData.Length == 32)
            {
                // 32-byte seed: generate key pair from seed
                keyPair = PublicKeyAuth.GenerateKeyPair(keyData);
            }
            else if (keyData.Length == 64)
            {
                // 64-byte private key: extract public key
                var publicKey = PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(keyData);
                return CryptoResult<KeySet>.Success(new KeySet
                {
                    PrivateKey = new CryptoKey(WalletNetworks.ED25519, keyData),
                    PublicKey = new CryptoKey(WalletNetworks.ED25519, publicKey)
                });
            }
            else
            {
                return CryptoResult<KeySet>.Failure(CryptoStatus.InvalidKey,
                    "ED25519 key data must be 32 bytes (seed) or 64 bytes (private key)");
            }

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.ED25519, keyPair.PrivateKey),
                PublicKey = new CryptoKey(WalletNetworks.ED25519, keyPair.PublicKey)
            });
        }, ct);
    }

    private Task<CryptoResult<KeySet>> RecoverNISTP256KeySetAsync(byte[] keyData, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (keyData.Length != 32)
                return CryptoResult<KeySet>.Failure(CryptoStatus.InvalidKey,
                    "NIST P-256 key data must be 32 bytes (D parameter)");

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = keyData
            };

            using var ecdsa = ECDsa.Create(parameters);
            var exportedParams = ecdsa.ExportParameters(false);

            var publicKey = new byte[64];
            Array.Copy(exportedParams.Q.X!, 0, publicKey, 0, 32);
            Array.Copy(exportedParams.Q.Y!, 0, publicKey, 32, 32);

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.NISTP256, keyData),
                PublicKey = new CryptoKey(WalletNetworks.NISTP256, publicKey)
            });
        }, ct);
    }

    private Task<CryptoResult<KeySet>> RecoverRSA4096KeySetAsync(byte[] keyData, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(keyData, out _);

            var publicKeyBytes = rsa.ExportRSAPublicKey();

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.RSA4096, keyData),
                PublicKey = new CryptoKey(WalletNetworks.RSA4096, publicKeyBytes)
            });
        }, ct);
    }

    private static CryptoResult<byte[]> DecryptKeyDataWithPassword(byte[] encryptedData, string password)
    {
        try
        {
            // Format: [16-byte salt][12-byte nonce][16-byte tag][ciphertext]
            const int saltLen = 16;
            const int nonceLen = 12;
            const int tagLen = 16;
            var minLen = saltLen + nonceLen + tagLen + 1;

            if (encryptedData.Length < minLen)
                return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, "Encrypted data too short");

            var salt = encryptedData.AsSpan(0, saltLen);
            var nonce = encryptedData.AsSpan(saltLen, nonceLen);
            var tag = encryptedData.AsSpan(saltLen + nonceLen, tagLen);
            var ciphertext = encryptedData.AsSpan(saltLen + nonceLen + tagLen);

            // Derive key from password via PBKDF2
            var key = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, 100_000, HashAlgorithmName.SHA256, 32);

            var plaintext = new byte[ciphertext.Length];
            using var aesGcm = new AesGcm(key, tagLen);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return CryptoResult<byte[]>.Success(plaintext);
        }
        catch (CryptographicException)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed, "Wrong password or corrupted data");
        }
    }

    /// <summary>
    /// Signs a hash with a private key.
    /// </summary>
    public async Task<CryptoResult<byte[]>> SignAsync(
        byte[] hash,
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (hash == null || hash.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Hash cannot be null or empty");

            if (privateKey == null || privateKey.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            var networkType = (WalletNetworks)network;
            return networkType switch
            {
                WalletNetworks.ED25519 => await SignED25519Async(hash, privateKey, cancellationToken),
                WalletNetworks.NISTP256 => await SignNISTP256Async(hash, privateKey, cancellationToken),
                WalletNetworks.RSA4096 => await SignRSA4096Async(hash, privateKey, cancellationToken),
                WalletNetworks.ML_DSA_65 => await Task.Run(() => _pqcSignatureProvider.SignMlDsa65(hash, privateKey), cancellationToken),
                WalletNetworks.SLH_DSA_128s => await Task.Run(() => _pqcSignatureProvider.SignSlhDsa128s(hash, privateKey), cancellationToken),
                WalletNetworks.SLH_DSA_192s => await Task.Run(() => _pqcSignatureProvider.SignSlhDsa192s(hash, privateKey), cancellationToken),
                WalletNetworks.ML_KEM_768 => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "ML-KEM-768 is a KEM, not a signature algorithm"),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.SigningFailed, $"Signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies a signature against a hash and public key.
    /// </summary>
    public async Task<CryptoStatus> VerifyAsync(
        byte[] signature,
        byte[] hash,
        byte network,
        byte[] publicKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (signature == null || signature.Length == 0)
                return CryptoStatus.InvalidParameter;

            if (hash == null || hash.Length == 0)
                return CryptoStatus.InvalidParameter;

            if (publicKey == null || publicKey.Length == 0)
                return CryptoStatus.InvalidKey;

            cancellationToken.ThrowIfCancellationRequested();

            var networkType = (WalletNetworks)network;
            return networkType switch
            {
                WalletNetworks.ED25519 => await VerifyED25519Async(signature, hash, publicKey, cancellationToken),
                WalletNetworks.NISTP256 => await VerifyNISTP256Async(signature, hash, publicKey, cancellationToken),
                WalletNetworks.RSA4096 => await VerifyRSA4096Async(signature, hash, publicKey, cancellationToken),
                WalletNetworks.ML_DSA_65 => await Task.Run(() => _pqcSignatureProvider.VerifyMlDsa65(hash, signature, publicKey), cancellationToken),
                WalletNetworks.SLH_DSA_128s => await Task.Run(() => _pqcSignatureProvider.VerifySlhDsa128s(hash, signature, publicKey), cancellationToken),
                WalletNetworks.SLH_DSA_192s => await Task.Run(() => _pqcSignatureProvider.VerifySlhDsa192s(hash, signature, publicKey), cancellationToken),
                WalletNetworks.ML_KEM_768 => CryptoStatus.InvalidParameter,
                _ => CryptoStatus.InvalidParameter
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoStatus.Cancelled;
        }
        catch
        {
            return CryptoStatus.InvalidSignature;
        }
    }

    /// <summary>
    /// Encrypts data with a public key.
    /// </summary>
    public async Task<CryptoResult<byte[]>> EncryptAsync(
        byte[] data,
        byte network,
        byte[] publicKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (data == null || data.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Data cannot be null or empty");

            if (publicKey == null || publicKey.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Public key cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            var networkType = (WalletNetworks)network;
            return networkType switch
            {
                WalletNetworks.ED25519 => await EncryptED25519Async(data, publicKey, cancellationToken),
                WalletNetworks.NISTP256 => await EncryptNISTP256Async(data, publicKey, cancellationToken),
                WalletNetworks.RSA4096 => await EncryptRSA4096Async(data, publicKey, cancellationToken),
                WalletNetworks.ML_KEM_768 => await _pqcEncapsulationProvider.EncryptWithKemAsync(data, publicKey, cancellationToken),
                WalletNetworks.ML_DSA_65 => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "ML-DSA-65 is a signature algorithm, not an encryption scheme"),
                WalletNetworks.SLH_DSA_128s => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "SLH-DSA-128s is a signature algorithm, not an encryption scheme"),
                WalletNetworks.SLH_DSA_192s => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "SLH-DSA-192s is a signature algorithm, not an encryption scheme"),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.EncryptionFailed, $"Encryption failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrypts data with a private key.
    /// </summary>
    public async Task<CryptoResult<byte[]>> DecryptAsync(
        byte[] ciphertext,
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (ciphertext == null || ciphertext.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Ciphertext cannot be null or empty");

            if (privateKey == null || privateKey.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            var networkType = (WalletNetworks)network;
            return networkType switch
            {
                WalletNetworks.ED25519 => await DecryptED25519Async(ciphertext, privateKey, cancellationToken),
                WalletNetworks.NISTP256 => await DecryptNISTP256Async(ciphertext, privateKey, cancellationToken),
                WalletNetworks.RSA4096 => await DecryptRSA4096Async(ciphertext, privateKey, cancellationToken),
                WalletNetworks.ML_KEM_768 => await _pqcEncapsulationProvider.DecryptWithKemAsync(ciphertext, privateKey, cancellationToken),
                WalletNetworks.ML_DSA_65 => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "ML-DSA-65 is a signature algorithm, not an encryption scheme"),
                WalletNetworks.SLH_DSA_128s => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "SLH-DSA-128s is a signature algorithm, not an encryption scheme"),
                WalletNetworks.SLH_DSA_192s => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "SLH-DSA-192s is a signature algorithm, not an encryption scheme"),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
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
    /// Calculates public key from private key.
    /// </summary>
    public async Task<CryptoResult<byte[]>> CalculatePublicKeyAsync(
        byte network,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (privateKey == null || privateKey.Length == 0)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            var networkType = (WalletNetworks)network;
            return networkType switch
            {
                WalletNetworks.ED25519 => await CalculateED25519PublicKeyAsync(privateKey, cancellationToken),
                WalletNetworks.NISTP256 => await CalculateNISTP256PublicKeyAsync(privateKey, cancellationToken),
                WalletNetworks.RSA4096 => await CalculateRSA4096PublicKeyAsync(privateKey, cancellationToken),
                WalletNetworks.ML_DSA_65 => await Task.Run(() => _pqcSignatureProvider.CalculateMlDsa65PublicKey(privateKey), cancellationToken),
                WalletNetworks.SLH_DSA_128s => await Task.Run(() => _pqcSignatureProvider.CalculateSlhDsa128sPublicKey(privateKey), cancellationToken),
                WalletNetworks.SLH_DSA_192s => await Task.Run(() => _pqcSignatureProvider.CalculateSlhDsa192sPublicKey(privateKey), cancellationToken),
                WalletNetworks.ML_KEM_768 => await Task.Run(() => _pqcEncapsulationProvider.CalculateMlKem768PublicKey(privateKey), cancellationToken),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Unsupported network type: {network}")
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.UnexpectedError, $"Failed to calculate public key: {ex.Message}");
        }
    }

    private Task<CryptoResult<KeySet>> GenerateED25519KeySetAsync(byte[]? seed, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            KeyPair keyPair;
            if (seed != null && seed.Length > 0)
            {
                // Ensure seed is exactly 32 bytes
                byte[] ed25519Seed = new byte[32];
                Array.Copy(seed, 0, ed25519Seed, 0, Math.Min(seed.Length, 32));
                keyPair = PublicKeyAuth.GenerateKeyPair(ed25519Seed);
            }
            else
            {
                keyPair = PublicKeyAuth.GenerateKeyPair();
            }

            var keySet = new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.ED25519, keyPair.PrivateKey),
                PublicKey = new CryptoKey(WalletNetworks.ED25519, keyPair.PublicKey)
            };

            return CryptoResult<KeySet>.Success(keySet);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> SignED25519Async(byte[] hash, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 64)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "ED25519 private key must be 64 bytes");

            var signature = PublicKeyAuth.SignDetached(hash, privateKey);
            return CryptoResult<byte[]>.Success(signature);
        }, cancellationToken);
    }

    private Task<CryptoStatus> VerifyED25519Async(byte[] signature, byte[] hash, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (publicKey.Length != 32)
                return CryptoStatus.InvalidKey;

            if (signature.Length != 64)
                return CryptoStatus.InvalidSignature;

            bool isValid = PublicKeyAuth.VerifyDetached(signature, hash, publicKey);
            return isValid ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> EncryptED25519Async(byte[] data, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (publicKey.Length != 32)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "ED25519 public key must be 32 bytes");

            // Convert Ed25519 public key to Curve25519 for encryption
            var curve25519PublicKey = PublicKeyAuth.ConvertEd25519PublicKeyToCurve25519PublicKey(publicKey);
            var ciphertext = SealedPublicKeyBox.Create(data, curve25519PublicKey);

            return CryptoResult<byte[]>.Success(ciphertext);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptED25519Async(byte[] ciphertext, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 64)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "ED25519 private key must be 64 bytes");

            // Convert Ed25519 private key to Curve25519 for decryption
            var curve25519PrivateKey = PublicKeyAuth.ConvertEd25519SecretKeyToCurve25519SecretKey(privateKey);

            // Extract public key from private key and convert to Curve25519
            var ed25519PublicKey = PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(privateKey);
            var curve25519PublicKey = PublicKeyAuth.ConvertEd25519PublicKeyToCurve25519PublicKey(ed25519PublicKey);

            var plaintext = SealedPublicKeyBox.Open(ciphertext, curve25519PrivateKey, curve25519PublicKey);

            return CryptoResult<byte[]>.Success(plaintext);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> CalculateED25519PublicKeyAsync(byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 64)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "ED25519 private key must be 64 bytes");

            var publicKey = PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(privateKey);
            return CryptoResult<byte[]>.Success(publicKey);
        }, cancellationToken);
    }

    private Task<CryptoResult<KeySet>> GenerateNISTP256KeySetAsync(byte[]? seed, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdsa.ExportParameters(true);

            // Private key is the D parameter
            var privateKey = parameters.D!;

            // Public key is X and Y coordinates concatenated
            var publicKey = new byte[64];
            Array.Copy(parameters.Q.X!, 0, publicKey, 0, 32);
            Array.Copy(parameters.Q.Y!, 0, publicKey, 32, 32);

            var keySet = new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.NISTP256, privateKey),
                PublicKey = new CryptoKey(WalletNetworks.NISTP256, publicKey)
            };

            return CryptoResult<KeySet>.Success(keySet);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> SignNISTP256Async(byte[] hash, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 32)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "NIST P-256 private key must be 32 bytes");

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey
            };

            using var ecdsa = ECDsa.Create(parameters);
            var signature = ecdsa.SignHash(hash);

            return CryptoResult<byte[]>.Success(signature);
        }, cancellationToken);
    }

    private Task<CryptoStatus> VerifyNISTP256Async(byte[] signature, byte[] hash, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (publicKey.Length != 64)
                return CryptoStatus.InvalidKey;

            var qx = new byte[32];
            var qy = new byte[32];
            Array.Copy(publicKey, 0, qx, 0, 32);
            Array.Copy(publicKey, 32, qy, 0, 32);

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = qx, Y = qy }
            };

            using var ecdsa = ECDsa.Create(parameters);
            bool isValid = ecdsa.VerifyHash(hash, signature);

            return isValid ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> EncryptNISTP256Async(byte[] data, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (publicKey.Length != 64)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "NIST P-256 public key must be 64 bytes (uncompressed X + Y)");

            // 1. Generate ephemeral P-256 key pair
            using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var ephemeralParams = ephemeral.ExportParameters(false);

            // 2. Import recipient's public key for ECDH
            var qx = new byte[32];
            var qy = new byte[32];
            Array.Copy(publicKey, 0, qx, 0, 32);
            Array.Copy(publicKey, 32, qy, 0, 32);
            var recipientParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = qx, Y = qy }
            };
            using var recipientKey = ECDiffieHellman.Create(recipientParams);

            // 3. ECDH to derive shared secret, then HKDF-SHA256 to get AES key
            var sharedSecret = ephemeral.DeriveKeyFromHash(
                recipientKey.PublicKey,
                HashAlgorithmName.SHA256,
                null, // no prepend
                new byte[] { 0x53, 0x6F, 0x72, 0x63, 0x68, 0x61, 0x45, 0x43, 0x49, 0x45, 0x53 }); // "SorchaECIES" append

            // Take first 32 bytes as AES-256 key
            var aesKey = sharedSecret[..32];

            // 4. AES-256-GCM encrypt
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            var ciphertext = new byte[data.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(aesKey, 16);
            aes.Encrypt(nonce, data, ciphertext, tag);

            // 5. Pack: [ephemeral X (32)] [ephemeral Y (32)] [nonce (12)] [ciphertext (var)] [tag (16)]
            // Total: 64 + 12 + data.Length + 16 = 92 + data.Length
            var ephemeralPubX = ephemeralParams.Q.X!;
            var ephemeralPubY = ephemeralParams.Q.Y!;
            var result = new byte[64 + 12 + ciphertext.Length + 16];
            Array.Copy(ephemeralPubX, 0, result, 0, 32);
            Array.Copy(ephemeralPubY, 0, result, 32, 32);
            Array.Copy(nonce, 0, result, 64, 12);
            Array.Copy(ciphertext, 0, result, 76, ciphertext.Length);
            Array.Copy(tag, 0, result, 76 + ciphertext.Length, 16);

            // 6. Zeroize sensitive material
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(aesKey);

            return CryptoResult<byte[]>.Success(result);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptNISTP256Async(byte[] ciphertext, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 32)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "NIST P-256 private key must be 32 bytes");

            // Minimum: 64 (ephemeral pub) + 12 (nonce) + 1 (min cipher) + 16 (tag) = 93
            if (ciphertext.Length < 93)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Ciphertext too short for ECIES");

            // 1. Unpack: [ephemeral X (32)] [ephemeral Y (32)] [nonce (12)] [ciphertext (var)] [tag (16)]
            var ephemeralX = ciphertext[..32];
            var ephemeralY = ciphertext[32..64];
            var nonce = ciphertext[64..76];
            var encryptedData = ciphertext[76..^16];
            var tag = ciphertext[^16..];

            // 2. Import ephemeral public key
            var ephemeralParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = ephemeralX, Y = ephemeralY }
            };
            using var ephemeralKey = ECDiffieHellman.Create(ephemeralParams);

            // 3. Import our private key for ECDH
            var recipientParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey
            };
            using var recipientKey = ECDiffieHellman.Create(recipientParams);

            // 4. ECDH + HKDF-SHA256 to derive AES key (same as encrypt)
            var sharedSecret = recipientKey.DeriveKeyFromHash(
                ephemeralKey.PublicKey,
                HashAlgorithmName.SHA256,
                null,
                new byte[] { 0x53, 0x6F, 0x72, 0x63, 0x68, 0x61, 0x45, 0x43, 0x49, 0x45, 0x53 }); // "SorchaECIES"
            var aesKey = sharedSecret[..32];

            // 5. AES-256-GCM decrypt
            var plaintext = new byte[encryptedData.Length];
            using var aes = new AesGcm(aesKey, 16);
            aes.Decrypt(nonce, encryptedData, tag, plaintext);

            // 6. Zeroize
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(aesKey);

            return CryptoResult<byte[]>.Success(plaintext);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> CalculateNISTP256PublicKeyAsync(byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (privateKey.Length != 32)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "NIST P-256 private key must be 32 bytes");

            var parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = privateKey
            };

            using var ecdsa = ECDsa.Create(parameters);
            var exportedParams = ecdsa.ExportParameters(false);

            var publicKey = new byte[64];
            Array.Copy(exportedParams.Q.X!, 0, publicKey, 0, 32);
            Array.Copy(exportedParams.Q.Y!, 0, publicKey, 32, 32);

            return CryptoResult<byte[]>.Success(publicKey);
        }, cancellationToken);
    }

    private Task<CryptoResult<KeySet>> GenerateRSA4096KeySetAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rsa = RSA.Create(4096);

            var privateKeyBytes = rsa.ExportRSAPrivateKey();
            var publicKeyBytes = rsa.ExportRSAPublicKey();

            var keySet = new KeySet
            {
                PrivateKey = new CryptoKey(WalletNetworks.RSA4096, privateKeyBytes),
                PublicKey = new CryptoKey(WalletNetworks.RSA4096, publicKeyBytes)
            };

            return CryptoResult<KeySet>.Success(keySet);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> SignRSA4096Async(byte[] hash, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);

            var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return CryptoResult<byte[]>.Success(signature);
        }, cancellationToken);
    }

    private Task<CryptoStatus> VerifyRSA4096Async(byte[] signature, byte[] hash, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(publicKey, out _);

            bool isValid = rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return isValid ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> EncryptRSA4096Async(byte[] data, byte[] publicKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // RSA-4096 can only encrypt small amounts of data
            // Maximum is (key_size_in_bytes - padding_overhead)
            // For OAEP with SHA-256: 4096/8 - 2*32 - 2 = 446 bytes max
            if (data.Length > 446)
                return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter,
                    "Data too large for RSA-4096 encryption (max 446 bytes)");

            using var rsa = RSA.Create();
            rsa.ImportRSAPublicKey(publicKey, out _);

            var ciphertext = rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);

            return CryptoResult<byte[]>.Success(ciphertext);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> DecryptRSA4096Async(byte[] ciphertext, byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);

            var plaintext = rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);

            return CryptoResult<byte[]>.Success(plaintext);
        }, cancellationToken);
    }

    private Task<CryptoResult<byte[]>> CalculateRSA4096PublicKeyAsync(byte[] privateKey, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);

            var publicKeyBytes = rsa.ExportRSAPublicKey();

            return CryptoResult<byte[]>.Success(publicKeyBytes);
        }, cancellationToken);
    }

    /// <summary>
    /// Concurrently signs data with both a classical and PQC key, producing a HybridSignature.
    /// </summary>
    public async Task<CryptoResult<HybridSignature>> HybridSignAsync(
        byte[] hash,
        byte classicalNetwork,
        byte[] classicalPrivateKey,
        byte pqcNetwork,
        byte[] pqcPrivateKey,
        byte[] pqcPublicKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (hash == null || hash.Length == 0)
                return CryptoResult<HybridSignature>.Failure(CryptoStatus.InvalidParameter, "Hash cannot be null or empty");

            cancellationToken.ThrowIfCancellationRequested();

            // Sign concurrently
            var classicalTask = SignAsync(hash, classicalNetwork, classicalPrivateKey, cancellationToken);
            var pqcTask = SignAsync(hash, pqcNetwork, pqcPrivateKey, cancellationToken);
            await Task.WhenAll(classicalTask, pqcTask);

            var classicalResult = classicalTask.Result;
            var pqcResult = pqcTask.Result;

            if (!classicalResult.IsSuccess)
                return CryptoResult<HybridSignature>.Failure(classicalResult.Status,
                    $"Classical signing failed: {classicalResult.ErrorMessage}");

            if (!pqcResult.IsSuccess)
                return CryptoResult<HybridSignature>.Failure(pqcResult.Status,
                    $"PQC signing failed: {pqcResult.ErrorMessage}");

            var hybrid = new HybridSignature
            {
                Classical = Convert.ToBase64String(classicalResult.Value!),
                ClassicalAlgorithm = AlgorithmMapper.ToAlgorithmName((WalletNetworks)classicalNetwork),
                Pqc = Convert.ToBase64String(pqcResult.Value!),
                PqcAlgorithm = AlgorithmMapper.ToAlgorithmName((WalletNetworks)pqcNetwork),
                WitnessPublicKey = Convert.ToBase64String(pqcPublicKey)
            };

            return CryptoResult<HybridSignature>.Success(hybrid);
        }
        catch (OperationCanceledException)
        {
            return CryptoResult<HybridSignature>.Failure(CryptoStatus.Cancelled, "Operation was cancelled");
        }
        catch (Exception ex)
        {
            return CryptoResult<HybridSignature>.Failure(CryptoStatus.SigningFailed, $"Hybrid signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies a HybridSignature using the specified verification mode.
    /// </summary>
    public async Task<CryptoStatus> HybridVerifyAsync(
        HybridSignature hybridSignature,
        byte[] hash,
        byte[]? classicalPublicKey,
        HybridVerificationMode mode = HybridVerificationMode.Strict,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (hybridSignature == null || !hybridSignature.IsValid())
                return CryptoStatus.InvalidParameter;

            if (hash == null || hash.Length == 0)
                return CryptoStatus.InvalidParameter;

            cancellationToken.ThrowIfCancellationRequested();

            bool classicalValid = false;
            bool pqcValid = false;

            // Verify classical component if present
            if (!string.IsNullOrEmpty(hybridSignature.Classical) && classicalPublicKey != null)
            {
                if (AlgorithmMapper.TryParseAlgorithm(hybridSignature.ClassicalAlgorithm!, out var classicalNetwork))
                {
                    var sig = Convert.FromBase64String(hybridSignature.Classical);
                    var result = await VerifyAsync(sig, hash, (byte)classicalNetwork, classicalPublicKey, cancellationToken);
                    classicalValid = result == CryptoStatus.Success;
                }
            }

            // Verify PQC component if present (public key from WitnessPublicKey)
            if (!string.IsNullOrEmpty(hybridSignature.Pqc) && !string.IsNullOrEmpty(hybridSignature.WitnessPublicKey))
            {
                if (AlgorithmMapper.TryParseAlgorithm(hybridSignature.PqcAlgorithm!, out var pqcNetworkEnum))
                {
                    var sig = Convert.FromBase64String(hybridSignature.Pqc);
                    var pubKey = Convert.FromBase64String(hybridSignature.WitnessPublicKey);
                    var result = await VerifyAsync(sig, hash, (byte)pqcNetworkEnum, pubKey, cancellationToken);
                    pqcValid = result == CryptoStatus.Success;
                }
            }

            return mode switch
            {
                HybridVerificationMode.Strict => (classicalValid && pqcValid) ? CryptoStatus.Success : CryptoStatus.InvalidSignature,
                HybridVerificationMode.Permissive => (classicalValid || pqcValid) ? CryptoStatus.Success : CryptoStatus.InvalidSignature,
                _ => CryptoStatus.InvalidSignature
            };
        }
        catch (OperationCanceledException)
        {
            return CryptoStatus.Cancelled;
        }
        catch
        {
            return CryptoStatus.InvalidSignature;
        }
    }

    private CryptoResult<KeySet> RecoverPqcKeySet(WalletNetworks network, byte[] keyData)
    {
        try
        {
            var publicKeyResult = network switch
            {
                WalletNetworks.ML_DSA_65 => _pqcSignatureProvider.CalculateMlDsa65PublicKey(keyData),
                WalletNetworks.SLH_DSA_128s => _pqcSignatureProvider.CalculateSlhDsa128sPublicKey(keyData),
                WalletNetworks.SLH_DSA_192s => _pqcSignatureProvider.CalculateSlhDsa192sPublicKey(keyData),
                WalletNetworks.ML_KEM_768 => _pqcEncapsulationProvider.CalculateMlKem768PublicKey(keyData),
                _ => CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, $"Not a PQC network: {network}")
            };

            if (!publicKeyResult.IsSuccess)
                return CryptoResult<KeySet>.Failure(publicKeyResult.Status, publicKeyResult.ErrorMessage);

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PrivateKey = new CryptoKey(network, keyData),
                PublicKey = new CryptoKey(network, publicKeyResult.Value!)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.InvalidKey,
                $"PQC key recovery failed: {ex.Message}");
        }
    }

}
