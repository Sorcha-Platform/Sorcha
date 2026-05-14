// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;
using Sorcha.UI.Components.User.Services.Signing;

namespace Sorcha.Wallet.Pwa.Services.Signing;

/// <summary>
/// v1 WebCrypto-backed implementation of
/// <see cref="IEphemeralVerifierIdentityService"/> (Feature 125, T035).
/// Generates a fresh EC P-256 key per verification session via the
/// existing <c>webcrypto-bridge.js</c> module; disposes by removing the
/// key from the bridge's in-process map.
/// </summary>
public sealed class EphemeralVerifierIdentityService : IEphemeralVerifierIdentityService
{
    private readonly IJSRuntime _js;

    /// <summary>Initialise a new identity service.</summary>
    public EphemeralVerifierIdentityService(IJSRuntime js)
        => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<EphemeralVerifierIdentity> BeginSessionAsync(CancellationToken ct = default)
    {
        var id = $"verifier-session/{Guid.NewGuid():N}";
        await _js.InvokeAsync<string>("SorchaWebCrypto.generateEcdsaP256", ct, id);
        var publicJwk = await _js.InvokeAsync<string>("SorchaWebCrypto.getPublicJwk", ct, id);
        var thumbprint = await _js.InvokeAsync<string>("SorchaWebCrypto.getThumbprint", ct, id);
        return new WebCryptoEphemeralVerifierIdentity(_js, id, thumbprint, publicJwk);
    }

    private sealed class WebCryptoEphemeralVerifierIdentity : EphemeralVerifierIdentity
    {
        private readonly IJSRuntime _js;
        private readonly string _keyId;
        private bool _disposed;

        public WebCryptoEphemeralVerifierIdentity(IJSRuntime js, string keyId, string clientId, string publicJwk)
            : base(clientId, publicJwk)
        {
            _js = js;
            _keyId = keyId;
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await _js.InvokeVoidAsync("SorchaWebCrypto.disposeKey", _keyId);
            }
            catch
            {
                // Disposal is best-effort — a JS exception during teardown
                // must not propagate. The key will be garbage-collected when
                // the page unloads in any event.
            }
        }
    }
}
