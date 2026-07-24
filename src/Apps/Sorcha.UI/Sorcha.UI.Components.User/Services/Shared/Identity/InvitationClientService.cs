// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// HTTP client service for managing organisation user invitations via the Tenant Service API.
/// </summary>
public class InvitationClientService : IInvitationClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InvitationClientService> _logger;

    public InvitationClientService(HttpClient httpClient, ILogger<InvitationClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrgInvitationDto>> GetInvitationsAsync(
        Guid organizationId,
        string? status,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations";

        if (!string.IsNullOrEmpty(status))
        {
            url += $"?status={Uri.EscapeDataString(status)}";
        }

        try
        {
            // The endpoint returns a bare JSON array (Produces<List<OrgInvitationResponse>>).
            var result = await _httpClient.GetFromJsonAsync<List<OrgInvitationDto>>(url, JsonDefaults.Api, ct);
            return result ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get invitations for organization {OrganizationId}", organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OrgInvitationDto> CreateInvitationAsync(
        Guid organizationId,
        CreateOrgInvitationRequest request,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations";

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, JsonDefaults.Api, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OrgInvitationDto>(JsonDefaults.Api, ct);
            return result ?? throw new InvalidOperationException("Failed to deserialize invitation response.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create invitation for organization {OrganizationId}", organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations/{Uri.EscapeDataString(invitationId.ToString())}/revoke";

        try
        {
            var response = await _httpClient.PostAsync(url, null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to revoke invitation {InvitationId} for organization {OrganizationId}",
                invitationId, organizationId);
            return false;
        }
    }
}
