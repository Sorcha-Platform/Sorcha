// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// HttpClient implementation for admin-level issued credential management.
/// </summary>
public class IssuedCredentialService : IIssuedCredentialService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IssuedCredentialService> _logger;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    public IssuedCredentialService(HttpClient httpClient, ILogger<IssuedCredentialService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<List<IssuedCredentialListItem>> GetIssuedCredentialsAsync(
        string? statusFilter = null, string? typeFilter = null)
    {
        try
        {
            var url = "/api/v1/credentials/issued";
            var queryParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(statusFilter))
                queryParts.Add($"status={Uri.EscapeDataString(statusFilter)}");

            if (!string.IsNullOrWhiteSpace(typeFilter))
                queryParts.Add($"type={Uri.EscapeDataString(typeFilter)}");

            if (queryParts.Count > 0)
                url = $"{url}?{string.Join("&", queryParts)}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch issued credentials: {StatusCode}", response.StatusCode);
                return [];
            }

            var items = await response.Content
                .ReadFromJsonAsync<List<IssuedCredentialListItem>>(JsonOptions);

            return items ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error fetching issued credentials");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> SuspendCredentialAsync(string credentialId, string reason)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{Uri.EscapeDataString(credentialId)}/suspend",
            new { reason },
            credentialId, "suspend");
    }

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> RevokeCredentialAsync(string credentialId, string reason)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{Uri.EscapeDataString(credentialId)}/revoke",
            new { reason },
            credentialId, "revoke");
    }

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> ReinstateCredentialAsync(string credentialId)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{Uri.EscapeDataString(credentialId)}/reinstate",
            new { },
            credentialId, "reinstate");
    }

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> RefreshCredentialAsync(string credentialId)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{Uri.EscapeDataString(credentialId)}/refresh",
            new { },
            credentialId, "refresh");
    }

    private async Task<CredentialOperationResult> ExecuteLifecycleOperationAsync(
        string url, object payload, string credentialId, string operation)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, JsonOptions);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content
                    .ReadFromJsonAsync<CredentialLifecycleResult>(JsonOptions);

                return result != null
                    ? CredentialOperationResult.Ok(result)
                    : CredentialOperationResult.Fail(CredentialErrorType.ServerError, "Empty response from server");
            }

            _logger.LogWarning("Failed to {Operation} credential {CredentialId}: {StatusCode}",
                operation, credentialId, response.StatusCode);

            return CredentialOperationResult.FromStatusCode((int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during {Operation} for credential {CredentialId}",
                operation, credentialId);
            return CredentialOperationResult.NetworkError(ex.Message);
        }
    }
}
