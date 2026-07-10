// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.Secp256k1;
using Sorcha.Cryptography.Secp256k1.Siwe;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Core.Services.Implementation;

/// <summary>
/// Derives a wallet's auxiliary Ethereum identity on demand from its encrypted seed and signs EIP-191 /
/// SIWE prove-control messages (Feature 180). Reuses the wallet's existing seed-decryption and derives at
/// <c>m/44'/60'/0'/0/{index}</c> — the wallet's primary algorithm is untouched. The private key is
/// derived, used, and cleared; it is never returned. Payloads that decode as a blockchain transaction
/// are refused (prove-control only).
/// </summary>
public sealed class EthereumIdentityService : IEthereumIdentityService
{
    private readonly IWalletRepository _repository;
    private readonly IKeyManagementService _keyManagement;
    private readonly ILogger<EthereumIdentityService> _logger;

    public EthereumIdentityService(
        IWalletRepository repository,
        IKeyManagementService keyManagement,
        ILogger<EthereumIdentityService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyManagement = keyManagement ?? throw new ArgumentNullException(nameof(keyManagement));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GetAddressAsync(string walletAddress, int index = 0, CancellationToken cancellationToken = default)
    {
        var (privateKey, publicKey) = await DeriveAsync(walletAddress, index, cancellationToken).ConfigureAwait(false);
        try
        {
            return EthereumAddress.FromPublicKey(Secp256k1PublicKey.FromSec1(publicKey));
        }
        finally
        {
            Array.Clear(privateKey);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> SignPersonalMessageAsync(string walletAddress, byte[] message, int index = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        GuardAgainstTransaction(message);

        var (privateKey, _) = await DeriveAsync(walletAddress, index, cancellationToken).ConfigureAwait(false);
        try
        {
            var digest = Eip191.PersonalSignDigest(message);
            return Secp256k1Signer.SignRecoverable(digest, privateKey);
        }
        finally
        {
            Array.Clear(privateKey);
        }
    }

    /// <inheritdoc />
    public async Task<SiweSignResult> SignSiweAsync(string walletAddress, SiweMessage message, int index = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var (privateKey, publicKey) = await DeriveAsync(walletAddress, index, cancellationToken).ConfigureAwait(false);
        try
        {
            var address = EthereumAddress.FromPublicKey(Secp256k1PublicKey.FromSec1(publicKey));
            // The message's address must be the signer's own address.
            message.Address = address;

            var text = SiweFormatter.Format(message);
            var bytes = Encoding.UTF8.GetBytes(text);
            GuardAgainstTransaction(bytes); // belt-and-braces; SIWE text is never RLP

            var digest = Eip191.PersonalSignDigest(bytes);
            var signature = Secp256k1Signer.SignRecoverable(digest, privateKey);

            return new SiweSignResult(text, "0x" + Convert.ToHexStringLower(signature), address);
        }
        finally
        {
            Array.Clear(privateKey);
        }
    }

    private async Task<(byte[] PrivateKey, byte[] PublicKey)> DeriveAsync(string walletAddress, int index, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new ArgumentException("Wallet address cannot be empty", nameof(walletAddress));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var wallet = await _repository.GetByAddressAsync(walletAddress, cancellationToken: ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Wallet not found: {walletAddress}");

        // Reuse the wallet's existing seed-decryption. Direct-master wallets carry the BIP39 seed blob;
        // legacy wallets fall back to the encrypted leaf (matching WalletManager's signing path).
        byte[] seed;
        if (!wallet.RecoveryEnabled && !string.IsNullOrEmpty(wallet.EncryptedMasterKeyBlob))
        {
            seed = await _keyManagement.DecryptPrivateKeyAsync(wallet.EncryptedMasterKeyBlob!, wallet.EncryptionKeyId).ConfigureAwait(false);
        }
        else
        {
            seed = await _keyManagement.DecryptPrivateKeyAsync(wallet.EncryptedPrivateKey, wallet.EncryptionKeyId).ConfigureAwait(false);
        }

        try
        {
            var path = new DerivationPath($"m/44'/60'/0'/0/{index}");
            return await _keyManagement.DeriveSecp256k1KeyAtPathAsync(seed, path).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(seed);
        }
    }

    /// <summary>
    /// Refuse a payload that decodes as a blockchain transaction (Feature 180 prove-control confinement):
    /// a typed-transaction envelope (<c>0x01/0x02/0x03</c> followed by an RLP list) or a legacy RLP list
    /// (leading byte <c>0xc0..0xff</c>). EIP-191-prefixed prove-control text never starts this way, so this
    /// is a defence-in-depth boundary that keeps the key from ever signing a transfer.
    /// </summary>
    private void GuardAgainstTransaction(ReadOnlySpan<byte> message)
    {
        var looksLikeTransaction =
            (message.Length >= 2 && message[0] is 0x01 or 0x02 or 0x03 && message[1] >= 0xc0)  // typed-tx envelope
            || (message.Length >= 1 && message[0] >= 0xc0);                                     // legacy RLP list

        if (looksLikeTransaction)
        {
            _logger.LogWarning("Refused to sign a payload that decodes as an Ethereum transaction (prove-control only).");
            throw new InvalidOperationException("Refusing to sign: the payload decodes as a blockchain transaction. The Ethereum key is prove-control only.");
        }
    }
}
