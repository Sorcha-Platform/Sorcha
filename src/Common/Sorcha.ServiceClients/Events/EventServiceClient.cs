// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Events.Models;

namespace Sorcha.ServiceClients.Events;

/// <summary>
/// HTTP client for creating activity events on the Tenant Service.
/// </summary>
public class EventServiceClient : IEventServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<EventServiceClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public EventServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<EventServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:TenantService:Address"]
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }

        _logger.LogInformation("EventServiceClient initialized (Address: {Address})", serviceAddress);
    }

    /// <inheritdoc/>
    public async Task<bool> CreateEventAsync(CreateActivityEventRequest request, CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Creating activity event: {EventType} from {Source}",
                request.EventType, request.SourceService);

            var response = await _httpClient.PostAsJsonAsync("api/events", request, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Activity event created: {EventType}", request.EventType);
                return true;
            }

            _logger.LogWarning(
                "Failed to create activity event {EventType}: {StatusCode}",
                request.EventType, response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error creating activity event {EventType}", request.EventType);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create activity event {EventType}", request.EventType);
            return false;
        }
    }
}
