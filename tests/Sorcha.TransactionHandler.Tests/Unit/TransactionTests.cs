using System;
using System.Threading.Tasks;
using Xunit;
using Sorcha.TransactionHandler.Core;
using Sorcha.TransactionHandler.Enums;
using Sorcha.TransactionHandler.Payload;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;

namespace Sorcha.TransactionHandler.Tests.Unit;

public class TransactionTests
{
    private readonly CryptoModule _cryptoModule;
    private readonly HashProvider _hashProvider;
    private readonly SymmetricCrypto _symmetricCrypto;

    public TransactionTests()
    {
        _cryptoModule = new CryptoModule();
        _hashProvider = new HashProvider();
        _symmetricCrypto = new SymmetricCrypto();
    }

    [Fact]
    public void Constructor_ShouldCreateTransaction()
    {
        // Arrange & Act
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(
            _cryptoModule,
            _hashProvider,
            payloadManager,
            TransactionVersion.V1);

        // Assert
        Assert.NotNull(transaction);
        Assert.Equal(TransactionVersion.V1, transaction.Version);
        Assert.NotNull(transaction.Timestamp);
    }

    [Fact]
    public void Recipients_ShouldSetAndGet()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);
        var recipients = new[] { "ws1qyqszqgp123", "ws1pqpszqgp456" };

        // Act
        transaction.Recipients = recipients;

        // Assert
        Assert.Equal(recipients, transaction.Recipients);
    }

    [Fact]
    public void Metadata_ShouldSetAndGet()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);
        var metadata = "{\"type\": \"test\"}";

        // Act
        transaction.Metadata = metadata;

        // Assert
        Assert.Equal(metadata, transaction.Metadata);
    }

    [Fact]
    public void PreviousTxHash_ShouldSetAndGet()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);
        var hash = "prev_hash_123";

        // Act
        transaction.PreviousTxHash = hash;

        // Assert
        Assert.Equal(hash, transaction.PreviousTxHash);
    }

    [Fact]
    public async Task SignAsync_ShouldSetSignatureAndSenderWallet()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        transaction.Recipients = new[] { "ws1qyqszqgp123" };

        // Act
        var status = await transaction.SignAsync(wallet.PrivateKeyWif);

        // Assert
        Assert.Equal(TransactionStatus.Success, status);
        Assert.NotNull(transaction.Signature);
        Assert.NotNull(transaction.SenderWallet);
        Assert.NotNull(transaction.TxId);
    }

    [Fact]
    public async Task SignAsync_ShouldFailWithEmptyKey()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);

        // Act
        var status = await transaction.SignAsync(string.Empty);

        // Assert
        Assert.Equal(TransactionStatus.InvalidSignature, status);
    }

    [Fact]
    public async Task VerifyAsync_SignedTransaction_FailsClosedAsNotImplemented()
    {
        // V1 in-model verify does not cryptographically check the signature — there is no
        // recoverable public key on the transaction (see Transaction.VerifyAsync remarks). It must
        // fail closed rather than return Success for a signature it never checked. This used to
        // assert Success, which was the no-op affirming itself.
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);
        var wallet = await TestHelpers.GenerateTestWalletAsync(WalletNetworks.ED25519);

        transaction.Recipients = new[] { wallet.Address };

        // Sign the transaction (the signing scaffolding does produce a signature).
        await transaction.SignAsync(wallet.PrivateKeyWif);

        // Act
        var status = await transaction.VerifyAsync();

        // Assert
        Assert.Equal(TransactionStatus.VerificationNotImplemented, status);
        Assert.NotEqual(TransactionStatus.Success, status);
    }

    [Fact]
    public async Task VerifyAsync_ShouldFailForUnsignedTransaction()
    {
        // Arrange
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(_cryptoModule, _hashProvider, payloadManager);

        // Act
        var status = await transaction.VerifyAsync();

        // Assert
        Assert.Equal(TransactionStatus.NotSigned, status);
    }

    [Fact]
    public void Constructor_ShouldDefaultToV1()
    {
        // Arrange & Act
        var payloadManager = new PayloadManager(_symmetricCrypto, _cryptoModule, _hashProvider);
        var transaction = new Transaction(
            _cryptoModule,
            _hashProvider,
            payloadManager);

        // Assert
        Assert.Equal(TransactionVersion.V1, transaction.Version);
    }
}
