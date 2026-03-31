// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

/// <summary>
/// Tests for RegisterManager.UpdateRegisterStatusAsync lifecycle transitions.
/// Verifies status transitions, event publishing, and timestamp updates.
/// </summary>
public class RegisterStatusLifecycleTests
{
    private readonly InMemoryRegisterRepository _repository;
    private readonly InMemoryEventPublisher _eventPublisher;
    private readonly RegisterManager _manager;

    public RegisterStatusLifecycleTests()
    {
        _repository = new InMemoryRegisterRepository();
        _eventPublisher = new InMemoryEventPublisher();
        _manager = new RegisterManager(_repository, _eventPublisher);
    }

    [Theory]
    [InlineData(RegisterStatus.Offline, RegisterStatus.Online)]
    [InlineData(RegisterStatus.Offline, RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Offline, RegisterStatus.Recovery)]
    [InlineData(RegisterStatus.Online, RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Online, RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Online, RegisterStatus.Recovery)]
    [InlineData(RegisterStatus.Checking, RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Checking, RegisterStatus.Online)]
    [InlineData(RegisterStatus.Checking, RegisterStatus.Recovery)]
    [InlineData(RegisterStatus.Recovery, RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Recovery, RegisterStatus.Online)]
    [InlineData(RegisterStatus.Recovery, RegisterStatus.Checking)]
    public async Task UpdateRegisterStatusAsync_AnyTransition_ShouldSucceed(
        RegisterStatus initialStatus,
        RegisterStatus newStatus)
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test Register");
        register.Status = initialStatus;
        await _manager.UpdateRegisterAsync(register);
        _eventPublisher.Clear();

        // Act
        var result = await _manager.UpdateRegisterStatusAsync(register.Id, newStatus);

        // Assert
        result.Status.Should().Be(newStatus);
    }

    [Fact]
    public async Task UpdateRegisterStatusAsync_ShouldPublishStatusChangedEvent()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test Register");
        _eventPublisher.Clear();

        // Act
        await _manager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Online);

        // Assert
        var events = _eventPublisher.GetPublishedEvents<RegisterStatusChangedEvent>();
        events.Should().HaveCount(1);

        var evt = events.First();
        evt.RegisterId.Should().Be(register.Id);
        evt.OldStatus.Should().Be(RegisterStatus.Offline.ToString());
        evt.NewStatus.Should().Be(RegisterStatus.Online.ToString());
        evt.ChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateRegisterStatusAsync_ShouldUpdateTimestamp()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test Register");
        var originalUpdatedAt = register.UpdatedAt;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);

        // Act
        var result = await _manager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Online);

        // Assert
        result.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateRegisterStatusAsync_SameStatus_ShouldStillPublishEvent()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test Register");
        _eventPublisher.Clear();

        // Act — transition to the same status (Offline → Offline)
        await _manager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Offline);

        // Assert — event is still published even for same-status transitions
        var events = _eventPublisher.GetPublishedEvents<RegisterStatusChangedEvent>();
        events.Should().HaveCount(1);

        var evt = events.First();
        evt.OldStatus.Should().Be(RegisterStatus.Offline.ToString());
        evt.NewStatus.Should().Be(RegisterStatus.Offline.ToString());
    }

    [Fact]
    public async Task UpdateRegisterStatusAsync_NonExistentRegister_ShouldThrowInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager.UpdateRegisterStatusAsync("nonexistent", RegisterStatus.Online));
    }

    [Fact]
    public async Task UpdateRegisterStatusAsync_ShouldPersistNewStatus()
    {
        // Arrange
        var register = await _manager.CreateRegisterAsync("Test Register");

        // Act
        await _manager.UpdateRegisterStatusAsync(register.Id, RegisterStatus.Recovery);

        // Assert — re-fetch from repository to verify persistence
        var retrieved = await _manager.GetRegisterAsync(register.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(RegisterStatus.Recovery);
    }
}
