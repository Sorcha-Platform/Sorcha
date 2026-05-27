// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;
using Sorcha.Tenant.Models.Persona;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Per-context persona cache (Feature 125, T018). Caches the decrypted
/// <see cref="PersonaReadModelV1"/> per organisational context to keep
/// context-switch latency under one second
/// (<c>SC-003</c>) and to power form autofill without a server round-trip
/// per page render.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by <c>(ContextOrgId ?? "personal")</c>. Cache entries never outlive
/// a sign-out — <see cref="ClearAllAsync"/> is the flush hook. The cache MUST
/// NOT be the source of truth for sensitive operations: form auto-fill is
/// fine, but signing a persona update always goes server-side first
/// (Tenant Service holds the encrypted ciphertext authoritatively).
/// </para>
/// <para>
/// Cache-management policy (15-minute stale-while-revalidate, refresh on
/// explicit user edit / context switch) is the caller's responsibility — this
/// store provides only the persistent slot. Callers land with US3 (PR-B) and
/// US2 (PR-D).
/// </para>
/// </remarks>
public interface IPerContextPersonaCache
{
    /// <summary>Read the cached persona for a context, or null if not cached.</summary>
    Task<PersonaReadModelV1?> GetAsync(Guid? contextOrgId, CancellationToken ct = default);

    /// <summary>Write (replace) the cached persona for a context.</summary>
    Task SetAsync(Guid? contextOrgId, PersonaReadModelV1 persona, CancellationToken ct = default);

    /// <summary>Remove the cached persona for a single context.</summary>
    Task RemoveAsync(Guid? contextOrgId, CancellationToken ct = default);

    /// <summary>Flush every cached persona — invoked on sign-out.</summary>
    Task ClearAllAsync(CancellationToken ct = default);
}

/// <summary>Helpers shared by the in-memory and IndexedDB implementations.</summary>
internal static class PerContextPersonaCacheKeys
{
    public const string PersonalKey = "personal";

    /// <summary>Cache key for a context — "personal" for null, "N"-format GUID otherwise.</summary>
    public static string KeyFor(Guid? contextOrgId)
        => contextOrgId is null ? PersonalKey : contextOrgId.Value.ToString("N");
}

/// <summary>In-memory <see cref="IPerContextPersonaCache"/> for tests.</summary>
public sealed class InMemoryPerContextPersonaCache : IPerContextPersonaCache
{
    private readonly Dictionary<string, PersonaReadModelV1> _cache = new();

    /// <inheritdoc />
    public Task<PersonaReadModelV1?> GetAsync(Guid? contextOrgId, CancellationToken ct = default)
        => Task.FromResult(_cache.TryGetValue(PerContextPersonaCacheKeys.KeyFor(contextOrgId), out var p) ? p : null);

    /// <inheritdoc />
    public Task SetAsync(Guid? contextOrgId, PersonaReadModelV1 persona, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(persona);
        _cache[PerContextPersonaCacheKeys.KeyFor(contextOrgId)] = persona;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(Guid? contextOrgId, CancellationToken ct = default)
    {
        _cache.Remove(PerContextPersonaCacheKeys.KeyFor(contextOrgId));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAllAsync(CancellationToken ct = default)
    {
        _cache.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>IndexedDB-backed <see cref="IPerContextPersonaCache"/>.</summary>
public sealed class IndexedDbPerContextPersonaCache : IPerContextPersonaCache
{
    private const string StoreName = "personas";

    private readonly IJSRuntime _js;
    /// <summary>Initialise a new instance.</summary>
    public IndexedDbPerContextPersonaCache(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    /// <inheritdoc />
    public async Task<PersonaReadModelV1?> GetAsync(Guid? contextOrgId, CancellationToken ct = default)
        => await _js.InvokeAsync<PersonaReadModelV1?>(
            "SorchaIndexedDb.get", ct, StoreName, PerContextPersonaCacheKeys.KeyFor(contextOrgId));

    /// <inheritdoc />
    public async Task SetAsync(Guid? contextOrgId, PersonaReadModelV1 persona, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(persona);
        await _js.InvokeVoidAsync(
            "SorchaIndexedDb.put", ct, StoreName, persona, PerContextPersonaCacheKeys.KeyFor(contextOrgId));
    }

    /// <inheritdoc />
    public async Task RemoveAsync(Guid? contextOrgId, CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.del", ct, StoreName, PerContextPersonaCacheKeys.KeyFor(contextOrgId));

    /// <inheritdoc />
    public async Task ClearAllAsync(CancellationToken ct = default)
        => await _js.InvokeVoidAsync("SorchaIndexedDb.clear", ct, StoreName);
}
