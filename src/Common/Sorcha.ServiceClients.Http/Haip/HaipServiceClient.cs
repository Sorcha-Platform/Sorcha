// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Haip;

/// <summary>
/// HTTP client for the Sorcha HAIP Service. Used by the Blueprint Service
/// for credential offer creation and presentation request management.
/// </summary>
public class HaipServiceClient : IHaipServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<HaipServiceClient> _logger;

    public HaipServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        ILogger<HaipServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CreateOfferResult> CreateCredentialOfferAsync(
        string issuerWalletAddress,
        string tenantId,
        string credentialType,
        Dictionary<string, object> claims,
        List<string>? disclosablePaths = null,
        CancellationToken ct = default)
    {
        var request = new
        {
            issuerWalletAddress,
            tenantId,
            credentialType,
            claims,
            disclosablePaths
        };

        await SetAuthHeaderAsync(ct);
        var response = await _httpClient.PostAsJsonAsync("/api/v1/offers", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);

        return new CreateOfferResult(
            Guid.Parse(result.GetProperty("offerId").GetString()!),
            result.GetProperty("credentialOfferUri").GetString()!,
            result.GetProperty("preAuthorizedCode").GetString()!,
            result.GetProperty("expiresAt").GetDateTimeOffset());
    }

    /// <inheritdoc />
    public async Task<CreatePresentationRequestResult> CreatePresentationRequestAsync(
        string credentialType,
        List<string>? requiredClaims = null,
        List<string>? acceptedIssuers = null,
        string? declaredQueryJson = null,
        CancellationToken ct = default)
    {
        var request = new Dictionary<string, object?>
        {
            ["credentialType"] = credentialType,
            ["requiredClaims"] = requiredClaims,
            ["acceptedIssuers"] = acceptedIssuers,
        };

        // Feature 181 US2 — forward the pre-built DCQL query as an object so the verifier
        // deserializes it into DeclaredQuery and serves it verbatim.
        if (!string.IsNullOrWhiteSpace(declaredQueryJson))
        {
            request["declaredQuery"] = JsonSerializer.Deserialize<JsonElement>(declaredQueryJson);
        }

        await SetAuthHeaderAsync(ct);
        var response = await _httpClient.PostAsJsonAsync("/api/v1/verifier/requests", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);

        return new CreatePresentationRequestResult(
            Guid.Parse(result.GetProperty("requestId").GetString()!),
            result.GetProperty("authorizationRequestUri").GetString()!,
            result.GetProperty("requestUri").GetString()!,
            result.GetProperty("nonce").GetString()!,
            result.GetProperty("expiresAt").GetDateTimeOffset());
    }

    /// <inheritdoc />
    public async Task<OfferStatusResult?> GetOfferStatusAsync(Guid offerId, CancellationToken ct = default)
    {
        await SetAuthHeaderAsync(ct);
        var response = await _httpClient.GetAsync($"/api/v1/offers/{offerId}", ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);

        return new OfferStatusResult(
            Guid.Parse(result.GetProperty("offerId").GetString()!),
            result.GetProperty("credentialType").GetString()!,
            result.GetProperty("status").GetString()!,
            result.GetProperty("createdAt").GetDateTimeOffset(),
            result.GetProperty("expiresAt").GetDateTimeOffset());
    }

    /// <inheritdoc />
    public async Task<VerificationResultResponse?> GetVerificationResultAsync(
        Guid requestId, CancellationToken ct = default)
    {
        await SetAuthHeaderAsync(ct);
        var response = await _httpClient.GetAsync($"/api/v1/verifier/requests/{requestId}/result", ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(SorchaJson.Options, ct);

        var isValid = result.TryGetProperty("result", out var resultProp) &&
                     resultProp.ValueKind != JsonValueKind.Null
            ? resultProp.GetProperty("isValid").GetBoolean()
            : (bool?)null;

        return new VerificationResultResponse(
            Guid.Parse(result.GetProperty("requestId").GetString()!),
            result.GetProperty("state").GetString()!,
            isValid,
            null, // Verified claims deserialized on demand
            null);
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "HAIP Service", cancellationToken);
}
