using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Events;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Events.Publishers;
using Sorcha.Wallet.Core.Repositories.Implementation;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

public class WalletManagerTests : IDisposable
{
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<IWalletUtilities> _mockWalletUtilities;
    private readonly InMemoryWalletRepository _repository;
    private readonly LocalEncryptionProvider _encryptionProvider;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly WalletManager _walletManager;

    public WalletManagerTests()
    {
        // Setup mocks
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockWalletUtilities = new Mock<IWalletUtilities>();

        // Setup real implementations for in-memory testing
        _repository = new InMemoryWalletRepository();
        _encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        _eventPublisher = new InMemoryEventPublisher(Mock.Of<ILogger<InMemoryEventPublisher>>());

        // Create service instances
        var keyManagement = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)_encryptionProvider,
            _mockCryptoModule.Object,
            _mockWalletUtilities.Object,
            Mock.Of<ILogger<KeyManagementService>>());

        var transactionService = new TransactionService(
            _mockCryptoModule.Object,
            _mockHashProvider.Object,
            Mock.Of<ILogger<TransactionService>>());

        var delegationService = new DelegationService(
            _repository,
            Mock.Of<ILogger<DelegationService>>());

        _walletManager = new WalletManager(
            keyManagement,
            transactionService,
            delegationService,
            _repository,
            _eventPublisher,
            Mock.Of<ILogger<WalletManager>>(),
            Mock.Of<IRecoveryKeyService>());

        // Setup default crypto module behavior
        SetupDefaultCryptoModule();
    }

    private void SetupDefaultCryptoModule()
    {
        // Setup crypto module to return deterministic keys based on seed
        // This ensures same seed = same keys (important for wallet recovery)
        _mockCryptoModule
            .Setup(x => x.GenerateKeySetAsync(
                It.IsAny<WalletNetworks>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletNetworks network, byte[] seed, CancellationToken ct) =>
            {
                // Generate deterministic keys from seed
                // Use seed hash to create consistent but unique keys
                var privateKey = new byte[32];
                var publicKey = new byte[32];

                // Create deterministic keys from seed
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(seed);
                Array.Copy(hash, privateKey, 32);

                // Public key is derived from private key hash
                var pubHash = sha256.ComputeHash(privateKey);
                Array.Copy(pubHash, publicKey, 32);

                var keySet = new KeySet
                {
                    PrivateKey = new CryptoKey(network, privateKey),
                    PublicKey = new CryptoKey(network, publicKey)
                };

                return CryptoResult<KeySet>.Success(keySet);
            });

        _mockWalletUtilities
            .Setup(x => x.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns((byte[] pk, byte network) => $"ws1{Convert.ToBase64String(pk)[..20]}");
    }

    [Fact]
    public async Task CreateWalletAsync_ShouldCreateWallet_WithValidParameters()
    {
        // Arrange
        var name = "Test Wallet";
        var algorithm = "ED25519";
        var owner = "user123";
        var tenant = "tenant1";

        // Act
        var (wallet, mnemonic) = await _walletManager.CreateWalletAsync(
            name, algorithm, owner, tenant);

        // Assert
        wallet.Should().NotBeNull();
        wallet.Name.Should().Be(name);
        wallet.Algorithm.Should().Be(algorithm);
        wallet.Owner.Should().Be(owner);
        wallet.Tenant.Should().Be(tenant);
        wallet.Status.Should().Be(WalletStatus.Active);
        wallet.Address.Should().NotBeNullOrEmpty();
        wallet.PublicKey.Should().NotBeNullOrEmpty();
        wallet.EncryptedPrivateKey.Should().NotBeNullOrEmpty();

        mnemonic.Should().NotBeNull();
        mnemonic.WordCount.Should().Be(12);

        // Verify wallet was saved
        var savedWallet = await _repository.GetByAddressAsync(wallet.Address);
        savedWallet.Should().NotBeNull();

        // Verify event was published
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().ContainSingle(e => e is WalletCreatedEvent);

        var createdEvent = events.OfType<WalletCreatedEvent>().First();
        createdEvent.WalletAddress.Should().Be(wallet.Address);
        createdEvent.Owner.Should().Be(owner);
        createdEvent.Algorithm.Should().Be(algorithm);
    }

    [Fact]
    public async Task CreateWalletAsync_ShouldGenerate24WordMnemonic_WhenRequested()
    {
        // Arrange
        var wordCount = 24;

        // Act
        var (wallet, mnemonic) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1", wordCount);

        // Assert
        mnemonic.WordCount.Should().Be(24);
    }

    [Fact]
    public async Task CreateWalletAsync_ShouldEncryptPrivateKey()
    {
        // Arrange & Act
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1");

        // Assert
        wallet.EncryptedPrivateKey.Should().NotBeNullOrEmpty();
        wallet.EncryptionKeyId.Should().NotBeNullOrEmpty();

        // Verify the encrypted key can be decrypted
        var decrypted = await _encryptionProvider.DecryptAsync(
            wallet.EncryptedPrivateKey,
            wallet.EncryptionKeyId);

        decrypted.Should().NotBeNull();
        decrypted.Length.Should().Be(32);
    }

    [Fact]
    public async Task RecoverWalletAsync_ShouldRecoverWallet_FromMnemonic()
    {
        // Arrange
        var originalMnemonic = Mnemonic.Generate(12);
        var name = "Recovered Wallet";
        var algorithm = "ED25519";
        var owner = "user123";
        var tenant = "tenant1";

        // Act
        var wallet = await _walletManager.RecoverWalletAsync(
            originalMnemonic, name, algorithm, owner, tenant);

        // Assert
        wallet.Should().NotBeNull();
        wallet.Name.Should().Be(name);
        wallet.Algorithm.Should().Be(algorithm);
        wallet.Owner.Should().Be(owner);
        wallet.Metadata["Recovered"].Should().Be("true");

        // Verify event
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().ContainSingle(e => e is WalletRecoveredEvent);
    }

    [Fact]
    public async Task RecoverWalletAsync_ShouldPopulateEncryptedMasterKeyBlob_WithBip39Seed()
    {
        // Issue #471: RecoverWalletAsync must populate EncryptedMasterKeyBlob
        // with the BIP39 PBKDF2 seed (64 bytes) so SignTransactionAsync can
        // derive purpose keys directly via ExtKey.CreateFromSeed(seed) rather
        // than the legacy double-HMAC chain off the BIP44 0/0/0/0 leaf.
        var mnemonic = Mnemonic.Generate(12);

        var wallet = await _walletManager.RecoverWalletAsync(
            mnemonic, "Test", "ED25519", "user1", "tenant1");

        wallet.EncryptedMasterKeyBlob.Should().NotBeNullOrEmpty(
            "the master seed blob is the entry point for direct-master derivation");
        wallet.RecoveryEnabled.Should().BeFalse(
            "the recovered system wallet blob is wallet-service-managed, not a Feature 060 recovery-key blob");

        // The blob round-trips through the wallet's own EncryptionKeyId, so
        // wallet-service can decrypt it at sign time without needing a
        // separately-issued recovery key.
        var keyManagement = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)_encryptionProvider,
            _mockCryptoModule.Object,
            _mockWalletUtilities.Object,
            Mock.Of<ILogger<KeyManagementService>>());
        var decrypted = await keyManagement.DecryptPrivateKeyAsync(
            wallet.EncryptedMasterKeyBlob!, wallet.EncryptionKeyId);
        decrypted.Length.Should().Be(64,
            "BIP39 PBKDF2 derives a 64-byte seed — this length is what differentiates the new blob from the legacy 32-byte master priv");

        // The decrypted seed must equal the same NBitcoin BIP39 seed the
        // mnemonic produces — i.e. the canonical input to ExtKey.CreateFromSeed.
        var expectedSeed = new NBitcoin.Mnemonic(mnemonic.Phrase).DeriveSeed(string.Empty);
        decrypted.Should().Equal(expectedSeed,
            "the blob carries the canonical BIP39 PBKDF2 seed, not the post-HMAC 32-byte master priv");
    }

    [Fact]
    public async Task RecoverWalletAsync_ShouldThrow_WhenWalletAlreadyExists()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);

        // Create wallet first time
        await _walletManager.RecoverWalletAsync(
            mnemonic, "Test", "ED25519", "user1", "tenant1");

        // Act & Assert - Try to recover again
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _walletManager.RecoverWalletAsync(
                mnemonic, "Test2", "ED25519", "user1", "tenant1"));
    }

    [Fact]
    public async Task GetWalletAsync_ShouldReturnWallet_WhenExists()
    {
        // Arrange
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1");

        // Act
        var retrieved = await _walletManager.GetWalletAsync(wallet.Address);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Address.Should().Be(wallet.Address);
        retrieved.Name.Should().Be("Test");
    }

    [Fact]
    public async Task GetWalletAsync_ShouldReturnNull_WhenNotExists()
    {
        // Act
        var wallet = await _walletManager.GetWalletAsync("nonexistent");

        // Assert
        wallet.Should().BeNull();
    }

    [Fact]
    public async Task GetWalletsByOwnerAsync_ShouldReturnOwnersWallets()
    {
        // Arrange
        var owner = "user123";
        var tenant = "tenant1";

        await _walletManager.CreateWalletAsync("Wallet1", "ED25519", owner, tenant);
        await _walletManager.CreateWalletAsync("Wallet2", "NISTP256", owner, tenant);
        await _walletManager.CreateWalletAsync("Wallet3", "ED25519", "other-user", tenant);

        // Act
        var wallets = await _walletManager.GetWalletsByOwnerAsync(owner, tenant);

        // Assert
        wallets.Should().HaveCount(2);
        wallets.Should().AllSatisfy(w => w.Owner.Should().Be(owner));
    }

    [Fact]
    public async Task UpdateWalletAsync_ShouldUpdateName()
    {
        // Arrange
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Original", "ED25519", "user1", "tenant1");

        // Act
        var updated = await _walletManager.UpdateWalletAsync(
            wallet.Address,
            name: "Updated Name");

        // Assert
        updated.Name.Should().Be("Updated Name");

        var retrieved = await _repository.GetByAddressAsync(wallet.Address);
        retrieved!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateWalletAsync_ShouldUpdateMetadata()
    {
        // Arrange
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1");

        var tags = new Dictionary<string, string>
        {
            ["environment"] = "production",
            ["version"] = "1.0"
        };

        // Act
        var updated = await _walletManager.UpdateWalletAsync(
            wallet.Address,
            tags: tags);

        // Assert
        updated.Metadata.Should().Contain("environment", "production");
        updated.Metadata.Should().Contain("version", "1.0");
    }

    [Fact]
    public async Task DeleteWalletAsync_ShouldMarkAsDeleted()
    {
        // Arrange
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1");

        // Act
        await _walletManager.DeleteWalletAsync(wallet.Address);

        // Assert
        var retrieved = await _repository.GetByAddressAsync(wallet.Address);
        retrieved!.Status.Should().Be(WalletStatus.Deleted);

        // Verify event
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().Contain(e => e is WalletStatusChangedEvent);
    }

    [Fact]
    public async Task SignTransactionAsync_ShouldSignTransaction()
    {
        // Arrange
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test", "ED25519", "user1", "tenant1");

        var transactionData = new byte[100];
        Random.Shared.NextBytes(transactionData);

        var expectedSignature = new byte[64];
        Random.Shared.NextBytes(expectedSignature);

        _mockCryptoModule
            .Setup(x => x.SignAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoResult<byte[]>.Success(expectedSignature));

        _mockHashProvider
            .Setup(x => x.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns(new byte[32]);

        // Act
        var signature = await _walletManager.SignTransactionAsync(
            wallet.Address,
            transactionData);

        // Assert
        signature.Signature.Should().Equal(expectedSignature);
        signature.PublicKey.Should().NotBeEmpty();

        // Verify event
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().Contain(e => e is TransactionSignedEvent);
    }

    [Fact]
    public async Task SignTransactionAsync_ShouldThrow_WhenWalletNotFound()
    {
        // Arrange
        var transactionData = new byte[100];

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _walletManager.SignTransactionAsync("nonexistent", transactionData));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateWalletAsync_ShouldThrow_WhenAlgorithmInvalid(string? algorithm)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _walletManager.CreateWalletAsync(
                "Test", algorithm!, "user1", "tenant1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateWalletAsync_ShouldThrow_WhenOwnerInvalid(string? owner)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _walletManager.CreateWalletAsync(
                "Test", "ED25519", owner!, "tenant1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateWalletAsync_ShouldThrow_WhenTenantInvalid(string? tenant)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _walletManager.CreateWalletAsync(
                "Test", "ED25519", "user1", tenant!));
    }

    [Fact]
    public async Task CreateWalletAsync_ShouldSupportDifferentAlgorithms()
    {
        // Arrange
        var algorithms = new[] { "ED25519", "NISTP256", "RSA4096" };

        // Act & Assert
        foreach (var algorithm in algorithms)
        {
            var (wallet, _) = await _walletManager.CreateWalletAsync(
                $"Wallet-{algorithm}", algorithm, "user1", "tenant1");

            wallet.Algorithm.Should().Be(algorithm);
        }
    }

    public void Dispose()
    {
        _repository.Clear();
        _eventPublisher.ClearEvents();
    }
}
