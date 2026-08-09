// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Registers;
using Sorcha.UI.Core.Models.Admin;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Registers;

/// <summary>
/// What an approver reads before approving (Feature 189 T084 / FR-027).
/// </summary>
/// <remarks>
/// The requirement is not "show the proposal" — it is that a person can <b>judge</b> the change.
/// Signing an opaque value is not approval, and a serialised operation on screen is that failure
/// wearing a review surface. So these assert on what a reader would actually take from the page:
/// who joins, who leaves, who changes role, and why there is nothing to preview when there is not.
/// </remarks>
public class GovernanceProposalReviewTests : BunitContext
{
    public GovernanceProposalReviewTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string Owner = "did:sorcha:w:ws11qownerorganisation000000000001";
    private const string Admin = "did:sorcha:w:ws11qadminorganisation000000000002";
    private const string Joiner = "did:sorcha:w:ws11qjoiningorganisation00000003";

    private static GovernanceProposalSummaryViewModel Proposal(
        string operationType = "Add",
        string status = GovernanceProposalStates.Open,
        string? statusReason = "None",
        List<GovernanceRosterMemberViewModel>? diff = null) => new()
    {
        ProposalId = "proposal-tx",
        OperationType = operationType,
        ProposedBy = Admin,
        TargetDid = Joiner,
        ProposedAt = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero),
        QuorumFormula = "Unanimous",
        Status = status,
        StatusReason = statusReason,
        ApprovalsRequired = 2,
        ApprovalsReceived = 1,
        RosterDiff = diff,
    };

    private static List<GovernanceRosterMemberViewModel> AddDiff() =>
    [
        new() { Subject = Owner, Role = "Owner", Change = GovernanceRosterChanges.Unchanged },
        new() { Subject = Admin, Role = "Admin", Change = GovernanceRosterChanges.Unchanged },
        new() { Subject = Joiner, Role = "Admin", Change = GovernanceRosterChanges.Added },
    ];

    /// <summary>The headline has to say what happens, in words, before anything else.</summary>
    [Fact]
    public void TheChangeIsStatedInWords_NotAsAnOperationCode()
    {
        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, Proposal()));

        var headline = cut.Find("[data-testid=proposal-headline]").TextContent;

        headline.Should().Contain("Add").And.Contain("governance roster");
        headline.Should().NotBe("Add", "an operation code is not something a person can judge");
    }

    [Fact]
    public void TheRosterDiff_MarksWhoJoins()
    {
        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, Proposal(diff: AddDiff())));

        cut.FindAll($"[data-testid=roster-row-{GovernanceRosterChanges.Added}]").Should().HaveCount(1);
        cut.Find("[data-testid=roster-diff]").TextContent.Should().Contain("Joins");
    }

    /// <summary>
    /// A departing member must be shown leaving, not simply absent — a row that disappears is a
    /// change the reader has to notice by its absence, which is exactly what FR-027 is about.
    /// </summary>
    [Fact]
    public void TheRosterDiff_ShowsADepartingMemberLeaving_RatherThanOmittingThem()
    {
        var diff = new List<GovernanceRosterMemberViewModel>
        {
            new() { Subject = Owner, Role = "Owner", Change = GovernanceRosterChanges.Unchanged },
            new() { Subject = Admin, Role = "Admin", Change = GovernanceRosterChanges.Removed },
        };

        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal,
            Proposal(operationType: "Remove", diff: diff)));

        var table = cut.Find("[data-testid=roster-diff]").TextContent;
        table.Should().Contain("Leaves");
        cut.FindAll($"[data-testid=roster-row-{GovernanceRosterChanges.Removed}]").Should().HaveCount(1);
    }

    /// <summary>A Transfer moves two members, and hiding either one hides who is being demoted.</summary>
    [Fact]
    public void ATransfer_ShowsBothRoleChanges()
    {
        var diff = new List<GovernanceRosterMemberViewModel>
        {
            new() { Subject = Owner, Role = "Admin", Change = GovernanceRosterChanges.RoleChanged },
            new() { Subject = Admin, Role = "Owner", Change = GovernanceRosterChanges.RoleChanged },
        };

        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal,
            Proposal(operationType: "Transfer", diff: diff)));

        cut.FindAll($"[data-testid=roster-row-{GovernanceRosterChanges.RoleChanged}]").Should().HaveCount(2);
    }

    /// <summary>
    /// The server withholds the diff in three distinct cases, and an empty panel would read as
    /// "nothing changes" in all of them. Each has to say which it is.
    /// </summary>
    [Theory]
    [InlineData(GovernanceProposalStates.Invalidated, "roster has moved on")]
    [InlineData(GovernanceProposalStates.Enacted, "already been written")]
    public void WhenThereIsNoDiff_ThePageSaysWhy(string status, string expected)
    {
        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal,
            Proposal(status: status, diff: null)));

        cut.Find("[data-testid=no-diff-reason]").TextContent.Should().Contain(expected);
    }

    [Fact]
    public void AnOperationThatChangesNoMembership_SaysSo()
    {
        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal,
            Proposal(operationType: "CryptoPolicyUpdate", diff: null)));

        cut.Find("[data-testid=no-diff-reason]").TextContent
            .Should().Contain("does not alter who governs");
    }

    /// <summary>
    /// The count is an upper bound — signatures are verified again at enactment — so the page must
    /// not let it read as "this will enact".
    /// </summary>
    [Fact]
    public void TheApprovalCount_IsQualified()
    {
        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, Proposal()));

        cut.Find("[data-testid=approval-count]").TextContent.Should().Contain("1 of 2");
        cut.Markup.Should().Contain("verified again");
    }

    /// <summary>FR-011c: an approval that cannot count is shown with its reason, in words.</summary>
    [Fact]
    public void ApprovalsThatCannotCount_AreShownWithTheReason()
    {
        var proposal = Proposal();
        proposal.ExcludedApprovals =
        [
            new() { ApproverDid = Joiner, TxId = "tx-x", Reason = "NotOnRoster" }
        ];

        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, proposal));

        cut.Find("[data-testid=excluded-approvals]").TextContent
            .Should().Contain("not on the governance roster");
    }

    /// <summary>
    /// A refusal reason the renderer does not recognise is shown verbatim rather than swallowed —
    /// a reason that vanishes is the silent drop FR-011c exists to prevent.
    /// </summary>
    [Fact]
    public void AnUnrecognisedRefusalReason_IsStillShown()
    {
        var proposal = Proposal();
        proposal.ExcludedApprovals =
        [
            new() { ApproverDid = Joiner, TxId = "tx-x", Reason = "SomethingAddedLater" }
        ];

        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, proposal));

        cut.Find("[data-testid=excluded-approvals]").TextContent.Should().Contain("SomethingAddedLater");
    }

    [Fact]
    public void AnApproval_IsAttributedToTheIndividualBehindIt()
    {
        var proposal = Proposal();
        proposal.Approvals =
        [
            new()
            {
                ApproverDid = Owner,
                IsApproval = true,
                ApprovedAt = DateTimeOffset.UnixEpoch,
                TxId = "approval-tx",
                AuthMethod = "HardwareBacked",
                AccountableIndividualDid = "did:sorcha:w:ws11qpersonwhoapproved000000009",
            }
        ];

        var cut = Render<GovernanceProposalReview>(p => p.Add(c => c.Proposal, proposal));

        cut.Find("[data-testid=approval-list]").TextContent.Should().Contain("authorised by");
    }
}
