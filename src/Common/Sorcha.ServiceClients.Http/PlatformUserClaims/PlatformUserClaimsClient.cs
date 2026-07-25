// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Configuration;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.PlatformUserClaims;

/// <summary>
/// Default <see cref="IPlatformUserClaimsClient"/> — GETs the Tenant Service internal live-claims
/// endpoint behind a <c>RequireService</c> service principal token.
/// </summary>
public sealed class PlatformUserClaimsClient : IPlatformUserClaimsClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<PlatformUserClaimsClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Initialises a new instance.</summary>
    public PlatformUserClaimsClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<PlatformUserClaimsClient> logger)
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
    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        Guid platformUserId,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Tenant Service (PlatformUserClaims)", cancellationToken);

        var query = Uri.EscapeDataString(string.Join(',', names));
        var url = $"api/internal/platform-users/{platformUserId}/claims?names={query}";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Never degrade to a token value or a default here — that is the #1264 bug.
            throw new PlatformUserClaimsUnavailableException(
                $"Could not reach the Tenant Service to resolve live claims for platform user {platformUserId}.",
                ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PlatformUserClaimsUnavailableException(
                $"Platform user {platformUserId} does not exist, so live claims cannot be resolved.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PlatformUserClaimsUnavailableException(
                $"Tenant Service returned {(int)response.StatusCode} resolving live claims for "
                + $"platform user {platformUserId}.");
        }

        var resolved = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(
            JsonOptions, cancellationToken);

        if (resolved is null)
        {
            throw new PlatformUserClaimsUnavailableException(
                $"Tenant Service returned an unreadable live-claims body for platform user {platformUserId}.");
        }

        return resolved;
    }
}
