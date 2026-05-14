// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Wraps the device's signing key for the citizen wallet PWA. Production deployments
/// hold the key as a non-extractable WebCrypto <c>CryptoKey</c> via the
/// <c>webcrypto-bridge.js</c> JS interop module (T055). v1 ships an in-memory
/// fallback for the MVP demo so the architecture can be exercised without the JS
/// bridge — the bridge is queued for the Phase 8 hardening pass.
/// </summary>
public interface IDeviceKeyService
{
    /// <summary>Get (creating on first call) the device public JWK for the local wallet.</summary>
    Task<JsonElement> GetPublicJwkAsync(CancellationToken ct = default);

    /// <summary>RFC 7638 thumbprint of the device JWK — stable id used in headers.</summary>
    Task<string> GetThumbprintAsync(CancellationToken ct = default);

    /// <summary>Sign raw bytes with the device key. Returns the raw signature (ASN.1 fixed format for ES256).</summary>
    Task<byte[]> SignAsync(byte[] data, CancellationToken ct = default);
}
