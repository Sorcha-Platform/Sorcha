// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// WebAuthn ceremony bridge for the wallet sign-in screen. Behind an interface so
/// AuthService unit tests use an in-memory fake instead of mocking IJSRuntime
/// (generic InvokeAsync&lt;T&gt; is brittle to mock — F114 lesson).
/// </summary>
public interface IPasskeyInterop
{
    /// <summary>True when the browser exposes the WebAuthn API.</summary>
    Task<bool> IsSupportedAsync();

    /// <summary>Runs navigator.credentials.get() and returns the assertion response JSON.</summary>
    Task<JsonElement> GetAssertionAsync(JsonElement options);
}

/// <summary>JS-backed <see cref="IPasskeyInterop"/> over <c>./js/webauthn.js</c>.</summary>
public sealed class PasskeyInterop : IPasskeyInterop, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    /// <summary>Initialises a new instance.</summary>
    public PasskeyInterop(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    private async Task<IJSObjectReference> ModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/webauthn.js");

    /// <inheritdoc />
    public async Task<bool> IsSupportedAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("isWebAuthnSupported"); }
        catch { return false; }
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetAssertionAsync(JsonElement options)
    {
        var module = await ModuleAsync();
        var responseJson = await module.InvokeAsync<string>("getCredential", options.GetRawText());
        return JsonSerializer.Deserialize<JsonElement>(responseJson);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is not null) { await _module.DisposeAsync(); _module = null; }
    }
}
