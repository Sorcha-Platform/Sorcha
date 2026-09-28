// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Provides ML-KEM-768 (FIPS 203) key encapsulation mechanism for quantum-safe key exchange.
/// <para>This provider is stateless — no key material is retained between calls.
/// Shared secrets returned in <see cref="EncapsulationResult"/> MUST be disposed to zeroize
/// the secret. Callers are responsible for zeroizing private key byte arrays via
/// <see cref="KeySet.Zeroize"/> or <see cref="CryptographicOperations.ZeroMemory"/>.</para>
/// </summary>
public sealed class PqcEncapsulationProvider : IDisposable
{
    /// <summary>ML-KEM-768 ciphertext size in bytes.</summary>
    public const int CiphertextSize = 1088;

    /// <summary>ML-KEM-768 shared secret size in bytes.</summary>
    public const int SharedSecretSize = 32;

    private bool _disposed;

    /// <summary>
    /// Generates an ML-KEM-768 key pair.
    /// </summary>
    public CryptoResult<KeySet> GenerateMlKem768KeyPair()
    {
        try
        {
            var generator = new MLKemKeyPairGenerator();
            generator.Init(new MLKemKeyGenerationParameters(new SecureRandom(), MLKemParameters.ml_kem_768));
            var keyPair = generator.GenerateKeyPair();

            var publicKey = ((MLKemPublicKeyParameters)keyPair.Public).GetEncoded();
            var privateKey = ((MLKemPrivateKeyParameters)keyPair.Private).GetEncoded();

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.ML_KEM_768, publicKey),
                PrivateKey = new CryptoKey(WalletNetworks.ML_KEM_768, privateKey)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"ML-KEM-768 key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Deterministically generates an ML-KEM-768 key pair from derived key material (#1689), using
    /// the FIPS 203 seed-based key generation (d ‖ z).
    /// </summary>
    /// <param name="derivedKey">The BIP32-derived key for the path; expanded per algorithm.</param>
    public CryptoResult<KeySet> GenerateMlKem768KeyPair(byte[] derivedKey)
    {
        try
        {
            var seed = PqcSeedDerivation.Expand(derivedKey, "ML-KEM-768", PqcSeedDerivation.MlKemSeedLength);
            var privateParams = MLKemPrivateKeyParameters.FromSeed(MLKemParameters.ml_kem_768, seed);

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.ML_KEM_768, privateParams.GetPublicKeyEncoded()),
                PrivateKey = new CryptoKey(WalletNetworks.ML_KEM_768, privateParams.GetEncoded())
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"ML-KEM-768 key derivation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Encapsulates a shared secret using the recipient's ML-KEM-768 public key.
    /// Returns the ciphertext and the shared secret.
    /// </summary>
    public CryptoResult<EncapsulationResult> Encapsulate(byte[] recipientPublicKey)
    {
        if (recipientPublicKey == null || recipientPublicKey.Length == 0)
            return CryptoResult<EncapsulationResult>.Failure(CryptoStatus.InvalidKey, "Recipient public key cannot be null or empty");

        try
        {
            var pubKeyParams = MLKemPublicKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, recipientPublicKey);
            var encapsulator = new MLKemEncapsulator(MLKemParameters.ml_kem_768);
            encapsulator.Init(pubKeyParams);

            var ciphertext = new byte[CiphertextSize];
            var sharedSecret = new byte[SharedSecretSize];
            encapsulator.Encapsulate(ciphertext, 0, ciphertext.Length, sharedSecret, 0, sharedSecret.Length);

            return CryptoResult<EncapsulationResult>.Success(new EncapsulationResult(ciphertext, sharedSecret));
        }
        catch (Exception ex)
        {
            return CryptoResult<EncapsulationResult>.Failure(CryptoStatus.EncryptionFailed,
                $"ML-KEM-768 encapsulation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Decapsulates a shared secret from ciphertext using the recipient's ML-KEM-768 private key.
    /// </summary>
    public CryptoResult<byte[]> Decapsulate(byte[] ciphertext, byte[] privateKey)
    {
        if (ciphertext == null || ciphertext.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Ciphertext cannot be null or empty");
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, privateKey);
            var decapsulator = new MLKemDecapsulator(MLKemParameters.ml_kem_768);
            decapsulator.Init(privKeyParams);

            var sharedSecret = new byte[SharedSecretSize];
            decapsulator.Decapsulate(ciphertext, 0, ciphertext.Length, sharedSecret, 0, sharedSecret.Length);

            return CryptoResult<byte[]>.Success(sharedSecret);
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.DecryptionFailed,
                $"ML-KEM-768 decapsulation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculates the ML-KEM-768 public key from a private key.
    /// </summary>
    public CryptoResult<byte[]> CalculateMlKem768PublicKey(byte[] privateKey)
    {
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = MLKemPrivateKeyParameters.FromEncoding(MLKemParameters.ml_kem_768, privateKey);
            return CryptoResult<byte[]>.Success(privKeyParams.GetPublicKeyEncoded());
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey,
                $"Failed to calculate ML-KEM-768 public key: {ex.Message}");
        }
    }

    /// <summary>
    /// Encrypts data using ML-KEM-768 encapsulation + XChaCha20-Poly1305 symmetric encryption.
    /// Returns the KEM ciphertext concatenated with the symmetric ciphertext.
    /// </summary>
    public async Task<CryptoResult<byte[]>> EncryptWithKemAsync(
        byte[] plaintext,
        byte[] recipientPublicKey,
        CancellationToken cancellationToken = default)
    {
        if (plaintext == null || plaintext.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Plaintext cannot be null or empty");

        // Encapsulate to get shared secret
        var encapResult = Encapsulate(recipientPublicKey);
        if (!encapResult.IsSuccess)
            return CryptoResult<byte[]>.Failure(encapResult.Status, encapResult.ErrorMessage);

        using var encap = encapResult.Value!;

        // Use shared secret as XChaCha20-Poly1305 key
        var symmetricCrypto = new SymmetricCrypto();
        var encryptResult = await symmetricCrypto.EncryptAsync(
            plaintext, Enums.EncryptionType.XCHACHA20_POLY1305, encap.SharedSecret, cancellationToken);

        if (!encryptResult.IsSuccess)
            return CryptoResult<byte[]>.Failure(encryptResult.Status, encryptResult.ErrorMessage);

        var sym = encryptResult.Value!;

        // Pack: [KEM ciphertext (1088)] [nonce (24)] [symmetric ciphertext (variable)]
        var packed = new byte[CiphertextSize + sym.IV.Length + sym.Data.Length];
        Array.Copy(encap.Ciphertext, 0, packed, 0, CiphertextSize);
        Array.Copy(sym.IV, 0, packed, CiphertextSize, sym.IV.Length);
        Array.Copy(sym.Data, 0, packed, CiphertextSize + sym.IV.Length, sym.Data.Length);

        return CryptoResult<byte[]>.Success(packed);
    }

    /// <summary>
    /// Decrypts data using ML-KEM-768 decapsulation + XChaCha20-Poly1305 symmetric decryption.
    /// </summary>
    public async Task<CryptoResult<byte[]>> DecryptWithKemAsync(
        byte[] packedCiphertext,
        byte[] privateKey,
        CancellationToken cancellationToken = default)
    {
        if (packedCiphertext == null || packedCiphertext.Length <= CiphertextSize + 24)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Packed ciphertext too short");

        // Unpack: [KEM ciphertext (1088)] [nonce (24)] [symmetric ciphertext]
        var kemCiphertext = packedCiphertext[..CiphertextSize];
        var nonce = packedCiphertext[CiphertextSize..(CiphertextSize + 24)];
        var symCiphertext = packedCiphertext[(CiphertextSize + 24)..];

        // Decapsulate to recover shared secret
        var decapResult = Decapsulate(kemCiphertext, privateKey);
        if (!decapResult.IsSuccess)
            return CryptoResult<byte[]>.Failure(decapResult.Status, decapResult.ErrorMessage);

        // Decrypt with shared secret
        var symmetricCrypto = new SymmetricCrypto();
        var cipherObj = new Models.SymmetricCiphertext
        {
            Data = symCiphertext,
            Key = decapResult.Value!,
            IV = nonce,
            Type = Enums.EncryptionType.XCHACHA20_POLY1305
        };

        return await symmetricCrypto.DecryptAsync(cipherObj, cancellationToken);
    }

    /// <summary>
    /// Zeroizes any retained key material and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of an ML-KEM encapsulation operation.
/// </summary>
/// <param name="Ciphertext">The encapsulated ciphertext to send to the recipient.</param>
/// <param name="SharedSecret">The shared secret derived from encapsulation.</param>
public record EncapsulationResult(byte[] Ciphertext, byte[] SharedSecret) : IDisposable
{
    /// <summary>
    /// Zeroizes the shared secret.
    /// </summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(SharedSecret);
    }
}
