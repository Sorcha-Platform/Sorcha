// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.ServiceClients.Validator;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Encryption.Models;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Service for building blockchain transactions
/// </summary>
public interface ITransactionBuilderService
{
    /// <summary>
    /// Builds an action transaction with metadata
    /// </summary>
    /// <param name="blueprintId">The blueprint ID</param>
    /// <param name="actionId">The action ID</param>
    /// <param name="instanceId">The workflow instance ID (generated if new)</param>
    /// <param name="previousTransactionHash">Optional hash of previous transaction in the workflow</param>
    /// <param name="encryptedPayloads">Dictionary mapping wallet addresses to encrypted payloads</param>
    /// <param name="senderWallet">The sender's wallet address</param>
    /// <param name="registerAddress">The register address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The built transaction</returns>
    Task<Transaction> BuildActionTransactionAsync(
        string blueprintId,
        string actionId,
        string? instanceId,
        string? previousTransactionHash,
        Dictionary<string, byte[]> encryptedPayloads,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a rejection transaction
    /// </summary>
    /// <param name="originalTransactionHash">The transaction being rejected</param>
    /// <param name="reason">The reason for rejection</param>
    /// <param name="senderWallet">The sender's wallet address</param>
    /// <param name="registerAddress">The register address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The built rejection transaction</returns>
    Task<Transaction> BuildRejectionTransactionAsync(
        string originalTransactionHash,
        string reason,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds file transactions for attachments
    /// </summary>
    /// <param name="files">The file attachments</param>
    /// <param name="parentTransactionHash">The parent transaction hash</param>
    /// <param name="senderWallet">The sender's wallet address</param>
    /// <param name="registerAddress">The register address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of file transactions</returns>
    Task<List<Transaction>> BuildFileTransactionsAsync(
        IEnumerable<FileAttachment> files,
        string parentTransactionHash,
        string senderWallet,
        string registerAddress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a file attachment
/// </summary>
public record FileAttachment(
    string FileName,
    string ContentType,
    byte[] Content
);

/// <summary>
/// Extended transaction builder service for orchestration
/// </summary>
public static class TransactionBuilderServiceExtensions
{
    /// <summary>
    /// Canonical JSON serializer options for deterministic payload hashing.
    /// MUST match the options used by Validator (TransactionValidator, ValidationEngine)
    /// and all serialization boundaries (ValidatorServiceClient, TransactionPoolPoller).
    /// Contract: compact, no property renaming, UnsafeRelaxedJsonEscaping (no \u002B for +).
    /// </summary>
    internal static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Builds an action transaction using orchestration context.
    /// Serializes disclosed payloads into transaction data for signing and submission.
    /// </summary>
    public static Task<BuiltTransaction> BuildActionTransactionAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Dictionary<string, object> payloadData,
        Dictionary<string, Dictionary<string, object>> disclosedPayloads,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? ""
        };

        // Serialize the disclosed payloads into transaction data bytes
        var transactionPayload = new
        {
            type = "action",
            blueprintId = blueprint.Id,
            actionId = action.Id,
            instanceId = instance.Id,
            previousTxId = previousTransactionId,
            timestamp = DateTimeOffset.UtcNow,
            payloads = disclosedPayloads
        };

        // Serialize with canonical options for deterministic hashing.
        // UnsafeRelaxedJsonEscaping ensures '+' in timestamps/base64 is not escaped to \u002B,
        // which would cause hash mismatches after HTTP/Redis round-trips.
        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);

        // Generate TxId as SHA-256 hash of canonical transaction data (64 hex chars)
        var hashBytes = System.Security.Cryptography.SHA256.HashData(transactionData);
        var txId = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // PayloadHash = SHA-256 of canonical JSON — same bytes as TxId since we serialize once
        // with deterministic options. The Validator re-canonicalizes with the same options to verify.
        var payloadHash = txId;

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            RegisterId = instance.RegisterId,
            Metadata = metadata
        });
    }

    /// <summary>
    /// Builds an action transaction with encrypted payloads.
    /// Serializes encrypted payload groups into transaction data for signing and submission.
    /// Used when the encryption pipeline has pre-encrypted the disclosed payloads.
    /// </summary>
    public static Task<BuiltTransaction> BuildEncryptedActionTransactionAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Dictionary<string, object> payloadData,
        EncryptedPayloadGroup[] encryptedGroups,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? ""
        };

        // Serialize encrypted groups into a JSON-friendly representation.
        // byte[] fields are Base64-encoded since JSON cannot hold raw binary.
        var serializedGroups = encryptedGroups.Select(g => new
        {
            groupId = g.GroupId,
            disclosedFields = g.DisclosedFields,
            ciphertext = Convert.ToBase64String(g.Ciphertext),
            nonce = Convert.ToBase64String(g.Nonce),
            plaintextHash = Convert.ToBase64String(g.PlaintextHash),
            encryptionAlgorithm = g.EncryptionAlgorithm.ToString(),
            wrappedKeys = g.WrappedKeys.Select(wk => new
            {
                walletAddress = wk.WalletAddress,
                encryptedKey = Convert.ToBase64String(wk.EncryptedKey),
                algorithm = wk.Algorithm.ToString()
            }).ToArray()
        }).ToArray();

        // Build transaction payload with encrypted payloads instead of plaintext.
        // The "contentEncoding" field distinguishes encrypted from plaintext transactions.
        var transactionPayload = new
        {
            type = "action",
            contentEncoding = "encrypted",
            blueprintId = blueprint.Id,
            actionId = action.Id,
            instanceId = instance.Id,
            previousTxId = previousTransactionId,
            timestamp = DateTimeOffset.UtcNow,
            encryptedPayloads = serializedGroups
        };

        // Serialize with canonical options for deterministic hashing.
        // UnsafeRelaxedJsonEscaping ensures '+' in base64 is not escaped to \u002B,
        // which would cause hash mismatches after HTTP/Redis round-trips.
        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);

        // Generate TxId as SHA-256 hash of canonical transaction data (64 hex chars)
        var hashBytes = System.Security.Cryptography.SHA256.HashData(transactionData);
        var txId = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // PayloadHash = SHA-256 of canonical JSON — same bytes as TxId since we serialize once
        // with deterministic options. The Validator re-canonicalizes with the same options to verify.
        var payloadHash = txId;

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            RegisterId = instance.RegisterId,
            Metadata = metadata
        });
    }

    /// <summary>
    /// Builds a rejection transaction using orchestration context.
    /// Serializes rejection data into transaction data for signing and submission.
    /// </summary>
    public static Task<BuiltTransaction> BuildRejectionTransactionAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Dictionary<string, object> rejectionData,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? "",
            ["rejectionReason"] = rejectionData.GetValueOrDefault("rejectionReason", "")!
        };

        // Serialize the rejection data into transaction data bytes
        var transactionPayload = new
        {
            type = "rejection",
            blueprintId = blueprint.Id,
            actionId = action.Id,
            instanceId = instance.Id,
            previousTxId = previousTransactionId,
            timestamp = DateTimeOffset.UtcNow,
            rejectionData
        };

        // Serialize with canonical options for deterministic hashing
        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);

        // Generate TxId as SHA-256 hash of canonical transaction data (64 hex chars)
        var hashBytes = System.Security.Cryptography.SHA256.HashData(transactionData);
        var txId = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // PayloadHash = TxId — same canonical bytes, same hash
        var payloadHash = txId;

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            TransactionType = "rejection",
            RegisterId = instance.RegisterId,
            Metadata = metadata
        });
    }
}

/// <summary>
/// Represents a built transaction ready for signing and submission
/// </summary>
public class BuiltTransaction
{
    /// <summary>
    /// The raw transaction data for signing
    /// </summary>
    public required byte[] TransactionData { get; init; }

    /// <summary>
    /// The transaction ID (64-char SHA-256 hex hash)
    /// </summary>
    public required string TxId { get; init; }

    /// <summary>
    /// The payload hash (64-char SHA-256 hex hash of compact JSON)
    /// </summary>
    public required string PayloadHash { get; init; }

    /// <summary>
    /// The signing data: "{TxId}:{PayloadHash}" — matches Validator verification contract
    /// </summary>
    public byte[] SigningData => System.Text.Encoding.UTF8.GetBytes($"{TxId}:{PayloadHash}");

    /// <summary>
    /// Transaction type (action, rejection, file)
    /// </summary>
    public string TransactionType { get; init; } = "action";

    /// <summary>
    /// The register this transaction belongs to
    /// </summary>
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>
    /// The sender wallet address (set by caller after building)
    /// </summary>
    public string SenderWallet { get; set; } = string.Empty;

    /// <summary>
    /// The signature (populated after signing)
    /// </summary>
    public byte[]? Signature { get; set; }

    /// <summary>
    /// Transaction metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Converts to an TransactionSubmission for submission to the Validator Service.
    /// Maps BuiltTransaction fields to the Validator's expected format per data-model.md.
    /// </summary>
    /// <param name="signResult">The wallet sign result containing signature and public key bytes</param>
    /// <returns>An TransactionSubmission ready for Validator Service submission</returns>
    public TransactionSubmission ToTransactionSubmission(Sorcha.ServiceClients.Wallet.WalletSignResult signResult)
    {
        var payloadElement = JsonSerializer.Deserialize<JsonElement>(TransactionData);

        return new TransactionSubmission
        {
            TransactionId = TxId,
            RegisterId = RegisterId,
            BlueprintId = Metadata.GetValueOrDefault("blueprintId")?.ToString() ?? string.Empty,
            ActionId = Metadata.GetValueOrDefault("actionId")?.ToString() ?? string.Empty,
            Payload = payloadElement,
            PayloadHash = PayloadHash,
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(signResult.Signature),
                    Algorithm = signResult.Algorithm
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = Metadata.GetValueOrDefault("previousTxId")?.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["instanceId"] = Metadata.GetValueOrDefault("instanceId")?.ToString() ?? string.Empty,
                ["Type"] = string.Equals(TransactionType, "rejection", StringComparison.OrdinalIgnoreCase)
                    ? "Rejection"
                    : "Action"
            }
        };
    }

    /// <summary>
    /// Converts to a TransactionModel for submission to the Register Service
    /// </summary>
    public Sorcha.Register.Models.TransactionModel ToTransactionModel()
    {
        var metaData = new Sorcha.Register.Models.TransactionMetaData
        {
            BlueprintId = Metadata.GetValueOrDefault("blueprintId")?.ToString(),
            InstanceId = Metadata.GetValueOrDefault("instanceId")?.ToString()
        };

        if (Metadata.TryGetValue("actionId", out var actionIdObj) && actionIdObj is int actionId)
        {
            metaData.ActionId = (uint)actionId;
        }

        return new Sorcha.Register.Models.TransactionModel
        {
            TxId = TxId,
            RegisterId = RegisterId,
            SenderWallet = SenderWallet,
            Signature = Signature != null ? Base64Url.EncodeToString(Signature) : string.Empty,
            MetaData = metaData,
            PayloadCount = 0,
            Payloads = Array.Empty<Sorcha.Register.Models.PayloadModel>(),
            TimeStamp = DateTime.UtcNow
        };
    }
}
