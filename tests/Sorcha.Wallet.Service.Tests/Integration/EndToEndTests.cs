namespace Sorcha.Wallet.Service.Tests.Integration;

/// <summary>
/// End-to-end integration tests demonstrating full wallet workflows
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<IWalletUtilities> _mockWalletUtilities;
    private readonly InMemoryWalletRepository _repository;
    private readonly LocalEncryptionProvider _encryptionProvider;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly WalletManager _walletManager;

    public EndToEndTests()
    {
        // Setup mocks
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockWalletUtilities = new Mock<IWalletUtilities>();

        // Setup real implementations for in-memory testing
        _repository = new InMemoryWalletRepository();
        _encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        _eventPublisher = new InMemoryEventPublisher(Mock.Of<ILogger<InMemoryEventPublisher>>());

        // Setup crypto module behavior
        SetupCryptoModule();

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
    }

    private void SetupCryptoModule()
    {
        // Setup deterministic key generation from seed
        _mockCryptoModule
            .Setup(x => x.GenerateKeySetAsync(
                It.IsAny<WalletNetworks>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletNetworks network, byte[] seed, CancellationToken ct) =>
            {
                var privateKey = new byte[32];
                var publicKey = new byte[32];

                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(seed);
                Array.Copy(hash, privateKey, 32);
                var pubHash = sha256.ComputeHash(privateKey);
                Array.Copy(pubHash, publicKey, 32);

                var keySet = new KeySet
                {
                    PrivateKey = new CryptoKey(network, privateKey),
                    PublicKey = new CryptoKey(network, publicKey)
                };

                return CryptoResult<KeySet>.Success(keySet);
            });

        // Setup address generation
        _mockWalletUtilities
            .Setup(x => x.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns((byte[] pk, byte network) => $"ws1{Convert.ToBase64String(pk)[..20]}");

        // Setup signing
        _mockCryptoModule
            .Setup(x => x.SignAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] hash, byte network, byte[] privateKey, CancellationToken ct) =>
            {
                var signature = new byte[64];
                Array.Copy(hash, signature, Math.Min(hash.Length, signature.Length));
                return CryptoResult<byte[]>.Success(signature);
            });

        // Setup hashing
        _mockHashProvider
            .Setup(x => x.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType hashType) =>
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                return sha256.ComputeHash(data);
            });
    }

    [Fact]
    public async Task CompleteWalletLifecycle_ShouldWork()
    {
        // STEP 1: Create wallet
        var (wallet, mnemonic) = await _walletManager.CreateWalletAsync(
            name: "My Primary Wallet",
            algorithm: "ED25519",
            owner: "alice@example.com",
            tenant: "example-tenant",
            wordCount: 12);

        // Verify wallet creation
        wallet.Should().NotBeNull();
        wallet.Status.Should().Be(WalletStatus.Active);
        wallet.Owner.Should().Be("alice@example.com");
        mnemonic.WordCount.Should().Be(12);

        // Verify event was published
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().Contain(e => e is WalletCreatedEvent);

        // STEP 2: Retrieve wallet
        var retrievedWallet = await _walletManager.GetWalletAsync(wallet.Address);
        retrievedWallet.Should().NotBeNull();
        retrievedWallet!.Address.Should().Be(wallet.Address);

        // STEP 3: Update wallet metadata
        var updatedWallet = await _walletManager.UpdateWalletAsync(
            wallet.Address,
            name: "Alice's Updated Wallet",
            tags: new Dictionary<string, string>
            {
                ["environment"] = "production",
                ["version"] = "1.0"
            });

        updatedWallet.Name.Should().Be("Alice's Updated Wallet");
        updatedWallet.Metadata.Should().Contain("environment", "production");

        // STEP 4: Sign a transaction
        var transactionData = System.Text.Encoding.UTF8.GetBytes("Transfer 100 coins to Bob");
        var signature = await _walletManager.SignTransactionAsync(
            wallet.Address,
            transactionData);

        signature.Should().NotBeNull();
        signature.Signature.Should().NotBeEmpty();
        signature.PublicKey.Should().NotBeEmpty();

        // Verify signing event
        events = _eventPublisher.GetPublishedEvents();
        events.Should().Contain(e => e is TransactionSignedEvent);

        // STEP 5: Grant access to another user
        var delegationService = new DelegationService(_repository, Mock.Of<ILogger<DelegationService>>());

        var access = await delegationService.GrantAccessAsync(
            wallet.Address,
            "bob@example.com",
            AccessRight.ReadWrite,
            "alice@example.com",
            "Grant access to Bob for co-management");

        access.Should().NotBeNull();
        access.Subject.Should().Be("bob@example.com");
        access.AccessRight.Should().Be(AccessRight.ReadWrite);

        // STEP 6: Verify Bob has access
        var bobHasAccess = await delegationService.HasAccessAsync(
            wallet.Address,
            "bob@example.com",
            AccessRight.ReadWrite);

        bobHasAccess.Should().BeTrue();

        // STEP 7: List all wallets for Alice
        var aliceWallets = await _walletManager.GetWalletsByOwnerAsync(
            "alice@example.com",
            "example-tenant");

        aliceWallets.Should().ContainSingle();
        aliceWallets.First().Address.Should().Be(wallet.Address);

        // STEP 8: Revoke Bob's access
        await delegationService.RevokeAccessAsync(
            wallet.Address,
            "bob@example.com",
            "alice@example.com");

        bobHasAccess = await delegationService.HasAccessAsync(
            wallet.Address,
            "bob@example.com",
            AccessRight.ReadWrite);

        bobHasAccess.Should().BeFalse();

        // STEP 9: Delete wallet (soft delete)
        await _walletManager.DeleteWalletAsync(wallet.Address);

        var deletedWallet = await _walletManager.GetWalletAsync(wallet.Address);
        deletedWallet!.Status.Should().Be(WalletStatus.Deleted);

        // Verify all operations succeeded
        var allEvents = _eventPublisher.GetPublishedEvents();
        allEvents.Should().Contain(e => e is WalletCreatedEvent);
        allEvents.Should().Contain(e => e is TransactionSignedEvent);
        allEvents.Should().Contain(e => e is WalletStatusChangedEvent);
    }

    [Fact]
    public async Task WalletRecoveryAndDeterministicKeys_ShouldWork()
    {
        // STEP 1: Create initial wallet
        var (originalWallet, mnemonic) = await _walletManager.CreateWalletAsync(
            "Original Wallet", "ED25519", "alice@example.com", "tenant1");

        var originalAddress = originalWallet.Address;
        var originalPublicKey = originalWallet.PublicKey;

        // STEP 2: "Lose" the wallet (simulate user losing device)
        // Delete the wallet and clear repository to fully simulate wallet loss
        await _walletManager.DeleteWalletAsync(originalAddress);
        _repository.Clear();
        _eventPublisher.ClearEvents();

        // STEP 3: Recover wallet from mnemonic
        var recoveredWallet = await _walletManager.RecoverWalletAsync(
            mnemonic,
            "Recovered Wallet",
            "ED25519",
            "alice@example.com",
            "tenant1");

        // Verify recovery produces same address (deterministic keys)
        recoveredWallet.Address.Should().Be(originalAddress);
        recoveredWallet.PublicKey.Should().Be(originalPublicKey);
        recoveredWallet.Metadata.Should().Contain("Recovered", "true");

        // Verify recovery event
        var events = _eventPublisher.GetPublishedEvents();
        events.Should().Contain(e => e is WalletRecoveredEvent);
    }

    [Fact]
    public async Task MultiTenantWallets_ShouldBeSeparated()
    {
        // Create wallets for different tenants
        var (tenant1Wallet, _) = await _walletManager.CreateWalletAsync(
            "Wallet 1", "ED25519", "alice@example.com", "tenant1");

        var (tenant2Wallet, _) = await _walletManager.CreateWalletAsync(
            "Wallet 2", "ED25519", "alice@example.com", "tenant2");

        var (anotherTenant1Wallet, _) = await _walletManager.CreateWalletAsync(
            "Wallet 3", "ED25519", "bob@example.com", "tenant1");

        // Verify tenant separation
        var tenant1Wallets = await _walletManager.GetWalletsByOwnerAsync(
            "alice@example.com", "tenant1");
        var tenant2Wallets = await _walletManager.GetWalletsByOwnerAsync(
            "alice@example.com", "tenant2");

        tenant1Wallets.Should().ContainSingle();
        tenant1Wallets.First().Address.Should().Be(tenant1Wallet.Address);

        tenant2Wallets.Should().ContainSingle();
        tenant2Wallets.First().Address.Should().Be(tenant2Wallet.Address);
    }

    [Fact]
    public async Task MultipleAlgorithms_ShouldBeSupported()
    {
        // Create wallets with different algorithms
        var (ed25519Wallet, _) = await _walletManager.CreateWalletAsync(
            "ED25519 Wallet", "ED25519", "alice@example.com", "tenant1");

        var (nistp256Wallet, _) = await _walletManager.CreateWalletAsync(
            "NISTP256 Wallet", "NISTP256", "alice@example.com", "tenant1");

        var (rsa4096Wallet, _) = await _walletManager.CreateWalletAsync(
            "RSA4096 Wallet", "RSA4096", "alice@example.com", "tenant1");

        // Verify all wallets created successfully
        ed25519Wallet.Algorithm.Should().Be("ED25519");
        nistp256Wallet.Algorithm.Should().Be("NISTP256");
        rsa4096Wallet.Algorithm.Should().Be("RSA4096");

        // All should have unique addresses
        var addresses = new[] { ed25519Wallet.Address, nistp256Wallet.Address, rsa4096Wallet.Address };
        addresses.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AccessControlHierarchy_ShouldWork()
    {
        // Create wallet
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Test Wallet", "ED25519", "alice@example.com", "tenant1");

        var delegationService = new DelegationService(_repository, Mock.Of<ILogger<DelegationService>>());

        // Grant different levels of access
        await delegationService.GrantAccessAsync(
            wallet.Address, "bob@example.com", AccessRight.Owner, "alice@example.com");
        await delegationService.GrantAccessAsync(
            wallet.Address, "charlie@example.com", AccessRight.ReadWrite, "alice@example.com");
        await delegationService.GrantAccessAsync(
            wallet.Address, "dave@example.com", AccessRight.ReadOnly, "alice@example.com");

        // Verify owner (Alice) has full access
        var aliceHasOwner = await delegationService.HasAccessAsync(
            wallet.Address, "alice@example.com", AccessRight.Owner);
        aliceHasOwner.Should().BeTrue();

        // Verify Bob (Owner delegate) has all rights
        var bobHasOwner = await delegationService.HasAccessAsync(
            wallet.Address, "bob@example.com", AccessRight.Owner);
        var bobHasReadWrite = await delegationService.HasAccessAsync(
            wallet.Address, "bob@example.com", AccessRight.ReadWrite);
        var bobHasReadOnly = await delegationService.HasAccessAsync(
            wallet.Address, "bob@example.com", AccessRight.ReadOnly);

        bobHasOwner.Should().BeTrue();
        bobHasReadWrite.Should().BeTrue();
        bobHasReadOnly.Should().BeTrue();

        // Verify Charlie (ReadWrite) has limited rights
        var charlieHasOwner = await delegationService.HasAccessAsync(
            wallet.Address, "charlie@example.com", AccessRight.Owner);
        var charlieHasReadWrite = await delegationService.HasAccessAsync(
            wallet.Address, "charlie@example.com", AccessRight.ReadWrite);
        var charlieHasReadOnly = await delegationService.HasAccessAsync(
            wallet.Address, "charlie@example.com", AccessRight.ReadOnly);

        charlieHasOwner.Should().BeFalse();
        charlieHasReadWrite.Should().BeTrue();
        charlieHasReadOnly.Should().BeTrue();

        // Verify Dave (ReadOnly) has minimal rights
        var daveHasOwner = await delegationService.HasAccessAsync(
            wallet.Address, "dave@example.com", AccessRight.Owner);
        var daveHasReadWrite = await delegationService.HasAccessAsync(
            wallet.Address, "dave@example.com", AccessRight.ReadWrite);
        var daveHasReadOnly = await delegationService.HasAccessAsync(
            wallet.Address, "dave@example.com", AccessRight.ReadOnly);

        daveHasOwner.Should().BeFalse();
        daveHasReadWrite.Should().BeFalse();
        daveHasReadOnly.Should().BeTrue();

        // Verify access list
        var activeAccess = await delegationService.GetActiveAccessAsync(wallet.Address);
        activeAccess.Should().HaveCount(3);
    }

    [Fact]
    public async Task ConcurrentWalletOperations_ShouldNotConflict()
    {
        // Create multiple wallets concurrently
        var createTasks = Enumerable.Range(0, 10)
            .Select(i => _walletManager.CreateWalletAsync(
                $"Wallet {i}",
                "ED25519",
                $"user{i}@example.com",
                "tenant1"))
            .ToArray();

        var wallets = await Task.WhenAll(createTasks);

        // Verify all wallets created successfully with unique addresses
        wallets.Should().HaveCount(10);
        wallets.Select(w => w.Item1.Address).Should().OnlyHaveUniqueItems();

        // Verify all can be retrieved
        var retrieveTasks = wallets.Select(w => _walletManager.GetWalletAsync(w.Item1.Address));
        var retrievedWallets = await Task.WhenAll(retrieveTasks);

        retrievedWallets.Should().AllSatisfy(w => w.Should().NotBeNull());
    }

    [Fact]
    public async Task EventPublishing_ShouldTrackAllOperations()
    {
        // Perform various operations
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            "Event Test Wallet", "ED25519", "alice@example.com", "tenant1");

        await _walletManager.UpdateWalletAsync(
            wallet.Address,
            name: "Updated Name");

        var txData = System.Text.Encoding.UTF8.GetBytes("test transaction");
        await _walletManager.SignTransactionAsync(wallet.Address, txData);

        await _walletManager.DeleteWalletAsync(wallet.Address);

        // Verify all events were published
        var events = _eventPublisher.GetPublishedEvents();

        events.Should().Contain(e => e is WalletCreatedEvent);
        events.Should().Contain(e => e is TransactionSignedEvent);
        events.Should().Contain(e => e is WalletStatusChangedEvent);

        // Verify event details
        var createdEvent = events.OfType<WalletCreatedEvent>().First();
        createdEvent.WalletAddress.Should().Be(wallet.Address);
        createdEvent.Owner.Should().Be("alice@example.com");
    }

    public void Dispose()
    {
        _repository.Clear();
        _eventPublisher.ClearEvents();
    }
}
