// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side audit service implementation.
/// Logs audit events to the backend Tenant Service.
/// </summary>
public class AuditService : IAuditService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuditService> _logger;
    private const string AuditEndpoint = "/api/audit";

    public AuditService(
        HttpClient httpClient,
        ILogger<AuditService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task LogAsync(
        AuditEventType eventType,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default)
    {
        await SendAuditEventAsync(eventType, null, null, details, cancellationToken);
    }

    public async Task LogOrganizationEventAsync(
        AuditEventType eventType,
        Guid organizationId,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default)
    {
        var enrichedDetails = details ?? new Dictionary<string, object>();
        enrichedDetails["organizationId"] = organizationId;

        await SendAuditEventAsync(eventType, organizationId, null, enrichedDetails, cancellationToken);
    }

    public async Task LogUserEventAsync(
        AuditEventType eventType,
        Guid organizationId,
        Guid userId,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default)
    {
        var enrichedDetails = details ?? new Dictionary<string, object>();
        enrichedDetails["organizationId"] = organizationId;
        enrichedDetails["targetUserId"] = userId;

        await SendAuditEventAsync(eventType, organizationId, userId, enrichedDetails, cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<AuditQueryResult>(cancellationToken: cancellationToken);
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

            var result = await response.Content.ReadFromJsonAsync<AuditRetentionDto>(cancellationToken: cancellationToken);
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

    private async Task SendAuditEventAsync(
        AuditEventType eventType,
        Guid? organizationId,
        Guid? userId,
        Dictionary<string, object>? details,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new AuditEventRequest
            {
                EventType = eventType.ToString(),
                OrganizationId = organizationId,
                TargetUserId = userId,
                Details = details,
                Timestamp = DateTimeOffset.UtcNow
            };

            // Fire and forget - don't block on audit logging
            var response = await _httpClient.PostAsJsonAsync(AuditEndpoint, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to send audit event {EventType}: {StatusCode}",
                    eventType, response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            // Log locally but don't throw - audit logging should not break functionality
            _logger.LogWarning(ex, "Failed to send audit event {EventType}", eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending audit event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Request payload for audit events.
    /// </summary>
    private record AuditEventRequest
    {
        public required string EventType { get; init; }
        public Guid? OrganizationId { get; init; }
        public Guid? TargetUserId { get; init; }
        public Dictionary<string, object>? Details { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
