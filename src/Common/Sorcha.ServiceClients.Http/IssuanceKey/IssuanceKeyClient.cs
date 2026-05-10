// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.IssuanceKey;

/// <summary>
/// HTTP-backed <see cref="IIssuanceKeyClient"/> targeting the wallet service's
/// <c>POST /api/v1/orgs/{orgId}/issuance-key/ensure</c> endpoint.
/// </summary>
public sealed class IssuanceKeyClient : IIssuanceKeyClient
{
    private readonly HttpClient _http;
    private readonly ILogger<IssuanceKeyClient> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public IssuanceKeyClient(HttpClient http, ILogger<IssuanceKeyClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnsureAsync(Guid organizationId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync(
                $"/api/v1/orgs/{organizationId}/issuance-key/ensure",
                content: null,
                ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "IssuanceKey ensure returned {Status} for org {OrgId}",
                    resp.StatusCode, organizationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IssuanceKey ensure failed for org {OrgId}; lazy rebuild may recover on next mint",
                organizationId);
        }
    }
}
