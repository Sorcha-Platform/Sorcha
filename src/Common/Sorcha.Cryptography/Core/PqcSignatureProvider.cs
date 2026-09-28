// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Provides ML-DSA-65 (FIPS 204) and SLH-DSA-128s (FIPS 205) post-quantum digital signature operations.
/// <para>This provider is stateless — no key material is retained between calls.
/// Callers are responsible for zeroizing private key byte arrays via <see cref="KeySet.Zeroize"/>
/// or <see cref="CryptographicOperations.ZeroMemory"/>.</para>
/// </summary>
public sealed class PqcSignatureProvider : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Generates an ML-DSA-65 key pair.
    /// </summary>
    public CryptoResult<KeySet> GenerateMlDsa65KeyPair()
    {
        try
        {
            var generator = new MLDsaKeyPairGenerator();
            generator.Init(new MLDsaKeyGenerationParameters(new SecureRandom(), MLDsaParameters.ml_dsa_65));
            var keyPair = generator.GenerateKeyPair();

            var publicKey = ((MLDsaPublicKeyParameters)keyPair.Public).GetEncoded();
            var privateKey = ((MLDsaPrivateKeyParameters)keyPair.Private).GetEncoded();

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.ML_DSA_65, publicKey),
                PrivateKey = new CryptoKey(WalletNetworks.ML_DSA_65, privateKey)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"ML-DSA-65 key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Deterministically generates an ML-DSA-65 key pair from derived key material (#1689), using the
    /// FIPS 204 seed-based key generation. Same input, same key — which is what lets a wallet be
    /// recovered and its purpose keys re-derived.
    /// </summary>
    /// <param name="derivedKey">The BIP32-derived key for the path; expanded per algorithm.</param>
    public CryptoResult<KeySet> GenerateMlDsa65KeyPair(byte[] derivedKey)
    {
        try
        {
            var seed = PqcSeedDerivation.Expand(derivedKey, "ML-DSA-65", PqcSeedDerivation.MlDsaSeedLength);
            var privateParams = MLDsaPrivateKeyParameters.FromSeed(MLDsaParameters.ml_dsa_65, seed);

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.ML_DSA_65, privateParams.GetPublicKeyEncoded()),
                PrivateKey = new CryptoKey(WalletNetworks.ML_DSA_65, privateParams.GetEncoded())
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"ML-DSA-65 key derivation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Signs data using ML-DSA-65 (deterministic mode).
    /// </summary>
    public CryptoResult<byte[]> SignMlDsa65(byte[] data, byte[] privateKey)
    {
        if (data == null || data.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Data cannot be null or empty");
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = MLDsaPrivateKeyParameters.FromEncoding(MLDsaParameters.ml_dsa_65, privateKey);
            var signer = new MLDsaSigner(MLDsaParameters.ml_dsa_65, deterministic: true);
            signer.Init(true, privKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            var signature = signer.GenerateSignature();

            return CryptoResult<byte[]>.Success(signature);
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.SigningFailed,
                $"ML-DSA-65 signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies an ML-DSA-65 signature.
    /// </summary>
    public CryptoStatus VerifyMlDsa65(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (data == null || data.Length == 0)
            return CryptoStatus.InvalidParameter;
        if (signature == null || signature.Length == 0)
            return CryptoStatus.InvalidSignature;
        if (publicKey == null || publicKey.Length == 0)
            return CryptoStatus.InvalidKey;

        try
        {
            var pubKeyParams = MLDsaPublicKeyParameters.FromEncoding(MLDsaParameters.ml_dsa_65, publicKey);
            var signer = new MLDsaSigner(MLDsaParameters.ml_dsa_65, deterministic: true);
            signer.Init(false, pubKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature) ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }
        catch
        {
            return CryptoStatus.InvalidSignature;
        }
    }

    /// <summary>
    /// Generates an SLH-DSA-SHA2-128s key pair.
    /// </summary>
    public CryptoResult<KeySet> GenerateSlhDsa128sKeyPair()
    {
        try
        {
            var generator = new SlhDsaKeyPairGenerator();
            generator.Init(new SlhDsaKeyGenerationParameters(new SecureRandom(), SlhDsaParameters.slh_dsa_sha2_128s));
            var keyPair = generator.GenerateKeyPair();

            var publicKey = ((SlhDsaPublicKeyParameters)keyPair.Public).GetEncoded();
            var privateKey = ((SlhDsaPrivateKeyParameters)keyPair.Private).GetEncoded();

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.SLH_DSA_128s, publicKey),
                PrivateKey = new CryptoKey(WalletNetworks.SLH_DSA_128s, privateKey)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"SLH-DSA-128s key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Deterministically generates an SLH-DSA key pair from derived key material (#1689).
    /// </summary>
    /// <remarks>
    /// FIPS 205 key generation draws SK.seed, SK.prf and PK.seed (n bytes each) and computes PK.root
    /// from them. The library exposes no from-seed constructor, so the generator is driven by a
    /// source that yields exactly those 3n bytes — and the result is checked to encode them, so a
    /// library change in draw order fails loudly instead of producing an unrelated key.
    /// </remarks>
    private static CryptoResult<KeySet> GenerateSlhDsaKeyPair(
        byte[] derivedKey, SlhDsaParameters parameters, int n, WalletNetworks network, string name)
    {
        try
        {
            var seeds = PqcSeedDerivation.Expand(derivedKey, name, 3 * n);
            var generator = new SlhDsaKeyPairGenerator();
            generator.Init(new SlhDsaKeyGenerationParameters(
                new SecureRandom(new PqcSeedDerivation.ExactBytesRandomGenerator(seeds)), parameters));
            var keyPair = generator.GenerateKeyPair();

            var privateKey = ((SlhDsaPrivateKeyParameters)keyPair.Private).GetEncoded();
            if (privateKey.Length < seeds.Length || !privateKey.AsSpan(0, seeds.Length).SequenceEqual(seeds))
            {
                return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                    $"{name} key derivation did not consume the supplied seeds as FIPS 205 specifies; refusing to return a key that could not be re-derived.");
            }

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(network, ((SlhDsaPublicKeyParameters)keyPair.Public).GetEncoded()),
                PrivateKey = new CryptoKey(network, privateKey)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"{name} key derivation failed: {ex.Message}");
        }
    }

    /// <summary>Deterministically generates an SLH-DSA-SHA2-128s key pair from derived key material (#1689).</summary>
    public CryptoResult<KeySet> GenerateSlhDsa128sKeyPair(byte[] derivedKey) =>
        GenerateSlhDsaKeyPair(derivedKey, SlhDsaParameters.slh_dsa_sha2_128s, 16, WalletNetworks.SLH_DSA_128s, "SLH-DSA-128s");

    /// <summary>Deterministically generates an SLH-DSA-SHA2-192s key pair from derived key material (#1689).</summary>
    public CryptoResult<KeySet> GenerateSlhDsa192sKeyPair(byte[] derivedKey) =>
        GenerateSlhDsaKeyPair(derivedKey, SlhDsaParameters.slh_dsa_sha2_192s, 24, WalletNetworks.SLH_DSA_192s, "SLH-DSA-192s");

    /// <summary>
    /// Signs data using SLH-DSA-SHA2-128s (deterministic mode).
    /// </summary>
    public CryptoResult<byte[]> SignSlhDsa128s(byte[] data, byte[] privateKey)
    {
        if (data == null || data.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Data cannot be null or empty");
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = SlhDsaPrivateKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_128s, privateKey);
            var signer = new SlhDsaSigner(SlhDsaParameters.slh_dsa_sha2_128s, deterministic: true);
            signer.Init(true, privKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            var signature = signer.GenerateSignature();

            return CryptoResult<byte[]>.Success(signature);
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.SigningFailed,
                $"SLH-DSA-128s signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies an SLH-DSA-SHA2-128s signature.
    /// </summary>
    public CryptoStatus VerifySlhDsa128s(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (data == null || data.Length == 0)
            return CryptoStatus.InvalidParameter;
        if (signature == null || signature.Length == 0)
            return CryptoStatus.InvalidSignature;
        if (publicKey == null || publicKey.Length == 0)
            return CryptoStatus.InvalidKey;

        try
        {
            var pubKeyParams = SlhDsaPublicKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_128s, publicKey);
            var signer = new SlhDsaSigner(SlhDsaParameters.slh_dsa_sha2_128s, deterministic: true);
            signer.Init(false, pubKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature) ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }
        catch
        {
            return CryptoStatus.InvalidSignature;
        }
    }

    /// <summary>
    /// Generates an SLH-DSA-SHA2-192s key pair.
    /// </summary>
    public CryptoResult<KeySet> GenerateSlhDsa192sKeyPair()
    {
        try
        {
            var generator = new SlhDsaKeyPairGenerator();
            generator.Init(new SlhDsaKeyGenerationParameters(new SecureRandom(), SlhDsaParameters.slh_dsa_sha2_192s));
            var keyPair = generator.GenerateKeyPair();

            var publicKey = ((SlhDsaPublicKeyParameters)keyPair.Public).GetEncoded();
            var privateKey = ((SlhDsaPrivateKeyParameters)keyPair.Private).GetEncoded();

            return CryptoResult<KeySet>.Success(new KeySet
            {
                PublicKey = new CryptoKey(WalletNetworks.SLH_DSA_192s, publicKey),
                PrivateKey = new CryptoKey(WalletNetworks.SLH_DSA_192s, privateKey)
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<KeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"SLH-DSA-192s key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Signs data using SLH-DSA-SHA2-192s (deterministic mode).
    /// </summary>
    public CryptoResult<byte[]> SignSlhDsa192s(byte[] data, byte[] privateKey)
    {
        if (data == null || data.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Data cannot be null or empty");
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = SlhDsaPrivateKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_192s, privateKey);
            var signer = new SlhDsaSigner(SlhDsaParameters.slh_dsa_sha2_192s, deterministic: true);
            signer.Init(true, privKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            var signature = signer.GenerateSignature();

            return CryptoResult<byte[]>.Success(signature);
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.SigningFailed,
                $"SLH-DSA-192s signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies an SLH-DSA-SHA2-192s signature.
    /// </summary>
    public CryptoStatus VerifySlhDsa192s(byte[] data, byte[] signature, byte[] publicKey)
    {
        if (data == null || data.Length == 0)
            return CryptoStatus.InvalidParameter;
        if (signature == null || signature.Length == 0)
            return CryptoStatus.InvalidSignature;
        if (publicKey == null || publicKey.Length == 0)
            return CryptoStatus.InvalidKey;

        try
        {
            var pubKeyParams = SlhDsaPublicKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_192s, publicKey);
            var signer = new SlhDsaSigner(SlhDsaParameters.slh_dsa_sha2_192s, deterministic: true);
            signer.Init(false, pubKeyParams);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature) ? CryptoStatus.Success : CryptoStatus.InvalidSignature;
        }
        catch
        {
            return CryptoStatus.InvalidSignature;
        }
    }

    /// <summary>
    /// Calculates the ML-DSA-65 public key from a private key.
    /// </summary>
    public CryptoResult<byte[]> CalculateMlDsa65PublicKey(byte[] privateKey)
    {
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = MLDsaPrivateKeyParameters.FromEncoding(MLDsaParameters.ml_dsa_65, privateKey);
            return CryptoResult<byte[]>.Success(privKeyParams.GetPublicKeyEncoded());
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey,
                $"Failed to calculate ML-DSA-65 public key: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculates the SLH-DSA-128s public key from a private key.
    /// </summary>
    public CryptoResult<byte[]> CalculateSlhDsa128sPublicKey(byte[] privateKey)
    {
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = SlhDsaPrivateKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_128s, privateKey);
            return CryptoResult<byte[]>.Success(privKeyParams.GetPublicKeyEncoded());
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey,
                $"Failed to calculate SLH-DSA-128s public key: {ex.Message}");
        }
    }

    /// <summary>
    /// Calculates the SLH-DSA-192s public key from a private key.
    /// </summary>
    public CryptoResult<byte[]> CalculateSlhDsa192sPublicKey(byte[] privateKey)
    {
        if (privateKey == null || privateKey.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Private key cannot be null or empty");

        try
        {
            var privKeyParams = SlhDsaPrivateKeyParameters.FromEncoding(SlhDsaParameters.slh_dsa_sha2_192s, privateKey);
            return CryptoResult<byte[]>.Success(privKeyParams.GetPublicKeyEncoded());
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey,
                $"Failed to calculate SLH-DSA-192s public key: {ex.Message}");
        }
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
