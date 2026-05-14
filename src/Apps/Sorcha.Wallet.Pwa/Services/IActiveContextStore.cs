// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Per-device store for the user's active organisational context
/// (Feature 125, T016). The active context determines which credentials,
/// applications, persona values, and activity history are visible on Home.
/// Persisted client-side so reopening the wallet preserves the last-active
/// context; intentionally NOT synced across devices (per
/// <c>data-model.md</c> — work on the work tablet, personal on the phone).
/// </summary>
/// <remarks>
/// Server-side enforcement of per-context content scoping is the security
/// boundary — the JWT carries the active <c>org_id</c> claim (acquired via
/// <c>/auth/switch-org</c>). This client-side store is a presentation-layer
/// optimisation, not a trust boundary.
/// </remarks>
public interface IActiveContextStore
{
    /// <summary>Returns the persisted active-context record, or null if never written (defaults to Personal).</summary>
    Task<ActiveContextRecord?> GetAsync(CancellationToken ct = default);

    /// <summary>Persist (or replace) the active-context record.</summary>
    Task SetAsync(ActiveContextRecord record, CancellationToken ct = default);

    /// <summary>Clear the active-context record — wallet falls back to Personal on next read.</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// One device's active context. <see cref="ContextOrgId"/> null means
/// Personal; non-null is the organisation id the user is currently acting
/// under. <see cref="SwitchedAt"/> is informational for diagnostics — the
/// authoritative "I'm in this context" signal is the JWT.
/// </summary>
/// <param name="ContextOrgId">Active organisation id; null = Personal context.</param>
/// <param name="SwitchedAt">UTC time the user last switched to this context.</param>
public sealed record ActiveContextRecord(
    Guid? ContextOrgId,
    DateTimeOffset SwitchedAt);

/// <summary>In-memory <see cref="IActiveContextStore"/> for tests.</summary>
public sealed class InMemoryActiveContextStore : IActiveContextStore
{
    private ActiveContextRecord? _record;
    /// <inheritdoc />
    public Task<ActiveContextRecord?> GetAsync(CancellationToken ct = default) => Task.FromResult(_record);
    /// <inheritdoc />
    public Task SetAsync(ActiveContextRecord record, CancellationToken ct = default)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        _record = null;
        return Task.CompletedTask;
    }
}

/// <summary>IndexedDB-backed <see cref="IActiveContextStore"/>.</summary>
public sealed class IndexedDbActiveContextStore : IActiveContextStore
{
    private const string StoreName = "context";
    private const string Key = "active";

    private readonly IJSRuntime _js;
    /// <summary>Initialise a new instance.</summary>
    public IndexedDbActiveContextStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<ActiveContextRecord?> GetAsync(CancellationToken ct = default)
        => await _js.InvokeAsync<ActiveContextRecord?>("SorchaIndexedDb.get", ct, StoreName, Key);

    /// <inheritdoc />
    public async Task SetAsync(ActiveContextRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record, Key);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, Key);
}
