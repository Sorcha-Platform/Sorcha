// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

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

    /// <summary>
    /// Feature 153 (D) — the personal/home (consumer) token, snapshotted when the citizen switches
    /// into an org capacity so it can be restored on return to Personal. Returns null if absent or
    /// expired. Separate slot from the active token.
    /// </summary>
    Task<AccessTokenRecord?> GetHomeAsync(CancellationToken ct = default);

    /// <summary>Persist the personal/home token snapshot.</summary>
    Task SetHomeAsync(AccessTokenRecord record, CancellationToken ct = default);

    /// <summary>Remove the home token (sign out, alongside <see cref="ClearAsync"/>).</summary>
    Task ClearHomeAsync(CancellationToken ct = default);
}

/// <summary>The wallet's stored access token plus the identity context it carries.</summary>
/// <param name="AccessToken">JWT access token.</param>
/// <param name="ExpiresAt">UTC expiry derived from the OAuth <c>expires_in</c> at issue time.</param>
/// <param name="Email">Citizen email, captured at sign-in for display only (NOT used for auth).</param>
/// <param name="RefreshToken">OAuth refresh token, used for silent renewal; null when none issued.</param>
public sealed record AccessTokenRecord(string AccessToken, DateTimeOffset ExpiresAt, string? Email, string? RefreshToken = null);

/// <summary>In-memory <see cref="IAccessTokenStore"/> for unit tests.</summary>
public sealed class InMemoryAccessTokenStore : IAccessTokenStore
{
    private AccessTokenRecord? _record;
    private AccessTokenRecord? _home;
    /// <inheritdoc />
    public Task<AccessTokenRecord?> GetAsync(CancellationToken ct = default) => Task.FromResult(_record);
    /// <inheritdoc />
    public Task SetAsync(AccessTokenRecord record, CancellationToken ct = default) { _record = record; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default) { _record = null; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<AccessTokenRecord?> GetHomeAsync(CancellationToken ct = default)
    {
        if (_home is not null && _home.ExpiresAt <= DateTimeOffset.UtcNow) _home = null;
        return Task.FromResult(_home);
    }
    /// <inheritdoc />
    public Task SetHomeAsync(AccessTokenRecord record, CancellationToken ct = default) { _home = record; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task ClearHomeAsync(CancellationToken ct = default) { _home = null; return Task.CompletedTask; }
}

/// <summary>IndexedDB-backed <see cref="IAccessTokenStore"/>.</summary>
public sealed class IndexedDbAccessTokenStore : IAccessTokenStore
{
    private const string StoreName = "device";
    private const string Key = "access-token";
    private const string HomeKey = "home-access-token";

    private readonly IJSRuntime _js;

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbAccessTokenStore(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<AccessTokenRecord?> GetAsync(CancellationToken ct = default)
    {
        AccessTokenRecord? row;
        try
        {
            row = await _js.InvokeAsync<AccessTokenRecord?>("SorchaIndexedDb.get", ct, StoreName, Key);
        }
        catch (JSException)
        {
            // The IndexedDB bridge is unavailable — most commonly because the app
            // is running on a non-secure origin (e.g. the Capacitor capacitor://
            // custom scheme on iOS) where crypto.subtle is absent, so
            // js/indexeddb-bridge.js returned early and never defined
            // globalThis.SorchaIndexedDb. Read as signed-out rather than letting an
            // uncaught throw white-screen the sign-in screen via the ErrorBoundary.
            return null;
        }
        if (row is null) return null;
        if (row.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            // Expired — purge so callers see signed-out state.
            try { await ClearAsync(ct); } catch (JSException) { /* bridge gone — already signed-out */ }
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

    /// <inheritdoc />
    public async Task<AccessTokenRecord?> GetHomeAsync(CancellationToken ct = default)
    {
        AccessTokenRecord? row;
        try
        {
            row = await _js.InvokeAsync<AccessTokenRecord?>("SorchaIndexedDb.get", ct, StoreName, HomeKey);
        }
        catch (JSException)
        {
            // See GetAsync — degrade to "no home token" when the bridge is absent.
            return null;
        }
        if (row is null) return null;
        if (row.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            try { await ClearHomeAsync(ct); } catch (JSException) { /* bridge gone */ }
            return null;
        }
        return row;
    }

    /// <inheritdoc />
    public async Task SetHomeAsync(AccessTokenRecord record, CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, record, HomeKey);
    }

    /// <inheritdoc />
    public async Task ClearHomeAsync(CancellationToken ct = default)
    {
        await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, HomeKey);
    }
}
