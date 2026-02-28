// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Buffers.Text;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.TransactionHandler.Models;
using Sorcha.TransactionHandler.Payload;
using Sorcha.TransactionHandler.Services;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.TransactionHandler.Serialization;

/// <summary>
/// JSON serializer for transactions.
/// </summary>
public class JsonTransactionSerializer : ITransactionSerializer
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ISymmetricCrypto _symmetricCrypto;

    /// <summary>
    /// Initializes a new instance of the JsonTransactionSerializer class.
    /// </summary>
    public JsonTransactionSerializer(ICryptoModule cryptoModule, IHashProvider hashProvider, ISymmetricCrypto symmetricCrypto)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
    }

    /// <inheritdoc/>
    public string SerializeToJson(ITransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        // Intentional sync-over-async: SerializeToJson implements the synchronous
        // ITransactionSerializer.SerializeToJson contract. GetAllAsync retrieves the
        // in-memory payload collection and completes synchronously in practice. Making
        // the serializer interface async would be a breaking change to ITransactionSerializer
        // and all consumers (JSON export, API responses, debugging output).
        var payloads = transaction.PayloadManager.GetAllAsync().GetAwaiter().GetResult();

        var obj = new
        {
            txId = transaction.TxId,
            version = (uint)transaction.Version,
            timestamp = transaction.Timestamp?.ToString("O"),
            previousTxHash = transaction.PreviousTxHash,
            senderWallet = transaction.SenderWallet,
            recipients = transaction.Recipients,
            metadata = transaction.Metadata != null ? JsonSerializer.Deserialize<object>(transaction.Metadata) : null,
            signature = transaction.Signature != null ? Base64Url.EncodeToString(transaction.Signature) : null,
            payloads = payloads.Select(p =>
            {
                var info = p.GetInfo();
                return new
                {
                    id = p.Id,
                    type = p.Type.ToString(),
                    size = p.OriginalSize,
                    isEncrypted = true,
                    isCompressed = p.IsCompressed,
                    accessibleBy = info.AccessibleBy,
                    data = SerializePayloadData(p.Data, info.ContentEncoding),
                    iv = Base64Url.EncodeToString(p.IV),
                    hash = Base64Url.EncodeToString(p.Hash),
                    contentType = info.ContentType,
                    contentEncoding = info.ContentEncoding
                };
            }).ToArray()
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <inheritdoc/>
    public ITransaction DeserializeFromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            throw new ArgumentNullException(nameof(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var version = (TransactionVersion)root.GetProperty("version").GetUInt32();
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager, version);

        // Set basic properties
        if (root.TryGetProperty("previousTxHash", out var prevHash))
            transaction.PreviousTxHash = prevHash.GetString();

        if (root.TryGetProperty("recipients", out var recipients))
            transaction.Recipients = recipients.EnumerateArray()
                .Select(r => r.GetString() ?? "")
                .ToArray();

        if (root.TryGetProperty("metadata", out var metadata))
            transaction.Metadata = metadata.GetRawText();

        if (root.TryGetProperty("timestamp", out var timestamp))
            transaction.Timestamp = DateTime.Parse(timestamp.GetString() ?? "");

        // Note: Cannot fully reconstruct signed transaction from JSON
        // as it would require private key information

        return transaction;
    }

    /// <summary>
    /// Serializes payload data based on ContentEncoding. Identity-encoded payloads
    /// (unencrypted JSON) are emitted as native JSON objects; all others as Base64url strings.
    /// </summary>
    private static object SerializePayloadData(byte[] data, string? contentEncoding)
    {
        if (contentEncoding == ContentEncodings.Identity)
            return JsonSerializer.Deserialize<JsonElement>(data);

        return Base64Url.EncodeToString(data);
    }

    /// <inheritdoc/>
    public byte[] SerializeToBinary(ITransaction transaction)
    {
        // Delegate binary serialization to BinaryTransactionSerializer
        var binarySerializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        return binarySerializer.SerializeToBinary(transaction);
    }

    /// <inheritdoc/>
    public ITransaction DeserializeFromBinary(byte[] data)
    {
        // Delegate binary deserialization to BinaryTransactionSerializer
        var binarySerializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        return binarySerializer.DeserializeFromBinary(data);
    }

    /// <inheritdoc/>
    public TransportPacket CreateTransportPacket(ITransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        // Use BinaryTransactionSerializer for transport packets
        var binarySerializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        return binarySerializer.CreateTransportPacket(transaction);
    }
}
