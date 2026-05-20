// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// A set of trusted X.509 root certificates and the CRL state consulted at validation time
/// (feature 135). Used by the x509-tenant and trustlist trust sources.
/// </summary>
public class TrustAnchorSet
{
    /// <summary>Trusted root certificates, DER-encoded.</summary>
    public IReadOnlyList<byte[]> Roots { get; set; } = [];

    /// <summary>The CRL version / thisUpdate consulted, when known (for evidence).</summary>
    public string? CrlVersion { get; set; }

    /// <summary>An identifier for the anchor set consulted (e.g. trust-list snapshot id).</summary>
    public string? AnchorSetId { get; set; }

    /// <summary>Freshness timestamp of the anchor set, when known (for evidence).</summary>
    public DateTimeOffset? Freshness { get; set; }

    /// <summary>Whether revocation (CRL) should be checked during chain build.</summary>
    public bool CheckRevocation { get; set; }
}

/// <summary>
/// Engine-local seam supplying trusted X.509 roots for chain validation (feature 135).
/// Service-layer adapters implement this over the tenant CA (<c>ITrustProvider</c>) for the
/// x509-tenant source and over the trust-list provider for the trustlist source. Returning
/// null means "no anchors available" — the x509 source then fails closed.
/// </summary>
public interface ITenantTrustAnchorProvider
{
    /// <summary>
    /// Returns the trusted roots for the given anchor identifier (null/empty = tenant default),
    /// or null when none are available.
    /// </summary>
    Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken cancellationToken = default);
}
