// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HttpClient implementation for wallet access delegation endpoints.
/// </summary>
public class WalletAccessService : IWalletAccessService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WalletAccessService> _logger;

    public WalletAccessService(HttpClient httpClient, ILogger<WalletAccessService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WalletAccessGrantViewModel?> GrantAccessAsync(
        string walletAddress, GrantAccessFormModel form, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/access",
                new { form.Subject, form.AccessRight, form.Reason, form.ExpiresAt },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to grant access on wallet {Address}: {StatusCode}",
                    walletAddress, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<WalletAccessGrantViewModel>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error granting access on wallet {Address}", walletAddress);
            return null;
        }
    }

    public async Task<List<WalletAccessGrantViewModel>> ListAccessAsync(
        string walletAddress, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/access", ct);

            if (!response.IsSuccessStatusCode)
                return [];

            return await response.Content.ReadFromJsonAsync<List<WalletAccessGrantViewModel>>(cancellationToken: ct) ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to list access grants for wallet {Address}", walletAddress);
            return [];
        }
    }

    public async Task<bool> RevokeAccessAsync(
        string walletAddress, string subject, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"/api/v1/wallets/{walletAddress}/access/{subject}", ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to revoke access for {Subject} on wallet {Address}",
                subject, walletAddress);
            return false;
        }
    }

    public async Task<AccessCheckResult?> CheckAccessAsync(
        string walletAddress, string subject, string requiredRight, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/access/{subject}/check?requiredRight={requiredRight}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<AccessCheckResult>(cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to check access for {Subject} on wallet {Address}",
                subject, walletAddress);
            return null;
        }
    }
}
