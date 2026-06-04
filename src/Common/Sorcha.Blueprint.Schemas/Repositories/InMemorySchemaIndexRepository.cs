// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Repositories;

/// <summary>
/// In-memory <see cref="ISchemaIndexRepository"/> used when no MongoDB connection
/// string is configured (local development, infra-free test hosts). Behaviour
/// mirrors <see cref="MongoSchemaIndexRepository"/> — same filter set, Title sort,
/// and Id-based cursor — so consumers behave identically against either backend.
/// Selected via the F113 storage-registration gate; emits a non-gating warning
/// because the schema index is a cache-style store, not an audited one.
/// </summary>
public sealed class InMemorySchemaIndexRepository : ISchemaIndexRepository
{
    // Keyed by the composite natural key (SourceProvider, SourceUri) — same key the
    // Mongo upsert filters on.
    private readonly ConcurrentDictionary<(string Provider, string Uri), SchemaIndexEntryDocument> _entries = new();
    private readonly object _idLock = new();
    private long _idCounter;

    /// <inheritdoc />
    public Task<SchemaIndexSearchResult> SearchAsync(
        string? search = null,
        string[]? sectors = null,
        string? provider = null,
        SchemaIndexStatus? status = null,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<SchemaIndexEntryDocument> query = _entries.Values;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(d =>
                (d.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (sectors is { Length: > 0 })
        {
            query = query.Where(d => d.SectorTags.Intersect(sectors, StringComparer.Ordinal).Any());
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(d => string.Equals(d.SourceProvider, provider, StringComparison.Ordinal));
        }

        // Status filter defaults to Active, matching the Mongo implementation.
        var statusStr = (status ?? SchemaIndexStatus.Active).ToString();
        query = query.Where(d => string.Equals(d.Status, statusStr, StringComparison.Ordinal));

        // Cursor pagination: Id > cursor (Ids are zero-padded so ordinal string
        // comparison is monotonic, matching Mongo's ObjectId ordering contract).
        if (!string.IsNullOrEmpty(cursor))
        {
            query = query.Where(d => string.CompareOrdinal(d.Id, cursor) > 0);
        }

        var filtered = query.ToList();
        var totalCount = filtered.Count;

        var page = filtered
            .OrderBy(d => d.Title, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        var nextCursor = page.Count == limit ? page[^1].Id : null;

        return Task.FromResult(new SchemaIndexSearchResult(page, totalCount, nextCursor));
    }

    /// <inheritdoc />
    public Task<SchemaIndexEntryDocument?> GetByProviderAndUriAsync(
        string sourceProvider,
        string sourceUri,
        CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue((sourceProvider, sourceUri), out var entry);
        return Task.FromResult(entry);
    }

    /// <inheritdoc />
    public Task<SchemaIndexEntryDocument?> GetByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        var entry = _entries.Values.FirstOrDefault(d =>
            string.Equals(d.ShortCode, shortCode, StringComparison.Ordinal));
        return Task.FromResult(entry);
    }

    /// <inheritdoc />
    public Task<bool> UpsertAsync(
        SchemaIndexEntryDocument entry,
        CancellationToken cancellationToken = default)
    {
        var key = (entry.SourceProvider, entry.SourceUri);
        var existing = _entries.TryGetValue(key, out var found) ? found : null;

        // Skip update if content hash unchanged — same short-circuit as Mongo.
        if (existing is not null && existing.ContentHash == entry.ContentHash)
        {
            return Task.FromResult(false);
        }

        if (existing is not null)
        {
            entry.Id = existing.Id;
            entry.ShortCode = existing.ShortCode;
            entry.DateAdded = existing.DateAdded;
        }
        else
        {
            entry.Id = NextId();
            entry.ShortCode = GenerateShortCode();
        }

        entry.DateModified = DateTimeOffset.UtcNow;
        _entries[key] = entry;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<int> BatchUpsertAsync(
        IEnumerable<SchemaIndexEntryDocument> entries,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (await UpsertAsync(entry, cancellationToken))
            {
                count++;
            }
        }
        return count;
    }

    /// <inheritdoc />
    public Task UpdateProviderStatusAsync(
        string sourceProvider,
        SchemaIndexStatus status,
        CancellationToken cancellationToken = default)
    {
        var statusStr = status.ToString();
        foreach (var entry in _entries.Values.Where(d =>
                     string.Equals(d.SourceProvider, sourceProvider, StringComparison.Ordinal)))
        {
            entry.Status = statusStr;
            entry.DateModified = DateTimeOffset.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> GetCountByProviderAsync(
        string sourceProvider,
        CancellationToken cancellationToken = default)
    {
        var count = _entries.Values.Count(d =>
            string.Equals(d.SourceProvider, sourceProvider, StringComparison.Ordinal));
        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Count);

    // Zero-padded monotonic id so ordinal string comparison orders like Mongo's ObjectId.
    private string NextId()
    {
        lock (_idLock)
        {
            return (++_idCounter).ToString("D24");
        }
    }

    private static string GenerateShortCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(6, bytes.ToArray(), (span, b) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = chars[b[i] % chars.Length];
        });
    }
}
