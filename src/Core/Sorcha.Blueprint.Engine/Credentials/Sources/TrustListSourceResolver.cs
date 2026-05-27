// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials.Sources;

/// <summary>
/// Trust-list trust source (feature 135, US2). Validates a credential's x5c chain against an
/// operator-curated trust-list snapshot rather than the tenant's own CA. Reuses the X.509 chain
/// build of <see cref="X509TenantTrustSourceResolver"/>; the only differences are the source kind
/// and that the anchor set is requested by the policy's <see cref="TrustSourceRef.TrustListId"/>.
/// The anchor provider supplied here is a service-layer adapter over <c>ITrustListProvider</c> that
/// maps a snapshot to a <see cref="TrustAnchorSet"/> (recording the snapshot id + freshness in the
/// evidence). The engine stays free of the network-bound provider.
/// </summary>
public sealed class TrustListSourceResolver(ITenantTrustAnchorProvider anchorProvider)
    : X509TenantTrustSourceResolver(anchorProvider)
{
    /// <inheritdoc />
    public override TrustSourceKind Kind => TrustSourceKind.TrustList;

    /// <summary>Requests the anchor set identified by the policy's trust-list id.</summary>
    protected override string? AnchorId(TrustSourceRef source) => source.TrustListId;
}
