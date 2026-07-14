// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 (US2) — caches each pending action's form context locally (encrypted) so the citizen
/// can open ANY of their pending actions offline, not just ones opened earlier. Refreshed from the
/// inbox while online; read by <c>ApplicationInstance</c> when offline / the live load fails.
/// </summary>
public interface IActionContextCache
{
    /// <summary>Returns the cached context for an action, or <c>null</c> if not prepared.</summary>
    Task<CachedActionContext?> GetAsync(string instanceId, int actionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the cached context for an instance's current action (offline open keys by instance id
    /// only — the action id is resolved server-side when online). Returns the most-recently-cached
    /// match, or <c>null</c> if the instance was not prepared.
    /// </summary>
    Task<CachedActionContext?> GetForInstanceAsync(string instanceId, CancellationToken ct = default);

    /// <summary>Caches/overwrites a single action context.</summary>
    Task PutAsync(CachedActionContext context, CancellationToken ct = default);

    /// <summary>
    /// Refreshes the cache from the citizen's current pending actions: loads each action's context
    /// (while online) and stores it. Best-effort — a single load failure does not abort the rest.
    /// Returns the number of contexts cached.
    /// </summary>
    Task<int> RefreshFromPendingAsync(CancellationToken ct = default);
}

/// <summary>Default <see cref="IActionContextCache"/> over the encrypted <c>actionContext</c> store.</summary>
public sealed class ActionContextCache : IActionContextCache
{
    private const string StoreName = "actionContext";

    private readonly IEncryptedObjectStore _store;
    private readonly IMyActionsClient _actions;
    private readonly IApplicationActionClient _actionClient;
    private readonly TimeProvider _clock;
    private readonly ILogger<ActionContextCache> _logger;

    /// <summary>Initialises a new instance.</summary>
    public ActionContextCache(
        IEncryptedObjectStore store,
        IMyActionsClient actions,
        IApplicationActionClient actionClient,
        TimeProvider clock,
        ILogger<ActionContextCache> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _actionClient = actionClient ?? throw new ArgumentNullException(nameof(actionClient));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<CachedActionContext?> GetAsync(string instanceId, int actionId, CancellationToken ct = default) =>
        _store.GetAsync<CachedActionContext>(StoreName, ActionDraft.MakeKey(instanceId, actionId), ct);

    /// <inheritdoc />
    public async Task<CachedActionContext?> GetForInstanceAsync(string instanceId, CancellationToken ct = default)
    {
        var all = await _store.ListAsync<CachedActionContext>(StoreName, ct).ConfigureAwait(false);
        CachedActionContext? best = null;
        foreach (var ctx in all)
        {
            if (string.Equals(ctx.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)
                && (best is null || ctx.CachedAt > best.CachedAt))
            {
                best = ctx;
            }
        }
        return best;
    }

    /// <inheritdoc />
    public Task PutAsync(CachedActionContext context, CancellationToken ct = default) =>
        _store.PutAsync(StoreName, context.Key, context, ct);

    /// <inheritdoc />
    public async Task<int> RefreshFromPendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<PendingActionItem> pending;
        try
        {
            pending = await _actions.GetPendingAsync(ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-cache skipped — pending actions unavailable (likely offline).");
            return 0;
        }

        var cached = 0;
        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (!Guid.TryParse(item.InstanceId, out var instanceGuid))
            {
                continue;
            }

            try
            {
                var loadResult = await _actionClient.LoadFormAsync(instanceGuid, ct).ConfigureAwait(false);
                if (loadResult.Status != ApplicationFormLoadStatus.Loaded || loadResult.Context is not { } formCtx)
                {
                    continue;
                }

                var context = new CachedActionContext
                {
                    InstanceId = item.InstanceId,
                    ActionId = formCtx.ActionId,
                    BlueprintId = formCtx.BlueprintId,
                    ActionJson = System.Text.Json.JsonSerializer.Serialize(
                        formCtx.Action, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                    RegisterId = formCtx.RegisterId,
                    SenderWallet = formCtx.SenderWallet,
                    Title = formCtx.Title,
                    CachedAt = _clock.GetUtcNow(),
                };
                await PutAsync(context, ct).ConfigureAwait(false);
                cached++;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pre-cache of action {InstanceId} failed; continuing.", item.InstanceId);
            }
        }
        return cached;
    }
}
