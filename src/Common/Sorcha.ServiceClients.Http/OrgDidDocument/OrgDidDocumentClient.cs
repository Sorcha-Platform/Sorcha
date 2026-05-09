// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.OrgDidDocument;

/// <summary>
/// HTTP-backed <see cref="IOrgDidDocumentClient"/> that POSTs to the Tenant Service's
/// internal <c>/orgs/{orgId}/did-document/regenerate</c> endpoint.
/// </summary>
public sealed class OrgDidDocumentClient : IOrgDidDocumentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OrgDidDocumentClient> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public OrgDidDocumentClient(HttpClient http, ILogger<OrgDidDocumentClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/orgs/{request.OrganizationId}/did-document/regenerate",
                request,
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OrgDidDocument regenerate returned {Status} for org {OrgId} reason {Reason}",
                    resp.StatusCode, request.OrganizationId, request.KeyEventReason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OrgDidDocument regenerate failed for org {OrgId} reason {Reason}; lazy rebuild will recover",
                request.OrganizationId, request.KeyEventReason);
        }
    }
}
