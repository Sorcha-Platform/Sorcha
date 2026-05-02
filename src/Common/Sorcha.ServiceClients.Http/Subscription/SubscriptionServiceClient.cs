// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Subscription;

/// <summary>
/// Service client for querying register subscriptions from the Tenant Service.
/// Used by Register Service to determine which registers an organisation can access.
/// </summary>
public interface ISubscriptionServiceClient
{
    /// <summary>
    /// Gets the register IDs that an organisation has active subscriptions to.
    /// </summary>
    /// <param name="orgId">Organisation identifier from JWT org_id claim</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of register IDs the org can access. Returns empty list on failure (fail-closed).</returns>
    Task<List<string>> GetActiveRegisterIdsForOrgAsync(Guid orgId, CancellationToken cancellationToken = default);
}

/// <summary>
/// HTTP client implementation for querying register subscriptions from Tenant Service.
/// Implements fail-closed pattern: returns empty list on any error.
/// </summary>
public class SubscriptionServiceClient : ISubscriptionServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<SubscriptionServiceClient> _logger;

    /// <summary>
    /// Initializes the subscription service client
    /// </summary>
    public SubscriptionServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<SubscriptionServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var baseAddress = configuration["ServiceClients:TenantService:Address"]
            ?? configuration["Services:TenantService:BaseAddress"]
            ?? "http://tenant-service";

        _httpClient.BaseAddress = new Uri(baseAddress);

        _logger.LogInformation("SubscriptionServiceClient initialized (Address: {Address})", baseAddress);
    }

    /// <inheritdoc />
    public async Task<List<string>> GetActiveRegisterIdsForOrgAsync(
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Resolving active register subscriptions for org {OrgId}", orgId);

            await SetAuthHeaderAsync(cancellationToken);

            // Fetch all subscriptions for the org — the endpoint returns paginated results
            var registerIds = new List<string>();
            var page = 1;
            const int pageSize = 100;
            int totalCount;

            do
            {
                var response = await _httpClient.GetAsync(
                    $"/api/organizations/{orgId}/register-subscriptions?page={page}&pageSize={pageSize}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Failed to resolve subscriptions for org {OrgId}: {StatusCode} — fail-closed, returning empty list",
                        orgId, response.StatusCode);
                    return [];
                }

                var result = await response.Content
                    .ReadFromJsonAsync<SubscriptionListResponse>(cancellationToken);

                if (result?.Items is null)
                {
                    _logger.LogWarning(
                        "Null response from subscription query for org {OrgId} — fail-closed, returning empty list",
                        orgId);
                    return [];
                }

                // Only include Active subscriptions
                foreach (var item in result.Items)
                {
                    if (string.Equals(item.Status, "Active", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(item.RegisterId))
                    {
                        registerIds.Add(item.RegisterId);
                    }
                }

                totalCount = result.TotalCount;
                page++;
            }
            while (registerIds.Count < totalCount && page <= 100); // Safety cap at 100 pages

            _logger.LogDebug(
                "Resolved {Count} active register subscriptions for org {OrgId}",
                registerIds.Count, orgId);

            return registerIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error resolving register subscriptions for org {OrgId} — fail-closed, returning empty list",
                orgId);
            return [];
        }
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Subscription Service", cancellationToken);

    /// <summary>
    /// Response model for paginated subscription list
    /// </summary>
    private record SubscriptionListResponse
    {
        /// <summary>Collection of items in the result set.</summary>
        [JsonPropertyName("items")]
        public List<SubscriptionItem>? Items { get; init; }

        /// <summary>Total number of items available.</summary>
        [JsonPropertyName("total_count")]
        public int TotalCount { get; init; }

        /// <summary>One-based page number for paginated results.</summary>
        [JsonPropertyName("page")]
        public int Page { get; init; }

        /// <summary>Number of items per page.</summary>
        [JsonPropertyName("page_size")]
        public int PageSize { get; init; }
    }

    /// <summary>
    /// Individual subscription item
    /// </summary>
    private record SubscriptionItem
    {
        /// <summary>Identifier of the register.</summary>
        [JsonPropertyName("register_id")]
        public string? RegisterId { get; init; }

        /// <summary>Current status of the resource.</summary>
        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
