// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AddressLookup;

/// <summary>
/// Pluggable postcode lookup provider. Implementations wrap an external API
/// (postcodes.io, OS Places, Loqate, etc.) and translate its response into
/// Sorcha's <see cref="AddressLookupResult"/> shape. A Sorcha deployment may
/// register zero or more providers; <see cref="AddressLookupService"/>
/// picks the most capable available provider for the country at request time.
/// </summary>
/// <remarks>
/// Implementations MUST:
/// <list type="bullet">
///   <item>Be safe to register as a singleton (stateless wrappers around <c>HttpClient</c>).</item>
///   <item>Never throw on network / HTTP failures — convert them to a result with <see cref="AddressLookupResult.IsValid"/> = <c>false</c> and log a warning.</item>
///   <item>Normalise the postcode input before querying (uppercase, single space, trim).</item>
///   <item>Echo the normalised postcode in the returned result regardless of validity.</item>
/// </list>
/// </remarks>
public interface IAddressLookupProvider
{
    /// <summary>Short identifier for this provider, used in telemetry and the provider catalogue.</summary>
    string ProviderName { get; }

    /// <summary>How much the provider can return for a matching postcode.</summary>
    AddressLookupCapability Capability { get; }

    /// <summary>ISO 3166-1 alpha-2 country codes the provider supports.</summary>
    IReadOnlyList<string> SupportedCountries { get; }

    /// <summary>
    /// Health check: can the provider currently service requests? Called
    /// periodically by the composition root and at request time when
    /// selecting a provider. Implementations should cache recent results to
    /// avoid hitting the upstream API on every call.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a postcode. Never throws on upstream failure — returns an
    /// <see cref="AddressLookupResult"/> with <see cref="AddressLookupResult.IsValid"/> = <c>false</c>
    /// and the provider's own name so callers can fall back cleanly.
    /// </summary>
    /// <param name="postcode">The raw postcode input. Implementations normalise before querying.</param>
    /// <param name="countryHint">Optional ISO 3166-1 alpha-2 country code; providers may use this to disambiguate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AddressLookupResult> LookupAsync(
        string postcode,
        string? countryHint = null,
        CancellationToken cancellationToken = default);
}
