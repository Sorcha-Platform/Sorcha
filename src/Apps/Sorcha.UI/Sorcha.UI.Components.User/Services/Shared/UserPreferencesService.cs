// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
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
            var response = await _httpClient.GetFromJsonAsync<UserPreferencesDto>("/api/preferences", JsonDefaults.Api);
            return response ?? new UserPreferencesDto();
        }
        catch (Exception ex)
        {
            LogPreferenceFailure(ex, "get user preferences");
            return new UserPreferencesDto();
        }
    }

    // A signed-out (or expired-session) caller gets a 401/403 here. That is an
    // expected, fully-handled outcome — we return defaults — so it must not log
    // at Error and alarm the browser console. Genuine failures (5xx, network)
    // still surface at Error. See also TenantHubConnection.StartAsync.
    private void LogPreferenceFailure(Exception ex, string action)
    {
        if (IsExpectedAuthFailure(ex))
        {
            _logger.LogDebug(ex, "Skipping {Action}: no valid session (auth failure).", action);
        }
        else
        {
            _logger.LogError(ex, "Failed to {Action}", action);
        }
    }

    private static bool IsExpectedAuthFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
                return true;
        }
        return false;
    }

    public async Task<UserPreferencesDto> UpdateUserPreferencesAsync(UpdateUserPreferencesRequest request)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync("/api/preferences", request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<UserPreferencesDto>(JsonDefaults.Api);
            return result ?? new UserPreferencesDto();
        }
        catch (Exception ex)
        {
            LogPreferenceFailure(ex, "update user preferences");
            return new UserPreferencesDto();
        }
    }

    public async Task<string?> GetDefaultWalletAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<DefaultWalletResponse>("/api/preferences/default-wallet", JsonDefaults.Api);
            return response?.DefaultWalletAddress;
        }
        catch (Exception ex)
        {
            LogPreferenceFailure(ex, "get default wallet");
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
            LogPreferenceFailure(ex, "set default wallet");
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
            LogPreferenceFailure(ex, "clear default wallet");
        }
    }
}
