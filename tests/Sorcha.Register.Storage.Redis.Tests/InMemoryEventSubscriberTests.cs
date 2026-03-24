// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Storage.Redis.Tests;

public class InMemoryEventSubscriberTests
{
    [Fact]
    public async Task PublishAsync_WithSubscriber_HandlerCalled()
    {
        var subscriber = new InMemoryEventSubscriber();
        var publisher = new InMemoryEventPublisher(subscriber);
        RegisterCreatedEvent? received = null;

        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            e => { received = e; return Task.CompletedTask; });

        await publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test",
            CreatedAt = DateTime.UtcNow
        });

        received.Should().NotBeNull();
        received!.RegisterId.Should().Be("reg-1");
    }

    [Fact]
    public async Task PublishAsync_MultipleHandlers_AllCalled()
    {
        var subscriber = new InMemoryEventSubscriber();
        var publisher = new InMemoryEventPublisher(subscriber);
        var callCount = 0;

        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => { callCount++; return Task.CompletedTask; });
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => { callCount++; return Task.CompletedTask; });

        await publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test"
        });

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_WrongTopic_HandlerNotCalled()
    {
        var subscriber = new InMemoryEventSubscriber();
        var publisher = new InMemoryEventPublisher(subscriber);
        var handlerCalled = false;

        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => { handlerCalled = true; return Task.CompletedTask; });

        await publisher.PublishAsync("register:deleted", new RegisterDeletedEvent
        {
            RegisterId = "reg-1",
            DeletedAt = DateTime.UtcNow
        });

        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_NoSubscribers_NoError()
    {
        var subscriber = new InMemoryEventSubscriber();
        var publisher = new InMemoryEventPublisher(subscriber);

        var act = () => publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test"
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublisherWithoutSubscriber_StillRecordsEvents()
    {
        var publisher = new InMemoryEventPublisher();

        await publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test"
        });

        publisher.GetPublishedEvents().Should().HaveCount(1);
    }

    [Fact]
    public void GetSubscriptionCount_ReturnsCorrectCount()
    {
        var subscriber = new InMemoryEventSubscriber();

        subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => Task.CompletedTask);

        subscriber.GetSubscriptionCount("register:created").Should().Be(1);
        subscriber.GetSubscriptionCount("register:deleted").Should().Be(0);
    }
}
