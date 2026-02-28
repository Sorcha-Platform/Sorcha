// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Events;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Events.Interfaces;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Core.Tests.Services;

public class WalletManagerTests
{
    private readonly Mock<IKeyManagementService> _keyManagementMock;
    private readonly Mock<ITransactionService> _transactionServiceMock;
    private readonly Mock<IDelegationService> _delegationServiceMock;
    private readonly Mock<IWalletRepository> _repositoryMock;
    private readonly Mock<IEventPublisher> _eventPublisherMock;
    private readonly WalletManager _sut;

    private const string TestOwner = "test-owner";
    private const string TestTenant = "test-tenant";
    private const string TestAlgorithm = "ED25519";
    private const string TestAddress = "test-wallet-address";

    public WalletManagerTests()
    {
        _keyManagementMock = new Mock<IKeyManagementService>();
        _transactionServiceMock = new Mock<ITransactionService>();
        _delegationServiceMock = new Mock<IDelegationService>();
        _repositoryMock = new Mock<IWalletRepository>();
        _eventPublisherMock = new Mock<IEventPublisher>();
        var loggerMock = new Mock<ILogger<WalletManager>>();

        _sut = new WalletManager(
            _keyManagementMock.Object,
            _transactionServiceMock.Object,
            _delegationServiceMock.Object,
            _repositoryMock.Object,
            _eventPublisherMock.Object,
            loggerMock.Object);
    }

    private void SetupKeyManagementForCreate()
    {
        var masterKey = new byte[32];
        var privateKey = new byte[32];
        var publicKey = new byte[32];

        _keyManagementMock.Setup(k => k.DeriveMasterKeyAsync(It.IsAny<Mnemonic>(), It.IsAny<string?>()))
            .ReturnsAsync(masterKey);
        _keyManagementMock.Setup(k => k.DeriveKeyAtPathAsync(masterKey, It.IsAny<DerivationPath>(), TestAlgorithm))
            .ReturnsAsync((privateKey, publicKey));
        _keyManagementMock.Setup(k => k.GenerateAddressAsync(publicKey, TestAlgorithm))
            .ReturnsAsync(TestAddress);
        _keyManagementMock.Setup(k => k.EncryptPrivateKeyAsync(privateKey, It.IsAny<string>()))
            .ReturnsAsync(("encrypted-key", "key-id"));
    }

    private static WalletEntity CreateWallet(
        string address = TestAddress,
        WalletStatus status = WalletStatus.Active)
    {
        return new WalletEntity
        {
            Address = address,
            EncryptedPrivateKey = "encrypted-key",
            EncryptionKeyId = "key-id",
            Algorithm = TestAlgorithm,
            Owner = TestOwner,
            Tenant = TestTenant,
            Name = "Test Wallet",
            Status = status
        };
    }

    #region CreateWalletAsync

    [Fact]
    public async Task CreateWalletAsync_ValidInput_CreatesWalletWithCorrectProperties()
    {
        SetupKeyManagementForCreate();

        var (wallet, mnemonic) = await _sut.CreateWalletAsync(
            "My Wallet", TestAlgorithm, TestOwner, TestTenant);

        wallet.Should().NotBeNull();
        wallet.Address.Should().Be(TestAddress);
        wallet.Algorithm.Should().Be(TestAlgorithm);
        wallet.Owner.Should().Be(TestOwner);
        wallet.Tenant.Should().Be(TestTenant);
        wallet.Name.Should().Be("My Wallet");
        wallet.Status.Should().Be(WalletStatus.Active);
        mnemonic.Should().NotBeNull();
        mnemonic.WordCount.Should().Be(12);
    }

    [Fact]
    public async Task CreateWalletAsync_ValidInput_SavesAndPublishesEvent()
    {
        SetupKeyManagementForCreate();

        await _sut.CreateWalletAsync("My Wallet", TestAlgorithm, TestOwner, TestTenant);

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<WalletEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(e => e.PublishAsync(
            It.Is<WalletCreatedEvent>(evt =>
                evt.WalletAddress == TestAddress &&
                evt.Owner == TestOwner &&
                evt.Tenant == TestTenant &&
                evt.Algorithm == TestAlgorithm),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateWalletAsync_EmptyAlgorithm_ThrowsArgumentException()
    {
        var act = () => _sut.CreateWalletAsync("My Wallet", "", TestOwner, TestTenant);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("algorithm");
    }

    [Fact]
    public async Task CreateWalletAsync_EmptyOwner_ThrowsArgumentException()
    {
        var act = () => _sut.CreateWalletAsync("My Wallet", TestAlgorithm, "", TestTenant);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("owner");
    }

    [Fact]
    public async Task CreateWalletAsync_EmptyTenant_ThrowsArgumentException()
    {
        var act = () => _sut.CreateWalletAsync("My Wallet", TestAlgorithm, TestOwner, "");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tenant");
    }

    #endregion

    #region DeleteWalletAsync

    [Fact]
    public async Task DeleteWalletAsync_ExistingWallet_SoftDeletesWithStatusChange()
    {
        var wallet = CreateWallet();
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        await _sut.DeleteWalletAsync(TestAddress);

        wallet.Status.Should().Be(WalletStatus.Deleted);
        _repositoryMock.Verify(r => r.UpdateAsync(wallet, It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisherMock.Verify(e => e.PublishAsync(
            It.Is<WalletStatusChangedEvent>(evt =>
                evt.OldStatus == WalletStatus.Active &&
                evt.NewStatus == WalletStatus.Deleted),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteWalletAsync_WalletNotFound_ThrowsInvalidOperationException()
    {
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _sut.DeleteWalletAsync(TestAddress);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteWalletAsync_EmptyAddress_ThrowsArgumentException()
    {
        var act = () => _sut.DeleteWalletAsync("");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("address");
    }

    #endregion

    #region RegisterDerivedAddressAsync

    [Fact]
    public async Task RegisterDerivedAddressAsync_ValidInput_CreatesAddress()
    {
        var wallet = CreateWallet();
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, true, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.RegisterDerivedAddressAsync(
            TestAddress,
            derivedPublicKey: "cHViS2V5",
            derivedAddress: "derived-addr-1",
            derivationPath: "m/44'/0'/0'/0/1",
            label: "My Address");

        result.Should().NotBeNull();
        result.Address.Should().Be("derived-addr-1");
        result.ParentWalletAddress.Should().Be(TestAddress);
        result.DerivationPath.Should().Be("m/44'/0'/0'/0/1");
        result.Label.Should().Be("My Address");
        result.Index.Should().Be(1);
        result.IsChange.Should().BeFalse();
        result.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterDerivedAddressAsync_GapLimitExceeded_ThrowsInvalidOperationException()
    {
        var wallet = CreateWallet();
        // Add 20 unused addresses at account 0, change=false
        for (int i = 0; i < 20; i++)
        {
            wallet.Addresses.Add(new WalletAddress
            {
                ParentWalletAddress = TestAddress,
                Address = $"unused-addr-{i}",
                DerivationPath = $"m/44'/0'/0'/0/{i}",
                Index = i,
                Account = 0,
                IsChange = false,
                IsUsed = false
            });
        }
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, true, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var act = () => _sut.RegisterDerivedAddressAsync(
            TestAddress,
            derivedPublicKey: "cHViS2V5",
            derivedAddress: "new-derived-addr",
            derivationPath: "m/44'/0'/0'/0/20");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Gap limit exceeded*");
    }

    [Fact]
    public async Task RegisterDerivedAddressAsync_DuplicateAddress_ThrowsInvalidOperationException()
    {
        var wallet = CreateWallet();
        wallet.Addresses.Add(new WalletAddress
        {
            ParentWalletAddress = TestAddress,
            Address = "existing-addr",
            DerivationPath = "m/44'/0'/0'/0/0",
            Index = 0
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, true, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var act = () => _sut.RegisterDerivedAddressAsync(
            TestAddress,
            derivedPublicKey: "cHViS2V5",
            derivedAddress: "existing-addr",
            derivationPath: "m/44'/0'/0'/0/1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task RegisterDerivedAddressAsync_InvalidBip44Path_ThrowsArgumentException()
    {
        var wallet = CreateWallet();
        _repositoryMock.Setup(r => r.GetByAddressAsync(TestAddress, true, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var act = () => _sut.RegisterDerivedAddressAsync(
            TestAddress,
            derivedPublicKey: "cHViS2V5",
            derivedAddress: "derived-addr",
            derivationPath: "m/49'/0'/0'/0/0");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("derivationPath");
    }

    #endregion
}
