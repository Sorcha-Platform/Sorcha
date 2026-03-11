// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.Wallet.Service.Tests.Integration;

/// <summary>
/// Integration tests for hybrid (classical + PQC) wallet creation and signing.
/// Uses the real CryptoModule to verify end-to-end cryptographic operations.
/// </summary>
public class HybridSigningIntegrationTests
{
    private readonly CryptoModule _cryptoModule;
    private readonly InMemoryWalletRepository _repository;
    private readonly LocalEncryptionProvider _encryptionProvider;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly WalletManager _walletManager;

    public HybridSigningIntegrationTests()
    {
        _cryptoModule = new CryptoModule();
        _repository = new InMemoryWalletRepository();
        _encryptionProvider = new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>());
        _eventPublisher = new InMemoryEventPublisher(Mock.Of<ILogger<InMemoryEventPublisher>>());

        // Mock wallet utilities (address generation is not the focus of this test)
        var mockWalletUtilities = new Mock<IWalletUtilities>();
        mockWalletUtilities
            .Setup(x => x.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns<byte[], byte>((key, network) =>
                $"ws1test{Convert.ToHexString(key[..Math.Min(8, key.Length)]).ToLowerInvariant()}");

        var keyManagement = new KeyManagementService(
            _encryptionProvider,
            _cryptoModule,
            mockWalletUtilities.Object,
            Mock.Of<ILogger<KeyManagementService>>());

        var mockHashProvider = new Mock<IHashProvider>();
        mockHashProvider
            .Setup(x => x.ComputeHash(It.IsAny<byte[]>(), It.IsAny<Cryptography.Enums.HashType>()))
            .Returns<byte[], Cryptography.Enums.HashType>((data, _) =>
                System.Security.Cryptography.SHA256.HashData(data));

        var transactionService = new TransactionService(
            _cryptoModule,
            mockHashProvider.Object,
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
            Mock.Of<ILogger<WalletManager>>());
    }

    [Fact]
    public async Task CreatePqcWallet_WithMlDsa65_ShouldSucceed()
    {
        var (wallet, mnemonic) = await _walletManager.CreateWalletAsync(
            name: "PQC Test Wallet",
            algorithm: "ML-DSA-65",
            owner: "test-owner",
            tenant: "test-tenant");

        wallet.Should().NotBeNull();
        wallet.Algorithm.Should().Be("ML-DSA-65");
        wallet.PublicKey.Should().NotBeNullOrEmpty();
        mnemonic.WordCount.Should().Be(12);
    }

    [Fact]
    public async Task SignWithPqcWallet_ShouldProduceVerifiableSignature()
    {
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            name: "PQC Signing Wallet",
            algorithm: "ML-DSA-65",
            owner: "test-owner",
            tenant: "test-tenant");

        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("pqc integration test"));

        var (signature, publicKey) = await _walletManager.SignTransactionAsync(
            wallet.Address,
            data,
            isPreHashed: true);

        var verifyResult = await _cryptoModule.VerifyAsync(
            signature, data, (byte)WalletNetworks.ML_DSA_65, publicKey);
        verifyResult.Should().Be(CryptoStatus.Success);
    }

    [Fact]
    public async Task HybridSign_BothWallets_ShouldProduceVerifiableHybridSignature()
    {
        var (classicalWallet, _) = await _walletManager.CreateWalletAsync(
            name: "Classical Wallet",
            algorithm: "ED25519",
            owner: "test-owner",
            tenant: "test-tenant");

        var (pqcWallet, _) = await _walletManager.CreateWalletAsync(
            name: "PQC Wallet",
            algorithm: "ML-DSA-65",
            owner: "test-owner",
            tenant: "test-tenant");

        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("hybrid integration test"));

        // Sign concurrently with both wallets
        var classicalTask = _walletManager.SignTransactionAsync(
            classicalWallet.Address, data, isPreHashed: true);
        var pqcTask = _walletManager.SignTransactionAsync(
            pqcWallet.Address, data, isPreHashed: true);
        await Task.WhenAll(classicalTask, pqcTask);

        var (classicalSig, classicalPubKey) = await classicalTask;
        var (pqcSig, pqcPubKey) = await pqcTask;

        // Assemble HybridSignature
        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(classicalSig),
            ClassicalAlgorithm = "ED25519",
            Pqc = Convert.ToBase64String(pqcSig),
            PqcAlgorithm = "ML-DSA-65",
            WitnessPublicKey = Convert.ToBase64String(pqcPubKey)
        };

        // Verify both independently
        hybrid.IsValid().Should().BeTrue();

        var classicalVerify = await _cryptoModule.VerifyAsync(
            classicalSig, data, (byte)WalletNetworks.ED25519, classicalPubKey);
        classicalVerify.Should().Be(CryptoStatus.Success, "classical signature should verify");

        var pqcVerify = await _cryptoModule.VerifyAsync(
            pqcSig, data, (byte)WalletNetworks.ML_DSA_65, pqcPubKey);
        pqcVerify.Should().Be(CryptoStatus.Success, "PQC signature should verify");

        // Also test via HybridVerifyAsync
        var hybridResult = await _cryptoModule.HybridVerifyAsync(hybrid, data, classicalPubKey);
        hybridResult.Should().Be(CryptoStatus.Success, "hybrid verify should accept both components");
    }

    [Fact]
    public async Task HybridSignature_JsonRoundTrip_ThroughWalletLayer()
    {
        var (classicalWallet, _) = await _walletManager.CreateWalletAsync(
            name: "Classical", algorithm: "ED25519", owner: "test-owner", tenant: "test-tenant");
        var (pqcWallet, _) = await _walletManager.CreateWalletAsync(
            name: "PQC", algorithm: "ML-DSA-65", owner: "test-owner", tenant: "test-tenant");

        var data = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("roundtrip integration"));

        var (classicalSig, classicalPubKey) = await _walletManager.SignTransactionAsync(
            classicalWallet.Address, data, isPreHashed: true);
        var (pqcSig, pqcPubKey) = await _walletManager.SignTransactionAsync(
            pqcWallet.Address, data, isPreHashed: true);

        var hybrid = new HybridSignature
        {
            Classical = Convert.ToBase64String(classicalSig),
            ClassicalAlgorithm = "ED25519",
            Pqc = Convert.ToBase64String(pqcSig),
            PqcAlgorithm = "ML-DSA-65",
            WitnessPublicKey = Convert.ToBase64String(pqcPubKey)
        };

        // Serialize and restore
        var json = hybrid.ToJson();
        var restored = HybridSignature.FromJson(json);

        // Restored signature still verifies
        restored.Should().NotBeNull();
        var result = await _cryptoModule.HybridVerifyAsync(restored!, data, classicalPubKey);
        result.Should().Be(CryptoStatus.Success);
    }
}
