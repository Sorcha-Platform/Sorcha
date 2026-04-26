// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Citizen.Wallet.Services.Presentation;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Persists the opaque sync cursor between calls. Lives in the device store
/// so it survives page refresh. Pulled out of <see cref="SyncService"/> so
/// tests can inject an in-memory implementation without mocking IJSRuntime.
/// </summary>
public interface ISyncCursorStore
{
    /// <summary>Returns the most recently persisted cursor, or null if first sync.</summary>
    Task<string?> GetAsync(CancellationToken ct = default);

    /// <summary>Persist a cursor for the next sync.</summary>
    Task SetAsync(string token, CancellationToken ct = default);
}

/// <summary>In-memory cursor store used by unit tests.</summary>
public sealed class InMemorySyncCursorStore : ISyncCursorStore
{
    private string? _token;
    /// <inheritdoc />
    public Task<string?> GetAsync(CancellationToken ct = default) => Task.FromResult(_token);
    /// <inheritdoc />
    public Task SetAsync(string token, CancellationToken ct = default) { _token = token; return Task.CompletedTask; }
}

/// <summary>
/// IndexedDB-backed cursor store. Singleton row in the <c>device</c> store
/// keyed <c>sync-cursor</c>.
/// </summary>
public sealed class IndexedDbSyncCursorStore : ISyncCursorStore
{
    private const string StoreName = "device";
    private const string Key = "sync-cursor";
    private readonly IJSRuntime _js;
    /// <summary>Initialises a new instance.</summary>
    public IndexedDbSyncCursorStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<string?> GetAsync(CancellationToken ct = default)
    {
        var row = await _js.InvokeAsync<CursorRow?>("SorchaIndexedDb.get", ct, StoreName, Key);
        return row?.Token;
    }

    /// <inheritdoc />
    public async Task SetAsync(string token, CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName,
            new CursorRow(token, DateTimeOffset.UtcNow), Key);
    }

    private sealed record CursorRow(string Token, DateTimeOffset SetAt);
}

/// <summary>
/// Drives wallet sync from the server (Feature 114, T107). Pulls deltas via
/// <see cref="ICitizenWalletClient.SyncAsync"/>, applies adds/revokes/replacements
/// to <see cref="ICredentialCache"/>, persists the cursor between calls, and
/// recovers from a 410 stale cursor by falling back to a full snapshot.
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Pull and apply the next delta (or full snapshot on first call / after 410).
    /// </summary>
    /// <returns>A summary of what changed.</returns>
    Task<SyncOutcome> SyncAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a single <see cref="ISyncService.SyncAsync"/> call.</summary>
/// <param name="Mode">Whether this was a delta sync or a full snapshot fallback.</param>
/// <param name="Added">Number of new credentials added to the cache.</param>
/// <param name="Revoked">Number of credentials revoked.</param>
/// <param name="Replaced">Number of credentials replaced by re-issuance.</param>
/// <param name="StatusListsToRefresh">Status list URIs the server flagged as stale.</param>
public sealed record SyncOutcome(
    SyncMode Mode,
    int Added,
    int Revoked,
    int Replaced,
    IReadOnlyList<string> StatusListsToRefresh);

/// <summary>How the latest sync ran.</summary>
public enum SyncMode
{
    /// <summary>Incremental delta applied since the last cursor.</summary>
    Delta = 0,
    /// <summary>Full snapshot pulled (first sync or recovery from a 410 stale cursor).</summary>
    FullSnapshot = 1
}

/// <summary>
/// Default <see cref="ISyncService"/>. Persists the cursor in the IndexedDB
/// <c>device</c> store (singleton key <c>sync-cursor</c>) so it survives page
/// refresh — no separate object store needed for v1.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ICitizenWalletClient _client;
    private readonly ICredentialCache _cache;
    private readonly IDelegationStore _delegations;
    private readonly ISyncCursorStore _cursors;
    private readonly ILogger<SyncService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public SyncService(
        ICitizenWalletClient client,
        ICredentialCache cache,
        IDelegationStore delegations,
        ISyncCursorStore cursors,
        ILogger<SyncService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _delegations = delegations ?? throw new ArgumentNullException(nameof(delegations));
        _cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SyncOutcome> SyncAsync(CancellationToken ct = default)
    {
        var cursor = await _cursors.GetAsync(ct);
        var delta = await _client.SyncAsync(cursor, ct);

        if (delta is null)
        {
            // 410 — cursor stale; fall back to a full snapshot.
            _logger.LogInformation("Sync cursor stale, falling back to full snapshot");
            return await FullSnapshotAsync(ct);
        }

        await ApplyDeltaAsync(delta, ct);
        await _cursors.SetAsync(delta.SyncToken, ct);

        if (delta.Delegation.Renewed && !string.IsNullOrEmpty(delta.Delegation.Jwt))
        {
            await _delegations.SetAsync(delta.Delegation.Jwt, ct);
        }

        return new SyncOutcome(
            SyncMode.Delta,
            delta.Credentials.Added.Count,
            delta.Credentials.Revoked.Count,
            delta.Credentials.Replaced.Count,
            delta.StatusListsToRefresh);
    }

    private async Task<SyncOutcome> FullSnapshotAsync(CancellationToken ct)
    {
        var snapshot = await _client.ListCredentialsAsync(ct);
        foreach (var payload in snapshot.Credentials)
        {
            await _cache.UpsertAsync(ToCachedCredential(payload), ct);
        }

        // Server cursor advances on /sync, not /credentials — do a fresh /sync to obtain one.
        var bootstrap = await _client.SyncAsync(null, ct);
        if (bootstrap is not null)
        {
            await _cursors.SetAsync(bootstrap.SyncToken, ct);
        }

        return new SyncOutcome(SyncMode.FullSnapshot, snapshot.Credentials.Count, 0, 0, []);
    }

    private async Task ApplyDeltaAsync(SyncResponse delta, CancellationToken ct)
    {
        foreach (var payload in delta.Credentials.Added)
        {
            await _cache.UpsertAsync(ToCachedCredential(payload), ct);
        }
        foreach (var replaced in delta.Credentials.Replaced)
        {
            await _cache.UpsertAsync(new CachedCredential
            {
                Id = replaced.NewId,
                Vct = string.Empty,
                RawSdJwt = replaced.Jwt,
                AvailableClaimNames = [],
            }, ct);
        }
        // Revoked entries are surfaced via the cache's existence; v1 leaves the
        // local row in place because the verifier will reject on its own status
        // check. A future revision can add ICredentialCache.RemoveAsync.
    }

    private static CachedCredential ToCachedCredential(CachedCredentialPayload payload) => new()
    {
        Id = payload.Id,
        Vct = payload.Vct,
        RawSdJwt = payload.Jwt,
        AvailableClaimNames = [],
        IssuerDid = payload.IssuerDid,
    };

}
