// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Exceptions;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Core.Tests.Services;

public class DelegationServiceTests
{
    private readonly Mock<IWalletRepository> _repositoryMock;
    private readonly DelegationService _sut;

    private const string WalletAddress = "test-wallet-address";
    private const string Subject = "test-subject";
    private const string GrantedBy = "test-granter";
    private const string Owner = "wallet-owner";

    public DelegationServiceTests()
    {
        _repositoryMock = new Mock<IWalletRepository>();
        var loggerMock = new Mock<ILogger<DelegationService>>();
        _sut = new DelegationService(_repositoryMock.Object, loggerMock.Object);
    }

    private static WalletEntity CreateWallet(string address = WalletAddress, string owner = Owner)
    {
        return new WalletEntity
        {
            Address = address,
            EncryptedPrivateKey = "encrypted-key",
            EncryptionKeyId = "key-id",
            Algorithm = "ED25519",
            Owner = owner,
            Tenant = "test-tenant",
            Name = "Test Wallet"
        };
    }

    #region GrantAccessAsync

    [Fact]
    public async Task GrantAccessAsync_ValidRequest_CreatesAccessEntry()
    {
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWallet());
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WalletAccess>());

        var result = await _sut.GrantAccessAsync(
            WalletAddress, Subject, AccessRight.ReadWrite, GrantedBy);

        result.Should().NotBeNull();
        result.ParentWalletAddress.Should().Be(WalletAddress);
        result.Subject.Should().Be(Subject);
        result.AccessRight.Should().Be(AccessRight.ReadWrite);
        result.GrantedBy.Should().Be(GrantedBy);
        result.IsActive.Should().BeTrue();

        _repositoryMock.Verify(r => r.AddAccessAsync(WalletAddress, It.IsAny<WalletAccess>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAccessAsync_WithExpiration_SetsExpiresAt()
    {
        var expiresAt = DateTime.UtcNow.AddDays(7);
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWallet());
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WalletAccess>());

        var result = await _sut.GrantAccessAsync(
            WalletAddress, Subject, AccessRight.ReadOnly, GrantedBy, expiresAt: expiresAt);

        result.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task GrantAccessAsync_DuplicateActiveAccess_ThrowsWalletAccessAlreadyExistsException()
    {
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateWallet());
        var existingAccess = new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadOnly
        };
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingAccess });

        var act = () => _sut.GrantAccessAsync(
            WalletAddress, Subject, AccessRight.ReadWrite, GrantedBy);

        await act.Should().ThrowAsync<WalletAccessAlreadyExistsException>();
    }

    [Fact]
    public async Task GrantAccessAsync_WalletNotFound_ThrowsWalletNotFoundException()
    {
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var act = () => _sut.GrantAccessAsync(
            WalletAddress, Subject, AccessRight.ReadWrite, GrantedBy);

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task GrantAccessAsync_EmptyWalletAddress_ThrowsArgumentException()
    {
        var act = () => _sut.GrantAccessAsync("", Subject, AccessRight.ReadWrite, GrantedBy);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("walletAddress");
    }

    #endregion

    #region RevokeAccessAsync

    [Fact]
    public async Task RevokeAccessAsync_ActiveAccess_SetsRevokedAtAndRevokedBy()
    {
        var access = new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadWrite
        };
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { access });

        await _sut.RevokeAccessAsync(WalletAddress, Subject, "revoker");

        access.RevokedAt.Should().NotBeNull();
        access.RevokedBy.Should().Be("revoker");

        _repositoryMock.Verify(r => r.UpdateAccessAsync(access, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAccessAsync_NoActiveAccess_ThrowsWalletNotFoundException()
    {
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WalletAccess>());

        var act = () => _sut.RevokeAccessAsync(WalletAddress, Subject, "revoker");

        await act.Should().ThrowAsync<WalletNotFoundException>();
    }

    [Fact]
    public async Task RevokeAccessAsync_EmptySubject_ThrowsArgumentException()
    {
        var act = () => _sut.RevokeAccessAsync(WalletAddress, "", "revoker");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("subject");
    }

    #endregion

    #region HasAccessAsync

    [Fact]
    public async Task HasAccessAsync_OwnerSubject_ReturnsTrue()
    {
        var wallet = CreateWallet();
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Owner, AccessRight.Owner);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAccessAsync_ReadWriteDelegate_CanAccessReadOnly()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadWrite
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadOnly);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAccessAsync_ReadWriteDelegate_CanAccessReadWrite()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadWrite
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadWrite);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAccessAsync_ReadWriteDelegate_CannotAccessOwner()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadWrite
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.Owner);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAccessAsync_ReadOnlyDelegate_CanAccessReadOnly()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadOnly
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadOnly);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasAccessAsync_ReadOnlyDelegate_CannotAccessReadWrite()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadOnly
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadWrite);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAccessAsync_WalletNotFound_ReturnsFalse()
    {
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadOnly);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasAccessAsync_RevokedDelegate_ReturnsFalse()
    {
        var wallet = CreateWallet();
        wallet.Delegates.Add(new WalletAccess
        {
            ParentWalletAddress = WalletAddress,
            Subject = Subject,
            GrantedBy = GrantedBy,
            AccessRight = AccessRight.ReadWrite,
            RevokedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        _repositoryMock.Setup(r => r.GetByAddressAsync(WalletAddress, false, true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet);

        var result = await _sut.HasAccessAsync(WalletAddress, Subject, AccessRight.ReadOnly);

        result.Should().BeFalse();
    }

    #endregion

    #region GetActiveAccessAsync

    [Fact]
    public async Task GetActiveAccessAsync_ReturnsActiveAccessFromRepository()
    {
        var accessList = new[]
        {
            new WalletAccess { ParentWalletAddress = WalletAddress, Subject = "user1", GrantedBy = GrantedBy },
            new WalletAccess { ParentWalletAddress = WalletAddress, Subject = "user2", GrantedBy = GrantedBy }
        };
        _repositoryMock.Setup(r => r.GetAccessAsync(WalletAddress, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessList);

        var result = await _sut.GetActiveAccessAsync(WalletAddress);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveAccessAsync_EmptyAddress_ThrowsArgumentException()
    {
        var act = () => _sut.GetActiveAccessAsync("");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("walletAddress");
    }

    #endregion
}
