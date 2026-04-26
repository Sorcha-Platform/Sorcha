// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Persists the citizen JWT (and a small amount of identity metadata)
/// across page reloads so the wallet stays signed in (Feature 114, T109
/// foundation). Production lives in the IndexedDB <c>device</c> store
/// keyed <c>access-token</c>; the in-memory variant is used by unit tests
/// where IJSRuntime isn't available.
/// </summary>
public interface IAccessTokenStore
{
    /// <summary>Returns the persisted token, or null if signed out.</summary>
    Task<AccessTokenRecord?> GetAsync(CancellationToken ct = default);

    /// <summary>Persist a freshly-issued token.</summary>
    Task SetAsync(AccessTokenRecord record, CancellationToken ct = default);

    /// <summary>Remove the stored token (sign out).</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>The wallet's stored access token plus the identity context it carries.</summary>
/// <param name="AccessToken">JWT access token.</param>
/// <param name="ExpiresAt">UTC expiry derived from the OAuth <c>expires_in</c> at issue time.</param>
/// <param name="Email">Citizen email, captured at sign-in for display only (NOT used for auth).</param>
public sealed record AccessTokenRecord(string AccessToken, DateTimeOffset ExpiresAt, string? Email);

/// <summary>In-memory <see cref="IAccessTokenStore"/> for unit tests.</summary>
public sealed class InMemoryAccessTokenStore : IAccessTokenStore
{
    private AccessTokenRecord? _record;
    /// <inheritdoc />
    public Task<AccessTokenRecord?> GetAsync(CancellationToken ct = default) => Task.FromResult(_record);
    /// <inheritdoc />
    public Task SetAsync(AccessTokenRecord record, CancellationToken ct = default) { _record = record; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default) { _record = null; return Task.CompletedTask; }
}

/// <summary>IndexedDB-backed <see cref="IAccessTokenStore"/>.</summary>
public sealed class IndexedDbAccessTokenStore : IAccessTokenStore
{
    private const string StoreName = "device";
    private const string Key = "access-token";

    private readonly IJSRuntime _js;

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbAccessTokenStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<AccessTokenRecord?> GetAsync(CancellationToken ct = default)
    {
        var row = await _js.InvokeAsync<AccessTokenRecord?>("SorchaIndexedDb.get", ct, StoreName, Key);
        if (row is null) return null;
        if (row.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            // Expired — purge so callers see signed-out state.
            await ClearAsync(ct);
            return null;
        }
        return row;
    }

    /// <inheritdoc />
    public async Task SetAsync(AccessTokenRecord record, CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record, Key);
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, Key);
    }
}
