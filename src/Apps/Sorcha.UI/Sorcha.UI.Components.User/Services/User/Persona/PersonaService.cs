// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Persona;

namespace Sorcha.UI.Core.Services.Persona;

/// <summary>
/// Default <see cref="IPersonaService"/> implementation. Wraps
/// <see cref="IPersonaClient"/> with a session-lifetime in-memory cache keyed
/// by <c>actingAs</c>, and stores the global autofill preference in browser
/// local storage so it persists across page reloads.
/// </summary>
/// <remarks>
/// The cache is deliberately per-service-instance (session lifetime in
/// Blazor WASM terms). Logout and org-switch flows should call
/// <see cref="InvalidateCache"/> to avoid leaking one user's persona across
/// account changes — or, more safely, reconstruct the DI scope entirely.
/// </remarks>
public sealed class PersonaService : IPersonaService
{
    private const string AutofillPreferenceKey = "sorcha.persona.autofillEnabled";
    private const bool AutofillDefault = true;

    private readonly IPersonaClient _client;
    private readonly ILocalStorageService _localStorage;
    private readonly ILogger<PersonaService> _logger;

    // Simple cache keyed by actingAs. v1 only ever stores "self".
    private readonly Dictionary<string, PersonaReadModelV1> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public PersonaService(
        IPersonaClient client,
        ILocalStorageService localStorage,
        ILogger<PersonaService> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> GetAsync(
        PersonaReadOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PersonaReadOptions();
        var cacheKey = options.ActingAs;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        var fetched = await _client.GetPersonaAsync(cacheKey, ct) ?? new PersonaReadModelV1();

        await _cacheLock.WaitAsync(ct);
        try
        {
            _cache[cacheKey] = fetched;
        }
        finally
        {
            _cacheLock.Release();
        }

        return fetched;
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> UpdateAsync(
        PersonaAttributesV1 persona,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(persona);

        var result = await _client.PutPersonaAsync(persona, ct);

        // Invalidate and prime the cache with the canonical server response.
        await _cacheLock.WaitAsync(ct);
        try
        {
            _cache.Clear();
            _cache["self"] = result;
        }
        finally
        {
            _cacheLock.Release();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(CancellationToken ct = default)
    {
        await _client.DeletePersonaAsync(ct);

        await _cacheLock.WaitAsync(ct);
        try
        {
            _cache.Clear();
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> GetAutofillEnabledAsync()
    {
        try
        {
            var raw = await _localStorage.GetItemAsync<bool?>(AutofillPreferenceKey);
            return raw ?? AutofillDefault;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read autofill preference; defaulting to enabled.");
            return AutofillDefault;
        }
    }

    /// <inheritdoc />
    public async Task SetAutofillEnabledAsync(bool enabled)
    {
        // Best-effort: this is a per-device convenience preference in localStorage, NOT part of the
        // (server-side, already-persisted) profile. On some mobile browsers a localStorage write can
        // throw (private mode / storage partitioning); that must not surface as "couldn't save
        // profile" after the persona itself saved successfully. Mirrors GetAutofillEnabledAsync.
        try
        {
            await _localStorage.SetItemAsync(AutofillPreferenceKey, enabled);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write autofill preference; continuing (local convenience setting).");
        }
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        // Blazor WASM runs on a single JS thread, so no lock is needed to
        // clear the dictionary safely. Callers (logout / org-switch) invoke
        // this synchronously from UI event handlers — we intentionally do
        // NOT take the SemaphoreSlim here because a blocking Wait() can
        // deadlock if the lock is held by an in-flight awaited call on the
        // same thread. On server-side Blazor or any multi-threaded host the
        // scoped DI container should be rebuilt on logout, making this
        // method unnecessary.
        _cache.Clear();
    }
}
