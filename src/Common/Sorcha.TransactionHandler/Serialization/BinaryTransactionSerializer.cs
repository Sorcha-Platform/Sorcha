// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Interfaces;
using Sorcha.TransactionHandler.Models;
using Sorcha.TransactionHandler.Payload;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;

namespace Sorcha.TransactionHandler.Serialization;

/// <summary>
/// Binary serializer for transactions using VarInt encoding.
/// </summary>
public class BinaryTransactionSerializer : ITransactionSerializer
{
    private readonly ICryptoModule _cryptoModule;
    private readonly IHashProvider _hashProvider;
    private readonly ISymmetricCrypto _symmetricCrypto;

    /// <summary>
    /// Initializes a new instance of the BinaryTransactionSerializer class.
    /// </summary>
    public BinaryTransactionSerializer(ICryptoModule cryptoModule, IHashProvider hashProvider, ISymmetricCrypto symmetricCrypto)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
    }

    /// <inheritdoc/>
    public byte[] SerializeToBinary(ITransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Write version
        writer.Write((uint)transaction.Version);

        // Write timestamp
        if (transaction.Timestamp.HasValue)
        {
            writer.Write(true);
            writer.Write(transaction.Timestamp.Value.ToBinary());
        }
        else
        {
            writer.Write(false);
        }

        // Write previous transaction hash
        WriteString(writer, transaction.PreviousTxHash ?? string.Empty);

        // Write sender wallet
        WriteString(writer, transaction.SenderWallet ?? string.Empty);

        // Write recipients
        var recipients = transaction.Recipients ?? Array.Empty<string>();
        WriteVarInt(writer, (ulong)recipients.Length);
        foreach (var recipient in recipients)
        {
            WriteString(writer, recipient);
        }

        // Write metadata
        WriteString(writer, transaction.Metadata ?? string.Empty);

        // Write signature
        if (transaction.Signature != null && transaction.Signature.Length > 0)
        {
            writer.Write(true);
            WriteVarInt(writer, (ulong)transaction.Signature.Length);
            writer.Write(transaction.Signature);
        }
        else
        {
            writer.Write(false);
        }

        // Write payloads
        // Intentional sync-over-async: SerializeToBinary implements the synchronous
        // ITransactionSerializer.SerializeToBinary contract. GetAllAsync retrieves the
        // in-memory payload collection and completes synchronously in practice. Making
        // the serializer interface async would be a breaking change to ITransactionSerializer
        // and all consumers (transport packets, binary wire format, peer service).
        var payloads = transaction.PayloadManager.GetAllAsync().GetAwaiter().GetResult();
        WriteVarInt(writer, (ulong)payloads.Count());

        foreach (var payload in payloads)
        {
            WriteVarInt(writer, (ulong)payload.Id);
            writer.Write((byte)payload.Type);
            WriteVarInt(writer, (ulong)payload.OriginalSize);
            writer.Write(payload.IsCompressed);

            // Write accessible by list
            var info = payload.GetInfo();
            var accessibleBy = info.AccessibleBy ?? Array.Empty<string>();
            WriteVarInt(writer, (ulong)accessibleBy.Length);
            foreach (var wallet in accessibleBy)
            {
                WriteString(writer, wallet);
            }

            // Write payload data
            WriteVarInt(writer, (ulong)payload.Data.Length);
            writer.Write(payload.Data);

            // Write IV
            WriteVarInt(writer, (ulong)payload.IV.Length);
            writer.Write(payload.IV);

            // Write hash
            WriteVarInt(writer, (ulong)payload.Hash.Length);
            writer.Write(payload.Hash);
        }

        return ms.ToArray();
    }

    /// <inheritdoc/>
    public ITransaction DeserializeFromBinary(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentNullException(nameof(data));

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Read version
        var version = (TransactionVersion)reader.ReadUInt32();

        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager, version);

        // Read timestamp
        bool hasTimestamp = reader.ReadBoolean();
        if (hasTimestamp)
        {
            transaction.Timestamp = DateTime.FromBinary(reader.ReadInt64());
        }

        // Read previous transaction hash
        var prevHash = ReadString(reader);
        if (!string.IsNullOrEmpty(prevHash))
            transaction.PreviousTxHash = prevHash;

        // Read sender wallet
        var senderWallet = ReadString(reader);
        // Note: Cannot set SenderWallet - it's set during signing

        // Read recipients
        var recipientCount = ReadVarInt(reader);
        var recipients = new string[recipientCount];
        for (ulong i = 0; i < recipientCount; i++)
        {
            recipients[i] = ReadString(reader);
        }
        transaction.Recipients = recipients;

        // Read metadata
        var metadata = ReadString(reader);
        if (!string.IsNullOrEmpty(metadata))
            transaction.Metadata = metadata;

        // Read signature (but cannot fully reconstruct signed transaction)
        bool hasSignature = reader.ReadBoolean();
        if (hasSignature)
        {
            var sigLength = ReadVarInt(reader);
            var signature = reader.ReadBytes((int)sigLength);
            // Note: Cannot set signature directly on transaction
        }

        // Read payloads
        var payloadCount = ReadVarInt(reader);
        for (ulong i = 0; i < payloadCount; i++)
        {
            var id = ReadVarInt(reader);
            var type = (PayloadType)reader.ReadByte();
            var originalSize = ReadVarInt(reader);
            var isCompressed = reader.ReadBoolean();

            // Read accessible by list
            var accessibleByCount = ReadVarInt(reader);
            var accessibleBy = new string[accessibleByCount];
            for (ulong j = 0; j < accessibleByCount; j++)
            {
                accessibleBy[j] = ReadString(reader);
            }

            // Read payload data
            var dataLength = ReadVarInt(reader);
            var payloadData = reader.ReadBytes((int)dataLength);

            // Read IV
            var ivLength = ReadVarInt(reader);
            var iv = reader.ReadBytes((int)ivLength);

            // Read hash
            var hashLength = ReadVarInt(reader);
            var hash = reader.ReadBytes((int)hashLength);

            // Note: Cannot fully reconstruct payload without encryption keys
            // This is a limitation for deserialization
        }

        return transaction;
    }

    /// <inheritdoc/>
    public string SerializeToJson(ITransaction transaction)
    {
        // Delegate to JsonTransactionSerializer
        var jsonSerializer = new JsonTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        return jsonSerializer.SerializeToJson(transaction);
    }

    /// <inheritdoc/>
    public ITransaction DeserializeFromJson(string json)
    {
        // Delegate to JsonTransactionSerializer
        var jsonSerializer = new JsonTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        return jsonSerializer.DeserializeFromJson(json);
    }

    /// <inheritdoc/>
    public TransportPacket CreateTransportPacket(ITransaction transaction)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        return new TransportPacket
        {
            TxId = transaction.TxId,
            RegisterId = null, // To be set by caller
            Data = SerializeToBinary(transaction)
        };
    }

    private void WriteVarInt(BinaryWriter writer, ulong value)
    {
        var encoded = VariableLengthInteger.Encode(value);
        writer.Write(encoded);
    }

    private ulong ReadVarInt(BinaryReader reader)
    {
        var ms = (MemoryStream)reader.BaseStream;
        var data = ms.ToArray();
        var offset = (int)ms.Position;

        var value = VariableLengthInteger.Decode(data, offset, out int bytesRead);
        ms.Position += bytesRead;

        return value;
    }

    private void WriteString(BinaryWriter writer, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteVarInt(writer, 0);
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(writer, (ulong)bytes.Length);
            writer.Write(bytes);
        }
    }

    private string ReadString(BinaryReader reader)
    {
        var length = ReadVarInt(reader);
        if (length == 0)
            return string.Empty;

        var bytes = reader.ReadBytes((int)length);
        return Encoding.UTF8.GetString(bytes);
    }

}
