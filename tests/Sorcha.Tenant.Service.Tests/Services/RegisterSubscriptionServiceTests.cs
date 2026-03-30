// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class RegisterSubscriptionServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly IRegisterSubscriptionService _service;
    private readonly Guid _testOrgId = Guid.NewGuid();
    private readonly Guid _testUserId = Guid.NewGuid();
    private const string ValidRegisterId = "abcdef0123456789abcdef0123456789";

    public RegisterSubscriptionServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        var loggerMock = new Mock<ILogger<RegisterSubscriptionService>>();
        _service = new RegisterSubscriptionService(_dbContext, loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_ValidRegister_CreatesSubscription()
    {
        // Arrange & Act
        var result = await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OrganizationId.Should().Be(_testOrgId);
        result.RegisterId.Should().Be(ValidRegisterId);
        result.RegisterName.Should().Be("Test Register");
        result.SubscriptionType.Should().Be("Public");
        result.Status.Should().Be("Active");
        result.SubscribedByUserId.Should().Be(_testUserId);
        result.RevokedAt.Should().BeNull();
        result.RevokedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_PublicSubscription_ImmediatelyActive_AppearsInOrgQuery()
    {
        // Arrange — subscribe to a public register
        await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Act — query subscribed registers (this filters by Active status)
        var subscribed = await _service.GetSubscribedRegistersForOrgAsync(
            _testOrgId, CancellationToken.None);

        // Assert — Public subscription should be immediately visible (Active, not Pending)
        subscribed.Should().HaveCount(1);
        subscribed[0].RegisterId.Should().Be(ValidRegisterId);
        subscribed[0].Status.Should().Be("Active");
        subscribed[0].SubscriptionType.Should().Be("Public");
    }

    [Fact]
    public async Task SubscribeAsync_DuplicateRegister_ThrowsInvalidOperationException()
    {
        // Arrange
        await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Act
        var act = () => _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has a subscription*");
    }

    [Theory]
    [InlineData("not-hex-at-all")]
    [InlineData("abcdef012345678")]  // Too short (15 chars)
    [InlineData("abcdef0123456789abcdef0123456789a")]  // Too long (33 chars)
    [InlineData("ABCDEF0123456789ABCDEF0123456789")]  // Uppercase
    [InlineData("")]
    public async Task SubscribeAsync_InvalidRegisterId_ThrowsArgumentException(string invalidId)
    {
        // Act
        var act = () => _service.SubscribeAsync(
            _testOrgId, invalidId, null, _testUserId, ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*32-character lowercase hex string*");
    }

    #endregion

    #region UnsubscribeAsync Tests

    [Fact]
    public async Task UnsubscribeAsync_PublicSubscription_SetsRevoked()
    {
        // Arrange
        await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        var revokingUserId = Guid.NewGuid();

        // Act
        await _service.UnsubscribeAsync(
            _testOrgId, ValidRegisterId, revokingUserId, CancellationToken.None);

        // Assert
        var result = await _service.GetAsync(_testOrgId, ValidRegisterId, CancellationToken.None);
        result.Should().NotBeNull();
        result!.Status.Should().Be("Revoked");
        result.RevokedByUserId.Should().Be(revokingUserId);
        result.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnsubscribeAsync_OwnerSubscription_ThrowsInvalidOperationException()
    {
        // Arrange
        await _service.CreateOwnerSubscriptionAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Act
        var act = () => _service.UnsubscribeAsync(
            _testOrgId, ValidRegisterId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*owner subscription*");
    }

    [Fact]
    public async Task UnsubscribeAsync_NotFound_ThrowsKeyNotFoundException()
    {
        // Act
        var act = () => _service.UnsubscribeAsync(
            _testOrgId, ValidRegisterId, Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    #endregion

    #region CreateOwnerSubscriptionAsync Tests

    [Fact]
    public async Task CreateOwnerSubscriptionAsync_CreatesActiveOwnerSubscription()
    {
        // Act
        var result = await _service.CreateOwnerSubscriptionAsync(
            _testOrgId, ValidRegisterId, "My Register", _testUserId, ct: CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OrganizationId.Should().Be(_testOrgId);
        result.RegisterId.Should().Be(ValidRegisterId);
        result.RegisterName.Should().Be("My Register");
        result.SubscriptionType.Should().Be("Owner");
        result.Status.Should().Be("Active");
        result.SubscribedByUserId.Should().Be(_testUserId);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_ReturnsPaginated()
    {
        // Arrange — create 3 subscriptions
        for (var i = 0; i < 3; i++)
        {
            var registerId = $"abcdef0123456789abcdef012345678{i}";
            await _service.SubscribeAsync(
                _testOrgId, registerId, $"Register {i}", _testUserId, ct: CancellationToken.None);
        }

        // Act — page 1, size 2
        var result = await _service.ListAsync(_testOrgId, 1, 2, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_DifferentOrg_ReturnsEmpty()
    {
        // Arrange
        await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Act
        var result = await _service.ListAsync(Guid.NewGuid(), 1, 10, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    #endregion

    #region GetSubscribedRegistersForOrgAsync Tests

    [Fact]
    public async Task GetSubscribedRegistersForOrgAsync_ReturnsAllActive()
    {
        // Arrange — create an Owner and a Public subscription (both should be Active)
        var ownerId = "abcdef0123456789abcdef0123456780";
        var publicId = "abcdef0123456789abcdef0123456781";

        await _service.CreateOwnerSubscriptionAsync(
            _testOrgId, ownerId, "Owner Register", _testUserId, ct: CancellationToken.None);

        await _service.SubscribeAsync(
            _testOrgId, publicId, "Public Register", _testUserId, ct: CancellationToken.None);

        // Act
        var result = await _service.GetSubscribedRegistersForOrgAsync(
            _testOrgId, CancellationToken.None);

        // Assert — both Owner and Public subscriptions are immediately Active
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.RegisterId == ownerId && r.Status == "Active");
        result.Should().Contain(r => r.RegisterId == publicId && r.Status == "Active");
    }

    [Fact]
    public async Task GetSubscribedRegistersForOrgAsync_ExcludesRevoked()
    {
        // Arrange
        await _service.SubscribeAsync(
            _testOrgId, ValidRegisterId, "Test Register", _testUserId, ct: CancellationToken.None);

        // Manually set to Active then revoke
        var sub = _dbContext.OrganizationRegisterSubscriptions
            .First(s => s.RegisterId == ValidRegisterId);
        sub.Status = SubscriptionStatus.Active;
        await _dbContext.SaveChangesAsync();

        await _service.UnsubscribeAsync(
            _testOrgId, ValidRegisterId, Guid.NewGuid(), CancellationToken.None);

        // Act
        var result = await _service.GetSubscribedRegistersForOrgAsync(
            _testOrgId, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion
}
