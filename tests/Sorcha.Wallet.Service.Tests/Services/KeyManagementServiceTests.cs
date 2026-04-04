namespace Sorcha.Wallet.Service.Tests.Services;

public class KeyManagementServiceTests : IDisposable
{
    private readonly Mock<ICryptoModule> _mockCryptoModule;
    private readonly Mock<IWalletUtilities> _mockWalletUtilities;
    private readonly LocalEncryptionProvider _encryptionProvider;
    private readonly KeyManagementService _keyManagement;

    public KeyManagementServiceTests()
    {
        _mockCryptoModule = new Mock<ICryptoModule>();
        _mockWalletUtilities = new Mock<IWalletUtilities>();
        _encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());

        _keyManagement = new KeyManagementService(
            (Sorcha.Wallet.Core.Encryption.Interfaces.IKeyProtectionProvider)_encryptionProvider,
            _mockCryptoModule.Object,
            _mockWalletUtilities.Object,
            Mock.Of<ILogger<KeyManagementService>>());

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup crypto module to return deterministic keys
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

        _mockWalletUtilities
            .Setup(x => x.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns((byte[] pk, byte network) => $"ws1{Convert.ToBase64String(pk)[..20]}");
    }

    [Fact]
    public async Task DeriveMasterKeyAsync_ShouldDeriveMasterKey_FromMnemonic()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);

        // Act
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);

        // Assert
        masterKey.Should().NotBeNull();
        masterKey.Should().HaveCount(32); // Master key is 32 bytes
    }

    [Fact]
    public async Task DeriveMasterKeyAsync_ShouldDeriveDifferentKeys_ForDifferentMnemonics()
    {
        // Arrange
        var mnemonic1 = Mnemonic.Generate(12);
        var mnemonic2 = Mnemonic.Generate(12);

        // Act
        var masterKey1 = await _keyManagement.DeriveMasterKeyAsync(mnemonic1);
        var masterKey2 = await _keyManagement.DeriveMasterKeyAsync(mnemonic2);

        // Assert
        masterKey1.Should().NotBeEquivalentTo(masterKey2);
    }

    [Fact]
    public async Task DeriveMasterKeyAsync_ShouldDeriveSameKey_ForSameMnemonic()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);

        // Act
        var masterKey1 = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var masterKey2 = await _keyManagement.DeriveMasterKeyAsync(mnemonic);

        // Assert
        masterKey1.Should().BeEquivalentTo(masterKey2);
    }

    [Fact]
    public async Task DeriveMasterKeyAsync_ShouldDeriveDifferentKeys_WithPassphrase()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);

        // Act
        var masterKeyNoPass = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var masterKeyWithPass = await _keyManagement.DeriveMasterKeyAsync(mnemonic, "password123");

        // Assert
        masterKeyNoPass.Should().NotBeEquivalentTo(masterKeyWithPass);
    }

    [Fact]
    public async Task DeriveMasterKeyAsync_ShouldThrow_WhenMnemonicIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _keyManagement.DeriveMasterKeyAsync(null!));
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldDeriveKeyPair_AtSpecificPath()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act
        var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path, "ED25519");

        // Assert
        privateKey.Should().NotBeNull();
        privateKey.Should().HaveCount(32);
        publicKey.Should().NotBeNull();
        publicKey.Should().HaveCount(32);
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldDeriveDifferentKeys_ForDifferentPaths()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var path1 = DerivationPath.CreateBip44(0, 0, 0, 0);
        var path2 = DerivationPath.CreateBip44(0, 0, 0, 1);

        // Act
        var (privateKey1, publicKey1) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path1, "ED25519");
        var (privateKey2, publicKey2) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path2, "ED25519");

        // Assert
        privateKey1.Should().NotBeEquivalentTo(privateKey2);
        publicKey1.Should().NotBeEquivalentTo(publicKey2);
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldDeriveSameKeys_ForSamePath()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act
        var (privateKey1, publicKey1) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path, "ED25519");
        var (privateKey2, publicKey2) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path, "ED25519");

        // Assert
        privateKey1.Should().BeEquivalentTo(privateKey2);
        publicKey1.Should().BeEquivalentTo(publicKey2);
    }

    [Theory]
    [InlineData("ED25519")]
    [InlineData("NISTP256")]
    [InlineData("RSA4096")]
    public async Task DeriveKeyAtPathAsync_ShouldSupportDifferentAlgorithms(string algorithm)
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act
        var (privateKey, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path, algorithm);

        // Assert
        privateKey.Should().NotBeNull();
        publicKey.Should().NotBeNull();
        _mockCryptoModule.Verify(x => x.GenerateKeySetAsync(
            It.IsAny<WalletNetworks>(),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldThrow_WhenMasterKeyIsEmpty()
    {
        // Arrange
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.DeriveKeyAtPathAsync(Array.Empty<byte>(), path, "ED25519"));
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldThrow_WhenPathIsNull()
    {
        // Arrange
        var masterKey = new byte[64];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _keyManagement.DeriveKeyAtPathAsync(masterKey, null!, "ED25519"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DeriveKeyAtPathAsync_ShouldThrow_WhenAlgorithmIsInvalid(string algorithm)
    {
        // Arrange
        var masterKey = new byte[64];
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.DeriveKeyAtPathAsync(masterKey, path, algorithm!));
    }

    [Fact]
    public async Task DeriveKeyAtPathAsync_ShouldThrow_WhenAlgorithmIsUnsupported()
    {
        // Arrange
        var masterKey = new byte[64];
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.DeriveKeyAtPathAsync(masterKey, path, "UNSUPPORTED"));
    }

    [Fact]
    public async Task GenerateAddressAsync_ShouldGenerateAddress_FromPublicKey()
    {
        // Arrange
        var publicKey = new byte[32];
        Random.Shared.NextBytes(publicKey);

        // Act
        var address = await _keyManagement.GenerateAddressAsync(publicKey, "ED25519");

        // Assert
        address.Should().NotBeNullOrEmpty();
        address.Should().StartWith("ws1");
    }

    [Fact]
    public async Task GenerateAddressAsync_ShouldGenerateSameAddress_ForSamePublicKey()
    {
        // Arrange
        var publicKey = new byte[32];
        Random.Shared.NextBytes(publicKey);

        // Act
        var address1 = await _keyManagement.GenerateAddressAsync(publicKey, "ED25519");
        var address2 = await _keyManagement.GenerateAddressAsync(publicKey, "ED25519");

        // Assert
        address1.Should().Be(address2);
    }

    [Fact]
    public async Task GenerateAddressAsync_ShouldThrow_WhenPublicKeyIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.GenerateAddressAsync(Array.Empty<byte>(), "ED25519"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GenerateAddressAsync_ShouldThrow_WhenAlgorithmIsInvalid(string algorithm)
    {
        // Arrange
        var publicKey = new byte[32];

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.GenerateAddressAsync(publicKey, algorithm!));
    }

    [Fact]
    public async Task EncryptPrivateKeyAsync_ShouldEncryptPrivateKey()
    {
        // Arrange
        var privateKey = new byte[32];
        Random.Shared.NextBytes(privateKey);
        await _encryptionProvider.CreateKeyAsync("test-key-id");

        // Act
        var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
            privateKey, "test-key-id");

        // Assert
        encryptedKey.Should().NotBeNullOrEmpty();
        keyId.Should().Be("test-key-id");
    }

    [Fact]
    public async Task EncryptPrivateKeyAsync_ShouldUseDefaultKeyId_WhenNotSpecified()
    {
        // Arrange
        var privateKey = new byte[32];
        Random.Shared.NextBytes(privateKey);

        // Act
        var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
            privateKey, string.Empty);

        // Assert
        encryptedKey.Should().NotBeNullOrEmpty();
        keyId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EncryptPrivateKeyAsync_ShouldThrow_WhenPrivateKeyIsEmpty()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.EncryptPrivateKeyAsync(Array.Empty<byte>(), "key-id"));
    }

    [Fact]
    public async Task DecryptPrivateKeyAsync_ShouldDecryptPrivateKey()
    {
        // Arrange
        var originalKey = new byte[32];
        Random.Shared.NextBytes(originalKey);
        await _encryptionProvider.CreateKeyAsync("test-key-id");
        var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
            originalKey, "test-key-id");

        // Act
        var decryptedKey = await _keyManagement.DecryptPrivateKeyAsync(encryptedKey, keyId);

        // Assert
        decryptedKey.Should().BeEquivalentTo(originalKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DecryptPrivateKeyAsync_ShouldThrow_WhenEncryptedKeyIsInvalid(string encryptedKey)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.DecryptPrivateKeyAsync(encryptedKey!, "key-id"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DecryptPrivateKeyAsync_ShouldThrow_WhenKeyIdIsInvalid(string keyId)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.DecryptPrivateKeyAsync("encrypted-key", keyId!));
    }

    [Fact]
    public async Task RotateEncryptionKeyAsync_ShouldReEncryptPrivateKey()
    {
        // Arrange
        var originalKey = new byte[32];
        Random.Shared.NextBytes(originalKey);
        await _encryptionProvider.CreateKeyAsync("old-key-id");
        await _encryptionProvider.CreateKeyAsync("new-key-id");
        var (encryptedKey, oldKeyId) = await _keyManagement.EncryptPrivateKeyAsync(
            originalKey, "old-key-id");
        var newKeyId = "new-key-id";

        // Act
        var reEncryptedKey = await _keyManagement.RotateEncryptionKeyAsync(
            encryptedKey, oldKeyId, newKeyId);

        // Assert
        reEncryptedKey.Should().NotBeNullOrEmpty();
        reEncryptedKey.Should().NotBe(encryptedKey);

        // Verify can decrypt with new key
        var decryptedKey = await _keyManagement.DecryptPrivateKeyAsync(reEncryptedKey, newKeyId);
        decryptedKey.Should().BeEquivalentTo(originalKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RotateEncryptionKeyAsync_ShouldThrow_WhenEncryptedKeyIsInvalid(string encryptedKey)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.RotateEncryptionKeyAsync(encryptedKey!, "old-id", "new-id"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RotateEncryptionKeyAsync_ShouldThrow_WhenOldKeyIdIsInvalid(string oldKeyId)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.RotateEncryptionKeyAsync("encrypted-key", oldKeyId!, "new-id"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RotateEncryptionKeyAsync_ShouldThrow_WhenNewKeyIdIsInvalid(string newKeyId)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _keyManagement.RotateEncryptionKeyAsync("encrypted-key", "old-id", newKeyId!));
    }

    [Fact]
    public async Task EncryptionRoundTrip_ShouldPreserveData()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var path = DerivationPath.CreateBip44(0, 0, 0, 0);
        var (privateKey, _) = await _keyManagement.DeriveKeyAtPathAsync(
            masterKey, path, "ED25519");
        await _encryptionProvider.CreateKeyAsync("test-key");

        // Act - Encrypt
        var (encryptedKey, keyId) = await _keyManagement.EncryptPrivateKeyAsync(
            privateKey, "test-key");

        // Act - Decrypt
        var decryptedKey = await _keyManagement.DecryptPrivateKeyAsync(encryptedKey, keyId);

        // Assert
        decryptedKey.Should().BeEquivalentTo(privateKey);
    }

    [Fact]
    public async Task BIP44Derivation_ShouldSupportMultipleAddresses()
    {
        // Arrange
        var mnemonic = Mnemonic.Generate(12);
        var masterKey = await _keyManagement.DeriveMasterKeyAsync(mnemonic);
        var addresses = new List<string>();

        // Act - Derive 5 addresses
        for (uint i = 0; i < 5; i++)
        {
            var path = DerivationPath.CreateBip44(0, 0, 0, i);
            var (_, publicKey) = await _keyManagement.DeriveKeyAtPathAsync(
                masterKey, path, "ED25519");
            var address = await _keyManagement.GenerateAddressAsync(publicKey, "ED25519");
            addresses.Add(address);
        }

        // Assert - All addresses should be unique
        addresses.Should().OnlyHaveUniqueItems();
        addresses.Should().HaveCount(5);
    }

    public void Dispose()
    {
        // No disposal needed - LocalEncryptionProvider manages its own resources
    }
}
