// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;

namespace Sorcha.Agent.Inbox;

/// <summary>
/// The disclosed prior-action data for the calling participant, as returned by the blueprint-service
/// disclosed-data endpoint (Feature 176).
/// </summary>
/// <param name="RecipientResolved">
/// True when the caller is a disclosure recipient and fields were disclosed to it. False → the caller is
/// not a recipient (drives a fail-closed hold).
/// </param>
/// <param name="DisclosedFields">
/// The merged disclosed prior-action payload the checks evaluate, or null when nothing was disclosed.
/// </param>
public sealed record DisclosedDataResult(bool RecipientResolved, JsonElement? DisclosedFields);

/// <summary>
/// Fetches the prior-action data disclosed to the agent's participant for a pending action. A seam so the
/// enrichment logic can be unit-tested without HTTP.
/// </summary>
public interface IDisclosedDataClient
{
    /// <summary>
    /// Gets the disclosed prior-action data for <paramref name="actionId"/> of
    /// <paramref name="instanceId"/>, as the authenticated agent. Returns null on any transport/HTTP
    /// failure so the caller can fail closed (hold + retry on the next poll).
    /// </summary>
    Task<DisclosedDataResult?> GetDisclosedDataAsync(string instanceId, uint actionId, CancellationToken cancellationToken);
}

/// <summary>
/// HTTP implementation of <see cref="IDisclosedDataClient"/>. Calls
/// <c>GET /api/workflows/{instanceId}/actions/{actionId}/disclosures</c> with the agent's own bearer
/// token, mirroring the raw-<see cref="HttpClient"/> pattern the rest of the agent uses
/// (<see cref="PollingInboxListener"/>, <c>ActionExecutor</c>). The agent authenticates as its user, so
/// the endpoint resolves the agent's own wallet as the disclosure recipient — the caller's user token is
/// required (a service-to-service client would resolve the wrong identity), which is why this does not go
/// through <c>IBlueprintServiceClient</c> (that client mints a service token). The bearer is also sent as
/// <c>X-Delegation-Token</c> so disclosure groups can be decrypted on encrypted (non dev-mode) registers.
/// </summary>
public sealed class HttpDisclosedDataClient : IDisclosedDataClient
{
    private readonly HttpClient _httpClient;
    private readonly AgentAuthService _authService;
    private readonly ILogger<HttpDisclosedDataClient> _logger;

    public HttpDisclosedDataClient(
        HttpClient httpClient, AgentAuthService authService, ILogger<HttpDisclosedDataClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DisclosedDataResult?> GetDisclosedDataAsync(
        string instanceId, uint actionId, CancellationToken cancellationToken)
    {
        try
        {
            var token = await _authService.GetTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/workflows/{Uri.EscapeDataString(instanceId)}/actions/{actionId}/disclosures");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Delegation-Token", token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Disclosed-data fetch for {InstanceId}/{ActionId} returned {StatusCode}",
                    instanceId, actionId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var recipientResolved =
                root.TryGetProperty("recipientResolved", out var rr) && rr.ValueKind == JsonValueKind.True;

            JsonElement? disclosedFields =
                root.TryGetProperty("disclosedFields", out var df) && df.ValueKind == JsonValueKind.Object
                    ? df.Clone()
                    : null;

            return new DisclosedDataResult(recipientResolved, disclosedFields);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Disclosed-data fetch failed for {InstanceId}/{ActionId}", instanceId, actionId);
            return null;
        }
    }
}
