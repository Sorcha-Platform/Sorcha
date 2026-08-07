// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// The published governance definition must say what the code believes it says (T050-T052 / FR-018).
/// </summary>
/// <remarks>
/// <para>
/// <c>register-governance-v1</c> had drifted from the code it supposedly described: it hardcoded
/// <c>approvalPercentage &gt;= 50.01</c>, which can express <c>StrictMajority</c> and nothing else —
/// so a register configured <c>Unanimous</c> would have had its rule quietly ignored had the
/// blueprint ever executed. It never has: nothing in <c>src/</c> instantiates it, which is exactly
/// why the drift went unnoticed.
/// </para>
/// <para>
/// These tests read the shipped file rather than a fixture. A copy would drift the same way the
/// blueprint did.
/// </para>
/// </remarks>
public sealed class GovernanceBlueprintDefinitionTests
{
    private static JsonElement Template()
    {
        var path = Path.Combine(RepoRoot(), "blueprints", "templates", "register-governance-v1.json");
        File.Exists(path).Should().BeTrue("the governance blueprint ships at {0}", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("template").Clone();
    }

    private static JsonElement Action(int id) =>
        Template().GetProperty("actions").EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == id);

    private static string RoutesJson(int actionId) =>
        Action(actionId).GetProperty("routes").GetRawText();

    [Fact]
    public void QuorumComesFromTheRegistersRule_NotAHardcodedPercentage()
    {
        // T050. The arithmetic belongs to GovernanceRosterService.ValidateQuorumAsync, which knows
        // the register's own QuorumFormula (R-007). The blueprint consumes the verdict.
        var routes = RoutesJson(2);

        routes.Should().NotContain("approvalPercentage",
            "a percentage cannot express Unanimous, so a consortium's rule would be silently ignored");
        routes.Should().NotContain("50.01");
        routes.Should().Contain("quorumMet", "the blueprint consumes the verdict, it does not compute it");
    }

    [Fact]
    public void EveryQuorumFormula_IsExpressible()
    {
        // The reason the percentage had to go: enumerate the formulas the code supports and assert
        // the blueprint is not written in terms that can only carry one of them.
        var routes = RoutesJson(2);

        foreach (var formula in Enum.GetNames<QuorumFormula>())
        {
            routes.Should().NotContain(formula,
                "the route must not special-case {0} — it consumes a boolean verdict that covers all "
                + "formulas, so a formula added later needs no blueprint change", formula);
        }
    }

    [Fact]
    public void AcceptRoleIsSkipped_WhenNobodyHasAnythingToAccept()
    {
        // T052 / R-008. A crypto-policy change has no target and no role. Routing it through
        // "Accept Role" would strand the proposal waiting for an acceptance that can never arrive —
        // a hang, not an error, which is the worst shape of failure.
        var routes = RoutesJson(2);

        routes.Should().Contain("requiresAcceptance");
        routes.Should().Contain("quorum-met-no-acceptance");

        Action(3).GetProperty("title").GetString().Should().Be("Accept Role",
            "the skip route is defined relative to this action");
    }

    [Fact]
    public void OwnerOverride_AlsoSkipsAcceptance_WhenThereIsNothingToAccept()
    {
        // The override is a second path to enactment, so it needs the same treatment. Fixing only
        // the quorum path would leave single-owner crypto-policy promotion hanging — and that is
        // the single most-used governance operation on this platform.
        RoutesJson(1).Should().Contain("owner-override-no-acceptance");
    }

    [Fact]
    public void CryptoPolicyUpdate_IsAnOfferedOperation()
    {
        // It exists in code as GovernanceOperationType.CryptoPolicyUpdate but was absent from the
        // definition, so the published workflow could not express the change it is most used for.
        Action(1).GetProperty("dataSchemas").GetRawText()
            .Should().Contain(nameof(GovernanceOperationType.CryptoPolicyUpdate));
    }

    [Fact]
    public void ProposalAndApproval_BothHaveAPayloadContract()
    {
        // T051. Without dataSchemas there was no contract for either payload, so nothing could
        // validate what a proposal or an approval actually carried.
        Action(1).TryGetProperty("dataSchemas", out var proposal).Should().BeTrue();
        Action(2).TryGetProperty("dataSchemas", out var approval).Should().BeTrue();

        proposal.GetArrayLength().Should().BeGreaterThan(0);
        approval.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void EveryApproval_MustCarryAnAccountabilityLink()
    {
        // FR-029 / R-017. An autonomous approver is delegated, not unaccountable — so the schema
        // requires `authorisation` rather than treating it as optional for machines.
        var approval = Action(2).GetProperty("dataSchemas")[0];

        approval.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("authorisation",
                "no approval may reach the ledger without resolving to a named individual");

        var auth = approval.GetProperty("properties").GetProperty("authorisation");
        auth.GetProperty("properties").GetProperty("delegation").GetRawText()
            .Should().Contain("scope",
                "a delegation must be able to withhold Transfer while permitting routine changes");
    }

    [Fact]
    public void ApprovalBinds_ApproveVersusReject()
    {
        // The field whose omission silently inverts a vote.
        Action(2).GetProperty("dataSchemas")[0].GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).Should().Contain("isApproval");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "blueprints"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (a directory containing both blueprints/ and src/) "
            + $"by walking up from {AppContext.BaseDirectory}.");
    }
}
