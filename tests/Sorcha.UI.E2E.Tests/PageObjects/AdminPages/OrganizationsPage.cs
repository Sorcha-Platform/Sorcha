// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects.AdminPages;

/// <summary>
/// Page object for the Organizations admin page (/admin/organizations).
/// </summary>
public class OrganizationsPage
{
    private readonly IPage _page;

    public OrganizationsPage(IPage page) => _page = page;

    // Page elements
    public ILocator PageTitle => _page.Locator("h4:has-text('Organizations'), h4:has-text('Organisations'), h5:has-text('Organizations'), h5:has-text('Organisations')");
    public ILocator CreateButton => _page.GetByRole(AriaRole.Button, new() { Name = "Create Organization" });
    public ILocator OrgTable => _page.Locator("[data-testid='organization-list']");
    public ILocator OrgFormDialog => _page.Locator(".mud-dialog");
    public ILocator NameInput => _page.Locator(".mud-dialog").GetByLabel("Name");
    public ILocator SubdomainInput => _page.Locator(".mud-dialog").GetByLabel("Subdomain");
    public ILocator SaveButton => _page.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Save" });
    public ILocator ShowInactiveCheckbox => _page.GetByLabel("Show inactive organizations");

    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.AdminOrganizations}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<bool> WaitForPageAsync()
    {
        try
        {
            // Use .First to avoid strict mode violation when multiple h4/h5 match
            await PageTitle.First.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ClickCreateAsync()
    {
        await CreateButton.First.ClickAsync();
        await OrgFormDialog.First.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
    }

    public async Task FillFormAsync(string name, string subdomain)
    {
        await NameInput.FillAsync(name);
        await SubdomainInput.FillAsync(subdomain);
    }

    public async Task SubmitFormAsync()
    {
        await SaveButton.ClickAsync();
    }

    public ILocator GetOrgRowByName(string name)
    {
        return OrgTable.Locator("tr").Filter(new() { HasText = name });
    }

    public ILocator EditButton(string name)
    {
        return GetOrgRowByName(name).GetByRole(AriaRole.Button, new() { Name = "Edit" });
    }

    public ILocator DeactivateButton(string name)
    {
        return GetOrgRowByName(name).GetByRole(AriaRole.Button, new() { Name = "Deactivate" });
    }
}
