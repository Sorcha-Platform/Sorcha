// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for BuiltTransaction.ToTransactionSubmission mapper
/// </summary>
public class TransactionBuilderExtensionsTests
{
    private static readonly byte[] TestPublicKey = new byte[32];
    private static readonly byte[] TestSignature = new byte[64];

    private static BuiltTransaction CreateTestTransaction()
    {
        var payload = new
        {
            type = "action",
            blueprintId = "bp-001",
            actionId = "3",
            instanceId = "inst-abc",
            data = new { field1 = "value1" }
        };

        var transactionData = JsonSerializer.SerializeToUtf8Bytes(payload);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(transactionData);
        var txId = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var payloadHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(transactionData))))).ToLowerInvariant();

        return new BuiltTransaction
        {
            TransactionData = transactionData,
            TxId = txId,
            PayloadHash = payloadHash,
            RegisterId = "register-42",
            SenderWallet = "wallet-sender-addr",
            Signature = TestSignature,
            Metadata = new Dictionary<string, object>
            {
                ["blueprintId"] = "bp-001",
                ["actionId"] = "3",
                ["instanceId"] = "inst-abc",
                ["previousTxId"] = "prev-tx-hash"
            }
        };
    }

    private static WalletSignResult CreateTestSignResult()
    {
        return new WalletSignResult
        {
            PublicKey = TestPublicKey,
            Signature = TestSignature,
            SignedBy = "wallet-sender-addr",
            Algorithm = "ED25519"
        };
    }

    [Fact]
    public void ToTransactionSubmission_MapsTransactionId()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.TransactionId.Should().Be(tx.TxId);
    }

    [Fact]
    public void ToTransactionSubmission_MapsRegisterId()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.RegisterId.Should().Be("register-42");
    }

    [Fact]
    public void ToTransactionSubmission_MapsBlueprintIdFromMetadata()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.BlueprintId.Should().Be("bp-001");
    }

    [Fact]
    public void ToTransactionSubmission_MapsActionIdFromMetadata()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.ActionId.Should().Be("3");
    }

    [Fact]
    public void ToTransactionSubmission_MapsPayloadAsJsonElement()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        result.Payload.GetProperty("type").GetString().Should().Be("action");
        result.Payload.GetProperty("blueprintId").GetString().Should().Be("bp-001");
    }

    [Fact]
    public void ToTransactionSubmission_ComputesPayloadHashFromSerializedPayload()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        // PayloadHash should be SHA-256 of the compact JSON serialization of the Payload
        var expectedJson = JsonSerializer.Serialize(result.Payload, new JsonSerializerOptions { WriteIndented = false });
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedJson);
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expectedBytes)).ToLowerInvariant();

        result.PayloadHash.Should().Be(expectedHash);
        result.PayloadHash.Should().HaveLength(64); // SHA-256 hex string
    }

    [Fact]
    public void ToTransactionSubmission_MapsSignatureInfoFromSignResult()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.Signatures.Should().HaveCount(1);
        var sig = result.Signatures[0];
        sig.PublicKey.Should().Be(Base64Url.EncodeToString(TestPublicKey));
        sig.SignatureValue.Should().Be(Base64Url.EncodeToString(TestSignature));
        sig.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public void ToTransactionSubmission_SetsCreatedAtToRecentTime()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();
        var before = DateTimeOffset.UtcNow;

        var result = tx.ToTransactionSubmission(signResult);

        result.CreatedAt.Should().BeOnOrAfter(before);
        result.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ToTransactionSubmission_MapsMetadataWithInstanceIdAndType()
    {
        var tx = CreateTestTransaction();
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.Metadata.Should().NotBeNull();
        result.Metadata!["instanceId"].Should().Be("inst-abc");
        result.Metadata["Type"].Should().Be("Action");
    }

    [Fact]
    public void ToTransactionSubmission_WithMissingMetadataKeys_UsesEmptyString()
    {
        var emptyData = JsonSerializer.SerializeToUtf8Bytes(new { });
        var tx = new BuiltTransaction
        {
            TransactionData = emptyData,
            TxId = "0000000000000000000000000000000000000000000000000000000000000000",
            PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(emptyData)).ToLowerInvariant(),
            RegisterId = "register-1",
            Metadata = new Dictionary<string, object>() // Empty metadata
        };
        var signResult = CreateTestSignResult();

        var result = tx.ToTransactionSubmission(signResult);

        result.BlueprintId.Should().BeEmpty();
        result.ActionId.Should().BeEmpty();
        result.Metadata!["instanceId"].Should().BeEmpty();
    }
}
