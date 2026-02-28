// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Configuration;

namespace Sorcha.UI.Core.Services.Configuration;

/// <summary>
/// Service for managing user profiles and UI configuration.
/// Stores configuration in browser LocalStorage.
/// </summary>
public class ConfigurationService : IConfigurationService, IDisposable
{
    private const string ProfilesStorageKey = "sorcha:profiles";
    private const string UiConfigStorageKey = "sorcha:ui-config";
    private const string ActiveProfileKey = "sorcha:active-profile";

    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <inheritdoc />
    public event EventHandler<ProfileChangedEventArgs>? ActiveProfileChanged;

    public ConfigurationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Profile>> GetProfilesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await GetProfilesInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal method to get profiles without acquiring the semaphore lock.
    /// Used when the lock is already held by the calling method.
    /// </summary>
    private async Task<List<Profile>> GetProfilesInternalAsync()
    {
        var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ProfilesStorageKey);

        if (string.IsNullOrEmpty(json))
        {
            // Initialize default profiles directly without re-acquiring lock
            await InitializeDefaultProfilesInternalAsync();
            json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ProfilesStorageKey);
        }

        return JsonSerializer.Deserialize<List<Profile>>(json!) ?? new List<Profile>();
    }

    /// <inheritdoc />
    public async Task<Profile?> GetProfileAsync(string name)
    {
        var profiles = await GetProfilesAsync();
        return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task SaveProfileAsync(Profile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (!profile.IsValid())
        {
            throw new ArgumentException("Invalid profile configuration", nameof(profile));
        }

        await _lock.WaitAsync();
        try
        {
            var profiles = await GetProfilesInternalAsync();
            var existingIndex = profiles.FindIndex(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                profiles[existingIndex] = profile.WithUpdatedTimestamp();
            }
            else
            {
                profiles.Add(profile);
            }

            var json = JsonSerializer.Serialize(profiles);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ProfilesStorageKey, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProfileAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name cannot be null or empty", nameof(name));
        }

        await _lock.WaitAsync();
        try
        {
            var profiles = await GetProfilesInternalAsync();
            var profile = profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return false;
            }

            if (profile.IsSystemProfile)
            {
                return false; // Cannot delete system profiles
            }

            profiles.Remove(profile);

            var json = JsonSerializer.Serialize(profiles);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ProfilesStorageKey, json);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> GetActiveProfileNameAsync()
    {
        var name = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ActiveProfileKey);
        return name ?? ProfileDefaults.DefaultActiveProfile;
    }

    /// <inheritdoc />
    public async Task<Profile?> GetActiveProfileAsync()
    {
        var profileName = await GetActiveProfileNameAsync();
        return await GetProfileAsync(profileName);
    }

    /// <inheritdoc />
    public async Task SetActiveProfileAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            throw new ArgumentException("Profile name cannot be null or empty", nameof(profileName));
        }

        var profile = await GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' not found");
        }

        // Get previous profile name before changing
        var previousProfileName = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", ActiveProfileKey);

        // Save the new active profile
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ActiveProfileKey, profileName);

        // Raise event to notify listeners
        ActiveProfileChanged?.Invoke(this, new ProfileChangedEventArgs
        {
            PreviousProfileName = previousProfileName,
            NewProfile = profile
        });
    }

    /// <inheritdoc />
    public async Task<UiConfiguration> GetUiConfigurationAsync()
    {
        var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", UiConfigStorageKey);

        if (string.IsNullOrEmpty(json))
        {
            return UiConfiguration.Default();
        }

        return JsonSerializer.Deserialize<UiConfiguration>(json) ?? UiConfiguration.Default();
    }

    /// <inheritdoc />
    public async Task SaveUiConfigurationAsync(UiConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var json = JsonSerializer.Serialize(configuration);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", UiConfigStorageKey, json);
    }

    /// <inheritdoc />
    public async Task InitializeDefaultProfilesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await InitializeDefaultProfilesInternalAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Internal method to initialize default profiles without acquiring the semaphore lock.
    /// Used when the lock is already held by the calling method.
    /// </summary>
    private async Task InitializeDefaultProfilesInternalAsync()
    {
        var profiles = ProfileDefaults.GetDefaultProfiles();
        var json = JsonSerializer.Serialize(profiles);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ProfilesStorageKey, json);

        // Set the default active profile
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", ActiveProfileKey, ProfileDefaults.DefaultActiveProfile);
    }

    /// <summary>
    /// Disposes the semaphore used for thread-safe storage access.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
