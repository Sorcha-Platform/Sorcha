// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.ServiceClients.Invitation;

/// <summary>
/// HTTP client for the Tenant Service's private register invitation endpoints.
/// Auth is expected on the supplied <see cref="HttpClient"/>; construct via
/// <c>IHttpClientFactory</c> with a bearer-attaching DelegatingHandler (Blazor UI)
/// or with a pre-authorised client for server-side callers.
/// </summary>
public sealed class RegisterInvitationServiceClient : IRegisterInvitationServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RegisterInvitationServiceClient> _logger;

    public RegisterInvitationServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<RegisterInvitationServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress is null)
        {
            var baseAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
                ?? throw new InvalidOperationException(
                    "No ServiceClients:TenantService:Address configured for RegisterInvitationServiceClient.");
            _httpClient.BaseAddress = new Uri(baseAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<InvitationCreatedResponse> CreateAsync(
        Guid sourceOrgId,
        CreateRegisterInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _httpClient.PostAsJsonAsync(
            $"api/organizations/{sourceOrgId}/register-invitations",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, "create invitation", cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<InvitationCreatedResponse>(
                   SorchaJson.Options, cancellationToken))
               ?? throw new InvalidOperationException("Empty body on create-invitation success.");
    }

    /// <inheritdoc />
    public async Task<InvitationAcceptedResponse> AcceptAsync(
        Guid targetOrgId,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _httpClient.PostAsJsonAsync(
            $"api/organizations/{targetOrgId}/register-invitations/accept",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, "accept invitation", cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<InvitationAcceptedResponse>(
                   SorchaJson.Options, cancellationToken))
               ?? throw new InvalidOperationException("Empty body on accept-invitation success.");
    }

    /// <inheritdoc />
    public async Task<InvitationListResponse> ListAsync(
        Guid orgId,
        string direction = "all",
        CancellationToken cancellationToken = default)
    {
        var path = $"api/organizations/{orgId}/register-invitations?direction={Uri.EscapeDataString(direction)}";
        var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, "list invitations", cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<InvitationListResponse>(
                   SorchaJson.Options, cancellationToken))
               ?? new InvitationListResponse { Invitations = Array.Empty<InvitationSummary>(), TotalCount = 0 };
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        Guid sourceOrgId,
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);

        var response = await _httpClient.DeleteAsync(
            $"api/organizations/{sourceOrgId}/register-invitations/{Uri.EscapeDataString(invitationId)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await BuildApiExceptionAsync(response, "revoke invitation", cancellationToken);
        }
    }

    private async Task<InvitationApiException> BuildApiExceptionAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken ct)
    {
        string message;
        try
        {
            // Server uses { "error": "<message>" } on failures. Falls back to status reason.
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            message = doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                ? err.GetString()!
                : response.ReasonPhrase ?? response.StatusCode.ToString();
        }
        catch
        {
            message = response.ReasonPhrase ?? response.StatusCode.ToString();
        }

        _logger.LogWarning(
            "Failed to {Action}: {StatusCode} — {Message}",
            action, (int)response.StatusCode, message);

        return new InvitationApiException(response.StatusCode, message);
    }
}
