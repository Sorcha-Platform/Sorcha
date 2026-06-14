// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services.Catalogue;

/// <summary>
/// Feature 154 (B) — the citizen service catalogue client: lists the services a citizen can start
/// (<c>GET /api/catalogue</c>) and starts one by creating a new application instance
/// (<c>POST /api/instances/</c>). The PWA's bearer chain carries the consumer-tier token.
/// </summary>
public interface ICatalogueClient
{
    /// <summary>
    /// Lists the startable services. A transient failure <b>throws</b> so the page can show a
    /// non-blocking notice (distinct from a genuinely empty catalogue).
    /// </summary>
    Task<IReadOnlyList<CatalogueItem>> GetServicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts <paramref name="item"/> by creating a new application instance; returns the new
    /// instance id, or <c>null</c> if the start failed.
    /// </summary>
    Task<string?> StartAsync(CatalogueItem item, CancellationToken ct = default);
}

/// <summary>Feature 154 — a startable service in the catalogue.</summary>
public sealed record CatalogueItem(string BlueprintId, string Title, string? Description, string RegisterId);

/// <summary>Default <see cref="ICatalogueClient"/> over the PWA's bearer-authed HttpClient.</summary>
public sealed class HttpCatalogueClient : ICatalogueClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Initialises a new instance.</summary>
    public HttpCatalogueClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogueItem>> GetServicesAsync(CancellationToken ct = default)
    {
        var items = await _http.GetFromJsonAsync<List<CatalogueItem>>("api/catalogue", JsonOptions, ct)
            .ConfigureAwait(false);
        return items ?? (IReadOnlyList<CatalogueItem>)Array.Empty<CatalogueItem>();
    }

    /// <inheritdoc />
    public async Task<string?> StartAsync(CatalogueItem item, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/instances/",
                new CreateInstanceBody(item.BlueprintId, item.RegisterId),
                JsonOptions, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var created = await response.Content.ReadFromJsonAsync<CreatedInstance>(JsonOptions, ct).ConfigureAwait(false);
            return string.IsNullOrEmpty(created?.Id) ? null : created!.Id;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private sealed record CreateInstanceBody(
        [property: JsonPropertyName("blueprintId")] string BlueprintId,
        [property: JsonPropertyName("registerId")] string RegisterId);

    private sealed record CreatedInstance([property: JsonPropertyName("id")] string? Id);
}
