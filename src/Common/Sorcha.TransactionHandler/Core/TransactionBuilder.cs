// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.TransactionHandler.Models;
using Sorcha.TransactionHandler.Payload;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.TransactionHandler.Core;

/// <summary>
/// Fluent builder for creating transactions.
/// </summary>
public class TransactionBuilder : ITransactionBuilder
{
    private Transaction? _transaction;
    private bool _isSigned = false;
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ISymmetricCrypto _symmetricCrypto;

    /// <summary>
    /// Initializes a new instance of the TransactionBuilder class.
    /// </summary>
    /// <param name="cryptoModule">The cryptography module</param>
    /// <param name="hashProvider">The hash provider</param>
    /// <param name="symmetricCrypto">The symmetric encryption provider</param>
    public TransactionBuilder(ICryptoModule cryptoModule, IHashProvider hashProvider, ISymmetricCrypto symmetricCrypto)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
    }

    /// <inheritdoc/>
    public ITransactionBuilder Create(TransactionVersion version = TransactionVersion.V1)
    {
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        _transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager, version);
        _isSigned = false;
        return this;
    }

    /// <inheritdoc/>
    public ITransactionBuilder WithPreviousTransaction(string txHash)
    {
        ValidateNotSigned();
        EnsureTransactionCreated();
        _transaction!.PreviousTxHash = txHash;
        return this;
    }

    /// <inheritdoc/>
    public ITransactionBuilder WithRecipients(params string[] walletAddresses)
    {
        ValidateNotSigned();
        EnsureTransactionCreated();
        ValidateWallets(walletAddresses);
        _transaction!.Recipients = walletAddresses;
        return this;
    }

    /// <inheritdoc/>
    public ITransactionBuilder WithMetadata(string jsonMetadata)
    {
        ValidateNotSigned();
        EnsureTransactionCreated();
        ValidateJson(jsonMetadata);
        _transaction!.Metadata = jsonMetadata;
        return this;
    }

    /// <inheritdoc/>
    public ITransactionBuilder WithMetadata<T>(T metadata) where T : class
    {
        ValidateNotSigned();
        EnsureTransactionCreated();

        if (metadata == null)
            throw new ArgumentNullException(nameof(metadata));

        var jsonMetadata = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        _transaction!.Metadata = jsonMetadata;
        return this;
    }

    /// <inheritdoc/>
    public ITransactionBuilder AddPayload(
        byte[] data,
        string[] recipientWallets,
        PayloadOptions? options = null)
    {
        ValidateNotSigned();
        EnsureTransactionCreated();

        if (data == null || data.Length == 0)
            throw new ArgumentException("Payload data cannot be null or empty", nameof(data));

        if (recipientWallets == null || recipientWallets.Length == 0)
            throw new ArgumentException("At least one recipient is required", nameof(recipientWallets));

        // Intentional sync-over-async: TransactionBuilder.AddPayload maintains a synchronous API
        // to preserve the fluent builder pattern (Create().AddPayload().AddPayload().SignAsync()).
        // The underlying AddPayloadAsync performs CPU-bound hash computation and in-memory
        // collection updates that complete synchronously in practice. Making this async would
        // require changing AddPayload to AddPayloadAsync, breaking the fluent builder API
        // contract and all callers across the codebase.
        var result = _transaction!.PayloadManager.AddPayloadAsync(
            data, recipientWallets, options).GetAwaiter().GetResult();

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to add payload: {result.ErrorMessage}");

        return this;
    }

    /// <inheritdoc/>
    public async Task<ITransactionBuilder> SignAsync(
        string wifPrivateKey,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionCreated();

        if (string.IsNullOrEmpty(wifPrivateKey))
            throw new ArgumentException("WIF private key cannot be null or empty", nameof(wifPrivateKey));

        var status = await _transaction!.SignAsync(wifPrivateKey, cancellationToken);

        if (status != TransactionStatus.Success)
            throw new InvalidOperationException($"Failed to sign transaction: {status}");

        _isSigned = true;
        return this;
    }

    /// <inheritdoc/>
    public TransactionResult<ITransaction> Build()
    {
        EnsureTransactionCreated();

        if (!_isSigned)
            return TransactionResult<ITransaction>.Failure(
                TransactionStatus.NotSigned,
                "Transaction must be signed before building");

        return TransactionResult<ITransaction>.Success(_transaction!);
    }

    private void ValidateNotSigned()
    {
        if (_isSigned)
            throw new InvalidOperationException(
                "Cannot modify transaction after signing");
    }

    private void EnsureTransactionCreated()
    {
        if (_transaction == null)
            throw new InvalidOperationException(
                "Transaction not created. Call Create() first.");
    }

    private void ValidateWallets(string[] wallets)
    {
        if (wallets == null || wallets.Length == 0)
            throw new ArgumentException("At least one wallet address is required", nameof(wallets));

        foreach (var wallet in wallets)
        {
            if (string.IsNullOrWhiteSpace(wallet))
                throw new ArgumentException("Wallet address cannot be null or empty", nameof(wallets));
        }
    }

    private void ValidateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON metadata cannot be null or empty", nameof(json));

        try
        {
            JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON metadata: {ex.Message}", nameof(json), ex);
        }
    }
}
