// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Models;

namespace Sorcha.Wallet.Providers.Azure;

/// <summary>
/// Azure Key Vault implementation of <see cref="ISigningProvider"/>.
/// Signing keys are created and held within Key Vault — private key material never leaves the HSM.
/// Uses NIST P-256 (ES256) signing algorithm.
/// </summary>
public class AzureSigningProvider : ISigningProvider
{
    private readonly KeyClient _keyClient;
    private readonly ILogger<AzureSigningProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureSigningProvider"/> class.
    /// </summary>
    /// <param name="options">Azure Key Vault configuration options.</param>
    /// <param name="logger">Logger for audit and diagnostics.</param>
    public AzureSigningProvider(
        IOptions<AzureKmsOptions> options,
        ILogger<AzureSigningProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.VaultUri))
            throw new ArgumentException("VaultUri must be configured.", nameof(options));

        var credential = AzureCredentialFactory.Create(opts);
        _keyClient = new KeyClient(new Uri(opts.VaultUri), credential);

        _logger.LogInformation(
            "Initialized AzureSigningProvider for vault {VaultUri}", opts.VaultUri);
    }

    /// <summary>
    /// Initializes a new instance for testing with an injected <see cref="KeyClient"/>.
    /// </summary>
    internal AzureSigningProvider(
        KeyClient keyClient,
        ILogger<AzureSigningProvider> logger)
    {
        _keyClient = keyClient ?? throw new ArgumentNullException(nameof(keyClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<KmsKeyInfo> CreateSigningKeyAsync(string keyId, string algorithm, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);

        // Azure Key Vault only supports P-256 for KMS-resident signing
        if (!algorithm.Equals("NISTP256", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Equals("P-256", StringComparison.OrdinalIgnoreCase) &&
            !algorithm.Equals("P256", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Algorithm '{algorithm}' is not supported for KMS-resident signing. " +
                "Azure Key Vault supports NIST P-256 (ES256).");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var keyOptions = new CreateEcKeyOptions(keyId, hardwareProtected: false)
            {
                CurveName = KeyCurveName.P256
            };

            var response = await _keyClient.CreateEcKeyAsync(keyOptions, ct);
            var key = response.Value;
            sw.Stop();

            // Extract the uncompressed public key (X + Y coordinates, 64 bytes)
            var ecParameters = key.Key.ToECDsa().ExportParameters(false);
            var publicKeyBytes = new byte[64];
            Buffer.BlockCopy(ecParameters.Q.X!, 0, publicKeyBytes, 0, 32);
            Buffer.BlockCopy(ecParameters.Q.Y!, 0, publicKeyBytes, 32, 32);

            _logger.LogInformation(
                "Created EC P-256 signing key '{KeyId}' in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);

            return new KmsKeyInfo(
                KeyId: key.Id.ToString(),
                PublicKey: publicKeyBytes,
                Algorithm: "NISTP256",
                CreatedAt: key.Properties.CreatedOn ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to create signing key '{KeyId}' in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> SignAsync(string kmsKeyId, byte[] data, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kmsKeyId);
        ArgumentNullException.ThrowIfNull(data);

        var sw = Stopwatch.StartNew();
        try
        {
            var cryptoClient = _keyClient.GetCryptographyClient(kmsKeyId);
            var result = await cryptoClient.SignAsync(SignatureAlgorithm.ES256, data, ct);
            sw.Stop();

            _logger.LogInformation(
                "Signed {DataLen}B with KMS key '{KeyId}' ({ElapsedMs}ms)",
                data.Length, kmsKeyId, sw.ElapsedMilliseconds);

            return result.Signature;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to sign with KMS key '{KeyId}' ({ElapsedMs}ms)",
                kmsKeyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyAsync(string kmsKeyId, byte[] data, byte[] signature, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kmsKeyId);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        var sw = Stopwatch.StartNew();
        try
        {
            var cryptoClient = _keyClient.GetCryptographyClient(kmsKeyId);
            var result = await cryptoClient.VerifyAsync(SignatureAlgorithm.ES256, data, signature, ct);
            sw.Stop();

            _logger.LogInformation(
                "Verified signature for KMS key '{KeyId}': {IsValid} ({ElapsedMs}ms)",
                kmsKeyId, result.IsValid, sw.ElapsedMilliseconds);

            return result.IsValid;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to verify signature with KMS key '{KeyId}' ({ElapsedMs}ms)",
                kmsKeyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetPublicKeyAsync(string kmsKeyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kmsKeyId);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _keyClient.GetKeyAsync(kmsKeyId, cancellationToken: ct);
            var key = response.Value;
            sw.Stop();

            // Extract the uncompressed public key (X + Y coordinates, 64 bytes)
            var ecParameters = key.Key.ToECDsa().ExportParameters(false);
            var publicKeyBytes = new byte[64];
            Buffer.BlockCopy(ecParameters.Q.X!, 0, publicKeyBytes, 0, 32);
            Buffer.BlockCopy(ecParameters.Q.Y!, 0, publicKeyBytes, 32, 32);

            _logger.LogInformation(
                "Retrieved public key for KMS key '{KeyId}' ({ElapsedMs}ms)",
                kmsKeyId, sw.ElapsedMilliseconds);

            return publicKeyBytes;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to get public key for KMS key '{KeyId}' ({ElapsedMs}ms)",
                kmsKeyId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
