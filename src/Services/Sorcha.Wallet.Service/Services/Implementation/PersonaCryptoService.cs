// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Core.Constants;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default implementation of <see cref="IPersonaCryptoService"/>. Loads the
/// target wallet, decrypts its private key, HKDFs a persona content key, and
/// encrypts / decrypts with XChaCha20-Poly1305.
/// </summary>
public sealed class PersonaCryptoService : IPersonaCryptoService
{
    private readonly WalletDbContext _db;
    private readonly IKeyManagementService _keyManagement;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ILogger<PersonaCryptoService> _logger;

    private const int ContentKeySizeBytes = 32; // XChaCha20-Poly1305 key size

    /// <summary>
    /// Initialises a new instance of the <see cref="PersonaCryptoService"/> class.
    /// </summary>
    public PersonaCryptoService(
        WalletDbContext db,
        IKeyManagementService keyManagement,
        ISymmetricCrypto symmetricCrypto,
        ILogger<PersonaCryptoService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PersonaCryptoResult> EncryptAsync(
        string walletAddress,
        byte[] plaintext,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(plaintext);

        var contentKey = await DeriveContentKeyAsync(walletAddress, ct);

        try
        {
            var encryptResult = await _symmetricCrypto.EncryptAsync(
                plaintext,
                EncryptionType.XCHACHA20_POLY1305,
                contentKey,
                ct);

            if (!encryptResult.IsSuccess || encryptResult.Value is null)
            {
                _logger.LogError(
                    "Persona encryption failed for wallet {WalletAddress}: {Error}",
                    walletAddress, encryptResult.ErrorMessage);
                throw new CryptographicException(
                    $"Persona encryption failed: {encryptResult.ErrorMessage}");
            }

            // Copy out immutable result bytes before the SymmetricCiphertext
            // zeroises its key material on dispose.
            var ciphertext = (byte[])encryptResult.Value.Data.Clone();
            var nonce = (byte[])encryptResult.Value.IV.Clone();

            encryptResult.Value.Dispose();

            return new PersonaCryptoResult(ciphertext, nonce, walletAddress);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> DecryptAsync(
        string walletAddress,
        byte[] ciphertext,
        byte[] nonce,
        string wrappedKeyRef,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(wrappedKeyRef);

        if (!string.Equals(wrappedKeyRef, walletAddress, StringComparison.Ordinal))
        {
            // v1 invariant: wrappedKeyRef == walletAddress. Any mismatch
            // means the caller handed us a ref for a different user, which
            // would otherwise leak identity across wallets. The typed
            // PersonaKeyRefMismatchException lets the endpoint layer map
            // this to a 400 (caller-side bug) without fragile string
            // matching on the exception message.
            throw new PersonaKeyRefMismatchException(
                "wrappedKeyRef does not match walletAddress — v1 invariant violated.");
        }

        var contentKey = await DeriveContentKeyAsync(walletAddress, ct);

        try
        {
            var symmetricCiphertext = new SymmetricCiphertext
            {
                Data = ciphertext,
                Key = contentKey,
                IV = nonce,
                Type = EncryptionType.XCHACHA20_POLY1305
            };

            var decryptResult = await _symmetricCrypto.DecryptAsync(symmetricCiphertext, ct);

            if (!decryptResult.IsSuccess || decryptResult.Value is null)
            {
                _logger.LogWarning(
                    "Persona decryption failed for wallet {WalletAddress} — ciphertext may be corrupt or tampered.",
                    walletAddress);
                throw new CryptographicException(
                    $"Persona decryption failed: {decryptResult.ErrorMessage}");
            }

            return decryptResult.Value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }

    /// <summary>
    /// Derives a 32-byte symmetric content key for the given wallet under the
    /// <c>sorcha:persona-vault</c> purpose. Caller is responsible for
    /// zeroising the returned buffer.
    /// </summary>
    private async Task<byte[]> DeriveContentKeyAsync(string walletAddress, CancellationToken ct)
    {
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Address == walletAddress, ct)
            ?? throw new KeyNotFoundException($"Wallet not found: {walletAddress}");

        var privateKey = await _keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedPrivateKey,
            wallet.EncryptionKeyId);

        try
        {
            var info = Encoding.UTF8.GetBytes(SorchaDerivationPaths.PersonaVault);

            // HKDF-SHA256 with no salt, info = "sorcha:persona-vault".
            // Deterministic per-wallet content key, distinct from the wallet
            // signing key by construction.
            var contentKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: privateKey,
                outputLength: ContentKeySizeBytes,
                salt: null,
                info: info);

            return contentKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }
}
