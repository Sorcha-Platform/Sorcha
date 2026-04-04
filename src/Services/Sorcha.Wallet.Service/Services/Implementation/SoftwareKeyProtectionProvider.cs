// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Software-based implementation of <see cref="IOrgKeyProtectionProvider"/>
/// that encrypts/decrypts organisation master seeds using AES-256-GCM
/// with a symmetric key sourced from application configuration.
/// </summary>
public class SoftwareKeyProtectionProvider : IOrgKeyProtectionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int RequiredKeyLength = 32;
    private const string DefaultKeyId = "software-default";

    private readonly byte[] _encryptionKey;
    private readonly ILogger<SoftwareKeyProtectionProvider> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="SoftwareKeyProtectionProvider"/> class.
    /// </summary>
    /// <param name="configuration">Application configuration containing the encryption key.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when configuration or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the encryption key is missing or invalid.</exception>
    public SoftwareKeyProtectionProvider(IConfiguration configuration, ILogger<SoftwareKeyProtectionProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var keyBase64 = configuration["OrgKeyProtection:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new InvalidOperationException(
                "OrgKeyProtection:EncryptionKey is not configured. " +
                "A Base64-encoded 32-byte key is required for software key protection.");
        }

        _encryptionKey = Convert.FromBase64String(keyBase64);
        if (_encryptionKey.Length != RequiredKeyLength)
        {
            throw new InvalidOperationException(
                $"OrgKeyProtection:EncryptionKey must decode to exactly {RequiredKeyLength} bytes. " +
                $"Got {_encryptionKey.Length} bytes.");
        }
    }

    /// <inheritdoc />
    public string ProviderName => "Software";

    /// <inheritdoc />
    /// <remarks>
    /// Generates a random 12-byte nonce, encrypts with AES-256-GCM,
    /// and returns the concatenation of nonce + ciphertext + tag.
    /// </remarks>
    public Task<(byte[] EncryptedSeed, string KeyId)> EncryptSeedAsync(byte[] seed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seed);

        _logger.LogDebug("Encrypting org master seed ({SeedLength} bytes) with software provider", seed.Length);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[seed.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, seed, ciphertext, tag);

        // Concatenate: nonce (12) + ciphertext (N) + tag (16)
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result, 0);
        ciphertext.CopyTo(result, NonceSize);
        tag.CopyTo(result, NonceSize + ciphertext.Length);

        _logger.LogDebug(
            "Org master seed encrypted successfully ({OutputLength} bytes, KeyId: {KeyId})",
            result.Length, DefaultKeyId);

        return Task.FromResult((result, DefaultKeyId));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Splits the encrypted data into nonce (first 12 bytes), ciphertext (middle),
    /// and tag (last 16 bytes), then decrypts with AES-256-GCM.
    /// </remarks>
    public Task<byte[]> DecryptSeedAsync(byte[] encryptedSeed, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedSeed);

        if (encryptedSeed.Length < NonceSize + TagSize)
        {
            throw new ArgumentException(
                $"Encrypted seed is too short ({encryptedSeed.Length} bytes). " +
                $"Minimum length is {NonceSize + TagSize} bytes (nonce + tag).",
                nameof(encryptedSeed));
        }

        _logger.LogDebug(
            "Decrypting org master seed ({EncryptedLength} bytes, KeyId: {KeyId}) with software provider",
            encryptedSeed.Length, keyId);

        var nonce = encryptedSeed.AsSpan(0, NonceSize);
        var ciphertextLength = encryptedSeed.Length - NonceSize - TagSize;
        var ciphertext = encryptedSeed.AsSpan(NonceSize, ciphertextLength);
        var tag = encryptedSeed.AsSpan(NonceSize + ciphertextLength, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        _logger.LogDebug(
            "Org master seed decrypted successfully ({PlaintextLength} bytes)",
            plaintext.Length);

        return Task.FromResult(plaintext);
    }
}
