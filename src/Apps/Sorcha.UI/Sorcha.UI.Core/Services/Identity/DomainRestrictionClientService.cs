// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Implementation of domain restriction client service.
/// Communicates with the Tenant Service domain restriction API.
/// </summary>
public class DomainRestrictionClientService : IDomainRestrictionClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DomainRestrictionClientService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DomainRestrictionClientService"/>.
    /// </summary>
    public DomainRestrictionClientService(
        HttpClient httpClient,
        ILogger<DomainRestrictionClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private static string BaseUrl(Guid organizationId) =>
        $"/api/organizations/{Uri.EscapeDataString(organizationId.ToString())}/domain-restrictions";

    /// <inheritdoc />
    public async Task<DomainRestrictionsDto> GetRestrictionsAsync(
        Guid organizationId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _httpClient.GetFromJsonAsync<DomainRestrictionsDto>(
                BaseUrl(organizationId), ct);

            return result ?? new DomainRestrictionsDto();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get domain restrictions for organization {OrganizationId}",
                organizationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRestrictionsAsync(
        Guid organizationId,
        string[] allowedDomains,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(
                BaseUrl(organizationId),
                new { AllowedDomains = allowedDomains },
                ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to update domain restrictions for organization {OrganizationId}",
                organizationId);
            return false;
        }
    }
}
