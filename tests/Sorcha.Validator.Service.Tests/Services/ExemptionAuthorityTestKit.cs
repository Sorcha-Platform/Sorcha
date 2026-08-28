// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Provenance;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models.Genesis;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Shared scaffolding for Feature 196 (#1591) exemption-authority tests.
/// </summary>
/// <remarks>
/// <b>Real hashing only.</b> The fingerprint here is computed with the same
/// <see cref="GenesisFileLoader.ComputeFingerprint"/> production uses. #1587's tests stubbed
/// <c>IHashProvider</c> to a fixed array, which made every hash compare equal by construction and
/// hid the defect under test behind a green suite — a stubbed digest cannot fail a digest comparison.
/// </remarks>
internal static class ExemptionAuthorityTestKit
{
    /// <summary>A trust anchor pinned to the fingerprint of <paramref name="genesisPublicKey"/>.</summary>
    public static INodeTrustAnchor AnchorFor(byte[] genesisPublicKey)
    {
        var anchor = new Mock<INodeTrustAnchor>();
        anchor.SetupGet(a => a.IsKnown).Returns(true);
        anchor.SetupGet(a => a.NetworkId).Returns("sorcha-test");
        anchor.SetupGet(a => a.GenesisPublicKeyFingerprint)
              .Returns(GenesisFileLoader.ComputeFingerprint(genesisPublicKey));
        anchor.SetupGet(a => a.GenesisPayloadHash).Returns("payload-hash");
        return anchor.Object;
    }

    /// <summary>A node that holds no anchor — it cannot tell, so it must withhold (FR-007).</summary>
    public static INodeTrustAnchor NoAnchor()
    {
        var anchor = new Mock<INodeTrustAnchor>();
        anchor.SetupGet(a => a.IsKnown).Returns(false);
        anchor.SetupGet(a => a.GenesisPublicKeyFingerprint).Returns((string?)null);
        return anchor.Object;
    }

    /// <summary>Builds a resolver over the supplied anchor and roster service.</summary>
    public static ExemptionAuthorityResolver Resolver(
        INodeTrustAnchor anchor, IGovernanceRosterService rosterService) =>
        new(anchor, rosterService, new Mock<ILogger<ExemptionAuthorityResolver>>().Object);
}
