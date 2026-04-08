// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// In-memory transient key-value store used to pass objects between Blazor
/// page navigations without serialising them through URLs. The service is
/// registered as Scoped so entries live for the tab/circuit lifetime.
/// </summary>
/// <remarks>
/// Typical usage: the listing page calls <see cref="Set{T}(string, T)"/> with
/// a correlation key before navigating, and the destination page calls
/// <see cref="Get{T}(string)"/> on init. <see cref="Get{T}(string)"/> removes
/// the entry on read to guarantee one-shot semantics and avoid stale data on
/// subsequent direct navigations (bookmarks, refresh) — in which case the
/// destination page falls back to an API fetch.
/// </remarks>
public sealed class NavigationStateService
{
    private readonly Dictionary<string, object> _state = new(StringComparer.Ordinal);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, overwriting
    /// any existing entry. A null value removes the entry.
    /// </summary>
    public void Set<T>(string key, T value) where T : class
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key must not be null or empty.", nameof(key));

        if (value is null)
        {
            _state.Remove(key);
            return;
        }

        _state[key] = value;
    }

    /// <summary>
    /// Retrieves the value stored under <paramref name="key"/> and removes the
    /// entry. Returns <see langword="null"/> if the key is not present, or if
    /// the stored value is not assignable to <typeparamref name="T"/>.
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        if (string.IsNullOrEmpty(key))
            return null;

        if (!_state.TryGetValue(key, out var value))
            return null;

        _state.Remove(key);
        return value as T;
    }

    /// <summary>
    /// Removes all stored entries. Intended for test cleanup; production code
    /// should rely on one-shot semantics instead of manual clearing.
    /// </summary>
    public void Clear() => _state.Clear();
}
