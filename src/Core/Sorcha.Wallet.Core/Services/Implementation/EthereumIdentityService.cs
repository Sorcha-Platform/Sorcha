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
/// SIWE prove-control messages (Feature 180) and — through the separate <see cref="IEthereumTransactionSigner"/>
/// surface — native ETH transactions (Feature 182, Phase 4). Reuses the wallet's existing seed-decryption
/// and derives at <c>m/44'/60'/0'/0/{index}</c> — the wallet's primary algorithm is untouched. The private
/// key is derived, used, and cleared; it is never returned. The prove-control message signers still refuse
/// any payload that decodes as a blockchain transaction; transactions are produced <b>only</b> through the
/// gated <see cref="SignTransactionAsync"/> path. This whole class is wired server-side only (the WASM PWA
/// never registers it), so value-moving signing never runs on-device.
/// </summary>
public sealed class EthereumIdentityService : IEthereumIdentityService, IEthereumTransactionSigner
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

    /// <inheritdoc />
    public async Task<SignedEthereumTransaction> SignTransactionAsync(
        string walletAddress, EthereumTransactionRequest request, int index = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var to = ParseAddress(request.To);
        if (request.ValueWei.Sign < 0)
            throw new ArgumentException("Transfer value must be non-negative.", nameof(request));
        if (request.MaxPriorityFeePerGasWei > request.MaxFeePerGasWei)
            throw new ArgumentException("Priority fee cannot exceed the max fee per gas.", nameof(request));

        var (privateKey, publicKey) = await DeriveAsync(walletAddress, index, cancellationToken).ConfigureAwait(false);
        try
        {
            var from = EthereumAddress.FromPublicKey(Secp256k1PublicKey.FromSec1(publicKey));

            // Native transfer: empty call data. This is the ONLY sanctioned transaction-producing path;
            // the EIP-191/SIWE prove-control guard is deliberately not applied here.
            var transaction = new EthereumTransaction(
                request.ChainId, request.Nonce, request.MaxPriorityFeePerGasWei, request.MaxFeePerGasWei,
                request.GasLimit, to, request.ValueWei, ReadOnlySpan<byte>.Empty);

            var signature = Secp256k1Signer.SignRecoverable(transaction.SigningHash(), privateKey);
            var signed = transaction.AssembleSigned(signature);

            return new SignedEthereumTransaction(signed.RawTransactionHex, signed.TransactionHash, from);
        }
        finally
        {
            Array.Clear(privateKey);
        }
    }

    private static byte[] ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Recipient address is required.");

        var hex = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
        if (hex.Length != 40)
            throw new ArgumentException("Recipient must be a 20-byte (0x + 40 hex) address.", nameof(address));

        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Recipient address is not valid hex.", nameof(address), ex);
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
