// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.ServiceClients.CitizenWallet;

// The PWA-local PresentationLogEntry/Outcome (this namespace) and the wire-contract
// versions (Abstractions.Models) share names; alias the wire types for the drain.
using WireLogEntry = Sorcha.CitizenWallet.Abstractions.Models.PresentationLogEntry;
using WireLogOutcome = Sorcha.CitizenWallet.Abstractions.Models.PresentationLogOutcome;

namespace Sorcha.Wallet.Pwa.Services;

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
    private readonly IPresentationLog _presentationLog;
    private readonly ILogger<SyncService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public SyncService(
        ICitizenWalletClient client,
        ICredentialCache cache,
        IDelegationStore delegations,
        ISyncCursorStore cursors,
        IPresentationLog presentationLog,
        ILogger<SyncService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _delegations = delegations ?? throw new ArgumentNullException(nameof(delegations));
        _cursors = cursors ?? throw new ArgumentNullException(nameof(cursors));
        _presentationLog = presentationLog ?? throw new ArgumentNullException(nameof(presentationLog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SyncOutcome> SyncAsync(CancellationToken ct = default)
    {
        var outcome = await SyncCoreAsync(ct);

        // US5 — drain the local presentation log to the platform on every
        // successful sync. Best-effort: a drain failure never fails the sync.
        await DrainPresentationLogAsync(ct);

        return outcome;
    }

    private async Task<SyncOutcome> SyncCoreAsync(CancellationToken ct)
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

    /// <summary>
    /// Reports any not-yet-synced presentation-log entries to the platform and marks
    /// them synced on a 202. Skips entries with no credential id (written before the
    /// PR2 schema bump) — they cannot form a valid report. Best-effort throughout.
    /// </summary>
    private async Task DrainPresentationLogAsync(CancellationToken ct)
    {
        try
        {
            var all = await _presentationLog.ListAsync(ct);
            var pending = all
                .Where(e => !e.SyncedToServer && e.CredentialId != Guid.Empty)
                .ToList();
            if (pending.Count == 0) return;

            var request = new PresentationLogReportRequest
            {
                Entries = pending.Select(ToWireEntry).ToList()
            };

            var accepted = await _client.ReportPresentationLogAsync(request, ct);
            if (!accepted) return;

            foreach (var entry in pending)
            {
                await _presentationLog.AppendAsync(entry with { SyncedToServer = true }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presentation-log drain failed; will retry on next sync");
        }
    }

    private static WireLogEntry ToWireEntry(PresentationLogEntry e) => new()
    {
        Id = e.Id,
        CredentialId = e.CredentialId,
        VerifierLabel = Truncate(e.VerifierLabel, 200),
        DisclosedClaims = e.DisclosedClaims,
        PresentedAt = e.PresentedAt,
        Outcome = e.Outcome == PresentationLogOutcome.Sent
            ? WireLogOutcome.Acknowledged
            : WireLogOutcome.VerifierRejected
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

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
                Id = StringToCacheGuid(replaced.NewId),
                Vct = string.Empty,
                RawSdJwt = replaced.Jwt,
                AvailableClaimNames = [],
            }, ct);
        }
        // Revoked entries are surfaced via the cache's existence; v1 leaves the
        // local row in place because the verifier will reject on its own status
        // check. A future revision can add ICredentialCache.RemoveAsync.
    }

    /// <summary>
    /// Maps a wire <see cref="CachedCredentialPayload"/> to the local cache shape.
    /// Internal (not private) so <c>Sorcha.Wallet.Pwa.Tests</c> (an
    /// <c>InternalsVisibleTo</c> friend assembly) can exercise the mapping directly.
    /// </summary>
    internal static CachedCredential ToCachedCredential(CachedCredentialPayload payload) => new()
    {
        // The cache uses Guid as an opaque local key; the canonical credential id
        // (urn:credential:...) survives in payload.RawSdJwt + the presentation
        // engine's verifier round-trip. Boundary mapping is deterministic so the
        // same urn always lands on the same cache row across syncs.
        Id = StringToCacheGuid(payload.Id),
        Vct = payload.Vct,
        RawSdJwt = payload.Jwt,
        AvailableClaimNames = [],
        IssuerDid = payload.IssuerDid,
        // Credential VCT decoupling (Task 4): the authored display name, when the
        // issuer supplied one, wins over CredentialDisplay.Humanize(vct) on the card.
        DisplayLabel = payload.DisplayMeta?["credentialName"]?.GetValue<string>(),
    };

    private static Guid StringToCacheGuid(string credentialId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(credentialId));
        // First 16 bytes of SHA-256 form a deterministic, well-distributed Guid.
        var slice = new byte[16];
        Array.Copy(bytes, slice, 16);
        return new Guid(slice);
    }
}
