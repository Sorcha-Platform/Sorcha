// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

public class PresentationAdminService : IPresentationAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PresentationAdminService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PresentationAdminService(HttpClient httpClient, ILogger<PresentationAdminService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PresentationRequestResultViewModel?> CreatePresentationRequestAsync(
        CreatePresentationRequestViewModel request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/presentations/request", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to create presentation request: {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PresentationRequestResultViewModel>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error creating presentation request");
            return null;
        }
    }

    public async Task<PresentationRequestResultViewModel?> GetPresentationResultAsync(
        string requestId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/presentations/{requestId}/result", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Presentation request {RequestId} not found", requestId);
                return null;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                return new PresentationRequestResultViewModel
                {
                    RequestId = requestId,
                    Status = "Expired"
                };
            }

            // 202 Accepted = still pending
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                return new PresentationRequestResultViewModel
                {
                    RequestId = requestId,
                    Status = "Pending"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get presentation result {RequestId}: {StatusCode}", requestId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PresentationRequestResultViewModel>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error getting presentation result {RequestId}", requestId);
            return null;
        }
    }
}
