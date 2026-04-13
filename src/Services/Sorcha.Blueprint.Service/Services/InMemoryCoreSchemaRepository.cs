// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ICoreSchemaRepository"/>.
/// Primitives are held as parsed <see cref="JsonNode"/> trees so the resolver
/// can merge without re-parsing each time.
/// </summary>
/// <remarks>
/// Keys are compared with <see cref="StringComparer.Ordinal"/> because JSON
/// Schema <c>$id</c> URIs are case-sensitive per RFC 3986. A blueprint that
/// references the wrong-case URI will correctly fail to resolve rather than
/// silently hit a near-match primitive.
/// </remarks>
public sealed class InMemoryCoreSchemaRepository : ICoreSchemaRepository
{
    private readonly ConcurrentDictionary<string, JsonNode> _store =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public JsonNode? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _store.TryGetValue(id, out var value) ? value : null;
    }

    /// <inheritdoc />
    public void Upsert(string id, JsonNode schema)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Primitive id must not be empty", nameof(id));
        ArgumentNullException.ThrowIfNull(schema);
        _store[id] = schema;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, JsonNode> GetAll() =>
        new Dictionary<string, JsonNode>(_store, StringComparer.Ordinal);
}
