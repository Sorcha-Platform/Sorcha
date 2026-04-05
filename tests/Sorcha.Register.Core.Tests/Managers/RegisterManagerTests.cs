// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

public class RegisterManagerTests
{
    private readonly InMemoryRegisterRepository _repository;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly RegisterManager _manager;

    public RegisterManagerTests()
    {
        _repository = new InMemoryRegisterRepository();
        _eventPublisher = new InMemoryEventPublisher();
        _manager = new RegisterManager(_repository, _eventPublisher);
    }

    [Fact]
    public async Task CreateRegisterAsync_WithValidData_ShouldCreateRegister()
    {
        // Arrange
        var name = "Test Register";

        // Act
        var result = await _manager.CreateRegisterAsync(name);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Id.Length.Should().Be(32); // GUID without hyphens
        result.Name.Should().Be(name);
        result.Height.Should().Be(0);
        result.Status.Should().Be(RegisterStatus.Offline);
        result.Advertise.Should().BeFalse();
        result.IsFullReplica.Should().BeTrue();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateRegisterAsync_ShouldPublishRegisterCreatedEvent()
    {
        // Arrange
        var name = "Test Register";

        // Act
        var result = await _manager.CreateRegisterAsync(name);

        // Assert
        var events = _eventPublisher.GetPublishedEvents<RegisterCreatedEvent>();
        events.Should().HaveCount(1);

        var evt = events.First();
        evt.RegisterId.Should().Be(result.Id);
        evt.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateRegisterAsync_WithInvalidName_ShouldThrowArgumentException(string invalidName)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.CreateRegisterAsync(invalidName));
    }

    [Fact]
    public async Task CreateRegisterAsync_WithNullName_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _manager.CreateRegisterAsync(null!));
    }

    [Fact]
    public async Task CreateRegisterAsync_WithNameTooLong_ShouldThrowArgumentException()
    {
        // Arrange
        var tooLongName = new string('a', 39); // Max is 38

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await _manager.CreateRegisterAsync(tooLongName));

        exception.Message.Should().Contain("between 1 and 38 characters");
    }

    [Fact]
    public async Task CreateRegisterAsync_WithAdvertiseTrue_ShouldSetAdvertise()
    {
        // Act
        var result = await _manager.CreateRegisterAsync("Test", advertise: true);

        // Assert
        result.Advertise.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRegisterAsync_WithPartialReplica_ShouldSetIsFullReplicaFalse()
    {
        // Act
        var result = await _manager.CreateRegisterAsync("Test", isFullReplica: false);

        // Assert
        result.IsFullReplica.Should().BeFalse();
    }

    [Fact]
    public async Task GetRegisterAsync_WithExistingId_ShouldReturnRegister()
    {
        // Arrange
        var created = await _manager.CreateRegisterAsync("Test");

        // Act
        var result = await _manager.GetRegisterAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Name.Should().Be(created.Name);
    }

    [Fact]
    public async Task GetRegisterAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Act
        var result = await _manager.GetRegisterAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllRegistersAsync_ShouldReturnAllRegisters()
    {
        // Arrange
        await _manager.CreateRegisterAsync("Register 1");
        await _manager.CreateRegisterAsync("Register 2");
        await _manager.CreateRegisterAsync("Register 3");

        // Act
        var result = await _manager.GetAllRegistersAsync();

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateRegisterAsync_ShouldUpdateRegister()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Original Name");
        register.Name = "Updated Name";
        register.Status = RegisterStatus.Online;
        register.Advertise = true;

        // Act
        var result = await _manager.UpdateRegisterAsync(register);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Updated Name");
        result.Status.Should().Be(RegisterStatus.Online);
        result.Advertise.Should().BeTrue();
        result.UpdatedAt.Should().BeAfter(result.CreatedAt);
    }

    [Fact]
    public async Task UpdateRegisterAsync_ShouldPersistChanges()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Original");
        register.Name = "Updated";

        // Act
        await _manager.UpdateRegisterAsync(register);
        var retrieved = await _manager.GetRegisterAsync(register.Id);

        // Assert
        retrieved!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task ExistsAsync_WithExistingRegister_ShouldReturnTrue()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test");

        // Act
        var result = await _manager.RegisterExistsAsync(register.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentRegister_ShouldReturnFalse()
    {
        // Act
        var result = await _manager.RegisterExistsAsync("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRegisterCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await _manager.CreateRegisterAsync("Register 1");
        await _manager.CreateRegisterAsync("Register 2");
        await _manager.CreateRegisterAsync("Register 3");

        // Act
        var result = await _manager.GetRegisterCountAsync();

        // Assert
        result.Should().Be(3);
    }

    [Fact]
    public async Task GetRegisterCountAsync_WithEmptyRepository_ShouldReturnZero()
    {
        // Act
        var result = await _manager.GetRegisterCountAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CreateRegisterAsync_ShouldGenerateUniqueIds()
    {
        // Arrange & Act
        var register1 = await _manager.CreateRegisterAsync("Register 1");
        var register2 = await _manager.CreateRegisterAsync("Register 2");
        var register3 = await _manager.CreateRegisterAsync("Register 3");

        // Assert
        var ids = new[] { register1.Id, register2.Id, register3.Id };
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task UpdateRegisterAsync_WithNullRegister_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _manager.UpdateRegisterAsync(null!));
    }

    [Theory]
    [InlineData(RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Online)]
    [InlineData(RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Recovery)]
    public async Task CreateRegisterAsync_ShouldDefaultToOfflineStatus(RegisterStatus expectedStatus)
    {
        // Act
        var register = await _manager.CreateRegisterAsync("Test");

        // Assert
        register.Status.Should().Be(RegisterStatus.Offline);
    }
}
