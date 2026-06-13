// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Feature 152 — reflects the browser's online/offline state (via the existing
/// <c>SorchaConnectivity</c> JS bridge) so the wallet can drive offline UI and flush the submit
/// queue on reconnect. <see cref="IsOnline"/> is a best-effort hint ("the browser has a network
/// route", not "the backend is reachable"); an actual submit attempt remains the real test.
/// </summary>
public interface IConnectivity : IAsyncDisposable
{
    /// <summary>Last known online state.</summary>
    bool IsOnline { get; }

    /// <summary>Raised when connectivity flips; the argument is the new online state.</summary>
    event Action<bool>? Changed;

    /// <summary>Registers the browser online/offline listeners and seeds <see cref="IsOnline"/>. Idempotent.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}

/// <summary>Default <see cref="IConnectivity"/> over the <c>SorchaConnectivity</c> JS bridge.</summary>
public sealed class BrowserConnectivity : IConnectivity
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<BrowserConnectivity>? _ref;
    private bool _initialised;

    /// <summary>Initialises a new instance.</summary>
    public BrowserConnectivity(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public bool IsOnline { get; private set; } = true;

    /// <inheritdoc />
    public event Action<bool>? Changed;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialised) return;
        _initialised = true;
        _ref = DotNetObjectReference.Create(this);
        try
        {
            IsOnline = await _js.InvokeAsync<bool>("SorchaConnectivity.register", ct, _ref).ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Bridge unavailable (e.g. SSR/prerender) — assume online; a failed submit is the real test.
            IsOnline = true;
        }
    }

    /// <summary>Invoked from JS when the browser online/offline state changes.</summary>
    [JSInvokable]
    public void OnConnectivityChanged(bool isOnline)
    {
        if (isOnline == IsOnline) return;
        IsOnline = isOnline;
        Changed?.Invoke(isOnline);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_ref is not null)
        {
            try { await _js.InvokeVoidAsync("SorchaConnectivity.unregister"); } catch { /* page teardown */ }
            _ref.Dispose();
            _ref = null;
        }
    }
}
