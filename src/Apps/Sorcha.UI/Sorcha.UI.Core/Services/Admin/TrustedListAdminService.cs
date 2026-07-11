// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Feature 181 US3 (T037) — HTTP-backed <see cref="ITrustedListAdminService"/>. Talks to the gateway,
/// which routes <c>/api/v1/trust/trustlists</c> to the Tenant Service. Auth rides the browser session.
/// </summary>
public sealed class TrustedListAdminService : ITrustedListAdminService
{
    private const string BasePath = "/api/v1/trust/trustlists";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<TrustedListAdminService> _logger;

    public TrustedListAdminService(HttpClient http, ILogger<TrustedListAdminService> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrustListSummary>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<TrustListSummary>>(BasePath, JsonOptions, ct).ConfigureAwait(false);
            return list ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to list trusted-list snapshots");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<TrustListDetail?> GetAsync(string trustListId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{BasePath}/{Uri.EscapeDataString(trustListId)}", ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<TrustListDetail>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to read trusted-list snapshot {TrustListId}", trustListId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<TrustListImportOutcome> ImportAsync(string trustListId, Stream document, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(trustListId), "trustListId" },
            };
            var fileContent = new StreamContent(document);
            content.Add(fileContent, "document", string.IsNullOrWhiteSpace(fileName) ? "trustlist.xml" : fileName);

            using var resp = await _http.PostAsync($"{BasePath}/import", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var summary = await resp.Content.ReadFromJsonAsync<TrustListSummary>(JsonOptions, ct).ConfigureAwait(false);
                return summary is not null
                    ? TrustListImportOutcome.Ok(summary)
                    : TrustListImportOutcome.Fail("PARSE", "Import succeeded but the response could not be read.");
            }

            var (code, message) = await ReadProblemAsync(resp, ct).ConfigureAwait(false);
            return TrustListImportOutcome.Fail(code, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to import trusted-list snapshot {TrustListId}", trustListId);
            return TrustListImportOutcome.Fail("NETWORK", ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string trustListId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.DeleteAsync($"{BasePath}/{Uri.EscapeDataString(trustListId)}", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to delete trusted-list snapshot {TrustListId}", trustListId);
            return false;
        }
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
