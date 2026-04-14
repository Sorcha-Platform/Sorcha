// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.AddressLookup;

/// <summary>
/// HTTP transport contract for the Tenant Service <c>/api/address-lookup/*</c>
/// endpoints. Exists so that Razor components (notably
/// <c>PostcodeLookupRenderer.razor</c>) can resolve an auth-wrapped client
/// via DI instead of injecting the default unauthenticated <c>HttpClient</c>.
/// <para>
/// Prior to Feature 103 wave 14 the renderer injected the default
/// <c>HttpClient</c>, which is registered as a bare <c>HttpClient</c>
/// (no <c>AuthenticatedHttpMessageHandler</c>) specifically for
/// <c>AuthenticationService</c> to avoid a circular DI dependency. Any
/// other component that did <c>@inject HttpClient Http</c> silently
/// inherited the unauthenticated client and hit 401 at the API Gateway's
/// <c>RequireAuthenticated</c> policy. Routing through this typed client
/// closes that footgun.
/// </para>
/// </summary>
public interface IAddressLookupClient
{
    /// <summary>
    /// Calls <c>GET /api/address-lookup/providers</c>. Returns the available
    /// provider list so the renderer can pick <c>FullAddress</c> vs
    /// <c>ValidateOnly</c> mode. Returns <c>null</c> on any failure — the
    /// caller falls back to a plain text input when discovery fails.
    /// </summary>
    Task<IReadOnlyList<ProviderInfo>?> GetProvidersAsync(CancellationToken ct = default);

    /// <summary>
    /// Calls <c>POST /api/address-lookup/postcode</c>. Returns the lookup
    /// result (candidates for <c>FullAddress</c>, metadata for
    /// <c>ValidateOnly</c>). Returns <c>null</c> on non-success so the
    /// caller can distinguish transient failure from an invalid postcode.
    /// </summary>
    Task<LookupResult?> LookupPostcodeAsync(string postcode, CancellationToken ct = default);
}

/// <summary>
/// Describes an address-lookup provider registered in the Tenant Service.
/// </summary>
public sealed class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public List<string> SupportedCountries { get; set; } = new();
    public bool Available { get; set; }
}

/// <summary>
/// Postcode lookup response. Either <see cref="Candidates"/> is populated
/// (FullAddress providers) or <see cref="Metadata"/> is populated
/// (ValidateOnly providers) — never both.
/// </summary>
public sealed class LookupResult
{
    public string Postcode { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public LookupMetadata? Metadata { get; set; }
    public List<AddressCandidate>? Candidates { get; set; }
}

/// <summary>
/// Coarse-grained location metadata returned by ValidateOnly providers.
/// </summary>
public sealed class LookupMetadata
{
    public string? Town { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// A single address candidate returned by FullAddress providers. Each
/// candidate maps 1:1 to a row the user can pick from the renderer dropdown.
/// </summary>
public sealed class AddressCandidate
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string Town { get; set; } = string.Empty;
    public string? Region { get; set; }
    public string Postcode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
}
