// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Software implementation of <see cref="ISecretProtectionProvider"/> using AES-256-GCM with a
/// symmetric key resolved by <see cref="TenantSecretKeyResolver"/>.
/// </summary>
/// <remarks>
/// The encrypt/decrypt body is byte-identical to
/// <c>Sorcha.Wallet.Service.Services.Implementation.SoftwareKeyProtectionProvider</c> (12-byte
/// nonce, 16-byte tag, envelope <c>nonce ∥ ciphertext ∥ tag</c>) so the two can converge onto a
/// shared provider later (see the convergence note on <see cref="ISecretProtectionProvider"/>).
/// The only deliberate difference is key sourcing: the resolved key + KeyId are injected here,
/// whereas Wallet reads its key directly from configuration.
/// </remarks>
public sealed class SoftwareSecretProtectionProvider : ISecretProtectionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int RequiredKeyLength = 32;

    private readonly byte[] _key;
    private readonly string _keyId;

    /// <summary>
    /// Initialises the provider with a resolved 32-byte key and its identifier.
    /// </summary>
    /// <param name="key">The 32-byte AES-256 key (from <see cref="TenantSecretKeyResolver"/>).</param>
    /// <param name="keyId">The identifier persisted alongside ciphertext (e.g. "jwt-derived-v1").</param>
    /// <exception cref="ArgumentNullException">Key or keyId is null.</exception>
    /// <exception cref="ArgumentException">Key is not exactly 32 bytes, or keyId is blank.</exception>
    public SoftwareSecretProtectionProvider(byte[] key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != RequiredKeyLength)
        {
            throw new ArgumentException(
                $"Secret-protection key must be exactly {RequiredKeyLength} bytes; got {key.Length}.",
                nameof(key));
        }
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException("KeyId must be provided.", nameof(keyId));
        }

        _key = key;
        _keyId = keyId;
    }

    /// <inheritdoc />
    public string ProviderName => "Software";

    /// <inheritdoc />
    public Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(byte[] plaintext, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Envelope: nonce (12) ∥ ciphertext (N) ∥ tag (16)
        var envelope = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(envelope, 0);
        ciphertext.CopyTo(envelope, NonceSize);
        tag.CopyTo(envelope, NonceSize + ciphertext.Length);

        return Task.FromResult((envelope, _keyId));
    }

    /// <inheritdoc />
    public Task<byte[]> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        if (ciphertext.Length < NonceSize + TagSize)
        {
            throw new ArgumentException(
                $"Ciphertext is too short ({ciphertext.Length} bytes); minimum is {NonceSize + TagSize} " +
                "(nonce + tag).",
                nameof(ciphertext));
        }

        // v1 is a single-key world (DB cleared on rollout). The keyId is retained on the record for
        // future rotation/provider discrimination; the current key is used to decrypt. A tag mismatch
        // (wrong key after a rotation, or tampering) surfaces as AuthenticationTagMismatchException —
        // callers MUST handle it safely (invalid-code / config error), never an unhandled 500.
        var nonce = ciphertext.AsSpan(0, NonceSize);
        var ciphertextLength = ciphertext.Length - NonceSize - TagSize;
        var payload = ciphertext.AsSpan(NonceSize, ciphertextLength);
        var tag = ciphertext.AsSpan(NonceSize + ciphertextLength, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, payload, tag, plaintext);

        return Task.FromResult(plaintext);
    }
}
