// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Sorcha.Blueprint.Models.Credentials;

using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Manages Bitstring Status Lists for credential revocation and suspension tracking.
/// </summary>
public interface IStatusListManager
{
    /// <summary>
    /// Gets or creates a status list for the given issuer, register, and purpose.
    /// </summary>
    Task<BitstringStatusList> GetOrCreateListAsync(
        string issuerWallet, string registerId, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Allocates the next available index in the list. Allocation is keyed solely by
    /// <c>(listId, index)</c> — the returned <see cref="StatusListAllocation"/> is the identity a
    /// credential's <c>credentialStatus</c> points at. <paramref name="credentialId"/> is optional
    /// context for logging only and is NOT stored or used as a key; pass <c>null</c> when allocating
    /// before the credential is signed (pre-allocation), rather than a synthetic placeholder (issue #220).
    /// </summary>
    Task<StatusListAllocation> AllocateIndexAsync(
        string issuerWallet, string registerId, string? credentialId, CancellationToken ct = default);

    /// <summary>
    /// Sets or clears a bit at the given index.
    /// </summary>
    Task<StatusListBitUpdate> SetBitAsync(
        string listId, int index, bool value, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a status list by its ID.
    /// </summary>
    Task<BitstringStatusList?> GetListAsync(string listId, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw decompressed bitstring bytes for a given list.
    /// Both the W3C and IETF envelopes derive from this single source of truth.
    /// </summary>
    Task<byte[]?> GetRawBitstringBytesAsync(string listId, CancellationToken ct = default);
}

/// <summary>
/// Result of allocating an index in a status list.
/// </summary>
/// <summary>
/// A reserved entry, held at the SAME index in the issuer's revocation and suspension lists.
/// </summary>
/// <remarks>
/// W3C Bitstring Status List defines revocation as *not reversible* and suspension as
/// *reversible*, so they are different statuses and cannot share a bit: a suspended credential
/// written to the revocation list is advertised to every verifier as REVOKED, and lifting the
/// suspension then clears a revocation bit the spec says can never be cleared. One index is
/// reserved across both lists so a credential carries one entry per purpose.
/// </remarks>
public record StatusListAllocation(
    string ListId,
    int Index,
    string StatusListUrl,
    string SuspensionListId,
    string SuspensionListUrl);

/// <summary>
/// Result of setting a bit in a status list.
/// </summary>
public record StatusListBitUpdate(string ListId, int Index, bool Value, int Version, string? RegisterTxId);

/// <summary>
/// In-memory implementation of <see cref="IStatusListManager"/>.
/// Production would persist to MongoDB or PostgreSQL.
/// </summary>
public class StatusListManager : IStatusListManager, IDisposable
{
    // In-process memo ONLY. The durable copy lives in IStatusListStore and the authoritative
    // status bits come from the register. This exists for speed, not for state: anything not
    // also in the store is lost on restart by design, which is what #1482 was about.
    private readonly ConcurrentDictionary<string, BitstringStatusList> _lists = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, StatusListReadiness> _readiness = new();
    private readonly IDistributedCache? _cache;
    private readonly IStatusListStore _store;
    private readonly StatusListLedgerReconciler _reconciler;
    private readonly ILogger<StatusListManager> _logger;
    private readonly string _baseUrl;

    public StatusListManager(
        ILogger<StatusListManager> logger,
        Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved urls,
        IStatusListStore store,
        StatusListLedgerReconciler reconciler,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _store = store;
        _reconciler = reconciler;
        _baseUrl = urls.BaseUrl;
    }

    /// <summary>
    /// How current a list is. <see cref="StatusListReadiness.Warming"/> means not folded yet,
    /// which is a retryable answer and NOT an assertion that nothing is revoked.
    /// </summary>
    public StatusListReadiness GetReadiness(string listId) =>
        _readiness.TryGetValue(listId, out var r) ? r : StatusListReadiness.Warming;

    /// <summary>
    /// Loads a list from the durable store (or creates it), then folds the register status-change
    /// events onto it once per process.
    /// </summary>
    /// <remarks>
    /// Creating an empty list and serving it is the dangerous path: an all-zero bitstring reads as
    /// nothing-is-revoked, which is exactly the wrong answer after a restart. So a freshly-loaded
    /// list is reconciled against the ledger before it is usable, and a failed reconcile marks the
    /// list Failed rather than Ready-and-empty.
    /// </remarks>
    private async Task<BitstringStatusList> LoadAndReconcileAsync(
        string listId, string issuerWallet, string registerId, string purpose, CancellationToken ct)
    {
        if (_lists.TryGetValue(listId, out var memo)
            && _readiness.TryGetValue(listId, out var r) && r == StatusListReadiness.Ready)
        {
            return memo;
        }

        var semaphore = _locks.GetOrAdd(listId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            if (_lists.TryGetValue(listId, out var again)
                && _readiness.TryGetValue(listId, out var r2) && r2 == StatusListReadiness.Ready)
            {
                return again;
            }

            _readiness[listId] = StatusListReadiness.Warming;

            var list = await _store.GetAsync(listId, ct).ConfigureAwait(false);
            if (list is null)
            {
                _logger.LogInformation(
                    "Creating new {Purpose} status list for issuer {Issuer} on register {Register}",
                    purpose, issuerWallet, registerId);
                list = BitstringStatusList.Create(issuerWallet, registerId, purpose);
                await _store.SaveAsync(list, ct).ConfigureAwait(false);
            }

            var outcome = await _reconciler.ReconcileAsync(list, listId, ct).ConfigureAwait(false);
            if (outcome.Readiness == StatusListReadiness.Ready)
            {
                await _store.SaveAsync(list, ct).ConfigureAwait(false);
                if (outcome.ReconciledToDocket is { } d)
                {
                    await _store.SetReconciledDocketAsync(listId, d, ct).ConfigureAwait(false);
                }
            }

            _readiness[listId] = outcome.Readiness;
            _lists[listId] = list;
            return list;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<BitstringStatusList> GetOrCreateListAsync(
        string issuerWallet, string registerId, string purpose, CancellationToken ct = default)
    {
        var key = $"{issuerWallet}-{registerId}-{purpose}-1";
        return await LoadAndReconcileAsync(key, issuerWallet, registerId, purpose, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StatusListAllocation> AllocateIndexAsync(
        string issuerWallet, string registerId, string? credentialId, CancellationToken ct = default)
    {
        var list = await GetOrCreateListAsync(issuerWallet, registerId, "revocation", ct);
        var suspension = await GetOrCreateListAsync(issuerWallet, registerId, "suspension", ct);
        var semaphore = _locks.GetOrAdd(list.Id, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            // ONE numbering space, stored twice (#1502). The index is decided once across both
            // lists and then reserved in each. The previous code allocated from each list
            // independently, compared the results, logged a warning when they disagreed, and
            // carried on — so a suspension list created later than its revocation sibling stayed
            // permanently behind, believing indexes were free that credentials already pointed at.
            //
            // Taking the max is what heals an already-drifted pair: the lagging list jumps to its
            // sibling's numbering. Skipping numbers in it is harmless — an index no credential
            // references simply has a clear bit, which reads as "in good standing", and no
            // credential is ever issued against it.
            var index = Math.Max(list.NextAvailableIndex, suspension.NextAvailableIndex);

            if (index >= list.Size || index >= suspension.Size)
            {
                _logger.LogWarning(
                    "Status list {ListId} is full (capacity: {Size})", list.Id, list.Size);
                throw new InvalidOperationException($"Status list {list.Id} is full");
            }

            if (list.NextAvailableIndex != suspension.NextAvailableIndex)
            {
                _logger.LogInformation(
                    "Status lists for issuer {Issuer} on register {Register} were at different "
                    + "indexes (revocation {Revocation}, suspension {Suspension}); both advanced to "
                    + "{Index} so this credential keeps one entry number in every purpose list",
                    issuerWallet, registerId, list.NextAvailableIndex, suspension.NextAvailableIndex, index);
            }

            // Persist immediately. NextAvailableIndex is the one thing the ledger cannot rebuild,
            // and if it resets a new credential is handed an index an older one already holds,
            // so revoking the new one would silently mark the old one revoked too.
            if (!list.ReserveIndex(index) || !suspension.ReserveIndex(index))
            {
                throw new InvalidOperationException(
                    $"Could not reserve index {index} across both purpose lists for {list.Id}");
            }

            _logger.LogInformation(
                "Allocated index {Index} in status lists {ListId} + {SuspensionId} for credential {CredentialId}",
                index, list.Id, suspension.Id, credentialId ?? "(pre-allocation — not yet signed)");

            await _store.SaveAsync(list, ct).ConfigureAwait(false);
            await _store.SaveAsync(suspension, ct).ConfigureAwait(false);

            var url = $"{_baseUrl}/{list.Id}";
            var suspensionUrl = $"{_baseUrl}/{suspension.Id}";
            return new StatusListAllocation(list.Id, index, url, suspension.Id, suspensionUrl);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StatusListBitUpdate> SetBitAsync(
        string listId, int index, bool value, string? reason = null, CancellationToken ct = default)
    {
        var list = await LoadByIdAsync(listId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Status list {listId} not found");

        var semaphore = _locks.GetOrAdd(listId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            list.SetBit(index, value);
            await _store.SaveAsync(list, ct).ConfigureAwait(false);

            // Invalidate cache
            if (_cache != null)
            {
                await _cache.RemoveAsync($"statuslist:{listId}", ct);
            }

            _logger.LogInformation(
                "Set bit {Index} to {Value} in status list {ListId} (v{Version}). Reason: {Reason}",
                index, value, listId, list.Version, reason ?? "(none)");

            return new StatusListBitUpdate(listId, index, value, list.Version, list.RegisterTxId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<BitstringStatusList?> GetListAsync(string listId, CancellationToken ct = default) =>
        await LoadByIdAsync(listId, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<byte[]?> GetRawBitstringBytesAsync(string listId, CancellationToken ct = default)
    {
        var list = await LoadByIdAsync(listId, ct).ConfigureAwait(false);
        if (list is null) return null;

        return BitstringStatusList.DecompressBitstring(list.EncodedList);
    }

    /// <summary>
    /// Loads a list already known to this node by id, reconciling it against the register on first
    /// touch. Returns null when the id has never existed here, a genuine 404 distinct from known
    /// but not yet folded, which <see cref="GetReadiness"/> reports.
    /// </summary>
    private async Task<BitstringStatusList?> LoadByIdAsync(string listId, CancellationToken ct)
    {
        if (_lists.TryGetValue(listId, out var memo)
            && _readiness.TryGetValue(listId, out var r) && r == StatusListReadiness.Ready)
        {
            return memo;
        }

        var stored = await _store.GetAsync(listId, ct).ConfigureAwait(false);
        if (stored is null) return null;

        return await LoadAndReconcileAsync(
            listId, stored.IssuerWallet, stored.RegisterId, stored.Purpose, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();
        GC.SuppressFinalize(this);
    }
}
