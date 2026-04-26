// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Citizen.Wallet.Services.Presentation;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Demo-grade in-memory <see cref="ICredentialCache"/>. The production impl
/// (T060) replaces this with an IndexedDB-backed store wrapped in
/// XChaCha20-Poly1305 via the libsodium-js bridge.
/// </summary>
public sealed class InMemoryCredentialCache : ICredentialCache
{
    private readonly ConcurrentDictionary<Guid, CachedCredential> _store = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<CachedCredential>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CachedCredential>>(_store.Values.ToList());

    /// <inheritdoc />
    public Task UpsertAsync(CachedCredential credential, CancellationToken ct = default)
    {
        _store[credential.Id] = credential;
        return Task.CompletedTask;
    }
}
