// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AddressLookup;

/// <summary>
/// Diagnostic description of a configured address lookup provider. Returned
/// by <c>GET /api/address-lookup/providers</c> so the form renderer can
/// decide which UI to show.
/// </summary>
public sealed record AddressLookupProviderInfo
{
    /// <summary>Provider identifier, e.g. <c>"postcodes.io"</c>, <c>"os-places"</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Capability tier the provider supports.</summary>
    public required AddressLookupCapability Capability { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country codes the provider supports.</summary>
    public required IReadOnlyList<string> SupportedCountries { get; init; }

    /// <summary>Result of the most recent health check on the provider.</summary>
    public required bool Available { get; init; }
}
