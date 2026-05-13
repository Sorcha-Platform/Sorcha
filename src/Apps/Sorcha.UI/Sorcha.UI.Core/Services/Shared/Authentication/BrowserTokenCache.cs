// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Encryption;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Browser LocalStorage-based token cache with encryption
/// </summary>
public class BrowserTokenCache : ITokenCache
{
    private const string StorageKeyPrefix = "sorcha:tokens:";
    private readonly IJSRuntime _jsRuntime;
    private readonly IEncryptionProvider _encryptionProvider;

    public BrowserTokenCache(IJSRuntime jsRuntime, IEncryptionProvider encryptionProvider)
    {
        _jsRuntime = jsRuntime;
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc />
    public async Task StoreTokenAsync(string profileName, TokenCacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or empty", nameof(profileName));
        }

        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var json = JsonSerializer.Serialize(entry);
        var encrypted = await _encryptionProvider.EncryptAsync(json);
        var key = GetStorageKey(profileName);

        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, encrypted);
    }

    /// <inheritdoc />
    public async Task<TokenCacheEntry?> GetTokenAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or empty", nameof(profileName));
        }

        var key = GetStorageKey(profileName);

        try
        {
            var encrypted = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);

            if (string.IsNullOrEmpty(encrypted))
            {
                return null;
            }

            var json = await _encryptionProvider.DecryptAsync(encrypted);
            var entry = JsonSerializer.Deserialize<TokenCacheEntry>(json);

            if (entry == null)
            {
                return null;
            }

            // Remove expired tokens
            if (entry.IsExpired)
            {
                await RemoveTokenAsync(profileName);
                return null;
            }

            return entry;
        }
        catch
        {
            // If decryption or deserialization fails, remove the corrupted token
            await RemoveTokenAsync(profileName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RemoveTokenAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or empty", nameof(profileName));
        }

        var key = GetStorageKey(profileName);
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
    }

    /// <inheritdoc />
    public async Task ClearAllAsync()
    {
        var profiles = await GetCachedProfilesAsync();
        foreach (var profile in profiles)
        {
            await RemoveTokenAsync(profile);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCachedProfilesAsync()
    {
        var length = await _jsRuntime.InvokeAsync<int>("eval", "localStorage.length");
        var profiles = new List<string>();

        for (int i = 0; i < length; i++)
        {
            var key = await _jsRuntime.InvokeAsync<string>("localStorage.key", i);
            if (key?.StartsWith(StorageKeyPrefix) == true)
            {
                var profileName = key.Substring(StorageKeyPrefix.Length);
                profiles.Add(profileName);
            }
        }

        return profiles;
    }

    private static string GetStorageKey(string profileName)
    {
        return $"{StorageKeyPrefix}{profileName}";
    }
}
