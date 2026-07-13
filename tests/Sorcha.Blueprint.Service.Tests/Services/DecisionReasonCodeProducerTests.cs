// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 184 — the producer side of the decision notice. The deciding participant's node is the
/// only place the reason is ever read from the payload: it is submitting that payload, so it can
/// plainly read it. What it publishes onward is a non-sensitive <b>code</b>, stamped onto the
/// sender-signed routing decision. Everything downstream — above all the recipient's node, which can
/// not read the payload at all — works from that code and the replicated blueprint.
/// </summary>
public class DecisionReasonCodeProducerTests
{
    private const string RejectRouteId = "rejected-terminal";

    private static ActionModel DecisionActionWith(params Route[] routes) => new()
    {
        Id = 2,
        Title = "Verify application",
        Sender = "analyst",
        Routes = routes.ToList(),
    };

    private static Route RejectRoute(string? reasonCodeField = "/reasonCode") => new()
    {
        Id = RejectRouteId,
        NextActionIds = [],
        DecisionNotice = new DecisionNotice
        {
            RecipientParticipantId = "citizen",
            ReasonCodeField = reasonCodeField,
            Title = "AIAS could not assure your identity",
            Reasons = new Dictionary<string, string> { ["postcode-not-found"] = "…" },
            FallbackMessage = "Your application was not approved.",
        },
    };

    [Fact]
    public void ResolveDecisionReasonCode_TakenRouteDeclaresANotice_ResolvesTheCodeFromThePayload()
    {
        var payload = new Dictionary<string, object>
        {
            ["decision"] = "rejected",
            ["reasonCode"] = "postcode-not-found",
            ["verificationNotes"] = "AIAS could not locate that address on any map.",
        };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(RejectRoute()), RejectRouteId, payload);

        code.Should().Be("postcode-not-found");
    }

    [Fact]
    public void ResolveDecisionReasonCode_TakenRouteDeclaresNoNotice_ReturnsNull()
    {
        // The approve route: the credential's arrival is the notification. Nothing to codify.
        var approveRoute = new Route { Id = "approved-to-claim", NextActionIds = [3] };
        var payload = new Dictionary<string, object> { ["reasonCode"] = "postcode-not-found" };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(approveRoute, RejectRoute()), "approved-to-claim", payload);

        code.Should().BeNull();
    }

    [Fact]
    public void ResolveDecisionReasonCode_NoticeDeclaresNoReasonCodeField_ReturnsNull()
    {
        // A notice with no per-reason variation always renders its fallback message.
        var payload = new Dictionary<string, object> { ["reasonCode"] = "postcode-not-found" };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(RejectRoute(reasonCodeField: null)), RejectRouteId, payload);

        code.Should().BeNull();
    }

    [Fact]
    public void ResolveDecisionReasonCode_PointerDoesNotResolve_ReturnsNull()
    {
        // The decider submitted no code. The recipient still gets the notice — with the fallback.
        var payload = new Dictionary<string, object> { ["decision"] = "rejected" };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(RejectRoute()), RejectRouteId, payload);

        code.Should().BeNull();
    }

    [Fact]
    public void ResolveDecisionReasonCode_NoRouteMatched_ReturnsNull()
    {
        var payload = new Dictionary<string, object> { ["reasonCode"] = "postcode-not-found" };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(RejectRoute()), matchedRouteId: null, payload);

        code.Should().BeNull();
    }

    [Fact]
    public void ResolveDecisionReasonCode_NestedPointer_Resolves()
    {
        var route = RejectRoute(reasonCodeField: "/assurance/reasonCode");
        var payload = new Dictionary<string, object>
        {
            ["assurance"] = JsonSerializer.Deserialize<JsonElement>("""{"reasonCode":"profanity"}"""),
        };

        var code = ActionExecutionService.ResolveDecisionReasonCode(
            DecisionActionWith(route), RejectRouteId, payload);

        code.Should().Be("profanity");
    }
}
