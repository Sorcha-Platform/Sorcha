// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Core.Domain.Events;

namespace Sorcha.Wallet.Core.Events.Interfaces;

/// <summary>
/// Publisher for wallet domain events
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a wallet event
    /// </summary>
    /// <param name="event">Event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync(WalletEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes multiple events in a batch
    /// </summary>
    /// <param name="events">Events to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishBatchAsync(
        IEnumerable<WalletEvent> events,
        CancellationToken cancellationToken = default);
}
