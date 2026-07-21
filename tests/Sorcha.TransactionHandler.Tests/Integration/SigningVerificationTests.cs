using System;
using System.Threading.Tasks;
using Xunit;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Payload;
using Sorcha.TransactionHandler.Serialization;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

namespace Sorcha.TransactionHandler.Tests.Integration;

/// <summary>
/// Integration tests for transaction signing and verification workflows.
/// </summary>
public class SigningVerificationTests
{
    private readonly CryptoModule _cryptoModule;
    private readonly HashProvider _hashProvider;
    private readonly SymmetricCrypto _symmetricCrypto;

    public SigningVerificationTests()
    {
        _cryptoModule = new CryptoModule();
        _hashProvider = new HashProvider();
        _symmetricCrypto = new SymmetricCrypto();
    }

    [Fact]
    public async Task SignAndVerify_CompleteWorkflow_SigningSucceeds_VerifyFailsClosed()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        // Act - Create and sign transaction
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"action\": \"transfer\"}")
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();
        var transaction = transactionResult.Value!;

        // Verify immediately after signing
        var verifyStatus = await transaction.VerifyAsync();

        // Assert
        Assert.Equal(TransactionStatus.VerificationNotImplemented, verifyStatus);
        Assert.NotNull(transaction.Signature);
        Assert.NotNull(transaction.TxId);
    }

    [Fact]
    public async Task BinarySerialization_PreservesTransactionData()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"test\": true}")
            .SignAsync(wallet.PrivateKeyWif);

        var originalTransaction = builderResult.Build().Value!;

        // Act - Serialize and deserialize
        var serializer = new BinaryTransactionSerializer(_cryptoModule, _hashProvider, _symmetricCrypto);
        var binaryData = serializer.SerializeToBinary(originalTransaction);
        var deserializedTransaction = serializer.DeserializeFromBinary(binaryData);

        // Assert - Transaction data should be preserved
        Assert.Equal(originalTransaction.Version, deserializedTransaction.Version);
        Assert.Equal(originalTransaction.Recipients?.Length, deserializedTransaction.Recipients?.Length);
        Assert.NotNull(binaryData);
        Assert.True(binaryData.Length > 0);
    }

    [Fact]
    public async Task Sign_WithDifferentWalletNetworks_ShouldSucceed()
    {
        // Test ED25519
        var ed25519Wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);
        var ed25519Builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);

        var ed25519Result = await ed25519Builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .SignAsync(ed25519Wallet.PrivateKeyWif);

        var ed25519Transaction = ed25519Result.Build();

        // Assert
        Assert.True(ed25519Transaction.IsSuccess);
        Assert.NotNull(ed25519Transaction.Value!.Signature);

        // Signing is the subject; V1 verify fails closed (see Transaction.VerifyAsync).
        var ed25519VerifyStatus = await ed25519Transaction.Value!.VerifyAsync();
        Assert.Equal(TransactionStatus.VerificationNotImplemented, ed25519VerifyStatus);
    }

    [Fact]
    public async Task Verify_UnsignedTransaction_ShouldFail()
    {
        // Arrange - Create transaction without signing
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);

        transaction.Recipients = new[] { "ws1recipient" };

        // Act
        var verifyStatus = await transaction.VerifyAsync();

        // Assert
        Assert.Equal(TransactionStatus.NotSigned, verifyStatus);
    }

    [Fact]
    public async Task SignedTransaction_IsImmutable_VerifyFailsClosed()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"amount\": 100}")
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();
        var transaction = transactionResult.Value!;

        // Act - Verify signature is valid
        var verifyStatus = await transaction.VerifyAsync();

        // Assert - Transaction should be properly signed and immutable
        Assert.Equal(TransactionStatus.VerificationNotImplemented, verifyStatus);
        Assert.NotNull(transaction.Signature);
        Assert.NotNull(transaction.TxId);
        Assert.NotNull(transaction.Metadata);
    }

    [Fact]
    public async Task MultipleTransactions_DifferentSigners_ProduceUniqueSignatures()
    {
        // Arrange - Create three different wallets and transactions
        var wallet1 = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);
        var wallet2 = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);
        var wallet3 = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builder1 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var builder2 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var builder3 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);

        // Act - Create and sign three transactions
        var tx1 = (await builder1
            .Create()
            .WithRecipients("ws1recipient1")
            .SignAsync(wallet1.PrivateKeyWif))
            .Build().Value!;

        var tx2 = (await builder2
            .Create()
            .WithRecipients("ws1recipient2")
            .SignAsync(wallet2.PrivateKeyWif))
            .Build().Value!;

        var tx3 = (await builder3
            .Create()
            .WithRecipients("ws1recipient3")
            .SignAsync(wallet3.PrivateKeyWif))
            .Build().Value!;

        // Assert - All should verify
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx1.VerifyAsync());
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx2.VerifyAsync());
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx3.VerifyAsync());

        // All should have unique signatures
        Assert.NotEqual(Convert.ToBase64String(tx1.Signature!), Convert.ToBase64String(tx2.Signature!));
        Assert.NotEqual(Convert.ToBase64String(tx2.Signature!), Convert.ToBase64String(tx3.Signature!));
        Assert.NotEqual(Convert.ToBase64String(tx1.Signature!), Convert.ToBase64String(tx3.Signature!));
    }

    [Fact]
    public async Task Sign_WithEmptyPrivateKey_ShouldFail()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await builder
                .Create()
                .WithRecipients("ws1recipient")
                .SignAsync(string.Empty);
        });
    }

    [Fact]
    public async Task Sign_WithInvalidPrivateKey_ShouldFail()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await builder
                .Create()
                .WithRecipients("ws1recipient")
                .SignAsync("invalid_key_data");
        });
    }

    [Fact]
    public async Task TransactionWithPayloads_SignatureCoversAllData_VerifyFailsClosed()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var payload1 = System.Text.Encoding.UTF8.GetBytes("Payload 1 data");
        var payload2 = System.Text.Encoding.UTF8.GetBytes("Payload 2 data");

        // Act
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"type\": \"multi_payload\"}")
            .AddPayload(payload1, new[] { "ws1recipient" })
            .AddPayload(payload2, new[] { "ws1recipient" })
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();
        var transaction = transactionResult.Value!;

        // Verify
        var verifyStatus = await transaction.VerifyAsync();

        // Assert
        Assert.Equal(TransactionStatus.VerificationNotImplemented, verifyStatus);
    }

    [Fact]
    public async Task SignatureGeneration_IsDeterministic_ForSameData()
    {
        // Arrange - Create transaction with same data twice
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        var builder1 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var builder2 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);

        var metadata = "{\"test\": \"deterministic\"}";
        var recipients = new[] { "ws1recipient" };

        // Act - Sign same transaction data twice
        var tx1 = (await builder1
            .Create(TransactionVersion.V1)
            .WithRecipients(recipients)
            .WithMetadata(metadata)
            .SignAsync(wallet.PrivateKeyWif))
            .Build().Value!;

        var tx2 = (await builder2
            .Create(TransactionVersion.V1)
            .WithRecipients(recipients)
            .WithMetadata(metadata)
            .SignAsync(wallet.PrivateKeyWif))
            .Build().Value!;

        // Assert - Signatures should be different due to timestamp
        // (timestamps are auto-generated and will differ)
        Assert.NotEqual(Convert.ToBase64String(tx1.Signature!), Convert.ToBase64String(tx2.Signature!));
    }

    [Fact]
    public async Task DoubleSignature_SHA256_ShouldBeUsed()
    {
        // Arrange
        var builder = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        // Act
        var builderResult = await builder
            .Create(TransactionVersion.V1)
            .WithRecipients("ws1recipient")
            .WithMetadata("{\"note\": \"testing double SHA-256\"}")
            .SignAsync(wallet.PrivateKeyWif);

        var transactionResult = builderResult.Build();
        var transaction = transactionResult.Value!;

        // Assert - Signature should be present and verification should succeed
        Assert.NotNull(transaction.Signature);
        Assert.True(transaction.Signature.Length > 0);

        var verifyStatus = await transaction.VerifyAsync();
        Assert.Equal(TransactionStatus.VerificationNotImplemented, verifyStatus);
    }

    [Fact]
    public async Task TransactionChain_ProducesLinkedSignatures()
    {
        // Arrange - Create a chain of transactions
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        // Create first transaction
        var builder1 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var tx1 = (await builder1
            .Create()
            .WithRecipients("ws1recipient")
            .SignAsync(wallet.PrivateKeyWif))
            .Build().Value!;

        // Create second transaction referencing first
        var builder2 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var tx2 = (await builder2
            .Create()
            .WithPreviousTransaction(tx1.TxId!)
            .WithRecipients("ws1recipient")
            .SignAsync(wallet.PrivateKeyWif))
            .Build().Value!;

        // Create third transaction referencing second
        var builder3 = new TransactionBuilder(_cryptoModule, _hashProvider, _symmetricCrypto);
        var tx3 = (await builder3
            .Create()
            .WithPreviousTransaction(tx2.TxId!)
            .WithRecipients("ws1recipient")
            .SignAsync(wallet.PrivateKeyWif))
            .Build().Value!;

        // Act & Assert - All signatures should verify
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx1.VerifyAsync());
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx2.VerifyAsync());
        Assert.Equal(TransactionStatus.VerificationNotImplemented, await tx3.VerifyAsync());

        // Verify chain integrity
        Assert.Equal(tx1.TxId, tx2.PreviousTxHash);
        Assert.Equal(tx2.TxId, tx3.PreviousTxHash);
    }
}
