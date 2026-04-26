// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Production <see cref="IDelegationStore"/> backed by IndexedDB store
/// <c>delegation</c> via <c>indexeddb-bridge.js</c>. Singleton row keyed
/// <c>"self"</c>. The delegation JWT is a public credential (signed by the
/// holder key, bound to a single device JWK) so it is persisted in plaintext
/// — confidentiality is not a property of the delegation itself.
/// </summary>
public sealed class IndexedDbDelegationStore : IDelegationStore
{
    private const string StoreName = "delegation";
    private const string Key = "self";

    private readonly IJSRuntime _js;

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbDelegationStore(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentAsync(CancellationToken ct = default)
    {
        var row = await _js.InvokeAsync<DelegationRow?>("SorchaIndexedDb.get", ct, StoreName, Key);
        return row?.Jwt;
    }

    /// <inheritdoc />
    public async Task SetAsync(string compactJwt, CancellationToken ct = default)
    {
        var row = new DelegationRow(compactJwt, DateTimeOffset.UtcNow);
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, row, Key);
    }

    private sealed record DelegationRow(string Jwt, DateTimeOffset SetAt);
}
