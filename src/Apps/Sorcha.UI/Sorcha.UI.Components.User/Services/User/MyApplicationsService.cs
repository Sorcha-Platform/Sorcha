// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Feature 186 — <see cref="IMyApplicationsService"/> over <c>/api/me/applications</c>.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> injected here MUST be one built on
/// <c>AuthenticatedHttpMessageHandler</c>. An ambient client carries no bearer token, so every call
/// 401s and the page renders an empty list that looks exactly like "you have no applications" — the
/// silent-401 trap this codebase has now hit several times.
/// </remarks>
public class MyApplicationsService : IMyApplicationsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyApplicationsService> _logger;

    /// <summary>Initialises a new <see cref="MyApplicationsService"/>.</summary>
    /// <param name="httpClient">Authenticated client pointed at the API gateway.</param>
    /// <param name="logger">Logger.</param>
    public MyApplicationsService(HttpClient httpClient, ILogger<MyApplicationsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PaginatedList<MyApplicationViewModel>> GetMyApplicationsAsync(
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/me/applications?page={page}&pageSize={pageSize}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Logged at Warning with the status, because an empty list and a rejected request
                // look identical on the page. 401 here means the client was built without the
                // authenticating handler.
                _logger.LogWarning(
                    "GET /api/me/applications returned {StatusCode}", response.StatusCode);
                return PaginatedList<MyApplicationViewModel>.Empty(pageSize);
            }

            var result = await response.Content
                .ReadFromJsonAsync<PaginatedList<MyApplicationViewModel>>(
                    JsonDefaults.Api, cancellationToken: cancellationToken);

            return result ?? PaginatedList<MyApplicationViewModel>.Empty(pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching the caller's applications");
            return PaginatedList<MyApplicationViewModel>.Empty(pageSize);
        }
    }

    /// <inheritdoc/>
    public async Task<MyApplicationDetailViewModel?> GetMyApplicationAsync(
        string instanceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/me/applications/{instanceId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // The server answers "not yours" and "no such thing" identically by design, so this
                // is the only outcome the client can act on: show the not-found view.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GET /api/me/applications/{InstanceId} returned {StatusCode}",
                    instanceId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MyApplicationDetailViewModel>(
                JsonDefaults.Api, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching application {InstanceId}", instanceId);
            return null;
        }
    }
}
