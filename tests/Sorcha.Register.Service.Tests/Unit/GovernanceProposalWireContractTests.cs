// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Register.Models;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// The shared client's proposal DTO must agree with what the server actually sends (T046).
/// </summary>
/// <remarks>
/// <para>
/// <c>GovernanceProposalSummary</c> and <c>GovernanceProposalView</c> are two hand-maintained
/// descriptions of one wire shape, which is the drift this codebase has been bitten by repeatedly —
/// and it fails in the quietest possible way. <c>System.Text.Json</c> ignores properties it cannot
/// match, so a renamed field does not throw, does not warn and does not fail a build: it
/// deserialises to a default. A proposals list would render as "no proposals" against a register
/// full of them.
/// </para>
/// <para>
/// So the agreement is derived here by reflection and by round-tripping real values, never from a
/// list of field names a human has to remember to update.
/// </para>
/// </remarks>
public sealed class GovernanceProposalWireContractTests
{
    /// <summary>
    /// The options the endpoints ACTUALLY serialise with.
    /// </summary>
    /// <remarks>
    /// The Register Service calls neither <c>ConfigureHttpJsonOptions</c> nor <c>AddJsonOptions</c>, so
    /// its minimal APIs use the web defaults — camelCase, and enums as NUMBERS unless the type says
    /// otherwise. An earlier version of this file asserted against <c>SorchaJson.Options</c>, which
    /// this service does not use: it would have passed while the endpoint emitted <c>"status": 1</c>
    /// and every client reading a string got nothing. Assert against the serialiser production runs.
    /// </remarks>
    private static readonly JsonSerializerOptions HostOptions = JsonSerializerOptions.Web;

    /// <summary>Everything the client expects to read must be something the server sends.</summary>
    [Fact]
    public void EveryClientProperty_ExistsOnTheServerView()
    {
        var server = typeof(GovernanceProposalView)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = typeof(GovernanceProposalSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !server.Contains(name))
            .ToList();

        missing.Should().BeEmpty(
            "a client property with no server counterpart silently deserialises to a default, and "
            + "nothing anywhere reports it");
    }

    /// <summary>
    /// Name agreement is not enough on its own — it would still pass if the server serialised under a
    /// different naming policy. This proves values actually land, through the real serialiser.
    /// </summary>
    [Fact]
    public void AServerViewRoundTripsIntoTheClientDto_WithEveryValueCarried()
    {
        var view = new GovernanceProposalView
        {
            ProposalId = "proposal-tx",
            OperationType = GovernanceOperationType.Transfer,
            ProposedBy = "did:sorcha:w:ws11qadmin",
            TargetDid = "did:sorcha:w:ws11qnew",
            ProposedAt = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero),
            RosterSnapshotId = "genesis-tx",
            QuorumFormula = QuorumFormula.Unanimous,
            Status = GovernanceProposalState.Invalidated,
            StatusReason = GovernanceProposalStateReason.RosterChanged,
            OutcomeTxId = "outcome-tx",
            ApprovalsRequired = 3,
            ApprovalsReceived = 2,
        };

        var json = JsonSerializer.Serialize(view, HostOptions);
        var client = JsonSerializer.Deserialize<GovernanceProposalSummary>(json, HostOptions);

        client.Should().NotBeNull();
        client!.ProposalId.Should().Be("proposal-tx");
        client.OperationType.Should().NotBeNullOrEmpty();
        client.ProposedBy.Should().Be("did:sorcha:w:ws11qadmin");
        client.TargetDid.Should().Be("did:sorcha:w:ws11qnew");
        client.ProposedAt.Should().Be(view.ProposedAt);
        client.ExpiresAt.Should().Be(view.ExpiresAt);
        client.RosterSnapshotId.Should().Be("genesis-tx");
        client.QuorumFormula.Should().NotBeNullOrEmpty();
        client.Status.Should().NotBeNullOrEmpty();
        client.StatusReason.Should().NotBeNullOrEmpty();
        client.OutcomeTxId.Should().Be("outcome-tx");
        client.ApprovalsRequired.Should().Be(3);
        client.ApprovalsReceived.Should().Be(2);
    }

    /// <summary>
    /// Not a formatting preference — the client types these as strings, so a numeric enum on the wire
    /// would deserialise to a bare number and a status filter written against "Enacted" would never
    /// match.
    /// </summary>
    [Fact]
    public void TheDerivedStatusGoesOnTheWireAsAName_NotANumber()
    {
        var view = new GovernanceProposalView
        {
            ProposalId = "p",
            OperationType = GovernanceOperationType.Add,
            ProposedBy = "did:sorcha:w:ws11q",
            Status = GovernanceProposalState.Enacted,
            StatusReason = GovernanceProposalStateReason.QuorumMet,
        };

        var json = JsonSerializer.Serialize(view, HostOptions);

        json.Should().Contain("nacted", "the state must survive as a name a reader can match on");
        json.Should().NotContain("\"status\":1");
    }
}
