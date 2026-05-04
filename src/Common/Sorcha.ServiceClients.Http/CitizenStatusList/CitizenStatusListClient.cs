// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.CitizenStatusList;

/// <summary>
/// Default <see cref="ICitizenStatusListClient"/> — POSTs to the Wallet Service
/// internal endpoint behind a <c>RequireService</c> service principal token.
/// </summary>
public sealed class CitizenStatusListClient : ICitizenStatusListClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<CitizenStatusListClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Initialises a new instance of the <see cref="CitizenStatusListClient"/> class.</summary>
    public CitizenStatusListClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<CitizenStatusListClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:WalletService:Address"]
            ?? "https+http://wallet-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default)
    {
        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Wallet Service (CitizenStatusList)", ct);

        var body = new
        {
            organizationId,
            listId,
            indexInList,
            deviceId,
            platformUserId
        };

        var response = await _httpClient.PostAsJsonAsync(
            "api/internal/citizen-status-list/revoke", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }
}
