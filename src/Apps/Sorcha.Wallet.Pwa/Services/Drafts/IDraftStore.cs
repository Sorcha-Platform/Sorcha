// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Services.Drafts.Models;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 (US1) — device-local, encrypted store of in-progress action drafts (form data +
/// captured media), keyed by <c>instanceId:actionId</c>. Lets a citizen fill an action offline,
/// close the app, and resume exactly where they left off.
/// </summary>
public interface IDraftStore
{
    /// <summary>Returns the draft for an action, or <c>null</c> if none saved.</summary>
    Task<ActionDraft?> GetAsync(string instanceId, int actionId, CancellationToken ct = default);

    /// <summary>Saves (upserts) a draft, stamping <see cref="ActionDraft.SavedAt"/>.</summary>
    Task SaveAsync(ActionDraft draft, CancellationToken ct = default);

    /// <summary>Deletes a draft (e.g. after a successful submit). Idempotent.</summary>
    Task DeleteAsync(string instanceId, int actionId, CancellationToken ct = default);

    /// <summary>Lists all saved drafts (for inbox badges).</summary>
    Task<IReadOnlyList<ActionDraft>> ListAsync(CancellationToken ct = default);
}

/// <summary>Default <see cref="IDraftStore"/> over the encrypted <c>drafts</c> store.</summary>
public sealed class DraftStore : IDraftStore
{
    private const string StoreName = "drafts";

    private readonly IEncryptedObjectStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Initialises a new instance.</summary>
    public DraftStore(IEncryptedObjectStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public Task<ActionDraft?> GetAsync(string instanceId, int actionId, CancellationToken ct = default) =>
        _store.GetAsync<ActionDraft>(StoreName, ActionDraft.MakeKey(instanceId, actionId), ct);

    /// <inheritdoc />
    public Task SaveAsync(ActionDraft draft, CancellationToken ct = default)
    {
        var stamped = draft with { SavedAt = _clock.GetUtcNow() };
        return _store.PutAsync(StoreName, stamped.Key, stamped, ct);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string instanceId, int actionId, CancellationToken ct = default) =>
        _store.DeleteAsync(StoreName, ActionDraft.MakeKey(instanceId, actionId), ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<ActionDraft>> ListAsync(CancellationToken ct = default) =>
        _store.ListAsync<ActionDraft>(StoreName, ct);
}
