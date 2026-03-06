// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// HttpClient implementation for encryption operation status endpoints.
/// </summary>
public class OperationStatusService : IOperationStatusService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OperationStatusService> _logger;

    public OperationStatusService(HttpClient httpClient, ILogger<OperationStatusService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EncryptionOperationViewModel?> GetStatusAsync(string operationId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/operations/{operationId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch operation status for {OperationId}: {StatusCode}", operationId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<EncryptionOperationViewModel>(JsonDefaults.Api, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error fetching operation status for {OperationId}", operationId);
            return null;
        }
    }
}
