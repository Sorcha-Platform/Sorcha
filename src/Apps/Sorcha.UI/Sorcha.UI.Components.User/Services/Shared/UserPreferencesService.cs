// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client service for the user preferences REST API.
/// </summary>
public interface IUserPreferencesService
{
    Task<UserPreferencesDto> GetUserPreferencesAsync();
    Task<UserPreferencesDto> UpdateUserPreferencesAsync(UpdateUserPreferencesRequest request);
    Task<string?> GetDefaultWalletAsync();
    Task SetDefaultWalletAsync(string walletAddress);
    Task ClearDefaultWalletAsync();
}

public class UserPreferencesService : IUserPreferencesService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserPreferencesService> _logger;

    public UserPreferencesService(HttpClient httpClient, ILogger<UserPreferencesService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserPreferencesDto> GetUserPreferencesAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<UserPreferencesDto>("/api/preferences");
            return response ?? new UserPreferencesDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user preferences");
            return new UserPreferencesDto();
        }
    }

    public async Task<UserPreferencesDto> UpdateUserPreferencesAsync(UpdateUserPreferencesRequest request)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync("/api/preferences", request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<UserPreferencesDto>();
            return result ?? new UserPreferencesDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user preferences");
            return new UserPreferencesDto();
        }
    }

    public async Task<string?> GetDefaultWalletAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<DefaultWalletResponse>("/api/preferences/default-wallet");
            return response?.DefaultWalletAddress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get default wallet");
            return null;
        }
    }

    public async Task SetDefaultWalletAsync(string walletAddress)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync("/api/preferences/default-wallet",
                new SetDefaultWalletRequest { WalletAddress = walletAddress });
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set default wallet");
        }
    }

    public async Task ClearDefaultWalletAsync()
    {
        try
        {
            var response = await _httpClient.DeleteAsync("/api/preferences/default-wallet");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear default wallet");
        }
    }
}
