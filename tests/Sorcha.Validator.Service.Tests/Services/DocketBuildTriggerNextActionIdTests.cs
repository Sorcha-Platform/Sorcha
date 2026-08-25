// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 145 (T024): the authoritative metadata projection in DocketBuildTriggerService carries the
/// VALIDATED routing decision (submission metadata key <c>routingDecision</c>, canonical JSON) onto the
/// typed <c>TransactionMetaData.RoutingDecision</c>, so every node's InstanceProjector folds the full
/// next-action set. These tests pin the parse/key contract that replaced the singular <c>nextActionId</c>
/// hint.
/// </summary>
public class DocketBuildTriggerNextActionIdTests
{
    private static string CanonicalDecision(RoutingDecision decision)
        => JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);

    [Fact]
    public void ResolveRoutingDecision_WithCanonicalJson_ReturnsFullDecision()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 1,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [new ActionRef { ActionId = 2 }, new ActionRef { ActionId = 3 }],
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "sig" },
        };
        var metadata = new Dictionary<string, string> { ["routingDecision"] = CanonicalDecision(decision) };

        var result = DocketBuildTriggerService.ResolveRoutingDecision(metadata);

        result.Should().NotBeNull();
        result!.CompletedActionId.Should().Be(1);
        result.NextActions.Select(a => a.ActionId).Should().Equal(2, 3);
    }

    [Fact]
    public void ResolveRoutingDecision_KeyAbsent_ReturnsNull()
    {
        var metadata = new Dictionary<string, string> { ["instanceId"] = "abc" };

        var result = DocketBuildTriggerService.ResolveRoutingDecision(metadata);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void ResolveRoutingDecision_AbsentOrMalformed_ReturnsNull(string raw)
    {
        var metadata = new Dictionary<string, string> { ["routingDecision"] = raw };

        var result = DocketBuildTriggerService.ResolveRoutingDecision(metadata);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveRoutingDecision_TerminalBranch_ReturnsEmptyNextActions()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 5,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [],
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "sig" },
        };
        var metadata = new Dictionary<string, string> { ["routingDecision"] = CanonicalDecision(decision) };

        var result = DocketBuildTriggerService.ResolveRoutingDecision(metadata);

        result.Should().NotBeNull();
        result!.CompletedActionId.Should().Be(5);
        result.NextActions.Should().BeEmpty();
    }
}
