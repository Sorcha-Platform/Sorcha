// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Services.HolderKeys;

/// <summary>
/// Feature 137 (cross-node submission, C3) — HTTP transport contract for the Wallet Service
/// <c>GET /api/v1/wallet/holder-keys</c> endpoint. Exists so <c>HolderKeyRenderer.razor</c> can
/// resolve an auth-wrapped client via DI (consumer-tier citizen JWT) rather than injecting a bare
/// <c>HttpClient</c> that would hit the gateway's auth policy with a 401. Mirrors the
/// <c>IAddressLookupClient</c> typed-client boundary so the renderer is unit-testable without Razor.
/// </summary>
public interface IHolderKeyClient
{
    /// <summary>
    /// Calls <c>GET /api/v1/wallet/holder-keys</c>. Returns the signed-in citizen's public delivery
    /// keys (holder JWK for the SD-JWT <c>cnf</c> binding, wallet encryption public key + algorithm
    /// for the on-register AEAD envelope). Returns <c>null</c> on any non-success — the renderer
    /// surfaces a retry affordance rather than silently submitting an unbindable application.
    /// </summary>
    Task<HolderKeysView?> GetHolderKeysAsync(CancellationToken ct = default);
}

/// <summary>
/// Feature 137 — the citizen's public delivery keys, as returned by
/// <c>GET /api/v1/wallet/holder-keys</c>. Public material only. The property names match the
/// wire contract (<c>holder-keys-endpoint.openapi.yaml</c>) so System.Text.Json camelCase
/// round-trips with the server's <c>HolderKeysResponse</c>.
/// </summary>
public sealed class HolderKeysView
{
    /// <summary>Slot-108 holder public JWK for the SD-JWT <c>cnf</c> binding.</summary>
    public JsonElement HolderJwk { get; set; }

    /// <summary>Base64 wallet public key used to wrap the on-register AEAD envelope.</summary>
    public string EncryptionPublicKey { get; set; } = string.Empty;

    /// <summary>Delivery algorithm: <c>ED25519</c> or <c>NISTP256</c>.</summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>The citizen's resolved wallet address.</summary>
    public string WalletAddress { get; set; } = string.Empty;
}
