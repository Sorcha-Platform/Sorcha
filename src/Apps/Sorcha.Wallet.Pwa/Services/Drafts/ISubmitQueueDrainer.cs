// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 (US3/US4) — flushes the offline submit queue by rebuilding each item's submission
/// context from the action-context cache and POSTing it via <see cref="IApplicationActionClient"/>.
/// The server response is classified (US4) into submit / retry / hold outcomes. Invoked on reconnect
/// and app open.
/// </summary>
public interface ISubmitQueueDrainer
{
    /// <summary>Attempts to submit every queued item; safe to call repeatedly (idempotent server-side).</summary>
    Task DrainAsync(CancellationToken ct = default);
}

/// <summary>Default <see cref="ISubmitQueueDrainer"/>.</summary>
public sealed class SubmitQueueDrainer : ISubmitQueueDrainer
{
    private readonly ISubmitQueue _queue;
    private readonly IApplicationActionClient _actionClient;
    private readonly IActionContextCache _contextCache;
    private readonly IDraftStore _drafts;
    private readonly ILogger<SubmitQueueDrainer> _logger;

    /// <summary>Initialises a new instance.</summary>
    public SubmitQueueDrainer(
        ISubmitQueue queue,
        IApplicationActionClient actionClient,
        IActionContextCache contextCache,
        IDraftStore drafts,
        ILogger<SubmitQueueDrainer> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _actionClient = actionClient ?? throw new ArgumentNullException(nameof(actionClient));
        _contextCache = contextCache ?? throw new ArgumentNullException(nameof(contextCache));
        _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task DrainAsync(CancellationToken ct = default) => _queue.DrainAsync(SubmitOneAsync, ct);

    private async Task<SubmitOutcome> SubmitOneAsync(QueuedSubmission item, CancellationToken ct)
    {
        var context = await BuildContextAsync(item, ct).ConfigureAwait(false);
        if (context is null)
        {
            // Can't rebuild the action (e.g. cache evicted) — treat as transient; a later drain with
            // connectivity will re-cache and retry.
            return SubmitOutcome.Retry;
        }

        var result = await _actionClient.SubmitAsync(context, item.Payload, ct).ConfigureAwait(false);
        var outcome = SubmitConflictClassifier.Classify(result);
        if (outcome == SubmitOutcome.Submitted)
        {
            // Clear any lingering draft for this action now it's accepted.
            try { await _drafts.DeleteAsync(item.InstanceId, item.ActionId, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "Draft cleanup after queued submit failed."); }
        }
        return outcome;
    }

    private async Task<ApplicationFormContext?> BuildContextAsync(QueuedSubmission item, CancellationToken ct)
    {
        var cached = await _contextCache.GetAsync(item.InstanceId, item.ActionId, ct).ConfigureAwait(false);
        if (cached is null || !Guid.TryParse(item.InstanceId, out var instanceGuid))
        {
            return null;
        }

        try
        {
            var action = System.Text.Json.JsonSerializer.Deserialize<Sorcha.Blueprint.Models.Action>(
                cached.ActionJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (action is null) return null;

            return new ApplicationFormContext(
                instanceGuid, action, cached.BlueprintId, cached.RegisterId, cached.SenderWallet,
                cached.ActionId, string.IsNullOrWhiteSpace(cached.Title) ? "Application" : cached.Title);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Rebuilding submission context for {InstanceId} failed.", item.InstanceId);
            return null;
        }
    }
}
