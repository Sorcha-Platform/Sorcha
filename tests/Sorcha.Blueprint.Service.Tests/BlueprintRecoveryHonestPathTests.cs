// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Feature 138 US4 (T051) — a blueprint with no sealed digest is not stored (no provenance), while a
/// blueprint whose canonical hash matches the sealed digest is accepted.
/// </summary>
public sealed class BlueprintRecoveryHonestPathTests
{
    private const string Blueprint = """{"title":"Permit","participants":[{"id":"applicant"}],"actions":[]}""";

    [Fact]
    public void TryVerifyProvenance_NoSealedHash_Rejected_NoProvenance()
    {
        var ok = BlueprintRecoveryService.TryVerifyProvenance(Blueprint, contentHash: "", out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("no_provenance");
    }

    [Fact]
    public void TryVerifyProvenance_MatchingSealedHash_Accepted()
    {
        // This is the producer→consumer contract: the digest the publish path sealed is exactly what
        // BlueprintContentHash.Compute yields for the same JSON.
        var sealedHash = BlueprintContentHash.Compute(Blueprint);

        var ok = BlueprintRecoveryService.TryVerifyProvenance(Blueprint, sealedHash, out var reason);

        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void Compute_IsStableAcrossWhitespaceVariants()
    {
        // Canonicalisation means producer and consumer agree regardless of incidental formatting.
        const string pretty = """
            {
                "title": "Permit",
                "participants": [ { "id": "applicant" } ],
                "actions": []
            }
            """;

        BlueprintContentHash.Compute(pretty).Should().Be(BlueprintContentHash.Compute(Blueprint));
    }
}
