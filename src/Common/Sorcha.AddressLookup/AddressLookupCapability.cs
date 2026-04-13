// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AddressLookup;

/// <summary>
/// How much an address lookup provider can return for a given postcode.
/// </summary>
public enum AddressLookupCapability
{
    /// <summary>
    /// Provider can confirm postcode validity and return coarse metadata
    /// (town, region, country, lat/long) but NOT individual street addresses.
    /// Example: postcodes.io for UK postcodes. The form renderer exposes this
    /// as "type postcode → town / region autofills; line1 / line2 stay manual".
    /// </summary>
    ValidateOnly = 0,

    /// <summary>
    /// Provider returns full street-address candidates for the postcode.
    /// Example: OS Places API (Royal Mail PAF data). The form renderer exposes
    /// this as "type postcode → 'Find address' button → pick from modal →
    /// all sibling fields autofill".
    /// </summary>
    FullAddress = 1
}
