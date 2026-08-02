// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Feature 186 (#1163) — the citizen "My Applications" surface, end to end.
/// </summary>
/// <remarks>
/// The base class checks console errors, 5xx responses and MudBlazor CSS health on every navigation,
/// so a page that renders but throws is caught here rather than in review.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("MyApplications")]
[Category("Authenticated")]
public class MyApplicationsTests : AuthenticatedDockerTestBase
{
    private MyApplicationsPage _myApplications = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _myApplications = new MyApplicationsPage(Page);
    }

    [Test]
    [Retry(2)]
    public async Task MyApplications_LoadsWithoutErrors()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyApplications);
    }

    [Test]
    public async Task MyApplications_ShowsEitherApplicationsOrAnEmptyState()
    {
        // Never a blank page: a citizen who has submitted nothing must be told so, not left
        // wondering whether the page failed.
        await _myApplications.NavigateAsync();

        var count = await _myApplications.GetApplicationCountAsync();
        if (count == 0)
        {
            Assert.That(await _myApplications.IsEmptyStateVisibleAsync(), Is.True,
                "with no applications the page must show its empty state");
        }
        else
        {
            Assert.That(count, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task NavigationOffersMyApplications_AndNamesTheWorkQueueDistinctly()
    {
        // The two entries used to read as synonyms, which is the confusion #1267/#1268 recorded —
        // a tester checked the actions list, saw "All Caught Up!", and concluded their application
        // had vanished.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var myApplications = Page.Locator("a[href$='my-applications']");
        var workQueue = Page.Locator("a[href$='my-actions']");

        Assert.That(await myApplications.CountAsync(), Is.GreaterThan(0),
            "the My Activity section must offer a \"what did I submit\" entry");
        Assert.That(await workQueue.CountAsync(), Is.GreaterThan(0),
            "the work queue entry must remain — it serves the analyst as well as the citizen");

        var workQueueText = await workQueue.First.InnerTextAsync();
        Assert.That(workQueueText, Does.Not.Contain("Pending Actions"),
            "the rename is what stops the two entries reading as the same list");
    }

    [Test]
    public async Task LegacyMyWorkflowsRoute_LandsOnMyApplications()
    {
        // It used to redirect to the start-a-new-application catalogue, so the route a citizen
        // would guess for "what did I submit" took them somewhere that could only start another.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWorkflows);

        // The redirect happens in OnInitialized, i.e. after the WASM runtime boots — so waiting on
        // NetworkIdle alone can observe the pre-redirect URL and pass or fail for the wrong reason.
        await Page.WaitForURLAsync(
            url => url.Contains("my-applications", StringComparison.Ordinal),
            new PageWaitForURLOptions { Timeout = 15_000 });

        Assert.That(Page.Url, Does.Contain("my-applications"));
        Assert.That(Page.Url, Does.Not.Contain("new-submissions"));
    }
}
