// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.TransactionHandler;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.TransactionHandler.Payload;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for building blockchain transactions
/// </summary>
public class TransactionBuilderService : ITransactionBuilderService
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ILogger<TransactionBuilderService> _logger;

    public TransactionBuilderService(
        ICryptoModule cryptoModule,
        IHashProvider hashProvider,
        ISymmetricCrypto symmetricCrypto,
        ILogger<TransactionBuilderService> logger)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Transaction> BuildActionTransactionAsync(
        string blueprintId,
        string actionId,
        string? instanceId,
        string? previousTransactionHash,
        Dictionary<string, byte[]> encryptedPayloads,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            throw new ArgumentException("Blueprint ID cannot be null or empty", nameof(blueprintId));
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            throw new ArgumentException("Action ID cannot be null or empty", nameof(actionId));
        }

        if (encryptedPayloads == null || !encryptedPayloads.Any())
        {
            throw new ArgumentException("Encrypted payloads cannot be null or empty", nameof(encryptedPayloads));
        }

        if (string.IsNullOrWhiteSpace(senderWallet))
        {
            throw new ArgumentException("Sender wallet cannot be null or empty", nameof(senderWallet));
        }

        if (string.IsNullOrWhiteSpace(registerAddress))
        {
            throw new ArgumentException("Register address cannot be null or empty", nameof(registerAddress));
        }

        // Generate instance ID if this is a new workflow instance
        var workflowInstanceId = instanceId ?? Guid.NewGuid().ToString();

        // Create metadata
        var metadata = new
        {
            type = "action",
            blueprintId,
            actionId,
            instanceId = workflowInstanceId,
            timestamp = DateTimeOffset.UtcNow
        };

        // Create payload manager
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);

        // Create transaction (note: SenderWallet is read-only and set during signing)
        var transaction = new Transaction(
            _cryptoModule,
            _hashProvider,
            payloadManager,
            TransactionVersion.V1)
        {
            Recipients = encryptedPayloads.Keys.ToArray(),
            Metadata = JsonSerializer.Serialize(metadata),
            PreviousTxHash = previousTransactionHash,
            RegisterId = registerAddress,
            Timestamp = DateTime.UtcNow
        };

        // Add encrypted payloads
        foreach (var (wallet, payload) in encryptedPayloads)
        {
            await payloadManager.AddPayloadAsync(
                payload,
                new[] { wallet },
                null,
                cancellationToken);
        }

        _logger.LogInformation(
            "Built action transaction for blueprint {BlueprintId}, action {ActionId}, instance {InstanceId}",
            blueprintId, actionId, workflowInstanceId);

        return transaction;
    }

    /// <inheritdoc/>
    public async Task<Transaction> BuildRejectionTransactionAsync(
        string originalTransactionHash,
        string reason,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(originalTransactionHash))
        {
            throw new ArgumentException("Original transaction hash cannot be null or empty", nameof(originalTransactionHash));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason cannot be null or empty", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(senderWallet))
        {
            throw new ArgumentException("Sender wallet cannot be null or empty", nameof(senderWallet));
        }

        if (string.IsNullOrWhiteSpace(registerAddress))
        {
            throw new ArgumentException("Register address cannot be null or empty", nameof(registerAddress));
        }

        // Create metadata for rejection
        var metadata = new
        {
            type = "rejection",
            rejectedTransactionHash = originalTransactionHash,
            reason,
            timestamp = DateTimeOffset.UtcNow
        };

        // Create payload manager
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);

        // Create rejection transaction (note: SenderWallet is read-only and set during signing)
        var transaction = new Transaction(
            _cryptoModule,
            _hashProvider,
            payloadManager,
            TransactionVersion.V1)
        {
            Recipients = Array.Empty<string>(), // Rejection is sent back, recipients determined by routing
            Metadata = JsonSerializer.Serialize(metadata),
            PreviousTxHash = originalTransactionHash,
            RegisterId = registerAddress,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Built rejection transaction for {OriginalTx} with reason: {Reason}",
            originalTransactionHash, reason);

        return transaction;
    }

    /// <inheritdoc/>
    public async Task<List<Transaction>> BuildFileTransactionsAsync(
        IEnumerable<FileAttachment> files,
        string parentTransactionHash,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default)
    {
        if (files == null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        if (string.IsNullOrWhiteSpace(parentTransactionHash))
        {
            throw new ArgumentException("Parent transaction hash cannot be null or empty", nameof(parentTransactionHash));
        }

        if (string.IsNullOrWhiteSpace(senderWallet))
        {
            throw new ArgumentException("Sender wallet cannot be null or empty", nameof(senderWallet));
        }

        if (string.IsNullOrWhiteSpace(registerAddress))
        {
            throw new ArgumentException("Register address cannot be null or empty", nameof(registerAddress));
        }

        var fileList = files.ToList();
        if (!fileList.Any())
        {
            _logger.LogDebug("No files provided, returning empty list");
            return new List<Transaction>();
        }

        var fileTransactions = new List<Transaction>();

        foreach (var file in fileList)
        {
            if (file.Content == null || file.Content.Length == 0)
            {
                _logger.LogWarning("Skipping file {FileName} with empty content", file.FileName);
                continue;
            }

            // Create metadata for file
            var metadata = new
            {
                type = "file",
                fileName = file.FileName,
                contentType = file.ContentType,
                size = file.Content.Length,
                parentTransactionHash,
                timestamp = DateTimeOffset.UtcNow
            };

            // Create payload manager
            var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);

            // Create file transaction (note: SenderWallet is read-only and set during signing)
            var transaction = new Transaction(
                _cryptoModule,
                _hashProvider,
                payloadManager,
                TransactionVersion.V1)
            {
                Recipients = Array.Empty<string>(), // Files are linked to parent transaction
                Metadata = JsonSerializer.Serialize(metadata),
                PreviousTxHash = parentTransactionHash,
                RegisterId = registerAddress,
                Timestamp = DateTime.UtcNow
            };

            // Add file content as payload
            await payloadManager.AddPayloadAsync(
                file.Content,
                new[] { senderWallet },
                null,
                cancellationToken);

            fileTransactions.Add(transaction);

            _logger.LogInformation(
                "Built file transaction for {FileName} ({Size} bytes)",
                file.FileName, file.Content.Length);
        }

        return fileTransactions;
    }

    /// <inheritdoc/>
    public async Task<EncryptedChunkResult> EncryptFileChunkAsync(
        byte[] chunkContent,
        byte[] masterFileKey,
        byte[] salt,
        int chunkIndex,
        string senderWallet,
        CancellationToken cancellationToken = default)
    {
        if (chunkContent == null || chunkContent.Length == 0)
            throw new ArgumentException("Chunk content cannot be null or empty.", nameof(chunkContent));

        if (masterFileKey == null)
            throw new ArgumentNullException(nameof(masterFileKey));

        if (salt == null)
            throw new ArgumentNullException(nameof(salt));

        if (chunkIndex < 0)
            throw new ArgumentException("Chunk index must be non-negative.", nameof(chunkIndex));

        if (string.IsNullOrWhiteSpace(senderWallet))
            throw new ArgumentException("Sender wallet cannot be null or empty.", nameof(senderWallet));

        var chunkKey = HkdfKeyDerivation.DeriveChunkKey(masterFileKey, salt, chunkIndex);

        var result = await _symmetricCrypto.EncryptAsync(
            chunkContent,
            EncryptionType.XCHACHA20_POLY1305,
            chunkKey,
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            _logger.LogError(
                "Failed to encrypt chunk {ChunkIndex} for sender {SenderWallet}: {Error}",
                chunkIndex, senderWallet, result.ErrorMessage);
            throw new InvalidOperationException($"Chunk encryption failed for index {chunkIndex}: {result.ErrorMessage}");
        }

        _logger.LogDebug(
            "Encrypted chunk {ChunkIndex} ({Size} bytes) for sender {SenderWallet}",
            chunkIndex, chunkContent.Length, senderWallet);

        return new EncryptedChunkResult(result.Value.Data, result.Value.IV);
    }

    /// <inheritdoc/>
    public FileUploadSession CreateFileUploadSession()
    {
        var masterFileKey = HkdfKeyDerivation.GenerateMasterFileKey();
        var salt = HkdfKeyDerivation.GenerateSalt();

        _logger.LogDebug("Created new file upload session");

        return new FileUploadSession(masterFileKey, salt);
    }
}
