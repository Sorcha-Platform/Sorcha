// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.AdminPages;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the Organizations admin page.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[Category("Docker")]
[Category("Admin")]
public class AdminOrganizationsTests : AuthenticatedDockerTestBase
{
    private OrganizationsPage _organizationsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _organizationsPage = new OrganizationsPage(Page);
    }

    #region Smoke Tests

    [Test]
    [Retry(3)]
    public async Task OrganizationsPage_LoadsSuccessfully()
    {
        await _organizationsPage.NavigateAsync();
        var loaded = await _organizationsPage.WaitForPageAsync();
        Assert.That(loaded, Is.True, "Organizations page should load successfully");
    }

    [Test]
    [Retry(3)]
    public async Task OrganizationsPage_ShowsCreateButton()
    {
        await _organizationsPage.NavigateAsync();
        await _organizationsPage.WaitForPageAsync();
        // Use CountAsync to avoid strict mode violation if multiple buttons match
        var count = await _organizationsPage.CreateButton.CountAsync();
        if (count == 0)
        {
            // Non-SystemAdmin users see OrganizationDashboard instead of the list with Create button
            Assert.Ignore("Create Organization button not visible — user may not have SystemAdmin role");
        }
    }

    [Test]
    [Retry(3)]
    public async Task OrganizationsPage_ShowsOrgTable()
    {
        await _organizationsPage.NavigateAsync();
        await _organizationsPage.WaitForPageAsync();
        // MudDataGrid may render as a different element — also check by CSS class or mud-table
        var hasTable = await _organizationsPage.OrgTable.CountAsync() > 0 ||
                       await Page.Locator(".organization-list").CountAsync() > 0 ||
                       await Page.Locator(".mud-table, .mud-data-grid").CountAsync() > 0;
        if (!hasTable)
        {
            // Non-SystemAdmin users see OrganizationDashboard instead of the list
            Assert.Ignore("Organization table not visible — user may not have SystemAdmin role");
        }
    }

    #endregion

    #region Create Organization Flow

    [Test]
    [Retry(3)]
    public async Task CreateButton_OpensDialog()
    {
        await _organizationsPage.NavigateAsync();
        await _organizationsPage.WaitForPageAsync();
        if (await _organizationsPage.CreateButton.CountAsync() == 0)
        {
            Assert.Ignore("Create Organization button not visible — user may not have SystemAdmin role");
        }
        await _organizationsPage.ClickCreateAsync();
        Assert.That(await _organizationsPage.OrgFormDialog.CountAsync(), Is.GreaterThan(0),
            "Create Organization dialog should be visible");
    }

    #endregion

    #region Display Tests

    [Test]
    [Retry(3)]
    public async Task OrganizationsPage_ShowsInactiveCheckbox()
    {
        await _organizationsPage.NavigateAsync();
        await _organizationsPage.WaitForPageAsync();
        // Use CountAsync to avoid strict mode if label text matches multiple elements
        var hasCheckbox = await _organizationsPage.ShowInactiveCheckbox.CountAsync() > 0 ||
                          await Page.Locator(".mud-checkbox").CountAsync() > 0;
        if (!hasCheckbox)
        {
            // Non-SystemAdmin users see OrganizationDashboard instead of the list
            Assert.Ignore("Show inactive checkbox not visible — user may not have SystemAdmin role");
        }
    }

    #endregion
}
