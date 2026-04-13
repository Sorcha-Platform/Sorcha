// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.AddressLookup;

/// <summary>
/// Composition root for postcode lookup. Owns the set of registered providers
/// and picks the best one at request time using the provider selection rules
/// from the design spec:
/// <list type="number">
///   <item>Prefer <see cref="AddressLookupCapability.FullAddress"/> over <see cref="AddressLookupCapability.ValidateOnly"/> for the target country.</item>
///   <item>Within a capability tier, prefer the first available provider.</item>
///   <item>If no provider supports the country, return a "none" graceful-degradation result so the form renderer falls back to plain text.</item>
/// </list>
/// </summary>
/// <remarks>
/// Availability is checked at call time rather than at DI resolution so that
/// transient upstream outages don't permanently remove a provider from the
/// rotation. Each provider caches its own health state.
/// </remarks>
public sealed class AddressLookupService
{
    private readonly IReadOnlyList<IAddressLookupProvider> _providers;
    private readonly ILogger<AddressLookupService> _logger;

    /// <summary>Initialises a new instance of the <see cref="AddressLookupService"/> class.</summary>
    public AddressLookupService(
        IEnumerable<IAddressLookupProvider> providers,
        ILogger<AddressLookupService> logger)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolve a postcode against the best available provider for the target
    /// country. Never throws on upstream failure — the caller always gets a
    /// usable <see cref="AddressLookupResult"/> (possibly with
    /// <see cref="AddressLookupResult.IsValid"/> = <c>false</c>).
    /// </summary>
    public async Task<AddressLookupResult> LookupAsync(
        string postcode,
        string? countryHint = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(postcode))
        {
            return NoProviderResult(string.Empty);
        }

        var country = countryHint ?? "GB";
        var provider = await SelectProviderAsync(country, cancellationToken);

        if (provider is null)
        {
            _logger.LogDebug(
                "No address lookup provider available for country {Country}; returning graceful-degradation result",
                country);
            return NoProviderResult(postcode);
        }

        return await provider.LookupAsync(postcode, countryHint, cancellationToken);
    }

    /// <summary>
    /// List configured providers with their current availability. Used by
    /// the <c>GET /api/address-lookup/providers</c> endpoint so the form
    /// renderer knows which control to show.
    /// </summary>
    public async Task<IReadOnlyList<AddressLookupProviderInfo>> ListProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<AddressLookupProviderInfo>(_providers.Count);
        foreach (var provider in _providers)
        {
            var available = await SafeAvailableAsync(provider, cancellationToken);
            results.Add(new AddressLookupProviderInfo
            {
                Name = provider.ProviderName,
                Capability = provider.Capability,
                SupportedCountries = provider.SupportedCountries,
                Available = available
            });
        }
        return results;
    }

    private async Task<IAddressLookupProvider?> SelectProviderAsync(string country, CancellationToken ct)
    {
        // Step 1: candidates that support the country
        var candidates = _providers
            .Where(p => p.SupportedCountries.Any(c => string.Equals(c, country, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0) return null;

        // Step 2: prefer FullAddress capability
        foreach (var capability in new[] { AddressLookupCapability.FullAddress, AddressLookupCapability.ValidateOnly })
        {
            foreach (var candidate in candidates.Where(c => c.Capability == capability))
            {
                if (await SafeAvailableAsync(candidate, ct))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private async Task<bool> SafeAvailableAsync(IAddressLookupProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.IsAvailableAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Provider {Provider} health check threw; treating as unavailable",
                provider.ProviderName);
            return false;
        }
    }

    private static AddressLookupResult NoProviderResult(string postcode) =>
        new()
        {
            Postcode = postcode,
            IsValid = false,
            Provider = "none",
            Capability = AddressLookupCapability.ValidateOnly
        };
}
