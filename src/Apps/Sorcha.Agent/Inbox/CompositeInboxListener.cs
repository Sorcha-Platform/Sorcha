// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Inbox;

/// <summary>
/// Merges multiple inbox listeners and deduplicates by action ID.
/// </summary>
public class CompositeInboxListener : IInboxListener
{
    private readonly IInboxListener[] _listeners;
    private readonly HashSet<string> _processedActionIds = [];
    private readonly ILogger<CompositeInboxListener>? _logger;

    public CompositeInboxListener(params IInboxListener[] listeners)
    {
        _listeners = listeners;
    }

    public CompositeInboxListener(ILogger<CompositeInboxListener> logger, params IInboxListener[] listeners)
    {
        _logger = logger;
        _listeners = listeners;
    }

    public async IAsyncEnumerable<PendingAction> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<PendingAction>();

        // Start all listeners as background tasks feeding the channel
        var tasks = _listeners.Select(listener => Task.Run(async () =>
        {
            try
            {
                await foreach (var action in listener.ListenAsync(cancellationToken))
                {
                    channel.Writer.TryWrite(action);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Inbox listener failed");
            }
        }, cancellationToken)).ToArray();

        // Complete channel when all listeners finish
        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), cancellationToken);

        // Yield deduplicated actions
        await foreach (var action in channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (_processedActionIds.Add(action.ActionId))
            {
                yield return action;
            }
            else
            {
                _logger?.LogDebug("Duplicate action {ActionId} skipped", action.ActionId);
            }
        }
    }

    /// <summary>
    /// Marks an action as processed (for external dedup tracking).
    /// </summary>
    public void MarkProcessed(string actionId) => _processedActionIds.Add(actionId);
}
