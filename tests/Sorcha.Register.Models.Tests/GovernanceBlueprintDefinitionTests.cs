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


    [Fact]
    public void QuorumComesFromTheRegistersRule_NotAHardcodedPercentage()
    {
        // T050, re-pointed 2026-08-09. It used to assert this against action 2's ROUTE conditions.
        // Those are gone (FR-018 restated — nothing evaluated them), but the requirement is not: a
        // percentage cannot express Unanimous, so a consortium's rule would be silently ignored.
        //
        // The rule now lives where it is actually enforced —
        // RegisterControlRecord.RegisterPolicy.Governance.QuorumFormula, frozen onto each proposal as
        // quorumFormulaAtRaise (FR-011a) — so the definition must declare that field and must not
        // restate the arithmetic anywhere.
        var definition = Template().GetRawText();

        definition.Should().NotContain("approvalPercentage",
            "a percentage cannot express Unanimous");
        definition.Should().NotContain("50.01");

        Action(1).GetProperty("dataSchemas")[0]
            .GetProperty("properties").GetProperty("operation")
            .GetProperty("properties").TryGetProperty("quorumFormulaAtRaise", out _)
            .Should().BeTrue("the frozen rule is part of the proposal's published contract");
    }

    // RETIRED 2026-08-09 with the routes they asserted on (FR-018 restated):
    //
    //   EveryQuorumFormula_IsExpressible — superseded by a stronger check.
    //     GovernanceControlPayloadContractTests.TheQuorumFormulaWireValues_AreExactlyWhatTheSchemaLists
    //     asserts set equality both ways between the schema's declared formulas and the ones the model
    //     can emit, so a formula added later fails the build rather than merely being un-special-cased.
    //
    //   AcceptRoleIsSkipped_WhenNobodyHasAnythingToAccept and
    //   OwnerOverride_AlsoSkipsAcceptance_WhenThereIsNothingToAccept — these pinned that a
    //     crypto-policy change would not hang waiting on an acceptance that can never arrive. The
    //     hazard was only ever expressible in the routes, and it was never real: `requiresAcceptance`
    //     is carried by no model, so nothing could have read it, and there is no Accept Role step in
    //     the flow the platform actually executes. Re-pointing them would have meant asserting
    //     behaviour that does not exist.

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
