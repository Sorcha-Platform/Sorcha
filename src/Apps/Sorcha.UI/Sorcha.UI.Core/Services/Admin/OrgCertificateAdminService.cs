// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Feature 181 US5 (T050) — HTTP-backed <see cref="IOrgCertificateAdminService"/>. Talks to the
/// gateway, which routes <c>/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}</c> to the
/// Tenant Service. Auth rides the browser session (platform tier). Mirrors
/// <see cref="TrustedListAdminService"/> in structure and error handling.
/// </summary>
public sealed class OrgCertificateAdminService : IOrgCertificateAdminService
{
    private const string BasePath = "/api/v1/trust/tenants";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<OrgCertificateAdminService> _logger;

    public OrgCertificateAdminService(HttpClient http, ILogger<OrgCertificateAdminService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string OrgPath(string tenantId, string orgWalletAddress) =>
        $"{BasePath}/{Uri.EscapeDataString(tenantId)}/orgs/{Uri.EscapeDataString(orgWalletAddress)}";

    /// <inheritdoc />
    public async Task<OrgCertificatesResult> ListAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<OrgCertificatesResult>(
                $"{OrgPath(tenantId, orgWalletAddress)}/certificates", JsonOptions, ct).ConfigureAwait(false);
            return result ?? new OrgCertificatesResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to list org certificates for {Org}", orgWalletAddress);
            return new OrgCertificatesResult();
        }
    }

    /// <inheritdoc />
    public async Task<OrgCsrOutcome> GenerateCsrAsync(string tenantId, string orgWalletAddress, string? subjectDn, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{OrgPath(tenantId, orgWalletAddress)}/csr",
                new { subjectDn },
                JsonOptions, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<OrgCsrResult>(JsonOptions, ct).ConfigureAwait(false);
                return result is not null
                    ? OrgCsrOutcome.Ok(result)
                    : OrgCsrOutcome.Fail("PARSE", "CSR generated but the response could not be read.");
            }

            var (code, message) = await ReadProblemAsync(resp, ct).ConfigureAwait(false);
            return OrgCsrOutcome.Fail(code, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to generate CSR for {Org}", orgWalletAddress);
            return OrgCsrOutcome.Fail("NETWORK", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OrgCertificateOutcome> ImportAsync(string tenantId, string orgWalletAddress, string certificatePem, IReadOnlyList<string>? chainPem, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{OrgPath(tenantId, orgWalletAddress)}/certificates/import",
                new { certificatePem, chainPem },
                JsonOptions, ct).ConfigureAwait(false);
            return await ReadCertificateOutcomeAsync(resp, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to import certificate for {Org}", orgWalletAddress);
            return OrgCertificateOutcome.Fail("NETWORK", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<OrgCertificateOutcome> EnrolAsync(string tenantId, string orgWalletAddress, string? orgDisplayName, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{OrgPath(tenantId, orgWalletAddress)}/enrol",
                new { orgDisplayName },
                JsonOptions, ct).ConfigureAwait(false);
            return await ReadCertificateOutcomeAsync(resp, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to enrol internal certificate for {Org}", orgWalletAddress);
            return OrgCertificateOutcome.Fail("NETWORK", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.DeleteAsync(
                $"{OrgPath(tenantId, orgWalletAddress)}/certificates/{certificateId}", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to retire certificate {CertificateId} for {Org}", certificateId, orgWalletAddress);
            return false;
        }
    }

    private static async Task<OrgCertificateOutcome> ReadCertificateOutcomeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
        {
            var cert = await resp.Content.ReadFromJsonAsync<OrgCertificateSummary>(JsonOptions, ct).ConfigureAwait(false);
            return cert is not null
                ? OrgCertificateOutcome.Ok(cert)
                : OrgCertificateOutcome.Fail("PARSE", "Request succeeded but the response could not be read.");
        }

        var (code, message) = await ReadProblemAsync(resp, ct).ConfigureAwait(false);
        return OrgCertificateOutcome.Fail(code, message);
    }

    private static async Task<(string? Code, string? Message)> ReadProblemAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            var code = root.TryGetProperty("error", out var e) ? e.GetString() : resp.StatusCode.ToString();
            var message = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (resp.StatusCode.ToString(), null);
        }
    }
}
