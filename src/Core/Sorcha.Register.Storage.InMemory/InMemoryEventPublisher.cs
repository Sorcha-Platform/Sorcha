// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Register.Core.Events;

namespace Sorcha.Register.Storage.InMemory;

/// <summary>
/// In-memory implementation of IEventPublisher for testing.
/// When an InMemoryEventSubscriber is provided, published events are dispatched to subscribers.
/// Thread-safe: uses ConcurrentBag for concurrent publish access.
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly ConcurrentBag<PublishedEvent> _publishedEvents = new();
    private readonly InMemoryEventSubscriber? _subscriber;

    public InMemoryEventPublisher() { }

    public InMemoryEventPublisher(InMemoryEventSubscriber subscriber)
    {
        _subscriber = subscriber;
    }

    public async Task PublishAsync<TEvent>(string topic, TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var publishedEvent = new PublishedEvent
        {
            Topic = topic,
            EventType = typeof(TEvent).Name,
            Event = eventData,
            PublishedAt = DateTime.UtcNow
        };

        _publishedEvents.Add(publishedEvent);

        // Dispatch to subscriber if wired up
        if (_subscriber is not null)
        {
            await _subscriber.DispatchAsync(topic, eventData);
        }
    }

    /// <summary>
    /// Gets all published events for testing verification
    /// </summary>
    public IReadOnlyList<PublishedEvent> GetPublishedEvents() => _publishedEvents.ToList().AsReadOnly();

    /// <summary>
    /// Gets published events of a specific type
    /// </summary>
    public IEnumerable<TEvent> GetPublishedEvents<TEvent>() where TEvent : class
    {
        return _publishedEvents
            .Where(e => e.Event is TEvent)
            .Select(e => (TEvent)e.Event)
            .ToList();
    }

    /// <summary>
    /// Clears all published events
    /// </summary>
    public void Clear()
    {
        _publishedEvents.Clear();
    }
}

/// <summary>
/// Represents a published event for testing
/// </summary>
public class PublishedEvent
{
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public required object Event { get; set; }
    public DateTime PublishedAt { get; set; }
}
