// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Feature 195 — the producer→consumer contract for recovered definitions: a definition with no
/// transaction id is not stored (no provenance), and one whose recomputed publication id matches the
/// transaction that carried it is accepted.
/// </summary>
/// <remarks>
/// Was Feature 138 US4 (T051), which checked the same property against a separately-sealed
/// <c>contentHash</c>. Corrected rather than deleted — the contract is unchanged in intent, but the
/// digest and the identity are now one value, so there is no longer a sibling field that could
/// disagree with the content it vouches for.
/// </remarks>
public sealed class BlueprintRecoveryHonestPathTests
{
    private const string Register = "b21d862d7aee471c89f844defb7fd108";
    private const string BlueprintId = "permit-v1";
    private const string Blueprint = """{"title":"Permit","participants":[{"id":"applicant"}],"actions":[]}""";

    [Fact]
    public void TryVerifyProvenance_NoTransactionId_Rejected_NoProvenance()
    {
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, Blueprint, publicationTxId: "", out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("no_provenance");
    }

    [Fact]
    public void TryVerifyProvenance_MatchingPublicationId_Accepted()
    {
        // The producer→consumer contract: the id the publish path assigned is exactly what
        // BlueprintPublicationId.ComputeFromDefinition yields for the same definition on the same
        // register.
        var txId = BlueprintPublicationId.ComputeFromDefinition(Register, BlueprintId, Blueprint);

        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            Register, BlueprintId, Blueprint, txId, out var reason);

        ok.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void PublicationId_IsStableAcrossWhitespaceAndKeyOrderVariants()
    {
        // Producer and consumer agree regardless of incidental formatting. Whitespace does not
        // survive a parse; key order does, and is normalised by the canonicaliser — which is exactly
        // why the canonicaliser exists rather than a set of serializer options.
        const string prettyAndReordered = """
            {
                "actions": [],
                "participants": [ { "id": "applicant" } ],
                "title": "Permit"
            }
            """;

        BlueprintPublicationId.ComputeFromDefinition(Register, BlueprintId, prettyAndReordered)
            .Should().Be(BlueprintPublicationId.ComputeFromDefinition(Register, BlueprintId, Blueprint));
    }
}
