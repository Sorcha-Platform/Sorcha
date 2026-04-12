// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Minimal service-to-service client for the Tenant Service's internal
/// owner-subscription endpoint. Called from <c>RegisterCreationOrchestrator.FinalizeAsync</c>
/// after owner attestations have been cryptographically verified.
/// </summary>
public interface ITenantSubscriptionClient
{
    /// <summary>
    /// Create an owner subscription for the supplied organisation. Idempotent —
    /// a 409 response from the tenant service is treated as success.
    /// </summary>
    Task<bool> CreateOwnerSubscriptionAsync(
        Guid organizationId,
        string registerId,
        string? registerName,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HttpClient-backed implementation. Uses the shared
/// <see cref="IServiceAuthClient"/> to acquire a service JWT (register-service
/// client credentials are seeded into the Tenant Service database by
/// <c>DatabaseInitializer</c>, so no deploy-time provisioning is required).
/// </summary>
public class TenantSubscriptionClient : ITenantSubscriptionClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<TenantSubscriptionClient> _logger;

    public TenantSubscriptionClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        ILogger<TenantSubscriptionClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> CreateOwnerSubscriptionAsync(
        Guid organizationId,
        string registerId,
        string? registerName,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            _logger.LogDebug(
                "Skipping owner subscription for register {RegisterId}: no organizationId (caller had no org_id claim).",
                registerId);
            return false;
        }

        var token = await _serviceAuth.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning(
                "Could not acquire service token for owner subscription on register {RegisterId} — subscription skipped.",
                registerId);
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/internal/register-owner-subscriptions")
        {
            Content = JsonContent.Create(new InternalOwnerSubscriptionRequestBody
            {
                OrganizationId = organizationId,
                RegisterId = registerId,
                RegisterName = registerName,
                OwnerUserId = ownerUserId
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Owner subscription created for org {OrgId} on register {RegisterId}",
                    organizationId, registerId);
                return true;
            }

            // 409 means somebody already subscribed this org (e.g. retry) — treat as success.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation(
                    "Owner subscription for org {OrgId} on register {RegisterId} already exists — treating as success.",
                    organizationId, registerId);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Owner subscription call failed for org {OrgId} on register {RegisterId}: HTTP {StatusCode} — {Body}",
                organizationId, registerId, (int)response.StatusCode, body);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Owner subscription call threw HttpRequestException for org {OrgId} on register {RegisterId}",
                organizationId, registerId);
            return false;
        }
    }

    /// <summary>
    /// Wire-level shape matching Tenant Service's InternalOwnerSubscriptionRequest.
    /// Kept as a private DTO to avoid cross-service coupling on tenant-service types.
    /// </summary>
    private sealed record InternalOwnerSubscriptionRequestBody
    {
        [JsonPropertyName("organizationId")]
        public required Guid OrganizationId { get; init; }

        [JsonPropertyName("registerId")]
        public required string RegisterId { get; init; }

        [JsonPropertyName("registerName")]
        public string? RegisterName { get; init; }

        [JsonPropertyName("ownerUserId")]
        public required Guid OwnerUserId { get; init; }
    }
}
