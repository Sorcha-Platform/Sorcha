// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side audit service implementation.
/// Reads the audit log the Tenant Service writes server-side at the mutating endpoints (#1655) —
/// this service has no write path of its own. See <see cref="IAuditService"/>.
/// </summary>
public class AuditService : IAuditService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        HttpClient httpClient,
        ILogger<AuditService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AuditQueryResult> QueryAuditEventsAsync(
        Guid organizationId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        string? eventType = null,
        Guid? userId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orgId = Uri.EscapeDataString(organizationId.ToString());
            var url = $"/api/organizations/{orgId}/audit?page={page}&pageSize={pageSize}";

            if (startDate.HasValue)
            {
                url += $"&startDate={Uri.EscapeDataString(startDate.Value.ToString("o"))}";
            }

            if (endDate.HasValue)
            {
                url += $"&endDate={Uri.EscapeDataString(endDate.Value.ToString("o"))}";
            }

            if (!string.IsNullOrEmpty(eventType))
            {
                url += $"&eventType={Uri.EscapeDataString(eventType)}";
            }

            if (userId.HasValue)
            {
                url += $"&userId={Uri.EscapeDataString(userId.Value.ToString())}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AuditQueryResult>(JsonDefaults.Api, cancellationToken);
            return result ?? new AuditQueryResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to query audit events for organization {OrganizationId}", organizationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying audit events for organization {OrganizationId}", organizationId);
            throw;
        }
    }

    public async Task<AuditRetentionDto> GetRetentionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orgId = Uri.EscapeDataString(organizationId.ToString());
            var url = $"/api/organizations/{orgId}/audit/retention";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AuditRetentionDto>(JsonDefaults.Api, cancellationToken);
            return result ?? new AuditRetentionDto { RetentionMonths = 12 };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get audit retention for organization {OrganizationId}", organizationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting audit retention for organization {OrganizationId}", organizationId);
            throw;
        }
    }

    public async Task<bool> UpdateRetentionAsync(
        Guid organizationId,
        int retentionMonths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var orgId = Uri.EscapeDataString(organizationId.ToString());
            var url = $"/api/organizations/{orgId}/audit/retention";

            var payload = new AuditRetentionDto { RetentionMonths = retentionMonths };
            var response = await _httpClient.PutAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to update audit retention for organization {OrganizationId}: {StatusCode}",
                    organizationId, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to update audit retention for organization {OrganizationId}", organizationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating audit retention for organization {OrganizationId}", organizationId);
            return false;
        }
    }
}
