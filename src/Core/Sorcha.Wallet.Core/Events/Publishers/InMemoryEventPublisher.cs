// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Domain.Events;
using Sorcha.Wallet.Core.Events.Interfaces;

namespace Sorcha.Wallet.Core.Events.Publishers;

/// <summary>
/// In-memory event publisher for development and testing.
/// Events are logged but not persisted or distributed.
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly ILogger<InMemoryEventPublisher> _logger;
    private readonly List<WalletEvent> _publishedEvents = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryEventPublisher"/> class.
    /// Events are stored in memory for testing and development but are not persisted or distributed.
    /// </summary>
    /// <param name="logger">Logger for event tracking and diagnostics.</param>
    public InMemoryEventPublisher(ILogger<InMemoryEventPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogWarning("InMemoryEventPublisher initialized. Events will not be persisted.");
    }

    /// <inheritdoc/>
    public Task PublishAsync(WalletEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null)
            throw new ArgumentNullException(nameof(@event));

        cancellationToken.ThrowIfCancellationRequested();

        _publishedEvents.Add(@event);

        _logger.LogInformation(
            "Published event {EventType} for wallet {WalletAddress} at {OccurredAt}",
            @event.GetType().Name,
            @event.WalletAddress,
            @event.OccurredAt);

        // Log event-specific details
        LogEventDetails(@event);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PublishBatchAsync(
        IEnumerable<WalletEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (events == null)
            throw new ArgumentNullException(nameof(events));

        var eventList = events.ToList();
        if (eventList.Count == 0)
            return Task.CompletedTask;

        foreach (var @event in eventList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _publishedEvents.Add(@event);
            _logger.LogInformation(
                "Published event {EventType} for wallet {WalletAddress} at {OccurredAt}",
                @event.GetType().Name,
                @event.WalletAddress,
                @event.OccurredAt);
            LogEventDetails(@event);
        }

        _logger.LogInformation("Published batch of {Count} events", eventList.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all published events (for testing purposes).
    /// </summary>
    public IReadOnlyList<WalletEvent> GetPublishedEvents() => _publishedEvents.AsReadOnly();

    /// <summary>
    /// Clears all published events (for testing purposes).
    /// </summary>
    public void ClearEvents()
    {
        _publishedEvents.Clear();
        _logger.LogDebug("Cleared all published events");
    }

    private void LogEventDetails(WalletEvent @event)
    {
        switch (@event)
        {
            case WalletCreatedEvent created:
                _logger.LogDebug(
                    "WalletCreatedEvent: Algorithm={Algorithm}, Owner={Owner}",
                    created.Algorithm,
                    created.Owner);
                break;

            case WalletRecoveredEvent recovered:
                _logger.LogDebug(
                    "WalletRecoveredEvent: Algorithm={Algorithm}, Owner={Owner}",
                    recovered.Algorithm,
                    recovered.Owner);
                break;

            case AddressGeneratedEvent addressGenerated:
                _logger.LogDebug(
                    "AddressGeneratedEvent: Address={Address}, Path={Path}",
                    addressGenerated.Address,
                    addressGenerated.DerivationPath);
                break;

            case TransactionSignedEvent txSigned:
                _logger.LogDebug(
                    "TransactionSignedEvent: TxId={TransactionId}, SignedBy={SignedBy}",
                    txSigned.TransactionId,
                    txSigned.SignedBy);
                break;

            case DelegateAddedEvent delegateAdded:
                _logger.LogDebug(
                    "DelegateAddedEvent: Subject={Subject}, AccessRight={AccessRight}",
                    delegateAdded.Subject,
                    delegateAdded.AccessRight);
                break;

            case DelegateRemovedEvent delegateRemoved:
                _logger.LogDebug(
                    "DelegateRemovedEvent: Subject={Subject}",
                    delegateRemoved.Subject);
                break;

            case WalletStatusChangedEvent statusChanged:
                _logger.LogDebug(
                    "WalletStatusChangedEvent: OldStatus={OldStatus}, NewStatus={NewStatus}",
                    statusChanged.OldStatus,
                    statusChanged.NewStatus);
                break;

            case KeyRotatedEvent keyRotated:
                _logger.LogDebug(
                    "KeyRotatedEvent: OldKeyId={OldKeyId}, NewKeyId={NewKeyId}",
                    keyRotated.OldKeyId,
                    keyRotated.NewKeyId);
                break;
        }
    }
}
