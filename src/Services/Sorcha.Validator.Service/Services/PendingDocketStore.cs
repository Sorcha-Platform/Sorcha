// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Thread-safe in-memory store for dockets awaiting consensus.
/// Implements efficient lookups by ID, register, and status.
/// </summary>
public class PendingDocketStore : IPendingDocketStore, IDisposable
{
    private readonly ConcurrentDictionary<string, DocketEntry> _dockets = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<PendingDocketStore> _logger;

    // Statistics
    private long _totalAdded;
    private long _totalRemoved;
    private readonly ConcurrentQueue<double> _stayDurations = new();

    public PendingDocketStore(ILogger<PendingDocketStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task AddAsync(Docket docket, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var entry = new DocketEntry
        {
            Docket = docket,
            AddedAt = DateTimeOffset.UtcNow,
            Signatures = new ConcurrentDictionary<string, Signature>()
        };

        // Add proposer's signature if present
        if (docket.ProposerSignature != null)
        {
            entry.Signatures[docket.ProposerValidatorId] = docket.ProposerSignature;
        }

        if (_dockets.TryAdd(docket.DocketId, entry))
        {
            Interlocked.Increment(ref _totalAdded);
            _logger.LogDebug(
                "Added pending docket {DocketId} for register {RegisterId}",
                docket.DocketId, docket.RegisterId);
        }
        else
        {
            _logger.LogWarning(
                "Docket {DocketId} already exists in pending store",
                docket.DocketId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Docket?> GetAsync(string docketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        if (_dockets.TryGetValue(docketId, out var entry))
        {
            return Task.FromResult<Docket?>(entry.Docket);
        }

        return Task.FromResult<Docket?>(null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Docket>> GetByRegisterAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var dockets = _dockets.Values
            .Where(e => e.Docket.RegisterId == registerId)
            .Select(e => e.Docket)
            .ToList();

        return Task.FromResult<IReadOnlyList<Docket>>(dockets);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Docket>> GetByStatusAsync(DocketStatus status, CancellationToken ct = default)
    {
        var dockets = _dockets.Values
            .Where(e => e.Docket.Status == status)
            .Select(e => e.Docket)
            .ToList();

        return Task.FromResult<IReadOnlyList<Docket>>(dockets);
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateStatusAsync(string docketId, DocketStatus status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        await _lock.WaitAsync(ct);
        try
        {
            if (!_dockets.TryGetValue(docketId, out var entry))
            {
                return false;
            }

            var previousStatus = entry.Docket.Status;
            entry.Docket.Status = status;

            _logger.LogDebug(
                "Updated docket {DocketId} status: {Previous} -> {New}",
                docketId, previousStatus, status);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<Docket?> AddSignatureAsync(
        string docketId,
        Signature signature,
        string validatorId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        await _lock.WaitAsync(ct);
        try
        {
            if (!_dockets.TryGetValue(docketId, out var entry))
            {
                return null;
            }

            if (entry.Signatures.TryAdd(validatorId, signature))
            {
                _logger.LogDebug(
                    "Added signature from {ValidatorId} to docket {DocketId} (total: {Count})",
                    validatorId, docketId, entry.Signatures.Count);
            }
            else
            {
                _logger.LogDebug(
                    "Signature from {ValidatorId} already exists for docket {DocketId}",
                    validatorId, docketId);
            }

            return entry.Docket;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(string docketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        if (_dockets.TryRemove(docketId, out var entry))
        {
            Interlocked.Increment(ref _totalRemoved);

            // Track how long it stayed in the store
            var duration = (DateTimeOffset.UtcNow - entry.AddedAt).TotalMilliseconds;
            TrackDuration(duration);

            _logger.LogDebug(
                "Removed pending docket {DocketId} (was in store for {Duration}ms)",
                docketId, duration);

            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<int> GetCountAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_dockets.Count);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Docket>> GetStaleDocketsAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - timeout;

        var staleDockets = _dockets.Values
            .Where(e => e.AddedAt < cutoff)
            .Select(e => e.Docket)
            .ToList();

        return Task.FromResult<IReadOnlyList<Docket>>(staleDockets);
    }

    /// <inheritdoc/>
    public Task<int> ClearRegisterAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var keysToRemove = _dockets
            .Where(kvp => kvp.Value.Docket.RegisterId == registerId)
            .Select(kvp => kvp.Key)
            .ToList();

        var removed = 0;
        foreach (var key in keysToRemove)
        {
            if (_dockets.TryRemove(key, out _))
            {
                removed++;
                Interlocked.Increment(ref _totalRemoved);
            }
        }

        _logger.LogInformation(
            "Cleared {Count} pending dockets for register {RegisterId}",
            removed, registerId);

        return Task.FromResult(removed);
    }

    /// <inheritdoc/>
    public PendingDocketStoreStats GetStats()
    {
        var byStatus = _dockets.Values
            .GroupBy(e => e.Docket.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var byRegister = _dockets.Values
            .GroupBy(e => e.Docket.RegisterId)
            .ToDictionary(g => g.Key, g => g.Count());

        double avgDuration = 0;
        if (_stayDurations.Count > 0)
        {
            avgDuration = _stayDurations.Average();
        }

        return new PendingDocketStoreStats
        {
            TotalPending = _dockets.Count,
            ByStatus = byStatus,
            ByRegister = byRegister,
            TotalAdded = Interlocked.Read(ref _totalAdded),
            TotalRemoved = Interlocked.Read(ref _totalRemoved),
            AverageTimeInStoreMs = avgDuration
        };
    }


    private void TrackDuration(double durationMs)
    {
        _stayDurations.Enqueue(durationMs);

        // Keep only last 1000 entries
        while (_stayDurations.Count > 1000)
        {
            _stayDurations.TryDequeue(out _);
        }
    }

    private class DocketEntry
    {
        public required Docket Docket { get; init; }
        public required DateTimeOffset AddedAt { get; init; }
        public required ConcurrentDictionary<string, Signature> Signatures { get; init; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
