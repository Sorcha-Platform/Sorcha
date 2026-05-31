// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Foundational tests for the Feature 145 carried <see cref="RoutingDecision"/> — canonical
/// serialization round-trip, full-set preservation (parallel branches), and the stable,
/// attestation-free signable bytes the producer signs and the validator re-derives.
/// </summary>
public class RoutingDecisionTests
{
    [Fact]
    public void RoutingDecision_CanonicalRoundTrip_PreservesAllFields()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 1,
            NextActions =
            [
                new ActionRef { ActionId = 2 },
                new ActionRef { ActionId = 3, BranchKey = "approve" },
            ],
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "c2ln" },
        };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);
        var round = JsonSerializer.Deserialize<RoutingDecision>(json, RegisterSerializationOptions.Canonical)!;

        Assert.Equal(1, round.CompletedActionId);
        Assert.Equal(2, round.NextActions.Count);
        Assert.Equal(2, round.NextActions[0].ActionId);
        Assert.Null(round.NextActions[0].BranchKey);
        Assert.Equal(3, round.NextActions[1].ActionId);
        Assert.Equal("approve", round.NextActions[1].BranchKey);
        Assert.Equal(AttestationKind.SenderSigned, round.Attestation!.Kind);
        Assert.Equal("c2ln", round.Attestation.Signature);
    }

    [Fact]
    public void RoutingDecision_CanonicalJson_UsesStableCamelCasePropertyNames()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 5,
            NextActions = [new ActionRef { ActionId = 6 }],
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "abc" },
        };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);

        // The #881 relay lesson: property names must be wire-stable, not PascalCase.
        Assert.Contains("\"completedActionId\":5", json);
        Assert.Contains("\"nextActions\":", json);
        Assert.Contains("\"actionId\":6", json);
        Assert.Contains("\"attestation\":", json);
        Assert.Contains("\"kind\":\"SenderSigned\"", json);
        Assert.DoesNotContain("CompletedActionId", json);
    }

    [Fact]
    public void RoutingDecision_FullSet_PreservesParallelBranchesEndToEnd()
    {
        // Regression for the singular-hint collapse: a multi-branch decision must round-trip
        // every branch, in order.
        var decision = new RoutingDecision
        {
            CompletedActionId = 1,
            NextActions =
            [
                new ActionRef { ActionId = 10, BranchKey = "a" },
                new ActionRef { ActionId = 20, BranchKey = "b" },
                new ActionRef { ActionId = 30, BranchKey = "c" },
            ],
        };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);
        var round = JsonSerializer.Deserialize<RoutingDecision>(json, RegisterSerializationOptions.Canonical)!;

        Assert.Equal(new[] { 10, 20, 30 }, round.NextActions.Select(a => a.ActionId).ToArray());
        Assert.Equal(new[] { "a", "b", "c" }, round.NextActions.Select(a => a.BranchKey).ToArray());
    }

    [Fact]
    public void ComputeSignableBytes_ExcludesAttestation_SoSignatureNeverSignsItself()
    {
        var withoutAttestation = new RoutingDecision
        {
            CompletedActionId = 7,
            NextActions = [new ActionRef { ActionId = 8 }],
        };
        var withAttestation = new RoutingDecision
        {
            CompletedActionId = 7,
            NextActions = [new ActionRef { ActionId = 8 }],
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "deadbeef" },
        };

        var bytesWithout = withoutAttestation.ComputeSignableBytes();
        var bytesWith = withAttestation.ComputeSignableBytes();

        // The signable bytes are identical regardless of the attestation: the signature is over
        // the attestation-free decision, so a verifier can re-derive them from the carried decision.
        Assert.Equal(bytesWithout, bytesWith);
        var text = Encoding.UTF8.GetString(bytesWith);
        Assert.DoesNotContain("deadbeef", text);
        Assert.DoesNotContain("attestation", text);
    }

    [Fact]
    public void ComputeSignableBytes_IsDeterministic_AcrossInstances()
    {
        var a = new RoutingDecision
        {
            CompletedActionId = 1,
            NextActions = [new ActionRef { ActionId = 2 }, new ActionRef { ActionId = 3 }],
        };
        var b = new RoutingDecision
        {
            CompletedActionId = 1,
            NextActions = [new ActionRef { ActionId = 2 }, new ActionRef { ActionId = 3 }],
        };

        Assert.Equal(a.ComputeSignableBytes(), b.ComputeSignableBytes());
    }

    [Fact]
    public void RoutingDecision_EmptyNextActions_RepresentsTerminalBranch()
    {
        var decision = new RoutingDecision { CompletedActionId = 9, NextActions = [] };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);
        var round = JsonSerializer.Deserialize<RoutingDecision>(json, RegisterSerializationOptions.Canonical)!;

        Assert.Empty(round.NextActions);
    }
}
