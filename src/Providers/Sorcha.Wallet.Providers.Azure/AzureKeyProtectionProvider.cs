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

namespace Sorcha.Wallet.Providers.Azure;

/// <summary>
/// Azure Key Vault implementation of <see cref="IKeyProtectionProvider"/>.
/// Uses RSA-OAEP-256 key wrapping for envelope encryption of Data Encryption Keys.
/// </summary>
public class AzureKeyProtectionProvider : IKeyProtectionProvider
{
    private readonly KeyClient _keyClient;
    private readonly ILogger<AzureKeyProtectionProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureKeyProtectionProvider"/> class.
    /// </summary>
    /// <param name="options">Azure Key Vault configuration options.</param>
    /// <param name="logger">Logger for audit and diagnostics.</param>
    public AzureKeyProtectionProvider(
        IOptions<AzureKmsOptions> options,
        ILogger<AzureKeyProtectionProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.VaultUri))
            throw new ArgumentException("VaultUri must be configured.", nameof(options));

        var credential = AzureCredentialFactory.Create(opts);
        _keyClient = new KeyClient(new Uri(opts.VaultUri), credential);

        _logger.LogInformation(
            "Initialized AzureKeyProtectionProvider for vault {VaultUri} (ManagedIdentity={UseManagedIdentity})",
            opts.VaultUri, opts.UseManagedIdentity);
    }

    /// <summary>
    /// Initializes a new instance for testing with an injected <see cref="KeyClient"/>.
    /// </summary>
    internal AzureKeyProtectionProvider(
        KeyClient keyClient,
        ILogger<AzureKeyProtectionProvider> logger)
    {
        _keyClient = keyClient ?? throw new ArgumentNullException(nameof(keyClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> CreateKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var sw = Stopwatch.StartNew();
        try
        {
            var keyOptions = new CreateRsaKeyOptions(keyId, hardwareProtected: false)
            {
                KeySize = 2048
            };

            var response = await _keyClient.CreateRsaKeyAsync(keyOptions, ct);
            sw.Stop();

            _logger.LogInformation(
                "Created RSA-2048 key '{KeyId}' in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);

            return response.Value.Id.ToString();
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to create key '{KeyId}' in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> WrapKeyAsync(string keyId, byte[] plaintext, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(plaintext);

        var sw = Stopwatch.StartNew();
        try
        {
            var cryptoClient = _keyClient.GetCryptographyClient(keyId);
            var result = await cryptoClient.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, plaintext, ct);
            sw.Stop();

            _logger.LogInformation(
                "Wrapped key using '{KeyId}' ({ElapsedMs}ms, {PlaintextLen}B -> {CiphertextLen}B)",
                keyId, sw.ElapsedMilliseconds, plaintext.Length, result.EncryptedKey.Length);

            return result.EncryptedKey;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to wrap key using '{KeyId}' ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> UnwrapKeyAsync(string keyId, byte[] ciphertext, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(ciphertext);

        var sw = Stopwatch.StartNew();
        try
        {
            var cryptoClient = _keyClient.GetCryptographyClient(keyId);
            var result = await cryptoClient.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, ciphertext, ct);
            sw.Stop();

            _logger.LogInformation(
                "Unwrapped key using '{KeyId}' ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);

            return result.Key;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to unwrap key using '{KeyId}' ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> KeyExistsAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var sw = Stopwatch.StartNew();
        try
        {
            await _keyClient.GetKeyAsync(keyId, cancellationToken: ct);
            sw.Stop();

            _logger.LogDebug(
                "Key '{KeyId}' exists in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            sw.Stop();
            _logger.LogDebug(
                "Key '{KeyId}' not found in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            return false;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Failed to check key '{KeyId}' in Key Vault ({ElapsedMs}ms)",
                keyId, sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Azure SDK manages connections internally; nothing to dispose.
        GC.SuppressFinalize(this);
    }
}
