// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.OrgDidDocument;

/// <summary>
/// HTTP-backed <see cref="IOrgDidDocumentClient"/> that POSTs to the Tenant Service's
/// internal <c>/orgs/{orgId}/did-document/regenerate</c> endpoint.
/// </summary>
public sealed class OrgDidDocumentClient : IOrgDidDocumentClient
{
    private readonly HttpClient _http;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<OrgDidDocumentClient> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public OrgDidDocumentClient(
        HttpClient http,
        IServiceAuthClient serviceAuth,
        ILogger<OrgDidDocumentClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            // C1 (catch-up security review 2026-07-29): the regenerate endpoint is RequireService.
            // Fail closed BEFORE sending when no service token is available — an anonymous POST
            // would come back 401, which the catch-all below maps to `false`, i.e. exactly the same
            // answer as a genuine regeneration failure. Callers treat `false` as "publish failed"
            // and (per H2) largely swallow it, so a silent credential-misconfiguration would look
            // like an intermittent Tenant problem and DID publishing would just stop.
            var token = await _serviceAuth.GetTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError(
                    "No service token available; refusing to POST DID-document regenerate for org "
                    + "{OrgId} reason {Reason}. The published document will be STALE — check the "
                    + "service client credentials for this host.",
                    request.OrganizationId, request.KeyEventReason);
                return false;
            }

            await ServiceClientAuthHelper.SetAuthHeaderAsync(
                _http, _serviceAuth, _logger, "Tenant Service (OrgDidDocument)", ct)
                .ConfigureAwait(false);

            var resp = await _http.PostAsJsonAsync(
                $"/orgs/{request.OrganizationId}/did-document/regenerate",
                request,
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                // NOT recoverable on its own — there is no background rebuild. The caller
                // decides whether to fail closed; see IOrgDidDocumentClient.RegenerateAsync.
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Distinct from a transport/server failure: the token was rejected, so EVERY
                    // subsequent regenerate will fail the same way until credentials are fixed.
                    // Logged at Error so it cannot hide among transient warnings.
                    _logger.LogError(
                        "OrgDidDocument regenerate was REJECTED ({Status}) for org {OrgId} reason "
                        + "{Reason}. The service token was refused by the Tenant Service — the "
                        + "published DID document is now STALE and issued credentials may not verify.",
                        resp.StatusCode, request.OrganizationId, request.KeyEventReason);
                    return false;
                }

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
