// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 183 (US2) — the routing → decision-notice resolution. When the agent's terminal
/// reject route (carrying <c>x-decision-notice</c>) is taken, exactly one durable notice is
/// written to the starting participant, carrying the on-brand reason. A route without the
/// annotation writes nothing, and a write failure never surfaces (sealing/routing unaffected).
/// </summary>
public class DecisionNoticeDispatcherTests
{
    private const string RejectCondition = """{ "==": [ { "var": "decision" }, "rejected" ] }""";

    private static Sorcha.Blueprint.Models.Action RejectAction(DecisionNotice? notice)
    {
        return new Sorcha.Blueprint.Models.Action
        {
            Id = 2,
            Title = "Verify Assured Identity Application",
            Routes = new List<Route>
            {
                new()
                {
                    Id = "approved-to-claim",
                    NextActionIds = new[] { 3 },
                    Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }"""),
                },
                new()
                {
                    Id = "rejected-terminal",
                    NextActionIds = Array.Empty<int>(),
                    IsDefault = true,
                    Condition = JsonNode.Parse(RejectCondition),
                    DecisionNotice = notice,
                },
            },
        };
    }

    private static DecisionNotice AiasNotice() => new()
    {
        RecipientParticipantId = "citizen",
        ReasonField = "/verificationNotes",
        Title = "AIAS could not assure your identity",
        Severity = "Warning",
    };

    // Reject route taken → RoutingResult carries no next actions.
    private static readonly RoutingResult RejectRouting = new() { NextActions = new List<NextAction>() };

    private static readonly Dictionary<string, object> RejectPayload = new()
    {
        ["decision"] = "rejected",
        ["verificationNotes"] = "AIAS needs a verified email before it can assure you.",
    };

    private sealed record Written(string Wallet, string InstanceId, string ActionId, string Title, string Reason, string Severity);

    [Fact]
    public async Task DispatchAsync_TakenRejectRouteWithNotice_WritesOneNoticeCarryingTheReason()
    {
        var writes = new List<Written>();
        DecisionNoticeDispatcher.WriteDecision write = (w, iid, aid, t, r, sev, _) =>
        {
            writes.Add(new Written(w, iid, aid, t, r, sev));
            return Task.CompletedTask;
        };

        await DecisionNoticeDispatcher.DispatchAsync(
            RejectAction(AiasNotice()),
            "instance-1",
            participantWallets: new Dictionary<string, string> { ["citizen"] = "ws1citizen" },
            mergedData: RejectPayload,
            routingResult: RejectRouting,
            conditionMatches: _ => true,
            write: write,
            logger: NullLogger.Instance,
            ct: CancellationToken.None);

        writes.Should().ContainSingle();
        var w = writes[0];
        w.Wallet.Should().Be("ws1citizen");
        w.InstanceId.Should().Be("instance-1");
        w.ActionId.Should().Be("2");
        w.Title.Should().Be("AIAS could not assure your identity");
        w.Reason.Should().Be("AIAS needs a verified email before it can assure you.");
        w.Severity.Should().Be("Warning");
    }

    [Fact]
    public async Task DispatchAsync_RouteWithoutNotice_WritesNothing()
    {
        var writes = new List<Written>();
        DecisionNoticeDispatcher.WriteDecision write = (w, iid, aid, t, r, sev, _) =>
        {
            writes.Add(new Written(w, iid, aid, t, r, sev));
            return Task.CompletedTask;
        };

        await DecisionNoticeDispatcher.DispatchAsync(
            RejectAction(notice: null),
            "instance-1",
            new Dictionary<string, string> { ["citizen"] = "ws1citizen" },
            RejectPayload,
            RejectRouting,
            _ => true,
            write,
            NullLogger.Instance,
            CancellationToken.None);

        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_RecipientNotBoundToWallet_WritesNothing()
    {
        var writes = new List<Written>();
        DecisionNoticeDispatcher.WriteDecision write = (w, iid, aid, t, r, sev, _) =>
        {
            writes.Add(new Written(w, iid, aid, t, r, sev));
            return Task.CompletedTask;
        };

        await DecisionNoticeDispatcher.DispatchAsync(
            RejectAction(AiasNotice()),
            "instance-1",
            participantWallets: new Dictionary<string, string>(), // citizen not bound
            RejectPayload,
            RejectRouting,
            _ => true,
            write,
            NullLogger.Instance,
            CancellationToken.None);

        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_NonMatchingCondition_WritesNothing()
    {
        var writes = new List<Written>();
        DecisionNoticeDispatcher.WriteDecision write = (w, iid, aid, t, r, sev, _) =>
        {
            writes.Add(new Written(w, iid, aid, t, r, sev));
            return Task.CompletedTask;
        };

        await DecisionNoticeDispatcher.DispatchAsync(
            RejectAction(AiasNotice()),
            "instance-1",
            new Dictionary<string, string> { ["citizen"] = "ws1citizen" },
            RejectPayload,
            RejectRouting,
            conditionMatches: _ => false, // reject route's condition does not match
            write,
            NullLogger.Instance,
            CancellationToken.None);

        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WriteThrows_DoesNotPropagate()
    {
        DecisionNoticeDispatcher.WriteDecision write = (_, _, _, _, _, _, _) =>
            throw new HttpRequestException("Tenant unavailable");

        var act = () => DecisionNoticeDispatcher.DispatchAsync(
            RejectAction(AiasNotice()),
            "instance-1",
            new Dictionary<string, string> { ["citizen"] = "ws1citizen" },
            RejectPayload,
            RejectRouting,
            _ => true,
            write,
            NullLogger.Instance,
            CancellationToken.None);

        await act.Should().NotThrowAsync(
            "a decision-notice dispatch failure must never affect sealing or routing");
    }
}
