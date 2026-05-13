// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Presentation;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Production <see cref="ICredentialCache"/> backed by IndexedDB store
/// <c>credentials</c> via <c>indexeddb-bridge.js</c>. Each credential is
/// serialised to JSON and encrypted under the wallet's content key
/// (AES-GCM-256 in v1; XChaCha20-Poly1305 once the libsodium bridge lands).
/// The content key is a non-extractable WebCrypto <c>CryptoKey</c> persisted
/// in the IndexedDB <c>device</c> store via structured-clone.
/// </summary>
public sealed class IndexedDbCredentialCache : ICredentialCache
{
    private readonly IJSRuntime _js;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbCredentialCache(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CachedCredential>> ListAsync(CancellationToken ct = default)
    {
        var jsonStrings = await _js.InvokeAsync<string[]>("SorchaIndexedDb.listCredentials", ct);
        var list = new List<CachedCredential>(jsonStrings.Length);
        foreach (var json in jsonStrings)
        {
            var cred = JsonSerializer.Deserialize<CachedCredential>(json, JsonOpts);
            if (cred is not null) list.Add(cred);
        }
        return list;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(CachedCredential credential, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(credential, JsonOpts);
        await _js.InvokeVoidAsync("SorchaIndexedDb.putCredential", ct, credential.Id.ToString(), json);
    }
}
