// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Buffers.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Blueprint.Models.JsonLd;

namespace Sorcha.TransactionHandler.Core;

/// <summary>
/// Represents a transaction in the Sorcha platform.
/// </summary>
public class Transaction : ITransaction
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;

    /// <summary>
    /// Initializes a new instance of the Transaction class.
    /// </summary>
    /// <param name="cryptoModule">The cryptography module</param>
    /// <param name="hashProvider">The hash provider</param>
    /// <param name="payloadManager">The payload manager</param>
    /// <param name="version">The transaction version</param>
    public Transaction(
        ICryptoModule cryptoModule,
        IHashProvider hashProvider,
        IPayloadManager payloadManager,
        TransactionVersion version = TransactionVersion.V1)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        PayloadManager = payloadManager ?? throw new ArgumentNullException(nameof(payloadManager));
        Version = version;
        Timestamp = DateTime.UtcNow;
    }

    /// <inheritdoc/>
    public string? TxId { get; private set; }

    /// <inheritdoc/>
    public TransactionVersion Version { get; }

    /// <inheritdoc/>
    public string? PreviousTxHash { get; set; }

    /// <inheritdoc/>
    public string? SenderWallet { get; private set; }

    /// <inheritdoc/>
    public string[]? Recipients { get; set; }

    /// <inheritdoc/>
    public string? Metadata { get; set; }

    /// <inheritdoc/>
    public DateTime? Timestamp { get; set; }

    /// <inheritdoc/>
    public byte[]? Signature { get; private set; }

    /// <inheritdoc/>
    public IPayloadManager PayloadManager { get; }

    /// <summary>
    /// Register (ledger) identifier for this transaction
    /// Used for generating DID URIs in JSON-LD format
    /// </summary>
    public string? RegisterId { get; set; }

    /// <summary>
    /// Docket number this transaction is sealed in
    /// </summary>
    public ulong? DocketNumber { get; set; }

    /// <inheritdoc/>
    public async Task<TransactionStatus> SignAsync(
        string wifPrivateKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(wifPrivateKey))
            return TransactionStatus.InvalidSignature;

        try
        {
            // Compute signing hash (double SHA-256)
            var signingData = SerializeForSigning();
            var hash1 = _hashProvider.ComputeHash(signingData, HashType.SHA256);
            var hash2 = _hashProvider.ComputeHash(hash1, HashType.SHA256);

            // TODO: Extract network and key from WIF
            // For now, use ED25519
            var network = WalletNetworks.ED25519;

            // Sign the hash
            var signResult = await _cryptoModule.SignAsync(
                hash2,
                (byte)network,
                Convert.FromBase64String(wifPrivateKey), // Simplified - should decode WIF properly
                cancellationToken);

            if (!signResult.IsSuccess || signResult.Value == null)
                return TransactionStatus.InvalidSignature;

            Signature = signResult.Value;

            // TODO: Calculate sender wallet from private key
            SenderWallet = "ws1temp"; // Placeholder

            // Calculate transaction ID
            TxId = Base64Url.EncodeToString(_hashProvider.ComputeHash(
                Signature,
                HashType.SHA256));

            return TransactionStatus.Success;
        }
        catch
        {
            return TransactionStatus.InvalidSignature;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Review M1 (DEFERRED — see the security-hardening backlog): this verify is a placeholder —
    /// it computes the signing hash but never compares it against the signature (public-key
    /// extraction is a "return success as placeholder" TODO), so it returns
    /// <see cref="TransactionStatus.Success"/> for any signed transaction with valid payloads.
    /// It is NOT on any live path (the V1 <c>TransactionFactory</c> is not DI-registered; the real
    /// validator verifies via <c>ICryptoModule.VerifyAsync</c>) and has no non-test callers, so the
    /// risk is purely latent. Making it fail loud is the right fix, but V1 is the module's only
    /// implemented transaction version and an entire signing/verification test suite asserts this
    /// behaviour — resolving it needs a decision (implement real V1 verification vs excise the V1
    /// verify path + its no-op tests), which is out of scope for the mechanical Bucket-B pass.
    /// </remarks>
    public async Task<TransactionStatus> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        if (Signature == null || string.IsNullOrEmpty(SenderWallet))
            return TransactionStatus.NotSigned;

        try
        {
            // Compute signing hash
            var signingData = SerializeForSigning();
            var hash1 = _hashProvider.ComputeHash(signingData, HashType.SHA256);
            var hash2 = _hashProvider.ComputeHash(hash1, HashType.SHA256);

            // TODO: Extract public key from wallet address
            // For now, return success as placeholder

            // Verify payloads
            var payloadsValid = await PayloadManager.VerifyAllAsync();
            if (!payloadsValid)
                return TransactionStatus.InvalidPayload;

            return TransactionStatus.Success;
        }
        catch
        {
            return TransactionStatus.InvalidSignature;
        }
    }

    /// <inheritdoc/>
    public byte[] SerializeToBinary()
    {
        // Serialize using the same format as BinaryTransactionSerializer
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);

        // Write version
        writer.Write((uint)Version);

        // Write timestamp
        if (Timestamp.HasValue)
        {
            writer.Write(true);
            writer.Write(Timestamp.Value.ToBinary());
        }
        else
        {
            writer.Write(false);
        }

        // Write previous transaction hash
        WriteBinaryString(writer, PreviousTxHash ?? string.Empty);

        // Write sender wallet
        WriteBinaryString(writer, SenderWallet ?? string.Empty);

        // Write recipients
        var recipients = Recipients ?? Array.Empty<string>();
        WriteBinaryVarInt(writer, (ulong)recipients.Length);
        foreach (var recipient in recipients)
        {
            WriteBinaryString(writer, recipient);
        }

        // Write metadata
        WriteBinaryString(writer, Metadata ?? string.Empty);

        // Write signature
        if (Signature != null && Signature.Length > 0)
        {
            writer.Write(true);
            WriteBinaryVarInt(writer, (ulong)Signature.Length);
            writer.Write(Signature);
        }
        else
        {
            writer.Write(false);
        }

        // Write payloads
        var payloads = PayloadManager.GetAllAsync().GetAwaiter().GetResult();
        var payloadList = payloads.ToArray();
        WriteBinaryVarInt(writer, (ulong)payloadList.Length);

        foreach (var payload in payloadList)
        {
            WriteBinaryVarInt(writer, (ulong)payload.Id);
            writer.Write((byte)payload.Type);
            WriteBinaryVarInt(writer, (ulong)payload.OriginalSize);
            writer.Write(payload.IsCompressed);

            var info = payload.GetInfo();
            var accessibleBy = info.AccessibleBy ?? Array.Empty<string>();
            WriteBinaryVarInt(writer, (ulong)accessibleBy.Length);
            foreach (var wallet in accessibleBy)
            {
                WriteBinaryString(writer, wallet);
            }

            WriteBinaryVarInt(writer, (ulong)payload.Data.Length);
            writer.Write(payload.Data);

            WriteBinaryVarInt(writer, (ulong)payload.IV.Length);
            writer.Write(payload.IV);

            WriteBinaryVarInt(writer, (ulong)payload.Hash.Length);
            writer.Write(payload.Hash);
        }

        return ms.ToArray();
    }

    private static void WriteBinaryVarInt(System.IO.BinaryWriter writer, ulong value)
    {
        var encoded = Sorcha.Cryptography.Utilities.VariableLengthInteger.Encode(value);
        writer.Write(encoded);
    }

    private static void WriteBinaryString(System.IO.BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteBinaryVarInt(writer, 0);
        }
        else
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteBinaryVarInt(writer, (ulong)bytes.Length);
            writer.Write(bytes);
        }
    }

    /// <inheritdoc/>
    public string SerializeToJson()
    {
        var obj = new
        {
            txId = TxId,
            version = (uint)Version,
            timestamp = Timestamp,
            previousTxHash = PreviousTxHash,
            senderWallet = SenderWallet,
            recipients = Recipients,
            metadata = Metadata != null ? JsonSerializer.Deserialize<object>(Metadata) : null,
            signature = Signature != null ? Base64Url.EncodeToString(Signature) : null,
            payloadCount = PayloadManager.Count
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Serializes the transaction to JSON-LD format
    /// Implements the Blockchain Transaction Format specification
    /// </summary>
    /// <param name="includeContext">Whether to include the @context field (default: true)</param>
    /// <returns>JSON-LD formatted transaction</returns>
    public string SerializeToJsonLd(bool includeContext = true)
    {
        var jsonLd = new JsonObject();

        // Add JSON-LD context
        if (includeContext)
        {
            jsonLd["@context"] = BlockchainContext.ContextUrl;
        }

        // Add JSON-LD type
        jsonLd["@type"] = "Transaction";

        // Add DID URI if RegisterId is set
        if (!string.IsNullOrWhiteSpace(RegisterId) && !string.IsNullOrWhiteSpace(TxId))
        {
            jsonLd["@id"] = BlockchainContext.GenerateDidUri(RegisterId, TxId);
        }

        // Add register identifier
        if (!string.IsNullOrWhiteSpace(RegisterId))
        {
            jsonLd["registerId"] = RegisterId;
        }

        // Add transaction properties
        if (!string.IsNullOrWhiteSpace(TxId))
        {
            jsonLd["txId"] = TxId;
        }

        if (!string.IsNullOrWhiteSpace(PreviousTxHash))
        {
            jsonLd["previousTxHash"] = PreviousTxHash;
        }

        if (DocketNumber.HasValue)
        {
            jsonLd["docketNumber"] = DocketNumber.Value;
        }

        jsonLd["version"] = (uint)Version;

        if (Timestamp.HasValue)
        {
            jsonLd["timestamp"] = Timestamp.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }

        if (!string.IsNullOrWhiteSpace(SenderWallet))
        {
            jsonLd["senderWallet"] = SenderWallet;
        }

        if (Recipients != null && Recipients.Length > 0)
        {
            var recipientsArray = new JsonArray();
            foreach (var recipient in Recipients)
            {
                recipientsArray.Add(recipient);
            }
            jsonLd["recipients"] = recipientsArray;
        }

        // Add metadata as embedded JSON
        if (!string.IsNullOrWhiteSpace(Metadata))
        {
            try
            {
                var metadataObj = JsonSerializer.Deserialize<JsonObject>(Metadata);
                if (metadataObj != null)
                {
                    jsonLd["metadata"] = metadataObj;
                }
            }
            catch
            {
                // If metadata is not valid JSON, store as string
                jsonLd["metadata"] = Metadata;
            }
        }

        // Add signature
        if (Signature != null)
        {
            jsonLd["signature"] = Base64Url.EncodeToString(Signature);
        }

        // Add payload count
        jsonLd["payloadCount"] = PayloadManager.Count;

        return jsonLd.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    /// <summary>
    /// Generates the DID URI for this transaction
    /// Requires RegisterId and TxId to be set
    /// </summary>
    /// <returns>DID URI or null if RegisterId or TxId is not set</returns>
    public string? GenerateDidUri()
    {
        if (string.IsNullOrWhiteSpace(RegisterId) || string.IsNullOrWhiteSpace(TxId))
            return null;

        return BlockchainContext.GenerateDidUri(RegisterId, TxId);
    }

    /// <summary>
    /// Serializes transaction data for signing.
    /// </summary>
    private byte[] SerializeForSigning()
    {
        // Simplified serialization - should match spec
        var json = new
        {
            version = (uint)Version,
            timestamp = Timestamp,
            previousTxHash = PreviousTxHash ?? "",
            recipients = Recipients ?? Array.Empty<string>(),
            metadata = Metadata ?? "",
            payloadCount = PayloadManager.Count
        };

        var jsonString = JsonSerializer.Serialize(json);
        return System.Text.Encoding.UTF8.GetBytes(jsonString);
    }
}
