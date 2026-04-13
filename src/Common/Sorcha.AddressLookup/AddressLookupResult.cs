// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AddressLookup;

/// <summary>
/// Result of a single postcode lookup. Wire shape for the
/// <c>POST /api/address-lookup/postcode</c> endpoint.
/// </summary>
/// <remarks>
/// When <see cref="IsValid"/> is <c>false</c> the callers are expected to fall
/// back to plain text entry. A "no provider configured" graceful-degradation
/// result is modelled as <c>IsValid = false</c> with <c>Provider = "none"</c>
/// so the form renderer can distinguish "we looked and found nothing" from
/// "nobody looked" while still taking the same fallback path.
/// </remarks>
public sealed record AddressLookupResult
{
    /// <summary>The normalised postcode (uppercase, single space). Always echoed back.</summary>
    public required string Postcode { get; init; }

    /// <summary>Whether the postcode is recognised by the provider.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Provider identifier (e.g. <c>"postcodes.io"</c>), or <c>"none"</c> when graceful degradation kicked in.</summary>
    public required string Provider { get; init; }

    /// <summary>Capability the provider reports for this country.</summary>
    public required AddressLookupCapability Capability { get; init; }

    /// <summary>
    /// Coarse metadata returned by a <see cref="AddressLookupCapability.ValidateOnly"/> provider.
    /// Null for <see cref="AddressLookupCapability.FullAddress"/> providers (they use <see cref="Candidates"/>).
    /// </summary>
    public AddressLookupMetadata? Metadata { get; init; }

    /// <summary>
    /// Full-address candidates returned by a <see cref="AddressLookupCapability.FullAddress"/> provider.
    /// Null or empty for validate-only providers.
    /// </summary>
    public IReadOnlyList<AddressCandidate>? Candidates { get; init; }
}

/// <summary>Coarse locality metadata returned by a validate-only provider.</summary>
public sealed record AddressLookupMetadata
{
    /// <summary>Town or city name, e.g. <c>"Edinburgh"</c>.</summary>
    public string? Town { get; init; }

    /// <summary>Administrative region, e.g. <c>"Scotland"</c>.</summary>
    public string? Region { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. <c>"GB"</c>.</summary>
    public string? Country { get; init; }

    /// <summary>Latitude (WGS84), if the provider returns one.</summary>
    public double? Latitude { get; init; }

    /// <summary>Longitude (WGS84), if the provider returns one.</summary>
    public double? Longitude { get; init; }
}

/// <summary>A full street-address candidate returned by a full-address provider.</summary>
public sealed record AddressCandidate
{
    /// <summary>First address line (required).</summary>
    public required string Line1 { get; init; }

    /// <summary>Second address line (flat number, building name, etc.).</summary>
    public string? Line2 { get; init; }

    /// <summary>Town or city.</summary>
    public required string Town { get; init; }

    /// <summary>Administrative region, if applicable.</summary>
    public string? Region { get; init; }

    /// <summary>The postcode that resolved to this candidate.</summary>
    public required string Postcode { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public required string Country { get; init; }

    /// <summary>
    /// Human-readable one-line label for the candidate picker UI, e.g.
    /// <c>"Flat 4, EH1 House, 1 Royal Mile, Edinburgh, EH1 1YZ"</c>.
    /// </summary>
    public required string DisplayLabel { get; init; }
}
