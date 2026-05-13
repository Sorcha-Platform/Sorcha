// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client service for the TOTP two-factor authentication REST API.
/// </summary>
public interface ITotpClientService
{
    /// <summary>
    /// Initiates TOTP setup — returns the secret, QR URI, and backup codes.
    /// </summary>
    Task<TotpSetupResponse?> SetupTotpAsync();

    /// <summary>
    /// Verifies the initial TOTP setup with a code from the authenticator app.
    /// </summary>
    /// <param name="code">The 6-digit code from the authenticator app.</param>
    /// <returns>True if the code was valid and TOTP is now active.</returns>
    Task<bool> VerifySetupAsync(string code);

    /// <summary>
    /// Validates a TOTP code during login or sensitive operations.
    /// </summary>
    /// <param name="code">The 6-digit code from the authenticator app.</param>
    /// <returns>True if the code is valid.</returns>
    Task<bool> ValidateCodeAsync(string code);

    /// <summary>
    /// Disables TOTP two-factor authentication for the current user.
    /// </summary>
    /// <returns>True if TOTP was successfully disabled.</returns>
    Task<bool> DisableTotpAsync();

    /// <summary>
    /// Gets the current TOTP enrollment status for the user.
    /// </summary>
    Task<TotpStatusResponse> GetStatusAsync();
}

/// <summary>
/// Implementation of the TOTP client service calling the Tenant Service API.
/// </summary>
public class TotpClientService : ITotpClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TotpClientService> _logger;

    public TotpClientService(HttpClient httpClient, ILogger<TotpClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TotpSetupResponse?> SetupTotpAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("/api/totp/setup", null);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TotpSetupResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate TOTP setup");
            return null;
        }
    }

    public async Task<bool> VerifySetupAsync(string code)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/totp/verify-setup",
                new TotpVerifyRequest { Code = code });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify TOTP setup");
            return false;
        }
    }

    public async Task<bool> ValidateCodeAsync(string code)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/totp/validate",
                new TotpVerifyRequest { Code = code });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate TOTP code");
            return false;
        }
    }

    public async Task<bool> DisableTotpAsync()
    {
        try
        {
            var response = await _httpClient.DeleteAsync("/api/totp");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable TOTP");
            return false;
        }
    }

    public async Task<TotpStatusResponse> GetStatusAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<TotpStatusResponse>("/api/totp/status");
            return response ?? new TotpStatusResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TOTP status");
            return new TotpStatusResponse();
        }
    }
}
