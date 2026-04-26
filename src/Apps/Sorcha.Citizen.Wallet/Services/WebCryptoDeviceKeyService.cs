// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Production <see cref="IDeviceKeyService"/> backed by the WebCrypto
/// <c>crypto.subtle</c> API via the <c>webcrypto-bridge.js</c> module. The
/// underlying private key is non-extractable — it never leaves the browser's
/// crypto store and never crosses the JS-interop boundary; only the public
/// JWK and signature bytes do.
/// </summary>
public sealed class WebCryptoDeviceKeyService : IDeviceKeyService
{
    private const string KeyId = "sorcha-wallet-device";
    private readonly IJSRuntime _js;
    private bool _generated;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initialises a new instance.</summary>
    public WebCryptoDeviceKeyService(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetPublicJwkAsync(CancellationToken ct = default)
    {
        await EnsureKeyAsync(ct);
        var json = await _js.InvokeAsync<string>("SorchaWebCrypto.getPublicJwk", ct, KeyId);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <inheritdoc />
    public async Task<string> GetThumbprintAsync(CancellationToken ct = default)
    {
        await EnsureKeyAsync(ct);
        return await _js.InvokeAsync<string>("SorchaWebCrypto.getThumbprint", ct, KeyId);
    }

    /// <inheritdoc />
    public async Task<byte[]> SignAsync(byte[] data, CancellationToken ct = default)
    {
        await EnsureKeyAsync(ct);
        var dataB64 = Base64Url.EncodeToString(data);
        var sigB64 = await _js.InvokeAsync<string>("SorchaWebCrypto.signEs256", ct, KeyId, dataB64);
        return Base64Url.DecodeFromChars(sigB64);
    }

    private async Task EnsureKeyAsync(CancellationToken ct)
    {
        if (_generated) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_generated) return;
            await _js.InvokeAsync<string>("SorchaWebCrypto.generateEcdsaP256", ct, KeyId);
            _generated = true;
        }
        finally { _gate.Release(); }
    }
}
