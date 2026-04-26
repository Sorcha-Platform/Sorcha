// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Citizen.Wallet.Services.Presentation;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Local credential cache. v1 demo keeps everything in memory; the production impl
/// (T060) backs a per-credential row in IndexedDB store <c>credentials</c>,
/// XChaCha20-Poly1305-encrypted under the device-derived content key.
/// </summary>
public interface ICredentialCache
{
    /// <summary>List all cached credentials available for presentation.</summary>
    Task<IReadOnlyList<CachedCredential>> ListAsync(CancellationToken ct = default);

    /// <summary>Add (or replace) a credential.</summary>
    Task UpsertAsync(CachedCredential credential, CancellationToken ct = default);
}
