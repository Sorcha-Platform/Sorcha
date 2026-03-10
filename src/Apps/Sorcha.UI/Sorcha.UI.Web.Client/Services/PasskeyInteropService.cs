// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;

namespace Sorcha.UI.Web.Client.Services;

/// <summary>
/// Provides C# interop with the browser WebAuthn API via the webauthn.js module.
/// Used by Blazor WASM components to perform passkey registration and authentication ceremonies.
/// </summary>
public class PasskeyInteropService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _module;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasskeyInteropService"/> class.
    /// </summary>
    /// <param name="jsRuntime">The Blazor JS runtime for interop calls.</param>
    public PasskeyInteropService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Lazily imports the webauthn.js ES module.
    /// </summary>
    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./js/webauthn.js");
        return _module;
    }

    /// <summary>
    /// Checks whether WebAuthn (PublicKeyCredential) is supported by the current browser.
    /// </summary>
    /// <returns><c>true</c> if WebAuthn is available; otherwise <c>false</c>.</returns>
    public async Task<bool> IsWebAuthnSupportedAsync()
    {
        var module = await GetModuleAsync();
        return await module.InvokeAsync<bool>("isWebAuthnSupported");
    }

    /// <summary>
    /// Performs the WebAuthn registration ceremony by calling navigator.credentials.create().
    /// </summary>
    /// <param name="options">The PublicKeyCredentialCreationOptions from the server as a <see cref="JsonElement"/>.</param>
    /// <returns>The attestation response as a <see cref="JsonElement"/> for server verification.</returns>
    public async Task<JsonElement> CreateCredentialAsync(JsonElement options)
    {
        var module = await GetModuleAsync();
        var optionsJson = options.GetRawText();
        var responseJson = await module.InvokeAsync<string>("createCredential", optionsJson);
        return JsonSerializer.Deserialize<JsonElement>(responseJson);
    }

    /// <summary>
    /// Performs the WebAuthn authentication ceremony by calling navigator.credentials.get().
    /// </summary>
    /// <param name="options">The PublicKeyCredentialRequestOptions from the server as a <see cref="JsonElement"/>.</param>
    /// <returns>The assertion response as a <see cref="JsonElement"/> for server verification.</returns>
    public async Task<JsonElement> GetCredentialAsync(JsonElement options)
    {
        var module = await GetModuleAsync();
        var optionsJson = options.GetRawText();
        var responseJson = await module.InvokeAsync<string>("getCredential", optionsJson);
        return JsonSerializer.Deserialize<JsonElement>(responseJson);
    }

    /// <summary>
    /// Disposes the imported JS module reference.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
            _module = null;
        }
    }
}
