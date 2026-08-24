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
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
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
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
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
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
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
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [new ActionRef { ActionId = 8 }],
        };
        var withAttestation = new RoutingDecision
        {
            CompletedActionId = 7,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
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
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [new ActionRef { ActionId = 2 }, new ActionRef { ActionId = 3 }],
        };
        var b = new RoutingDecision
        {
            CompletedActionId = 1,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
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

    // ---------------------------------------------------------------------------------------------
    // Feature 184 — the decision-notice carrier: the taken route's id and a non-sensitive reason code
    // ride the signed decision so the recipient's node can render a notice from the replicated
    // blueprint without decrypting payload.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RoutingDecision_CanonicalRoundTrip_PreservesRouteIdAndReasonCode()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 2,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [],
            RouteId = "rejected-terminal",
            ReasonCode = "postcode-not-found",
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "c2ln" },
        };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);
        var round = JsonSerializer.Deserialize<RoutingDecision>(json, RegisterSerializationOptions.Canonical)!;

        Assert.Contains("\"routeId\":\"rejected-terminal\"", json);
        Assert.Contains("\"reasonCode\":\"postcode-not-found\"", json);
        Assert.Equal("rejected-terminal", round.RouteId);
        Assert.Equal("postcode-not-found", round.ReasonCode);
    }

    [Fact]
    public void RoutingDecision_AbsentRouteIdAndReasonCode_DeserializeToNull()
    {
        // A transaction sealed before Feature 184 carries neither field — it must deserialize
        // cleanly and simply produce no notice.
        const string legacyJson = """{"completedActionId":2,"nextActions":[]}""";

        var round = JsonSerializer.Deserialize<RoutingDecision>(legacyJson, RegisterSerializationOptions.Canonical)!;

        Assert.Null(round.RouteId);
        Assert.Null(round.ReasonCode);
    }

    [Fact]
    public void ComputeSignableBytes_IncludesRouteId_SoItCannotBeAlteredInTransit()
    {
        var a = new RoutingDecision { CompletedActionId = 2, NextActions = [], RouteId = "rejected-terminal" };
        var b = new RoutingDecision { CompletedActionId = 2, NextActions = [], RouteId = "approved-to-claim" };

        // If RouteId were omitted from the object ComputeSignableBytes() rebuilds, these would be
        // byte-identical — the field would ride the wire unauthenticated while appearing signed.
        Assert.NotEqual(a.ComputeSignableBytes(), b.ComputeSignableBytes());
        Assert.Contains("rejected-terminal", Encoding.UTF8.GetString(a.ComputeSignableBytes()));
    }

    [Fact]
    public void ComputeSignableBytes_IncludesReasonCode_SoTheReasonCannotBeAlteredInTransit()
    {
        var a = new RoutingDecision { CompletedActionId = 2, NextActions = [], ReasonCode = "postcode-not-found" };
        var b = new RoutingDecision { CompletedActionId = 2, NextActions = [], ReasonCode = "profanity" };

        Assert.NotEqual(a.ComputeSignableBytes(), b.ComputeSignableBytes());
        Assert.Contains("postcode-not-found", Encoding.UTF8.GetString(a.ComputeSignableBytes()));
    }

    // ---------------------------------------------------------------------------------------------
    // Feature 194 — the pin: the executable definition this action was executed against.
    //
    // NOTE these per-field tests are a hand-written list and cannot catch the NEXT field added to
    // the record. RoutingDecisionSigningCoverageTests is the reflection-driven guard that can; these
    // remain because they state the intent of each specific field in a readable way.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputeSignableBytes_IncludesTheBlueprintPin_SoAnInstanceCannotBeMovedOntoAnotherDefinition()
    {
        var a = new RoutingDecision { CompletedActionId = 2, NextActions = [], BlueprintDefinitionTxId = new string('a', 64) };
        var b = new RoutingDecision { CompletedActionId = 2, NextActions = [], BlueprintDefinitionTxId = new string('b', 64) };

        // Were the pin omitted from the rebuild, these would be byte-identical — and a sender could
        // rewrite which definition their action claims to have run against, with a signature that
        // still verified.
        Assert.NotEqual(a.ComputeSignableBytes(), b.ComputeSignableBytes());
        Assert.Contains(new string('a', 64), Encoding.UTF8.GetString(a.ComputeSignableBytes()));
    }

    [Fact]
    public void RoutingDecision_AbsentPin_DeserializesToNull_AndSerializesIdenticallyToAPreFeatureDecision()
    {
        // A transaction sealed before Feature 194 carries no pin. It must deserialize cleanly, and a
        // null pin must not change a single byte of the canonical form — otherwise every existing
        // signature would stop verifying the moment this field shipped.
        const string legacyJson = """{"completedActionId":2,"nextActions":[]}""";

        var legacy = JsonSerializer.Deserialize<RoutingDecision>(legacyJson, RegisterSerializationOptions.Canonical)!;
        Assert.Null(legacy.BlueprintDefinitionTxId);

        var reserialised = JsonSerializer.Serialize(legacy, RegisterSerializationOptions.Canonical);
        Assert.DoesNotContain("blueprintDefinitionTxId", reserialised);

        var freshWithNullPin = new RoutingDecision { CompletedActionId = 2, NextActions = [] };
        Assert.Equal(legacy.ComputeSignableBytes(), freshWithNullPin.ComputeSignableBytes());
    }

    [Fact]
    public void RoutingDecision_CanonicalRoundTrip_PreservesThePin()
    {
        var decision = new RoutingDecision
        {
            CompletedActionId = 1,
            NextActions = [new ActionRef { ActionId = 2 }],
            BlueprintDefinitionTxId = "9f2c00112233445566778899aabbccddeeff00112233445566778899aabbccdd",
            Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "c2ln" },
        };

        var json = JsonSerializer.Serialize(decision, RegisterSerializationOptions.Canonical);
        var round = JsonSerializer.Deserialize<RoutingDecision>(json, RegisterSerializationOptions.Canonical)!;

        Assert.Contains("\"blueprintDefinitionTxId\":", json);
        Assert.Equal(decision.BlueprintDefinitionTxId, round.BlueprintDefinitionTxId);
    }
}
