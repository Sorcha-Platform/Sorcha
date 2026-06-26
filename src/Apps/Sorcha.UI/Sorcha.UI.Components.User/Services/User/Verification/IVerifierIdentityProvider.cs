// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Per-host abstraction that provides the verifier identity to embed in an OID4VP presentation
/// request (Feature 164, B3). The PWA host returns an ephemeral P-256 JWK thumbprint; the desk
/// host returns a stable <c>did:sorcha:verifier:{orgId}</c>. WASM-safe — no server-only types.
/// </summary>
public interface IVerifierIdentityProvider
{
    /// <summary>
    /// Returns the verifier <c>client_id</c> to embed in the create-request. May be an ephemeral
    /// P-256 thumbprint or a stable DID depending on the host.
    /// </summary>
    Task<string> GetClientIdAsync(CancellationToken ct = default);
}
