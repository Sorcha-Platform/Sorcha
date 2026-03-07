// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Threading.Channels;

using Sorcha.PeerRouter.Models;

namespace Sorcha.PeerRouter.Services;

/// <summary>
/// Circular event buffer with fan-out SSE broadcast capability.
/// Stores recent events for new client catch-up, and broadcasts to all live SSE subscribers.
/// Each subscriber gets its own Channel for independent consumption.
/// </summary>
public sealed class EventBuffer
{
    private readonly ConcurrentQueue<RouterEvent> _buffer = new();
    private readonly ConcurrentDictionary<Guid, Channel<RouterEvent>> _subscribers = new();
    private readonly int _maxSize;
    private int _count;

    public EventBuffer(RouterConfiguration config)
    {
        _maxSize = config.EventBufferSize;
    }

    /// <summary>
    /// Adds an event to the buffer and broadcasts it to all SSE subscribers.
    /// </summary>
    public void Add(RouterEvent routerEvent)
    {
        _buffer.Enqueue(routerEvent);
        var count = Interlocked.Increment(ref _count);

        // Trim oldest events if over capacity
        while (count > _maxSize && _buffer.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _count);
        }

        // Fan-out to all subscribers
        foreach (var (_, channel) in _subscribers)
        {
            channel.Writer.TryWrite(routerEvent);
        }
    }

    /// <summary>
    /// Returns a snapshot of all buffered events (newest last).
    /// </summary>
    public IReadOnlyList<RouterEvent> GetSnapshot() => [.. _buffer];

    /// <summary>
    /// Returns the current buffer size.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Returns an async enumerable that yields buffered events first,
    /// then new events as they arrive (for SSE follow mode).
    /// Each caller gets an independent subscription.
    /// </summary>
    public async IAsyncEnumerable<RouterEvent> FollowAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RouterEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true
        });

        _subscribers.TryAdd(id, channel);
        try
        {
            // First, yield all buffered events for catch-up
            foreach (var e in _buffer)
            {
                yield return e;
            }

            // Then stream new events as they arrive
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return e;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
