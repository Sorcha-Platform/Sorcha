// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.Haip.Service.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 111 — Relay that forwards HAIP verifier results to the Blueprint Service's
/// presentation callback endpoint, attaching a service-to-service JWT.
/// </summary>
public sealed class PresentationCallbackRelay
{
    private const string ConsumerName = "haip";

    private readonly HttpClient _http;
    private readonly IServiceAuthClient _authClient;
    private readonly ILogger<PresentationCallbackRelay> _logger;

    public PresentationCallbackRelay(
        HttpClient http,
        IServiceAuthClient authClient,
        ILogger<PresentationCallbackRelay> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// POST the verification result to Blueprint Service. Fire-and-observe — failures
    /// are logged but do not surface back to the wallet. The pending-presentation
    /// TTL gives the caller a natural retry window.
    /// </summary>
    public async Task RelayAsync(
        Guid presentationRequestId,
        VerificationResult result,
        CancellationToken ct)
    {
        await ServiceClientAuthHelper.SetAuthHeaderAsync(_http, _authClient, _logger, "Haip", ct);

        var path = $"/api/presentations/callbacks/{ConsumerName}/{presentationRequestId}";
        try
        {
            using var response = await _http.PostAsJsonAsync(path, result, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Blueprint callback relay returned {Status} for requestId {RequestId}: {Body}",
                    (int)response.StatusCode, presentationRequestId, body);
            }
            else
            {
                _logger.LogInformation(
                    "Blueprint callback relay succeeded for requestId {RequestId}",
                    presentationRequestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Blueprint callback relay failed for requestId {RequestId}",
                presentationRequestId);
        }
    }
}
