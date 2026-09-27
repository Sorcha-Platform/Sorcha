// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Implementation of <see cref="IRehearsalApiService"/> calling the Blueprint Service full-rehearsal
/// endpoints through the authenticated gateway <see cref="HttpClient"/> (Feature 142, US2).
/// </summary>
public sealed class RehearsalApiService : IRehearsalApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RehearsalApiService> _logger;

    /// <summary>Creates the service over the authenticated gateway HTTP client.</summary>
    public RehearsalApiService(HttpClient httpClient, ILogger<RehearsalApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<StartRehearsalOutcome> StartFullRehearsalAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new StartRehearsalRequest { Mode = RehearsalMode.Full };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{blueprintId}/rehearsals", request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.LogInformation(
                    "Full rehearsal for blueprint {Id} blocked by validation (409)", blueprintId);
                return StartRehearsalOutcome.BlockedByValidation();
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to start full rehearsal for blueprint {Id}: {StatusCode}",
                    blueprintId, response.StatusCode);
                return StartRehearsalOutcome.Errored();
            }

            var rehearsal = await response.Content.ReadFromJsonAsync<Rehearsal>(JsonDefaults.Api, cancellationToken);
            return rehearsal is null
                ? StartRehearsalOutcome.Errored()
                : StartRehearsalOutcome.Started(rehearsal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting full rehearsal for blueprint {Id}", blueprintId);
            return StartRehearsalOutcome.Errored();
        }
    }

    /// <inheritdoc />
    public async Task<Rehearsal?> GetRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Rehearsal>(
                $"/api/blueprints/{blueprintId}/rehearsals/{rehearsalId}", JsonDefaults.Api, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rehearsal {RehearsalId} for blueprint {Id}", rehearsalId, blueprintId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Rehearsal?> SwitchRoleAsync(string blueprintId, Guid rehearsalId, string role, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SwitchRehearsalRoleRequest { Role = role };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{blueprintId}/rehearsals/{rehearsalId}/role", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to switch role to {Role} on rehearsal {RehearsalId}: {StatusCode}",
                    role, rehearsalId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<Rehearsal>(JsonDefaults.Api, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error switching role on rehearsal {RehearsalId} for blueprint {Id}", rehearsalId, blueprintId);
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// #1724 — two fixes over the original implementation:
    /// <list type="bullet">
    /// <item>
    /// The wire contract carries <c>payload</c> as a JSON OBJECT; <see cref="SubmitRehearsalStepRequest.PayloadJson"/>
    /// holds it as a raw string under the same wire name. Posting the DTO as-is put a JSON *string*
    /// on the wire, which the server binds as a <see cref="JsonElement"/> and silently reads as an
    /// empty payload for anything but an object root. The payload is parsed here and posted as an
    /// object instead — the same fix <c>BlueprintServiceClient.SubmitRehearsalStepAsync</c> carries
    /// for the MCP path (#1691 / #1723).
    /// </item>
    /// <item>
    /// A non-success response (422 validation failure, 404, 403, ...) carries a plain
    /// <c>{ "error": "..." }</c> body, never a <see cref="Rehearsal"/>. The previous code tried to
    /// deserialize that body as a <see cref="Rehearsal"/> on 422 — which does not throw (unknown
    /// properties are ignored, missing ones default), so it silently returned a BLANK rehearsal and
    /// wiped the caller's walk-through state instead of surfacing the refusal.
    /// </item>
    /// </list>
    /// </remarks>
    public async Task<SubmitStepOutcome> SubmitStepAsync(string blueprintId, Guid rehearsalId, int actionId, string payloadJson, CancellationToken cancellationToken = default)
    {
        JsonElement payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(payloadJson)
                ? JsonSerializer.Deserialize<JsonElement>("{}")
                : JsonSerializer.Deserialize<JsonElement>(payloadJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Submit rehearsal step {RehearsalId}: payload is not valid JSON", rehearsalId);
            return SubmitStepOutcome.Refused($"The step payload is not valid JSON: {ex.Message}");
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return SubmitStepOutcome.Refused(
                "The step payload must be a JSON object of the action's fields, not a JSON "
                + $"{payload.ValueKind.ToString().ToLowerInvariant()}.");
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{blueprintId}/rehearsals/{rehearsalId}/steps",
                new { actionId, payload }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var rehearsal = await response.Content.ReadFromJsonAsync<Rehearsal>(JsonDefaults.Api, cancellationToken);
                return rehearsal is null
                    ? SubmitStepOutcome.Refused("The Blueprint Service answered with an empty body.")
                    : SubmitStepOutcome.Applied(rehearsal);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var reason = ReadErrorReason(body)
                ?? $"The Blueprint Service refused the step ({(int)response.StatusCode} {response.StatusCode}).";
            _logger.LogWarning(
                "Failed to submit step {ActionId} on rehearsal {RehearsalId}: {StatusCode}: {Reason}",
                actionId, rehearsalId, response.StatusCode, reason);
            return SubmitStepOutcome.Refused(reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting step {ActionId} on rehearsal {RehearsalId} for blueprint {Id}", actionId, rehearsalId, blueprintId);
            return SubmitStepOutcome.Refused("A network or server error occurred while submitting the step.");
        }
    }

    /// <summary>Reads the <c>error</c> string from a <c>{ "error": "..." }</c> refusal body, or null.</summary>
    private static string? ReadErrorReason(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                ? err.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"/api/blueprints/{blueprintId}/rehearsals/{rehearsalId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting rehearsal {RehearsalId} for blueprint {Id}", rehearsalId, blueprintId);
            return false;
        }
    }
}
