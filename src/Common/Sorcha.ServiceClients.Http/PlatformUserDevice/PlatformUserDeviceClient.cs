// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.PlatformUserDevice;

/// <summary>
/// Default <see cref="IPlatformUserDeviceClient"/> — POSTs to the Tenant Service
/// internal endpoint behind a <c>RequireService</c> service principal token.
/// </summary>
public sealed class PlatformUserDeviceClient : IPlatformUserDeviceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<PlatformUserDeviceClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PlatformUserDeviceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<PlatformUserDeviceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:TenantService:Address"]
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<PlatformUserDeviceRegistrationResult> RegisterAsync(
        Guid platformUserId,
        string label,
        string devicePublicJwkThumbprint,
        string devicePublicJwkJson,
        string platform,
        string userAgent,
        DateTimeOffset delegationExpiresAt,
        string delegationCredentialJti,
        int statusListIndex,
        CancellationToken ct = default)
    {
        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Tenant Service (PlatformUserDevice)", ct);

        var body = new
        {
            platformUserId,
            label,
            devicePublicJwkThumbprint,
            devicePublicJwkJson,
            platform,
            userAgent,
            delegationExpiresAt,
            delegationCredentialJti,
            statusListIndex
        };

        var response = await _httpClient.PostAsJsonAsync(
            "api/internal/platform-user-devices", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PlatformUserDeviceRegistrationResult>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException(
            "Tenant Service returned an empty body for platform-user-device registration.");
    }
}
