// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.Json;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.InMemory;

/// <summary>
/// In-memory implementation of IWormStore for development and testing.
/// Enforces append-only semantics - no updates or deletes.
/// </summary>
/// <typeparam name="TDocument">Document type.</typeparam>
/// <typeparam name="TId">Document identifier type (typically ulong for sequence).</typeparam>
public class InMemoryWormStore<TDocument, TId> : IWormStore<TDocument, TId>
    where TDocument : class
    where TId : notnull
{
    private readonly ConcurrentDictionary<TId, string> _store = new();
    private readonly Func<TDocument, TId> _idSelector;
    private readonly JsonSerializerOptions _jsonOptions;
    private ulong _currentSequence;
    private readonly object _sequenceLock = new();

    /// <summary>
    /// Initializes a new instance of the InMemoryWormStore.
    /// </summary>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <param name="idSetter">Optional function to set ID on document for auto-sequencing.</param>
    public InMemoryWormStore(
        Func<TDocument, TId> idSelector,
        Func<TDocument, TId, TDocument>? idSetter = null)
    {
        _idSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc/>
    public Task<TDocument> AppendAsync(TDocument document, CancellationToken cancellationToken = default)
    {
        var id = _idSelector(document);

        var json = JsonSerializer.Serialize(document, _jsonOptions);

        if (!_store.TryAdd(id, json))
        {
            throw new InvalidOperationException($"Document with ID '{id}' already exists. WORM storage does not allow updates.");
        }

        lock (_sequenceLock)
        {
            // Track the highest ID seen for sequence tracking
            if (id is ulong ulongId)
            {
                if (ulongId > _currentSequence)
                {
                    _currentSequence = ulongId;
                }
            }
            else
            {
                // For non-ulong IDs, just increment
                _currentSequence++;
            }
        }

        return Task.FromResult(document);
    }

    /// <inheritdoc/>
    public async Task AppendBatchAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default)
    {
        // Validate all documents first
        var docList = documents.ToList();
        foreach (var doc in docList)
        {
            var id = _idSelector(doc);
            if (_store.ContainsKey(id))
            {
                throw new InvalidOperationException($"Document with ID '{id}' already exists. WORM storage does not allow updates.");
            }
        }

        // Then append all
        foreach (var doc in docList)
        {
            await AppendAsync(doc, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(id, out var json))
        {
            return Task.FromResult(JsonSerializer.Deserialize<TDocument>(json, _jsonOptions));
        }
        return Task.FromResult<TDocument?>(null);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<TDocument>> GetRangeAsync(
        TId startId,
        TId endId,
        CancellationToken cancellationToken = default)
    {
        // This assumes TId is comparable (like ulong for ledger heights)
        var comparer = Comparer<TId>.Default;

        var documents = _store
            .Where(kvp => comparer.Compare(kvp.Key, startId) >= 0 && comparer.Compare(kvp.Key, endId) <= 0)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => JsonSerializer.Deserialize<TDocument>(kvp.Value, _jsonOptions)!)
            .ToList();

        return Task.FromResult<IEnumerable<TDocument>>(documents);
    }

    /// <inheritdoc/>
    public Task<IEnumerable<TDocument>> QueryAsync(
        Expression<Func<TDocument, bool>> filter,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var compiled = filter.Compile();
        var query = _store.Values
            .Select(json => JsonSerializer.Deserialize<TDocument>(json, _jsonOptions)!)
            .Where(compiled);

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return Task.FromResult<IEnumerable<TDocument>>(query.ToList());
    }

    /// <inheritdoc/>
    public Task<ulong> GetCurrentSequenceAsync(CancellationToken cancellationToken = default)
    {
        lock (_sequenceLock)
        {
            return Task.FromResult(_currentSequence);
        }
    }

    /// <inheritdoc/>
    public Task<long> CountAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            return Task.FromResult((long)_store.Count);
        }

        var compiled = filter.Compile();
        var count = _store.Values
            .Select(json => JsonSerializer.Deserialize<TDocument>(json, _jsonOptions)!)
            .Count(compiled);

        return Task.FromResult((long)count);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.ContainsKey(id));
    }

    /// <inheritdoc/>
    public Task<IntegrityCheckResult> VerifyIntegrityAsync(
        TId? startId = default,
        TId? endId = default,
        CancellationToken cancellationToken = default)
    {
        // In-memory store always has integrity (no corruption possible)
        long documentsChecked = _store.Count;

        // Check if we should filter by range - compare against default value
        var hasStartId = !EqualityComparer<TId?>.Default.Equals(startId, default);
        var hasEndId = !EqualityComparer<TId?>.Default.Equals(endId, default);

        if (hasStartId && hasEndId)
        {
            var comparer = Comparer<TId>.Default;
            documentsChecked = _store.Keys
                .Count(k => comparer.Compare(k, startId!) >= 0 && comparer.Compare(k, endId!) <= 0);
        }

        var result = new IntegrityCheckResult(
            IsValid: true,
            DocumentsChecked: documentsChecked,
            CorruptedDocuments: 0,
            Violations: Array.Empty<IntegrityViolation>());

        return Task.FromResult(result);
    }

    /// <summary>
    /// Clears all documents from the store.
    /// FOR TESTING ONLY - this violates WORM semantics.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        lock (_sequenceLock)
        {
            _currentSequence = 0;
        }
    }
}
