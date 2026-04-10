// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Constants;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Derives and signs with the per-wallet holder binding key under
/// <c>sorcha:credential-holder-binding</c> for KB-JWT signing in SD-JWT VC presentations.
/// </summary>
public class HolderBindingKeyService : IHolderBindingKeyService
{
    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly ILogger<HolderBindingKeyService> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="HolderBindingKeyService"/> class.
    /// </summary>
    public HolderBindingKeyService(
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        ILogger<HolderBindingKeyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetPublicJwkAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var (_, publicKey, algorithm) = await DeriveBindingKeyAsync(walletAddress, ct);

        _logger.LogInformation(
            "Derived holder binding public key for wallet {Address} (algorithm: {Algorithm})",
            walletAddress, algorithm);

        return BuildJwk(publicKey, algorithm);
    }

    /// <inheritdoc />
    public async Task<(byte[] Signature, string Algorithm)> SignKbJwtAsync(
        string walletAddress,
        byte[] signingInput,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(signingInput);

        var (privateKey, _, algorithm) = await DeriveBindingKeyAsync(walletAddress, ct);

        var signature = SignWithKey(signingInput, privateKey, algorithm);

        _logger.LogInformation(
            "Signed KB-JWT for wallet {Address} using holder binding key",
            walletAddress);

        return (signature, algorithm);
    }

    private async Task<(byte[] PrivateKey, byte[] PublicKey, string Algorithm)> DeriveBindingKeyAsync(
        string walletAddress, CancellationToken ct)
    {
        var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, ct)
            ?? throw new KeyNotFoundException($"Wallet {walletAddress} not found");

        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        var resolvedPath = SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.CredentialHolderBinding);
        var parsedPath = new DerivationPath(resolvedPath);

        // For holder binding, always derive using the wallet's native algorithm
        var (derivedPrivate, derivedPublic) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, parsedPath, wallet.Algorithm);

        return (derivedPrivate, derivedPublic, wallet.Algorithm);
    }

    private static JsonElement BuildJwk(byte[] publicKey, string algorithm)
    {
        var alg = algorithm.ToUpperInvariant();
        Dictionary<string, object> jwk;

        if (alg is "ED25519" or "EDDSA")
        {
            jwk = new Dictionary<string, object>
            {
                ["kty"] = "OKP",
                ["crv"] = "Ed25519",
                ["x"] = Base64UrlEncode(publicKey)
            };
        }
        else if (alg is "ES256" or "P-256" or "P256" or "NIST-P256" or "NISTP256" or "ECDSA-P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            jwk = new Dictionary<string, object>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncode(parameters.Q.X!),
                ["y"] = Base64UrlEncode(parameters.Q.Y!)
            };
        }
        else
        {
            // Fallback: encode as raw key material
            jwk = new Dictionary<string, object>
            {
                ["kty"] = "OKP",
                ["crv"] = algorithm,
                ["x"] = Base64UrlEncode(publicKey)
            };
        }

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    private static byte[] SignWithKey(byte[] data, byte[] privateKey, string algorithm)
    {
        var alg = algorithm.ToUpperInvariant();

        if (alg is "ED25519" or "EDDSA")
        {
            return Sodium.PublicKeyAuth.SignDetached(data, privateKey);
        }

        if (alg is "ES256" or "P-256" or "P256" or "NIST-P256" or "NISTP256" or "ECDSA-P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(privateKey, out _);
            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }

        throw new NotSupportedException($"Unsupported holder binding key algorithm: {algorithm}");
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
