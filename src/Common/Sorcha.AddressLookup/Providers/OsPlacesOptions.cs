// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AddressLookup.Providers;

/// <summary>
/// Configuration for <see cref="OsPlacesProvider"/>. Bound from the
/// <c>Tenant:AddressLookup:OsPlaces</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// The Ordnance Survey Places API requires a licensed API key. When
/// <see cref="ApiKey"/> is unset the provider is not registered (the DI
/// extension checks this at startup), so deployments without an OS Places
/// licence fall through to the default <see cref="PostcodesIoProvider"/>.
/// </remarks>
public sealed class OsPlacesOptions
{
    /// <summary>Configuration section name bound to this options type.</summary>
    public const string SectionName = "Tenant:AddressLookup:OsPlaces";

    /// <summary>
    /// API key issued by Ordnance Survey. The provider is not registered
    /// when this is null or empty.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL for the OS Places API. Defaults to the production endpoint;
    /// override for sandboxes or regional endpoints.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.os.uk/search/places/v1/";
}
