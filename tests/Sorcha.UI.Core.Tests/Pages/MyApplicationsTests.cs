// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Bunit;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Testing;

using Xunit;

using MyApplicationsPage = Sorcha.UI.Web.Client.Pages.MyApplications;

namespace Sorcha.UI.Core.Tests.Pages;

/// <summary>
/// Feature 186 (#1163) — the citizen's "My Applications" page.
/// </summary>
/// <remarks>
/// <para>
/// Two behaviours here are regressions in waiting rather than ordinary rendering assertions.
/// </para>
/// <para>
/// <b>A refusal must not read as a plain finish.</b> The row shows <c>Outcome</c>, which the server
/// derives from the recorded decision; showing <c>State</c> instead would tell a refused citizen
/// their application "completed", because a refusal ends the application exactly as an approval does.
/// </para>
/// <para>
/// <b>A row must offer an action only when one can be taken.</b> That is the whole of #1268 — a
/// just-submitted application still offering a live "Take Action" button. It cannot recur while the
/// button is gated on the server-computed <c>NeedsYou</c> rather than on the mere existence of a
/// current action.
/// </para>
/// </remarks>
public sealed class MyApplicationsTests : ComponentTestFixture
{
    private readonly Mock<IMyApplicationsService> _applications = new();

    public MyApplicationsTests()
    {
        Services.AddLogging();
        Services.AddSingleton(_applications.Object);
    }

    private static MyApplicationViewModel Row(
        string id = "inst-1",
        string title = "Assured Identity",
        string state = "Active",
        string? outcome = null,
        string? reason = null,
        bool needsYou = false,
        string? reference = "AI-CYB-14-A7K3") => new()
        {
            InstanceId = id,
            BlueprintId = "bp-1",
            BlueprintTitle = title,
            InstanceReference = reference,
            State = state,
            Outcome = outcome ?? state,
            DecisionReason = reason,
            DecisionTitle = reason is null ? null : "We could not assure your identity",
            DecisionSeverity = reason is null ? null : "Warning",
            NeedsYou = needsYou,
            StepNumber = 2,
            TotalSteps = 3,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private void ServiceReturns(params MyApplicationViewModel[] rows) =>
        _applications
            .Setup(s => s.GetMyApplicationsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaginatedList<MyApplicationViewModel>.Create(rows, rows.Length, 1, 20));

    private void ServiceNeverCompletes() =>
        _applications
            .Setup(s => s.GetMyApplicationsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<PaginatedList<MyApplicationViewModel>>().Task);

    [Fact]
    public void WhileLoading_ShowsAProgressIndicator()
    {
        ServiceNeverCompletes();

        var page = Render<MyApplicationsPage>();

        page.FindAll("[data-testid='my-applications-loading']").Should().NotBeEmpty();
    }

    [Fact]
    public void WithNoApplications_ShowsAnEmptyState_NotABlankPage()
    {
        ServiceReturns();

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid='my-applications-empty']").Should().NotBeEmpty());
        page.FindAll("[data-testid^='my-application-row-']").Should().BeEmpty();
    }

    [Fact]
    public void RendersARowPerApplication_WithItsServiceNameAndReference()
    {
        ServiceReturns(Row("inst-1"), Row("inst-2", title: "Building Warrant", reference: "BW-DUM-02-9QX1"));

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid^='my-application-row-']").Should().HaveCount(2));
        page.Markup.Should().Contain("Assured Identity");
        page.Markup.Should().Contain("Building Warrant");
        page.Markup.Should().Contain("AI-CYB-14-A7K3");
    }

    [Fact]
    public void ARefusedApplication_ShowsTheOutcomeAndTheReason_NotJustCompleted()
    {
        ServiceReturns(Row(
            state: "Completed",
            outcome: "NotApproved",
            reason: "The document you provided could not be read clearly."));

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid='my-application-reason-inst-1']").Should().NotBeEmpty());
        page.Markup.Should().Contain("could not be read clearly");
        page.Markup.Should().NotContain(">Completed<",
            "a refused application reported as \"Completed\" is the defect this page exists to remove");
    }

    [Fact]
    public void AnApplicationWithNoReason_RendersNoReasonElementAtAll()
    {
        // FR-013: absent means absent. An empty reason line reads to a citizen as a fault.
        ServiceReturns(Row(state: "Completed", outcome: "Completed"));

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid^='my-application-row-']").Should().HaveCount(1));
        page.FindAll("[data-testid='my-application-reason-inst-1']").Should().BeEmpty();
    }

    [Fact]
    public void AnApplicationWaitingOnTheCitizen_OffersAContinueAffordance()
    {
        ServiceReturns(Row(needsYou: true));

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid='my-application-continue-inst-1']").Should().NotBeEmpty());
    }

    [Fact]
    public void AJustSubmittedApplication_OffersNoAction()
    {
        // #1268 dissolved: the button is gated on NeedsYou, not on the existence of a current step.
        ServiceReturns(Row(needsYou: false));

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid^='my-application-row-']").Should().HaveCount(1));
        page.FindAll("[data-testid='my-application-continue-inst-1']").Should().BeEmpty(
            "offering an action that cannot be taken is exactly the defect in #1268");
    }

    [Fact]
    public void EachRowLinksToItsDetailView()
    {
        ServiceReturns(Row());

        var page = Render<MyApplicationsPage>();

        page.WaitForAssertion(() =>
            page.FindAll("[data-testid='my-application-open-inst-1']").Should().NotBeEmpty());
        page.Find("[data-testid='my-application-open-inst-1']")
            .GetAttribute("href").Should().Contain("my-applications/inst-1");
    }
}
