// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Sorcha.AddressLookup.Providers;

/// <summary>
/// Default UK postcode provider. Wraps the free, no-key, unlimited-use
/// <a href="https://api.postcodes.io">postcodes.io</a> API. Ships as
/// <see cref="AddressLookupCapability.ValidateOnly"/> because postcodes.io
/// only returns coarse locality metadata (town, region, country, lat/long);
/// full street-address lookup requires a separately licensed provider
/// (<see cref="OsPlacesProvider"/> or similar).
/// </summary>
/// <remarks>
/// <para>
/// postcodes.io is MIT-licensed, has no rate limit, and requires no
/// registration, so it's safe to ship as default-on. Consumers that need
/// full-address lookup layer OS Places on top of this via opt-in config.
/// </para>
/// <para>
/// The provider never throws on upstream failure — network errors, non-200
/// responses, and JSON parse failures are all converted to an
/// <c>IsValid = false</c> result with a warning log.
/// </para>
/// </remarks>
public sealed class PostcodesIoProvider : IAddressLookupProvider
{
    /// <summary>Named HttpClient key for <see cref="IHttpClientFactory"/> lookup.</summary>
    public const string HttpClientName = "Sorcha.AddressLookup.PostcodesIo";

    private static readonly string[] UkOnly = ["GB"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PostcodesIoProvider> _logger;

    /// <summary>Initialises a new instance of the <see cref="PostcodesIoProvider"/> class.</summary>
    public PostcodesIoProvider(HttpClient httpClient, ILogger<PostcodesIoProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => "postcodes.io";

    /// <inheritdoc />
    public AddressLookupCapability Capability => AddressLookupCapability.ValidateOnly;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCountries => UkOnly;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Lightweight reachability check — postcodes.io supports
            // /postcodes/{postcode}/validate which returns a single boolean.
            using var response = await _httpClient.GetAsync(
                "postcodes/SW1A%201AA/validate", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "postcodes.io health check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<AddressLookupResult> LookupAsync(
        string postcode,
        string? countryHint = null,
        CancellationToken cancellationToken = default)
    {
        var normalised = NormalisePostcode(postcode);

        // Country hint mismatch — postcodes.io only serves GB. Return a graceful
        // degradation result rather than throwing.
        if (countryHint is not null && !string.Equals(countryHint, "GB", StringComparison.OrdinalIgnoreCase))
        {
            return NotValid(normalised);
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                $"postcodes/{Uri.EscapeDataString(normalised)}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return NotValid(normalised);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "postcodes.io returned {Status} for postcode {Postcode}",
                    (int)response.StatusCode, normalised);
                return NotValid(normalised);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<PostcodesIoEnvelope>(body, JsonOptions);
            if (envelope?.Result is null)
            {
                return NotValid(normalised);
            }

            return new AddressLookupResult
            {
                Postcode = normalised,
                IsValid = true,
                Provider = ProviderName,
                Capability = Capability,
                Metadata = new AddressLookupMetadata
                {
                    Town = envelope.Result.PostTown ?? envelope.Result.AdminDistrict,
                    Region = envelope.Result.Region ?? envelope.Result.Country,
                    Country = "GB",
                    Latitude = envelope.Result.Latitude,
                    Longitude = envelope.Result.Longitude
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "postcodes.io lookup failed for postcode {Postcode}; falling back to plain text",
                normalised);
            return NotValid(normalised);
        }
    }

    private AddressLookupResult NotValid(string normalised) =>
        new()
        {
            Postcode = normalised,
            IsValid = false,
            Provider = ProviderName,
            Capability = Capability
        };

    /// <summary>
    /// Normalise a postcode to uppercase with a single space before the last
    /// three characters (the standard UK format, e.g. "EH1 1YZ").
    /// Non-alphanumeric characters are stripped.
    /// </summary>
    internal static string NormalisePostcode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var stripped = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (stripped.Length < 5) return stripped; // not a valid UK postcode shape; return as-is for the API to reject

        var outward = stripped[..^3];
        var inward = stripped[^3..];
        return $"{outward} {inward}";
    }

    // ------- DTO for postcodes.io JSON envelope -------

    private sealed record PostcodesIoEnvelope(int Status, PostcodesIoPostcode? Result);

    private sealed record PostcodesIoPostcode(
        string? Postcode,
        [property: JsonPropertyName("post_town")] string? PostTown,
        [property: JsonPropertyName("admin_district")] string? AdminDistrict,
        string? Region,
        string? Country,
        double? Latitude,
        double? Longitude);
}
