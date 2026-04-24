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
using Sorcha.TransactionHandler.Encryption;

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

    /// <summary>
    /// Encrypts a file chunk using an HKDF-derived key from the master file key.
    /// The chunk key is derived via <see cref="HkdfKeyDerivation.DeriveChunkKey"/> binding the
    /// master key, salt, and chunk index so that each chunk is encrypted with a unique key.
    /// </summary>
    /// <param name="chunkContent">The raw bytes of the chunk to encrypt.</param>
    /// <param name="masterFileKey">The 32-byte master file key for this upload session.</param>
    /// <param name="salt">The random salt generated for this upload session.</param>
    /// <param name="chunkIndex">The zero-based index of this chunk within the file.</param>
    /// <param name="senderWallet">The sender's wallet address (used for audit context).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="EncryptedChunkResult"/> containing the ciphertext and nonce.</returns>
    Task<EncryptedChunkResult> EncryptFileChunkAsync(
        byte[] chunkContent,
        byte[] masterFileKey,
        byte[] salt,
        int chunkIndex,
        string senderWallet,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new master file key and random salt for a file upload session.
    /// The returned values must be retained by the caller for the lifetime of the upload;
    /// the master key is later wrapped per recipient at action submission time.
    /// </summary>
    /// <returns>A <see cref="FileUploadSession"/> containing the master key and salt.</returns>
    FileUploadSession CreateFileUploadSession();
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
/// The result of encrypting a single file chunk: the ciphertext bytes and the nonce used.
/// </summary>
public record EncryptedChunkResult(byte[] EncryptedContent, byte[] Nonce);

/// <summary>
/// Session context for a chunked file upload: the 32-byte master key and its associated salt.
/// The master key is wrapped per recipient at action submission time; the salt is stored in the FileReference.
/// </summary>
public record FileUploadSession(byte[] MasterFileKey, byte[] Salt);

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

        // Extract recipient wallet addresses from the disclosed payloads dictionary.
        // In dev-mode (plaintext), the dictionary keys are the wallet addresses
        // that each payload is disclosed to.
        var recipients = disclosedPayloads.Keys
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = recipients
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

        // Extract recipient wallet addresses from the encrypted disclosure groups.
        // Each wrappedKey entry is a wallet that can decrypt the group — these are
        // the transaction's recipients and must be set so the Register Service's
        // InboundTransactionRouter can notify their Wallet Services on docket seal.
        var recipients = encryptedGroups
            .SelectMany(g => g.WrappedKeys)
            .Select(wk => wk.WalletAddress)
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = recipients
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

        // Rejection recipients: the rejector (so the rejection is visible in their
        // own wallet context) and the route-back target participant (who needs to
        // re-attempt the target action). Populating RecipientsWallets is load-bearing:
        // without it, InboundTransactionRouter can't notify the wallets on docket seal
        // and StateReconstructionService has no WalletAccess entry to attribute the
        // transaction against — leaving a hole in the chain walk that causes the
        // next submission to pick a stale PreviousTransactionId and trip VAL_CHAIN_FORK.
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (instance.ParticipantWallets != null)
        {
            if (!string.IsNullOrEmpty(action.Sender)
                && instance.ParticipantWallets.TryGetValue(action.Sender, out var senderWallet)
                && !string.IsNullOrEmpty(senderWallet))
            {
                recipients.Add(senderWallet);
            }

            var targetActionId = action.RejectionConfig?.TargetActionId;
            if (targetActionId.HasValue)
            {
                var targetAction = blueprint.Actions?.FirstOrDefault(a => a.Id == targetActionId.Value);
                var targetParticipantId = action.RejectionConfig?.TargetParticipantId ?? targetAction?.Sender;
                if (!string.IsNullOrEmpty(targetParticipantId)
                    && instance.ParticipantWallets.TryGetValue(targetParticipantId, out var targetWallet)
                    && !string.IsNullOrEmpty(targetWallet))
                {
                    recipients.Add(targetWallet);
                }
            }
        }

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            TransactionType = "rejection",
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = recipients.ToList()
        });
    }

    /// <summary>
    /// Builds a presentation-initiated transaction (Feature 111). Carries the submitter
    /// wallet, action reference, requirements digest, and presentation request id, but
    /// NO credential data — nothing has been presented at this point.
    /// </summary>
    public static Task<BuiltTransaction> BuildPresentationInitiatedAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Guid presentationRequestId,
        string consumerName,
        byte[] requirementsDigest,
        int validityWindowSeconds,
        string submitterWallet,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? "",
            ["presentationRequestId"] = presentationRequestId.ToString(),
            ["consumerName"] = consumerName
        };

        var transactionPayload = new
        {
            type = "presentation-initiated",
            blueprintId = blueprint.Id,
            actionId = action.Id,
            instanceId = instance.Id,
            previousTxId = previousTransactionId,
            presentationRequestId,
            consumerName,
            submitterWallet,
            requirementsDigest = Convert.ToHexString(requirementsDigest).ToLowerInvariant(),
            validityWindowSeconds,
            timestamp = DateTimeOffset.UtcNow
        };

        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);
        var txId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(transactionData)).ToLowerInvariant();

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = txId,
            TransactionType = "presentation-initiated",
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = new List<string> { submitterWallet }
        });
    }

    /// <summary>
    /// Builds a presentation-outcome transaction (Feature 111). On success, carries
    /// verified claims + presentationSubmissionHash; on decline, carries the reason
    /// code and (when verbose) verifier diagnostics.
    /// </summary>
    public static Task<BuiltTransaction> BuildPresentationOutcomeAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Guid presentationRequestId,
        string consumerName,
        string submitterWallet,
        string outcomeKind,                                // "success" | "decline"
        IReadOnlyDictionary<string, object>? verifiedClaims,
        string? declineReason,
        IReadOnlyDictionary<string, object>? verifierDiagnostics,
        string? presentationSubmissionHash,
        IReadOnlyDictionary<string, object>? actionPayload, // non-credential fields carried from the original attempt
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? "",
            ["presentationRequestId"] = presentationRequestId.ToString(),
            ["consumerName"] = consumerName,
            ["outcomeKind"] = outcomeKind
        };

        var transactionPayload = new Dictionary<string, object?>
        {
            ["type"] = "presentation-outcome",
            ["kind"] = outcomeKind,
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId,
            ["presentationRequestId"] = presentationRequestId,
            ["consumerName"] = consumerName,
            ["submitterWallet"] = submitterWallet,
            ["timestamp"] = DateTimeOffset.UtcNow
        };

        if (string.Equals(outcomeKind, "success", StringComparison.OrdinalIgnoreCase))
        {
            transactionPayload["verifiedClaims"] = verifiedClaims;
            transactionPayload["presentationSubmissionHash"] = presentationSubmissionHash;
            if (actionPayload is not null) transactionPayload["actionPayload"] = actionPayload;
        }
        else
        {
            transactionPayload["reason"] = declineReason;
            if (verifierDiagnostics is not null) transactionPayload["verifierDiagnostics"] = verifierDiagnostics;
        }

        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);
        var txId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(transactionData)).ToLowerInvariant();

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = txId,
            TransactionType = "presentation-outcome",
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = new List<string> { submitterWallet }
        });
    }

    /// <summary>
    /// Builds a presentation-abandoned transaction (Feature 111). Carries only the
    /// presentation request id and abandonment timestamp. Only written when the
    /// blueprint opts in via recordAbandonment = true.
    /// </summary>
    public static Task<BuiltTransaction> BuildPresentationAbandonedAsync(
        this ITransactionBuilderService service,
        BlueprintModel blueprint,
        Instance instance,
        ActionModel action,
        Guid presentationRequestId,
        string consumerName,
        string submitterWallet,
        int validityWindowSeconds,
        string? previousTransactionId,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprint.Id,
            ["actionId"] = action.Id,
            ["instanceId"] = instance.Id,
            ["previousTxId"] = previousTransactionId ?? "",
            ["presentationRequestId"] = presentationRequestId.ToString(),
            ["consumerName"] = consumerName
        };

        var transactionPayload = new
        {
            type = "presentation-abandoned",
            blueprintId = blueprint.Id,
            actionId = action.Id,
            instanceId = instance.Id,
            previousTxId = previousTransactionId,
            presentationRequestId,
            consumerName,
            submitterWallet,
            validityWindowSeconds,
            abandonedAt = DateTimeOffset.UtcNow
        };

        var transactionData = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(transactionPayload, CanonicalJsonOptions);
        var txId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(transactionData)).ToLowerInvariant();

        return Task.FromResult(new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = txId,
            TransactionType = "presentation-abandoned",
            RegisterId = instance.RegisterId,
            Metadata = metadata,
            RecipientsWallets = new List<string> { submitterWallet }
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
    /// Recipient wallet addresses extracted from disclosure groups. Used by the
    /// Register Service's InboundTransactionRouter to notify recipient Wallet
    /// Services when the transaction is sealed in a docket.
    /// </summary>
    public List<string> RecipientsWallets { get; set; } = [];

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
    /// <param name="sequenceNumber">Per-sender monotonic sequence number for replay protection (SEC-AUDIT 4.2)</param>
    /// <returns>An TransactionSubmission ready for Validator Service submission</returns>
    public TransactionSubmission ToTransactionSubmission(Sorcha.ServiceClients.Wallet.WalletSignResult signResult, long sequenceNumber = 0)
    {
        var payloadElement = JsonSerializer.Deserialize<JsonElement>(TransactionData);

        var submissionMetadata = new Dictionary<string, string>
        {
            ["instanceId"] = Metadata.GetValueOrDefault("instanceId")?.ToString() ?? string.Empty,
            ["Type"] = MapTransactionTypeName(TransactionType)
        };

        // Feature 111 — propagate lifecycle markers onto the sealed transaction's
        // TrackingData so retry-gating (US3) and audit consumers can query outcome
        // kind without decrypting payloads.
        if (Metadata.TryGetValue("outcomeKind", out var outcomeKind) && outcomeKind is not null)
            submissionMetadata["outcomeKind"] = outcomeKind.ToString()!;
        if (Metadata.TryGetValue("presentationRequestId", out var reqId) && reqId is not null)
            submissionMetadata["presentationRequestId"] = reqId.ToString()!;
        if (Metadata.TryGetValue("consumerName", out var consumer) && consumer is not null)
            submissionMetadata["consumerName"] = consumer.ToString()!;

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
                    Algorithm = signResult.Algorithm,
                    SignedBy = SenderWallet
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = Metadata.GetValueOrDefault("previousTxId")?.ToString(),
            SequenceNumber = sequenceNumber,
            RecipientsWallets = RecipientsWallets.Count > 0 ? RecipientsWallets : null,
            Metadata = submissionMetadata
        };
    }

    // Maps the internal TransactionType string to the TransactionType enum name
    // used by DocketBuildTriggerService.Enum.TryParse on the validator side.
    private static string MapTransactionTypeName(string transactionType) => transactionType switch
    {
        "rejection" => "Rejection",
        "presentation-initiated" => "PresentationInitiated",
        "presentation-outcome" => "PresentationOutcome",
        "presentation-abandoned" => "PresentationAbandoned",
        _ => "Action"
    };

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
