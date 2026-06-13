// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 — a thin device-local, encrypted object store over the IndexedDB bridge's generic
/// encrypted ops (<c>SorchaIndexedDb.putEnc/getEnc/listEnc/delEnc</c>). Each value is serialised to
/// JSON and sealed with the same XChaCha20-Poly1305 device content key the credential cache uses.
/// Keys are strings; values carry their own logical key. Used by the draft store, submit queue, and
/// action-context cache.
/// </summary>
public interface IEncryptedObjectStore
{
    /// <summary>Encrypt + upsert <paramref name="value"/> under <paramref name="key"/>.</summary>
    Task PutAsync<T>(string storeName, string key, T value, CancellationToken ct = default);

    /// <summary>Get + decrypt the value under <paramref name="key"/>, or <c>null</c> if absent/undecryptable.</summary>
    Task<T?> GetAsync<T>(string storeName, string key, CancellationToken ct = default) where T : class;

    /// <summary>List + decrypt every value in the store (undecryptable rows are evicted, not thrown).</summary>
    Task<IReadOnlyList<T>> ListAsync<T>(string storeName, CancellationToken ct = default) where T : class;

    /// <summary>Delete the value under <paramref name="key"/>. Idempotent.</summary>
    Task DeleteAsync(string storeName, string key, CancellationToken ct = default);
}

/// <summary>Default <see cref="IEncryptedObjectStore"/> backed by <c>indexeddb-bridge.js</c>.</summary>
public sealed class IndexedDbEncryptedObjectStore : IEncryptedObjectStore
{
    private readonly IJSRuntime _js;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbEncryptedObjectStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public Task PutAsync<T>(string storeName, string key, T value, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        return _js.InvokeVoidAsync("SorchaIndexedDb.putEnc", ct, storeName, key, json).AsTask();
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string storeName, string key, CancellationToken ct = default) where T : class
    {
        var json = await _js.InvokeAsync<string?>("SorchaIndexedDb.getEnc", ct, storeName, key).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> ListAsync<T>(string storeName, CancellationToken ct = default) where T : class
    {
        var rows = await _js.InvokeAsync<EncRow[]>("SorchaIndexedDb.listEnc", ct, storeName).ConfigureAwait(false);
        var list = new List<T>(rows.Length);
        foreach (var row in rows)
        {
            var value = JsonSerializer.Deserialize<T>(row.Json, JsonOpts);
            if (value is not null) list.Add(value);
        }
        return list;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storeName, string key, CancellationToken ct = default) =>
        _js.InvokeVoidAsync("SorchaIndexedDb.delEnc", ct, storeName, key).AsTask();

    private sealed record EncRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("json")] string Json);
}
