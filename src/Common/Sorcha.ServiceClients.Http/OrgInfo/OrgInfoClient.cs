// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.ServiceClients.OrgInfo;

/// <summary>
/// Default <see cref="IOrgInfoClient"/> — GETs the Tenant Service internal endpoint
/// <c>/api/internal/orgs/{orgId}/wallet-address</c> behind a <c>RequireService</c>
/// service-principal token (Feature 149).
/// </summary>
public sealed class OrgInfoClient : IOrgInfoClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<OrgInfoClient> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public OrgInfoClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<OrgInfoClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<string?> ResolveCanonicalWalletAddressAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        try
        {
            await ServiceClientAuthHelper.SetAuthHeaderAsync(
                _httpClient, _serviceAuth, _logger, "Tenant Service (OrgInfo)", ct)
                .ConfigureAwait(false);

            using var resp = await _httpClient
                .GetAsync($"api/internal/orgs/{organizationId}/wallet-address", ct)
                .ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // Org unknown or no provisioned wallet — caller fails issuance closed.
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Org wallet-address resolve returned {Status} for org {OrgId}",
                    resp.StatusCode, organizationId);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("walletAddress", out var addrEl)
                && addrEl.ValueKind == JsonValueKind.String)
            {
                var addr = addrEl.GetString();
                return string.IsNullOrWhiteSpace(addr) ? null : addr;
            }

            _logger.LogWarning(
                "Org wallet-address document for {OrgId} had no string 'walletAddress' field", organizationId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Org wallet-address resolve failed for org {OrgId}", organizationId);
            return null;
        }
    }
}
