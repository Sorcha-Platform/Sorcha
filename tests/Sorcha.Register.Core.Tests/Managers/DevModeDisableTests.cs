// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

/// <summary>
/// Tests for RegisterManager.DisableDevModeAsync — the one-way toggle
/// that permanently disables dev mode on a register, requiring
/// field-level encryption from that point forward.
/// </summary>
public class DevModeDisableTests
{
    private readonly InMemoryRegisterRepository _repository;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly RegisterManager _manager;

    public DevModeDisableTests()
    {
        _repository = new InMemoryRegisterRepository();
        _eventPublisher = new InMemoryEventPublisher();
        _manager = new RegisterManager(_repository, _eventPublisher);
    }

    [Fact]
    public async Task DisableDevModeAsync_WhenDevModeEnabled_ShouldReturnTrueAndDisable()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Dev Register", devMode: true);
        register.DevMode.Should().BeTrue();

        // Act
        var result = await _manager.DisableDevModeAsync(register.Id);

        // Assert
        result.Should().BeTrue();

        var updated = await _manager.GetRegisterAsync(register.Id);
        updated.Should().NotBeNull();
        updated!.DevMode.Should().BeFalse();
    }

    [Fact]
    public async Task DisableDevModeAsync_WhenDevModeAlreadyDisabled_ShouldReturnFalse()
    {
        // Arrange — create register with devMode=false (default)
        var register = await _manager.CreateRegisterAsync("Prod Register");
        register.DevMode.Should().BeFalse();

        // Act
        var result = await _manager.DisableDevModeAsync(register.Id);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DisableDevModeAsync_WhenRegisterNotFound_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager.DisableDevModeAsync("nonexistent-register-id"));
    }

    [Fact]
    public async Task DisableDevModeAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Dev Register", devMode: true);
        var originalUpdatedAt = register.UpdatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);

        // Act
        await _manager.DisableDevModeAsync(register.Id);

        // Assert
        var updated = await _manager.GetRegisterAsync(register.Id);
        updated.Should().NotBeNull();
        updated!.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task DisableDevModeAsync_CalledTwice_SecondCallReturnsFalse()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Dev Register", devMode: true);

        // Act
        var firstResult = await _manager.DisableDevModeAsync(register.Id);
        var secondResult = await _manager.DisableDevModeAsync(register.Id);

        // Assert
        firstResult.Should().BeTrue();
        secondResult.Should().BeFalse();

        var updated = await _manager.GetRegisterAsync(register.Id);
        updated!.DevMode.Should().BeFalse();
    }
}
