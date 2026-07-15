// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Services.DeviceKeys;

/// <summary>
/// Citizen OID4VP device-bound <c>cnf</c> (Phase 1, #1195) — host-neutral seam that lets the shared
/// <c>DeviceKeyRenderer</c> obtain this device's PUBLIC signing JWK without referencing the PWA-only
/// <c>IDeviceKeyService</c>. In Option A the SD-JWT holder-binding key (<c>cnf.jwk</c>) IS the device
/// key, so the renderer writes this JWK into the <c>/holderJwk</c> payload slot the issuer reads at
/// credential-issuance time. Wallet delivery keys stay the wallet's keys (from <c>IHolderKeyClient</c>).
/// </summary>
public interface IDeviceKeyProvider
{
    /// <summary>
    /// Returns this device's PUBLIC signing JWK (EC P-256) for the SD-JWT <c>cnf</c> binding,
    /// or <c>null</c> when no device key is available (e.g. a non-PWA host). Public material only.
    /// </summary>
    Task<JsonElement?> GetDevicePublicJwkAsync(CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IDeviceKeyProvider"/> registered by <c>AddSorchaUserComponents</c> so DI stays
/// resolvable in the web host, where the device-bound apply is out of scope for Phase 1. Returns
/// <c>null</c> so <c>DeviceKeyRenderer</c> shows its error/retry state rather than binding a wrong key.
/// A PWA host overrides this with a real implementation.
/// </summary>
public sealed class NullDeviceKeyProvider : IDeviceKeyProvider
{
    /// <inheritdoc />
    public Task<JsonElement?> GetDevicePublicJwkAsync(CancellationToken ct = default)
        => Task.FromResult<JsonElement?>(null);
}
