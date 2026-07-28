// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task<bool> RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default)
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
                // NOT recoverable on its own — there is no background rebuild. The caller
                // decides whether to fail closed; see IOrgDidDocumentClient.RegenerateAsync.
                _logger.LogWarning(
                    "OrgDidDocument regenerate returned {Status} for org {OrgId} reason {Reason}",
                    resp.StatusCode, request.OrganizationId, request.KeyEventReason);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OrgDidDocument regenerate failed for org {OrgId} reason {Reason}",
                request.OrganizationId, request.KeyEventReason);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> ResolveCanonicalDidAsync(Guid orgId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"/orgs/{orgId}/did.json", ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // Org has no published DID document — never issued a credential.
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Org DID resolve returned {Status} for org {OrgId}", resp.StatusCode, orgId);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("id", out var idEl)
                && idEl.ValueKind == JsonValueKind.String)
            {
                var did = idEl.GetString();
                return string.IsNullOrWhiteSpace(did) ? null : did;
            }

            _logger.LogWarning("Org DID document for {OrgId} had no string 'id' field", orgId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Org DID resolve failed for org {OrgId}", orgId);
            return null;
        }
    }
}
