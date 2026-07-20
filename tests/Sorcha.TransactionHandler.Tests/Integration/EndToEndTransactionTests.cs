using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Models;
using Sorcha.TransactionHandler.Serialization;
using Sorcha.TransactionHandler.Versioning;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

namespace Sorcha.TransactionHandler.Tests.Integration;

/// <summary>
/// End-to-end integration tests for complete transaction workflows.
/// </summary>
public class EndToEndTransactionTests
{
    private readonly CryptoModule _cryptoModule;
    private readonly HashProvider _hashProvider;
    private readonly SymmetricCrypto _symmetricCrypto;

    public EndToEndTransactionTests()
    {
        _cryptoModule = new CryptoModule();
        _hashProvider = new HashProvider();
        _symmetricCrypto = new SymmetricCrypto();
    }

    [Fact]
    public async Task CompleteTransactionWorkflow_ShouldSucceed()
    {
        // Arrange - Create a transaction using the builder
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);
        var recipient = "ws1qyqszqgp123recipient";

        var payloadData = System.Text.Encoding.UTF8.GetBytes("Test payload content");

        // Act - Build and sign transaction
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients(recipient)
            .WithMetadata("{\"type\": \"test_transaction\", \"amount\": 100}")
            .AddPayload(payloadData, new[] { recipient })
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();

        // Assert - Transaction created successfully
        Assert.True(transactionResult.IsSuccess);
        Assert.NotNull(transactionResult.Value);

        var transaction = transactionResult.Value;
        Assert.NotNull(transaction.TxId);
        Assert.NotNull(transaction.Signature);
        Assert.NotNull(transaction.SenderWallet);
        Assert.Equal(TransactionVersion.V1, transaction.Version);

        // V1 in-model verify fails closed — it cannot check the signature (no recoverable public
        // key; see Transaction.VerifyAsync). The signing assertions above are the real subject here.
        var verifyStatus = await transaction.VerifyAsync();
        Assert.Equal(TransactionStatus.VerificationNotImplemented, verifyStatus);
    }

    [Fact]
    public async Task TransactionSerialization_RoundTrip_ShouldPreserveData()
    {
        // Arrange - Create and sign a transaction
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient1", "ws1recipient2")
            .WithMetadata("{\"test\": true}")
            .WithPreviousTransaction("previous_tx_hash_123")
            .SignAsync(wallet.PrivateKeyWif);

        var originalTransaction = builderResult.Build().Value!;

        // Act - Serialize to binary and deserialize
        var binarySerializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        var binaryData = binarySerializer.SerializeToBinary(originalTransaction);
        var deserializedTransaction = binarySerializer.DeserializeFromBinary(binaryData);

        // Assert - Data preserved
        Assert.Equal(originalTransaction.Version, deserializedTransaction.Version);
        Assert.Equal(originalTransaction.Recipients?.Length, deserializedTransaction.Recipients?.Length);
        Assert.Equal(originalTransaction.PreviousTxHash, deserializedTransaction.PreviousTxHash);
    }

    [Fact]
    public async Task TransactionWithMultiplePayloads_ShouldSucceed()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);
        var recipients = new[] { "ws1recipient1", "ws1recipient2" };

        var payload1 = System.Text.Encoding.UTF8.GetBytes("First payload");
        var payload2 = System.Text.Encoding.UTF8.GetBytes("Second payload");
        var payload3 = System.Text.Encoding.UTF8.GetBytes("Third payload");

        // Act
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients(recipients)
            .AddPayload(payload1, recipients)
            .AddPayload(payload2, recipients)
            .AddPayload(payload3, recipients)
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();

        // Assert
        Assert.True(transactionResult.IsSuccess);
        var transaction = transactionResult.Value!;

        var payloads = await transaction.PayloadManager.GetAllAsync();
        Assert.Equal(3, payloads.Count());
    }

    [Fact]
    public async Task TransactionChaining_WithPreviousHash_ShouldSucceed()
    {
        // Arrange - Create first transaction
        var builder1 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet1 = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var tx1Result = await builder1
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .SignAsync(wallet1.PrivateKeyWif);

        var transaction1 = tx1Result.Build().Value!;

        // Act - Create second transaction referencing the first
        var builder2 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet2 = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var tx2Result = await builder2
            .Create(TransactionVersion.V1)
            .WithPreviousTransaction(transaction1.TxId!)
            .WithRecipients("ws1recipient2")
            .SignAsync(wallet2.PrivateKeyWif);

        var transaction2 = tx2Result.Build().Value!;

        // Assert
        Assert.Equal(transaction1.TxId, transaction2.PreviousTxHash);
        Assert.NotEqual(transaction1.TxId, transaction2.TxId);
    }

    [Fact]
    public async Task JsonSerialization_RoundTrip_ShouldPreserveStructure()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"type\": \"document\", \"size\": 1024}")
            .SignAsync(wallet.PrivateKeyWif);

        var originalTransaction = builderResult.Build().Value!;

        // Act
        var jsonSerializer = new JsonTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        var json = jsonSerializer.SerializeToJson(originalTransaction);

        // Assert - JSON contains expected fields
        Assert.Contains("\"txId\"", json);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"signature\"", json);
        Assert.Contains("\"metadata\"", json);

        // Deserialize and verify
        var deserializedTransaction = jsonSerializer.DeserializeFromJson(json);
        Assert.Equal(originalTransaction.Version, deserializedTransaction.Version);
    }

    [Fact]
    public async Task TransportPacket_Creation_ShouldIncludeAllData()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .SignAsync(wallet.PrivateKeyWif);

        var transaction = builderResult.Build().Value!;

        // Act
        var serializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        var packet = serializer.CreateTransportPacket(transaction);

        // Assert
        Assert.NotNull(packet);
        Assert.Equal(transaction.TxId, packet.TxId);
        Assert.NotNull(packet.Data);
        Assert.True(packet.Data.Length > 0);
    }

    [Fact]
    public async Task VersionFactory_CreateAndDeserialize_ShouldWork()
    {
        // Arrange
        var versionDetector = new VersionDetector();
        var factory = new TransactionFactory(_cryptoModule, _hashProvider, _symmetricCrypto, versionDetector);

        // Create a V4 transaction
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .SignAsync(wallet.PrivateKeyWif);

        var originalTransaction = builderResult.Build().Value!;

        // Serialize
        var serializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        var binaryData = serializer.SerializeToBinary(originalTransaction);

        // Act - Use factory to deserialize with auto-detection
        var deserializedTransaction = factory.Deserialize(binaryData);

        // Assert
        Assert.NotNull(deserializedTransaction);
        Assert.Equal(TransactionVersion.V1, deserializedTransaction.Version);
    }

    [Fact]
    public async Task MetadataWithComplexObject_ShouldSerializeCorrectly()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var metadata = new
        {
            Type = "contract_execution",
            ContractId = "contract_123",
            Parameters = new
            {
                Amount = 1000,
                Currency = "SOR",
                Timestamp = DateTime.UtcNow
            },
            Tags = new[] { "urgent", "verified" }
        };

        // Act
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata(metadata)
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();

        // Assert
        Assert.True(transactionResult.IsSuccess);
        var transaction = transactionResult.Value!;
        Assert.NotNull(transaction.Metadata);
        Assert.Contains("contract_execution", transaction.Metadata);
        Assert.Contains("contract_123", transaction.Metadata);
    }

    [Fact]
    public async Task LargePayload_ShouldHandleCorrectly()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        // Create a 1MB payload
        var largePayload = new byte[1024 * 1024];
        new Random().NextBytes(largePayload);

        // Act
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .AddPayload(largePayload, new[] { "ws1recipient" })
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();

        // Assert
        Assert.True(transactionResult.IsSuccess);
        var transaction = transactionResult.Value!;

        var payloads = await transaction.PayloadManager.GetAllAsync();
        var payload = payloads.First();
        Assert.True(payload.Data.Length > 0);
    }
}
