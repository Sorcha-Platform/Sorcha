// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// HTTP client service for managing organization invitations via the Tenant Service API.
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
    public async Task<InvitationListResult> GetInvitationsAsync(
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
            var result = await _httpClient.GetFromJsonAsync<InvitationListResult>(url, ct);
            return result ?? new InvitationListResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get invitations for organization {OrganizationId}", organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InvitationDto?> GetInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations/{Uri.EscapeDataString(invitationId.ToString())}";

        try
        {
            return await _httpClient.GetFromJsonAsync<InvitationDto>(url, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get invitation {InvitationId} for organization {OrganizationId}",
                invitationId, organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<InvitationDto> CreateInvitationAsync(
        Guid organizationId,
        CreateInvitationRequest request,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations";

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<InvitationDto>(ct);
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

    /// <inheritdoc />
    public async Task<bool> ResendInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken ct)
    {
        var url = $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/invitations/{Uri.EscapeDataString(invitationId.ToString())}/resend";

        try
        {
            var response = await _httpClient.PostAsync(url, null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to resend invitation {InvitationId} for organization {OrganizationId}",
                invitationId, organizationId);
            return false;
        }
    }
}
