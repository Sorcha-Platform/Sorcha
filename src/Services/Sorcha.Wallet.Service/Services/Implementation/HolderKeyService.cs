// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Constants;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default implementation of <see cref="IHolderKeyService"/>. Derives the per-citizen
/// holder key under <c>sorcha:citizen-holder</c> (slot 108) by re-using the existing
/// wallet-derivation pipeline. Public keys + thumbprints are cached in Redis with a
/// 24h TTL to keep the enrolment + delegation hot paths fast; signing always re-derives
/// from the wallet so private key material never lives outside a single operation.
/// </summary>
/// <remarks>
/// Modelled on <see cref="HolderBindingKeyService"/> (slot 105, per-wallet KB-JWT signer)
/// — same structural pattern, different derivation slot, plus thumbprint helper used
/// by <see cref="IDeviceDelegationIssuer"/> to populate the
/// <c>did:sorcha:holder:{thumbprint}</c> issuer claim on delegation credentials.
/// </remarks>
public sealed class HolderKeyService : IHolderKeyService
{
    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly IDistributedCache _cache;
    private readonly ILogger<HolderKeyService> _logger;

    private const string CachePrefix = "sorcha:wallet:citizen-holder:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>Initialises a new instance of the <see cref="HolderKeyService"/> class.</summary>
    public HolderKeyService(
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        IDistributedCache cache,
        ILogger<HolderKeyService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetHolderPublicJwkAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var cacheKey = CachePrefix + walletAddress + ":jwk";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            return JsonSerializer.Deserialize<JsonElement>(cached);
        }

        var (_, publicKey, algorithm) = await DeriveHolderKeyAsync(walletAddress, ct);
        var jwk = BuildJwk(publicKey, algorithm);

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(jwk),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        _logger.LogInformation(
            "Derived citizen holder public JWK for wallet {Address} (algorithm: {Algorithm})",
            walletAddress, algorithm);

        return jwk;
    }

    /// <inheritdoc />
    public async Task<(byte[] Signature, string Algorithm)> SignAsync(
        string walletAddress,
        byte[] signingInput,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(signingInput);

        var (privateKey, _, algorithm) = await DeriveHolderKeyAsync(walletAddress, ct);

        try
        {
            var signature = SignWithKey(signingInput, privateKey, algorithm);

            _logger.LogInformation(
                "Signed citizen-holder payload for wallet {Address} (algorithm: {Algorithm}, {Bytes} bytes)",
                walletAddress, algorithm, signingInput.Length);

            return (signature, algorithm);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetHolderJwkThumbprintAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var cacheKey = CachePrefix + walletAddress + ":thumbprint";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        var jwk = await GetHolderPublicJwkAsync(walletAddress, ct);
        var thumbprint = ComputeJwkThumbprint(jwk);

        await _cache.SetStringAsync(
            cacheKey,
            thumbprint,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        return thumbprint;
    }

    private async Task<(byte[] PrivateKey, byte[] PublicKey, string Algorithm)> DeriveHolderKeyAsync(
        string walletAddress, CancellationToken ct)
    {
        var wallet = await _repository.GetByAddressAsync(walletAddress, false, false, false, ct)
            ?? throw new KeyNotFoundException($"Wallet {walletAddress} not found");

        var masterKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey, wallet.EncryptionKeyId);

        var resolvedPath = SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.CitizenHolder);
        var parsedPath = new DerivationPath(resolvedPath);

        // Citizen-holder credentials are consumed by external verifiers via the JWK in
        // the cnf claim, so the holder key must be classical (ES256 / Ed25519). PQC-primary
        // wallets derive a classical co-key — same pattern as HolderBindingKeyService /
        // HaipIssuerCoKeyService.
        var derivationAlg = WalletAlgorithmClassification.ClassicalAlgorithms.Contains(wallet.Algorithm)
            ? wallet.Algorithm
            : WalletAlgorithmClassification.DefaultClassicalAlgorithm;

        var (derivedPrivate, derivedPublic) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, parsedPath, derivationAlg);

        return (derivedPrivate, derivedPublic, derivationAlg);
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
                ["x"] = Base64Url.EncodeToString(publicKey)
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
                ["x"] = Base64Url.EncodeToString(parameters.Q.X!),
                ["y"] = Base64Url.EncodeToString(parameters.Q.Y!)
            };
        }
        else
        {
            throw new NotSupportedException(
                $"Unsupported algorithm '{algorithm}' for citizen holder key JWK. " +
                "Only Ed25519 and P-256 are supported (HAIP / OID4VP convention).");
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

        throw new NotSupportedException($"Unsupported citizen holder key algorithm: {algorithm}");
    }

    /// <summary>
    /// Computes the canonical RFC 7638 JWK thumbprint over the JWK's required members.
    /// Returned as 43-character base64url-encoded SHA-256.
    /// </summary>
    private static string ComputeJwkThumbprint(JsonElement jwk)
    {
        // RFC 7638: required members for EC = crv, kty, x, y; for OKP = crv, kty, x.
        // Lexicographic order, no whitespace.
        var kty = jwk.GetProperty("kty").GetString();
        var crv = jwk.GetProperty("crv").GetString();
        var x = jwk.GetProperty("x").GetString();

        string canonical;
        if (kty == "EC")
        {
            var y = jwk.GetProperty("y").GetString();
            canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        }
        else if (kty == "OKP")
        {
            canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\"}}";
        }
        else
        {
            throw new NotSupportedException($"Cannot compute thumbprint for kty='{kty}'");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64Url.EncodeToString(hash);
    }
}
