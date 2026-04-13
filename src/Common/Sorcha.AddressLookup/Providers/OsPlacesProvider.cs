// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.AddressLookup.Providers;

/// <summary>
/// Ordnance Survey Places API provider. Full-address lookup for UK postcodes
/// using Royal Mail PAF data via the OS Places API.
/// </summary>
/// <remarks>
/// <para>
/// Requires a licensed API key configured in
/// <c>Tenant:AddressLookup:OsPlaces:ApiKey</c>. When the key is absent the
/// DI extension does not register this provider and deployments fall through
/// to <see cref="PostcodesIoProvider"/>.
/// </para>
/// <para>
/// OS Places enforces rate limits; the provider never throws on upstream
/// failure (including 429 rate-limit responses) — all failures become
/// <c>IsValid = false</c> results with a warning log so the form renderer can
/// fall back cleanly.
/// </para>
/// </remarks>
public sealed class OsPlacesProvider : IAddressLookupProvider
{
    /// <summary>Named HttpClient key for <see cref="IHttpClientFactory"/> lookup.</summary>
    public const string HttpClientName = "Sorcha.AddressLookup.OsPlaces";

    private static readonly string[] UkOnly = ["GB"];
    private static readonly JsonSerializerOptions JsonOptions = new();
    private static readonly TimeSpan HealthCheckTtl = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly OsPlacesOptions _options;
    private readonly ILogger<OsPlacesProvider> _logger;
    private readonly object _healthLock = new();
    private DateTimeOffset _healthCheckedAt = DateTimeOffset.MinValue;
    private bool _healthCachedValue;

    /// <summary>Initialises a new instance of the <see cref="OsPlacesProvider"/> class.</summary>
    public OsPlacesProvider(
        HttpClient httpClient,
        IOptions<OsPlacesOptions> options,
        ILogger<OsPlacesProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                $"{nameof(OsPlacesProvider)} requires an API key at '{OsPlacesOptions.SectionName}:ApiKey'. " +
                "Do not register this provider when the key is absent — rely on the DI extension's key-presence check.");
        }

        // Send the API key as a header rather than a query string parameter to
        // keep it out of structured logs (gateway, load balancer, upstream
        // access logs). OS Places accepts the key via either transport.
        if (!_httpClient.DefaultRequestHeaders.Contains("key"))
        {
            _httpClient.DefaultRequestHeaders.Add("key", _options.ApiKey);
        }
    }

    /// <inheritdoc />
    public string ProviderName => "os-places";

    /// <inheritdoc />
    public AddressLookupCapability Capability => AddressLookupCapability.FullAddress;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCountries => UkOnly;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // TTL-cached result — see PostcodesIoProvider for rationale. OS Places
        // enforces a rate limit so caching health probes is especially important.
        lock (_healthLock)
        {
            if (DateTimeOffset.UtcNow - _healthCheckedAt < HealthCheckTtl)
            {
                return _healthCachedValue;
            }
        }

        bool result;
        try
        {
            using var response = await GetPostcodeAsync("SW1A 1AA", cancellationToken);
            result = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "OS Places health check failed");
            result = false;
        }

        lock (_healthLock)
        {
            _healthCachedValue = result;
            _healthCheckedAt = DateTimeOffset.UtcNow;
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<AddressLookupResult> LookupAsync(
        string postcode,
        string? countryHint = null,
        CancellationToken cancellationToken = default)
    {
        var normalised = PostcodesIoProvider.NormalisePostcode(postcode);

        if (countryHint is not null && !string.Equals(countryHint, "GB", StringComparison.OrdinalIgnoreCase))
        {
            return NotValid(normalised);
        }

        try
        {
            using var response = await GetPostcodeAsync(normalised, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return NotValid(normalised);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "OS Places rate-limited the lookup for {Postcode}; falling back", normalised);
                return NotValid(normalised);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OS Places returned {Status} for postcode {Postcode}",
                    (int)response.StatusCode, normalised);
                return NotValid(normalised);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<OsPlacesEnvelope>(body, JsonOptions);
            var candidates = envelope?.Results?
                .Select(ToCandidate)
                .Where(c => c is not null)
                .Cast<AddressCandidate>()
                .ToList()
                ?? [];

            if (candidates.Count == 0)
            {
                return NotValid(normalised);
            }

            return new AddressLookupResult
            {
                Postcode = normalised,
                IsValid = true,
                Provider = ProviderName,
                Capability = Capability,
                Candidates = candidates
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OS Places lookup failed for postcode {Postcode}; falling back to plain text",
                normalised);
            return NotValid(normalised);
        }
    }

    private Task<HttpResponseMessage> GetPostcodeAsync(string normalisedPostcode, CancellationToken ct)
    {
        // API key travels in the default request header (set in the constructor)
        // so it never appears in access logs, reverse-proxy logs, or browser history.
        var url = $"postcode?postcode={Uri.EscapeDataString(normalisedPostcode)}";
        return _httpClient.GetAsync(url, ct);
    }

    private AddressLookupResult NotValid(string normalised) =>
        new()
        {
            Postcode = normalised,
            IsValid = false,
            Provider = ProviderName,
            Capability = Capability
        };

    private static AddressCandidate? ToCandidate(OsPlacesResult result)
    {
        var dpa = result.Dpa;
        if (dpa is null || string.IsNullOrWhiteSpace(dpa.Address)) return null;

        // OS Places returns a single comma-separated address line. We pull
        // the pieces we need out of the structured DPA fields and fall back
        // to the flat Address string for the display label.
        return new AddressCandidate
        {
            Line1 = BuildLine1(dpa),
            Line2 = dpa.BuildingName ?? dpa.SubBuildingName,
            Town = dpa.PostTown ?? "",
            Region = null,
            Postcode = dpa.Postcode ?? "",
            Country = "GB",
            DisplayLabel = dpa.Address ?? ""
        };
    }

    private static string BuildLine1(OsPlacesDpa dpa)
    {
        var number = dpa.BuildingNumber ?? dpa.SubBuildingName;
        var street = dpa.ThoroughfareName ?? dpa.DependentThoroughfareName ?? "";
        if (string.IsNullOrWhiteSpace(number)) return street;
        if (string.IsNullOrWhiteSpace(street)) return number;
        return $"{number} {street}".Trim();
    }

    // ------- DTOs for the OS Places JSON envelope -------
    // Only the fields we actually use are modelled; the upstream payload has
    // many more (UPRN, classification codes, OS grid refs, etc.).

    private sealed record OsPlacesEnvelope(
        [property: JsonPropertyName("results")] IReadOnlyList<OsPlacesResult>? Results);

    private sealed record OsPlacesResult(
        [property: JsonPropertyName("DPA")] OsPlacesDpa? Dpa);

    private sealed record OsPlacesDpa(
        [property: JsonPropertyName("ADDRESS")] string? Address,
        [property: JsonPropertyName("BUILDING_NUMBER")] string? BuildingNumber,
        [property: JsonPropertyName("BUILDING_NAME")] string? BuildingName,
        [property: JsonPropertyName("SUB_BUILDING_NAME")] string? SubBuildingName,
        [property: JsonPropertyName("THOROUGHFARE_NAME")] string? ThoroughfareName,
        [property: JsonPropertyName("DEPENDENT_THOROUGHFARE_NAME")] string? DependentThoroughfareName,
        [property: JsonPropertyName("POST_TOWN")] string? PostTown,
        [property: JsonPropertyName("POSTCODE")] string? Postcode);
}
