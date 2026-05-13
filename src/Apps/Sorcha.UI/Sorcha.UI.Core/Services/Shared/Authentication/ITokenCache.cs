// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Service for encrypted token caching in LocalStorage
/// </summary>
public interface ITokenCache
{
    /// <summary>
    /// Stores a token entry in encrypted cache
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <param name="entry">Token cache entry to store</param>
    Task StoreTokenAsync(string profileName, TokenCacheEntry entry);

    /// <summary>
    /// Retrieves a cached token entry
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>Token cache entry or null if not found/expired</returns>
    Task<TokenCacheEntry?> GetTokenAsync(string profileName);

    /// <summary>
    /// Removes a cached token entry
    /// </summary>
    /// <param name="profileName">Profile name</param>
    Task RemoveTokenAsync(string profileName);

    /// <summary>
    /// Clears all cached tokens
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// Gets all profile names with cached tokens
    /// </summary>
    /// <returns>List of profile names</returns>
    Task<IReadOnlyList<string>> GetCachedProfilesAsync();
}
