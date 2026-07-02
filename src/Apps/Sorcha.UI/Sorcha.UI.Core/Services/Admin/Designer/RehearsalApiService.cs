// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
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
    public async Task<Rehearsal?> SubmitStepAsync(string blueprintId, Guid rehearsalId, int actionId, string payloadJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SubmitRehearsalStepRequest { ActionId = actionId, PayloadJson = payloadJson };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{blueprintId}/rehearsals/{rehearsalId}/steps", request, cancellationToken);

            // 200 (applied) and 422 (validation failure) both carry the refreshed rehearsal body.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                return await response.Content.ReadFromJsonAsync<Rehearsal>(JsonDefaults.Api, cancellationToken);
            }

            _logger.LogWarning(
                "Failed to submit step {ActionId} on rehearsal {RehearsalId}: {StatusCode}",
                actionId, rehearsalId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting step {ActionId} on rehearsal {RehearsalId} for blueprint {Id}", actionId, rehearsalId, blueprintId);
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
