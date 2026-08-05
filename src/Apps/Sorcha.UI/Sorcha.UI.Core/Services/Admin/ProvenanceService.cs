// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Provenance;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Typed client over <c>/api/provenance/*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Must be constructed with an authenticated <see cref="HttpClient"/></b> — one built on
/// <c>AuthenticatedHttpMessageHandler</c>, as every typed client in this assembly is. An ambient
/// injected <c>HttpClient</c> sends no bearer token, and the resulting 401 would surface here as a
/// null and render as "no provenance available", which is indistinguishable from a register with no
/// history. A silent auth failure dressed as an empty result is the worst possible outcome for an
/// evidence view.
/// </para>
/// <para>
/// Failures return null rather than throwing: the caller renders "could not load" beside whatever it
/// did manage to fetch (FR-020). Everything about what the evidence <i>means</i> is decided on the
/// server, in the engine — this client shapes JSON and nothing else.
/// </para>
/// </remarks>
public sealed class ProvenanceService : IProvenanceService
{
    private readonly HttpClient _http;
    private readonly ILogger<ProvenanceService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Creates the client. <paramref name="httpClient"/> must carry the bearer token.</summary>
    public ProvenanceService(HttpClient httpClient, ILogger<ProvenanceService> logger)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<RegisterSpinePage?> GetSpineAsync(
        string registerId,
        ulong? fromDocket = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/provenance/registers/{Uri.EscapeDataString(registerId)}?pageSize={pageSize}";
        if (fromDocket is { } from)
        {
            url += $"&fromDocket={from}";
        }

        return await GetAsync<RegisterSpinePage>(url, "spine", registerId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProvenanceTrailView?> GetTrailAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/provenance/registers/{Uri.EscapeDataString(registerId)}/dockets/{docketNumber}";

        return await GetAsync<ProvenanceTrailView>(url, "trail", registerId, cancellationToken);
    }

    private async Task<T?> GetAsync<T>(
        string url,
        string surface,
        string registerId,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var response = await _http.GetAsync(url, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Logged at warning with the status because a 401/403 here means the client was
                // built without the authenticated handler, and that is otherwise invisible.
                _logger.LogWarning(
                    "Provenance {Surface} request for register {RegisterId} returned {Status}",
                    surface, registerId, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Provenance {Surface} request for register {RegisterId} failed", surface, registerId);
            return null;
        }
    }
}
