// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.IssuanceKey;

internal sealed class SignOnBehalfRequest
{
    public string DataBase64Url { get; set; } = "";
}

internal sealed class SignOnBehalfResponse
{
    public string SignatureBase64Url { get; set; } = "";
    public string Kid { get; set; } = "";
    public string IssuerDid { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public int RotationIndex { get; set; }
}

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

    /// <inheritdoc />
    public async Task<IssuanceSignResult?> SignAsync(
        Guid organizationId, byte[] dataToSign, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dataToSign);

        try
        {
            var body = new SignOnBehalfRequest
            {
                DataBase64Url = Convert.ToBase64String(dataToSign).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            };
            var resp = await _http.PostAsJsonAsync(
                $"/api/v1/orgs/{organizationId}/issuance-key/sign", body, ct).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // No Active issuance key — caller falls back to local signing.
                return null;
            }

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "IssuanceKey sign returned {Status} for org {OrgId}",
                    resp.StatusCode, organizationId);
                return null;
            }

            var result = await resp.Content.ReadFromJsonAsync<SignOnBehalfResponse>(ct).ConfigureAwait(false);
            if (result is null) return null;

            var sig = Convert.FromBase64String(result.SignatureBase64Url
                .Replace('-', '+').Replace('_', '/').PadRight(
                    result.SignatureBase64Url.Length + (4 - result.SignatureBase64Url.Length % 4) % 4, '='));

            return new IssuanceSignResult(
                Signature: sig,
                Kid: result.Kid,
                IssuerDid: result.IssuerDid,
                Algorithm: result.Algorithm,
                RotationIndex: result.RotationIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IssuanceKey sign failed for org {OrgId}; falling back to local signing",
                organizationId);
            return null;
        }
    }
}
