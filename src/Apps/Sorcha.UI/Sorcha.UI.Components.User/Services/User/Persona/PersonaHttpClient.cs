// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Persona;

/// <summary>
/// Default <see cref="IPersonaClient"/> implementation — thin HTTP wrapper
/// around the Tenant Service <c>/me/persona</c> endpoints.
/// </summary>
public sealed class PersonaHttpClient : IPersonaClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PersonaHttpClient> _logger;

    public PersonaHttpClient(HttpClient httpClient, ILogger<PersonaHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1?> GetPersonaAsync(string actingAs = "self", CancellationToken ct = default)
    {
        try
        {
            var url = "/api/me/persona";
            if (!string.Equals(actingAs, "self", StringComparison.Ordinal))
            {
                url += $"?actingAs={Uri.EscapeDataString(actingAs)}";
            }

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GET /api/me/persona returned {Status} {Reason}",
                    (int)response.StatusCode, response.ReasonPhrase);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PersonaReadModelV1>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch persona");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PersonaReadModelV1> PutPersonaAsync(PersonaAttributesV1 persona, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/me/persona", persona, ct);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemWire>(cancellationToken: ct);
            if (problem?.Errors is { Count: > 0 })
            {
                throw new PersonaValidationException(problem.Errors);
            }
            throw new InvalidOperationException("Persona write rejected with bad request.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new PersonaWalletNotProvisionedException();
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PersonaReadModelV1>(JsonDefaults.Api, ct);
        return result ?? new PersonaReadModelV1();
    }

    /// <inheritdoc />
    public async Task DeletePersonaAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync("/api/me/persona", ct);

        // 204 No Content or 200 OK are both success. 401/403 are auth errors
        // we let the caller surface; anything else is unexpected.
        if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK))
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed class ValidationProblemWire
    {
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}
