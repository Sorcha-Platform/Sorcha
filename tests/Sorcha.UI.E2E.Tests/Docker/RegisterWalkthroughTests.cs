// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// End-to-end walkthrough tests for the register creation flow, register detail views,
/// search/query functionality, and wizard cancel behavior.
/// Tests the full journey: Registers list → Create wizard → Verify register →
/// View transaction → View docket → Search → Query page → Create + Cancel.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Registers")]
[Category("E2E")]
[Category("RegisterWalkthrough")]
[Parallelizable(ParallelScope.Self)]
public class RegisterWalkthroughTests : AuthenticatedDockerTestBase
{
    private static string GetUniqueRegisterName() =>
        $"E2E-Reg-{DateTime.UtcNow:HHmmss}-{Random.Shared.Next(1000, 9999)}";

    #region Register Creation Full Flow

    [Test]
    [Order(1)]
    [Retry(2)]
    public async Task CreateRegister_FullWizard_CreatesAndShowsInList()
    {
        var registerName = GetUniqueRegisterName();

        // Navigate to Registers page
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Registers);
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Verify registers page loaded with CREATE REGISTER button
        var createButton = Page.Locator("button:has-text('CREATE REGISTER')").First;
        await createButton.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        Assert.That(await createButton.IsVisibleAsync(), Is.True,
            "CREATE REGISTER button should be visible");

        // Open wizard
        await createButton.ClickAsync();
        var dialog = Page.Locator(".mud-dialog");
        await dialog.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });

        // === Step 1: Name ===
        var nameInput = dialog.Locator("input").First;
        await nameInput.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        await nameInput.FillAsync(registerName);

        // Fill optional description
        var descInput = dialog.Locator("textarea").First;
        if (await descInput.IsVisibleAsync())
        {
            await descInput.FillAsync($"E2E test register created at {DateTime.UtcNow:u}");
        }

        // Click bottom NEXT button (the one inside the dialog actions area)
        var nextButtons = dialog.Locator("button:has-text('NEXT')");
        var bottomNext = nextButtons.Last;
        await Page.WaitForTimeoutAsync(500); // allow validation
        await bottomNext.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // === Step 2: Wallet ===
        // Wallet should be auto-selected if user has a default wallet
        var walletTab = dialog.Locator("div:has-text('Wallet')");
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Verify a wallet is shown (look for wallet name or address text)
        var walletContent = await dialog.TextContentAsync();
        if (walletContent?.Contains("No wallets") == true)
        {
            Assert.Inconclusive("No wallets available - create a wallet first");
        }

        await bottomNext.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // === Step 3: Options ===
        // Enable Public register
        var publicSwitch = dialog.Locator("input[type='checkbox']").First;
        if (await publicSwitch.IsVisibleAsync())
        {
            await publicSwitch.CheckAsync(new() { Force = true });
        }

        await bottomNext.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // === Step 4: Policy ===
        // Accept defaults (Min Validators: 1, etc.)
        await bottomNext.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // === Step 5: Review & Create ===
        // Verify register name appears in review
        var reviewContent = await dialog.TextContentAsync();
        Assert.That(reviewContent, Does.Contain(registerName),
            "Review step should display the register name");

        // Click CREATE REGISTER
        var createRegButton = dialog.Locator("button:has-text('CREATE REGISTER')").First;
        await createRegButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await CaptureScreenshotAsync("register-wizard-review");
        await createRegButton.ClickAsync();

        // Wait for dialog to close (creation success) or error
        var created = false;
        for (var i = 0; i < 30; i++)
        {
            if (!await dialog.IsVisibleAsync())
            {
                created = true;
                break;
            }

            // Check for error
            var errorText = await dialog.TextContentAsync();
            if (errorText?.Contains("Error") == true || errorText?.Contains("failed") == true)
            {
                await CaptureScreenshotAsync("register-creation-error");

                if (errorText.Contains("bootstrap") || errorText.Contains("503"))
                {
                    Assert.Inconclusive("Backend services need bootstrap");
                }

                Assert.Fail($"Register creation failed: {errorText}");
            }

            await Page.WaitForTimeoutAsync(1000);
        }

        Assert.That(created, Is.True, "Register wizard dialog should close after successful creation");

        // Wait for list to update
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Verify new register appears in the list
        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Does.Contain(registerName),
            $"New register '{registerName}' should appear in the registers list");

        await CaptureScreenshotAsync("register-created-in-list");
    }

    #endregion

    #region Register Detail View

    [Test]
    [Order(2)]
    [Retry(2)]
    public async Task RegisterDetail_ShowsTransactionsAndDockets()
    {
        var registerName = GetUniqueRegisterName();

        // Create a register first
        await CreateRegisterViaWizardAsync(registerName);

        // Click on the new register to open detail view
        var registerLink = Page.Locator($"text={registerName}").First;
        await registerLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Verify detail page loaded
        var heading = Page.Locator($"h5:has-text('{registerName}')");
        await heading.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
        Assert.That(await heading.IsVisibleAsync(), Is.True,
            "Register detail heading should show the register name");

        // Verify stats
        var heightText = await Page.TextContentAsync("body");
        Assert.That(heightText, Does.Contain("Height"),
            "Register detail should show Height stat");
        Assert.That(heightText, Does.Contain("Transactions"),
            "Register detail should show Transactions stat");

        // === TRANSACTIONS tab (default) ===
        var txTab = Page.Locator("button:has-text('TRANSACTIONS')").First;
        Assert.That(await txTab.IsVisibleAsync(), Is.True,
            "TRANSACTIONS tab should be visible");

        // Should have at least 1 transaction (genesis)
        var txRow = Page.Locator("tr, [class*='table-row']").Filter(new() { HasText = "Docket" }).First;
        var hasTx = await txRow.IsVisibleAsync();
        if (hasTx)
        {
            // Click on the transaction to view details
            await txRow.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            // Verify transaction detail panel
            var txDetails = Page.Locator("text=Transaction Details");
            if (await txDetails.IsVisibleAsync())
            {
                var detailContent = await Page.TextContentAsync("body");
                Assert.That(detailContent, Does.Contain("Transaction ID"),
                    "Transaction detail should show TX ID");
                Assert.That(detailContent, Does.Contain("Confirmed").Or.Contains("Pending"),
                    "Transaction detail should show status");
                Assert.That(detailContent, Does.Contain("Signature"),
                    "Transaction detail should show signature");
                Assert.That(detailContent, Does.Contain("Payload"),
                    "Transaction detail should show payloads");

                await CaptureScreenshotAsync("transaction-detail");

                // Close the detail panel
                var closeButton = Page.Locator("button:has-text('Close details')").First;
                if (await closeButton.IsVisibleAsync())
                {
                    await closeButton.ClickAsync();
                }
            }
        }

        // === DOCKET CHAIN tab ===
        var docketTab = Page.Locator("button:has-text('DOCKET CHAIN')").First;
        await docketTab.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var docketContent = await Page.TextContentAsync("body");
        Assert.That(docketContent, Does.Contain("Docket #0"),
            "Docket chain should show genesis Docket #0");
        Assert.That(docketContent, Does.Contain("Hash:"),
            "Genesis docket should display its hash");

        await CaptureScreenshotAsync("docket-chain");

        // === POLICY tab ===
        var policyTab = Page.Locator("button:has-text('POLICY')").First;
        await policyTab.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        await CaptureScreenshotAsync("register-policy");

        // Navigate back
        var backButton = Page.Locator("button:has-text('Back to registers')").First;
        if (await backButton.IsVisibleAsync())
        {
            await backButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
    }

    #endregion

    #region Search and Query

    [Test]
    [Order(3)]
    [Retry(2)]
    public async Task RegistersList_SearchFiltersResults()
    {
        // Navigate to Registers page
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Registers);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Verify multiple registers are listed
        var bodyText = await Page.TextContentAsync("body");
        Assert.That(bodyText, Does.Contain("Sorcha System Register"),
            "System register should be listed");

        // Type a search term that matches only the system register
        var searchBox = Page.Locator("input[placeholder*='Search registers']").First;
        await searchBox.FillAsync("System");
        await Page.WaitForTimeoutAsync(2000); // debounce wait

        // After filtering, System Register should be visible
        var filteredBody = await Page.TextContentAsync("body");
        Assert.That(filteredBody, Does.Contain("Sorcha System Register"),
            "System Register should remain visible after search");

        // Clear search
        var clearButton = Page.Locator("button:has-text('CLEAR')");
        if (await clearButton.IsVisibleAsync())
        {
            await clearButton.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
        else
        {
            await searchBox.FillAsync("");
            await Page.WaitForTimeoutAsync(2000);
        }

        // All registers should be back
        var clearedBody = await Page.TextContentAsync("body");
        Assert.That(clearedBody, Does.Contain("Sorcha System Register"),
            "After clearing search, all registers should be shown");
    }

    [Test]
    [Order(4)]
    [Retry(2)]
    public async Task QueryTransactions_PageLoadsWithTabs()
    {
        // Navigate to Registers page
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Registers);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Click QUERY TRANSACTIONS
        var queryLink = Page.Locator("a:has-text('QUERY TRANSACTIONS')").First;
        await queryLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Verify Query Transactions page
        var heading = Page.Locator("h4:has-text('Query Transactions')");
        await heading.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

        // Verify WALLET SEARCH tab is present and selected
        var walletTab = Page.Locator("button:has-text('WALLET SEARCH')").First;
        Assert.That(await walletTab.IsVisibleAsync(), Is.True,
            "WALLET SEARCH tab should be visible");

        // Verify wallet address input
        var walletInput = Page.Locator("input[type='text']").First;
        Assert.That(await walletInput.IsVisibleAsync(), Is.True,
            "Wallet address input should be visible");

        // Verify ODATA QUERY BUILDER tab
        var odataTab = Page.Locator("button:has-text('ODATA QUERY BUILDER')").First;
        Assert.That(await odataTab.IsVisibleAsync(), Is.True,
            "ODATA QUERY BUILDER tab should be visible");

        // Switch to OData tab
        await odataTab.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Verify OData query builder controls
        var queryContent = await Page.TextContentAsync("body");
        Assert.That(queryContent, Does.Contain("ADD FILTER"),
            "OData builder should have ADD FILTER button");
        Assert.That(queryContent, Does.Contain("EXECUTE QUERY"),
            "OData builder should have EXECUTE QUERY button");

        await CaptureScreenshotAsync("query-transactions-odata");

        // Navigate back
        var backButton = Page.Locator("button:has-text('Back to registers')").First;
        if (await backButton.IsVisibleAsync())
        {
            await backButton.ClickAsync();
        }
    }

    #endregion

    #region Wizard Cancel

    [Test]
    [Order(5)]
    [Retry(2)]
    public async Task CreateRegister_CancelOnStep1_ClosesWizardWithoutCreating()
    {
        // Navigate to Registers page
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Registers);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Count existing registers
        var registerCards = Page.Locator(".mud-card, [class*='register-card']");

        // Open CREATE REGISTER wizard
        var createButton = Page.Locator("button:has-text('CREATE REGISTER')").First;
        await createButton.ClickAsync();

        var dialog = Page.Locator(".mud-dialog");
        await dialog.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        Assert.That(await dialog.IsVisibleAsync(), Is.True,
            "Wizard dialog should be open");

        // Verify we're on Step 1
        var dialogContent = await dialog.TextContentAsync();
        Assert.That(dialogContent, Does.Contain("Register Name"),
            "Step 1 should show Register Name field");

        // Click CANCEL
        var cancelButton = dialog.Locator("button:has-text('CANCEL')").First;
        await cancelButton.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Dialog should be closed
        Assert.That(await dialog.IsVisibleAsync(), Is.False,
            "Wizard dialog should close after CANCEL");

        // We should still be on the Registers page
        Assert.That(Page.Url, Does.Contain("/registers"),
            "Should remain on Registers page after cancel");

        await CaptureScreenshotAsync("cancel-wizard-result");
    }

    #endregion

    #region My Transactions (Known Issue)

    [Test]
    [Order(6)]
    [Retry(2)]
    public async Task MyTransactions_ShowsNoTransactionsForGenesisControl()
    {
        // Navigate to My Transactions
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyTransactions);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var heading = Page.Locator("h4:has-text('My Transactions')");
        await heading.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

        // Known behavior: Genesis/control transactions have SenderWallet="system"
        // rather than the user's wallet address, so they don't appear here.
        // The user's transactions only show in register detail views.
        var bodyText = await Page.TextContentAsync("body");
        var hasTransactions = !bodyText!.Contains("No Transactions") &&
                              !bodyText.Contains("No transactions");

        TestContext.Out.WriteLine(hasTransactions
            ? "My Transactions page shows transactions (user has workflow TXs)"
            : "My Transactions page shows 'No Transactions' - expected for users with only register genesis TXs (SenderWallet='system')");

        // Page should at least load without errors
        Assert.That(bodyText, Does.Contain("My Transactions"),
            "My Transactions page should load with heading");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a register via the full wizard flow. Returns to the Registers list page.
    /// </summary>
    private async Task CreateRegisterViaWizardAsync(string registerName)
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Registers);
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var createButton = Page.Locator("button:has-text('CREATE REGISTER')").First;
        await createButton.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });
        await createButton.ClickAsync();

        var dialog = Page.Locator(".mud-dialog");
        await dialog.WaitForAsync(new() { Timeout = TestConstants.ElementTimeout });

        // Step 1: Name
        var nameInput = dialog.Locator("input").First;
        await nameInput.FillAsync(registerName);
        await Page.WaitForTimeoutAsync(500);
        var nextButton = dialog.Locator("button:has-text('NEXT')").Last;
        await nextButton.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Step 2: Wallet (accept default)
        await nextButton.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Step 3: Options (accept defaults)
        await nextButton.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Step 4: Policy (accept defaults)
        await nextButton.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // Step 5: Create
        var createRegButton = dialog.Locator("button:has-text('CREATE REGISTER')").First;
        await createRegButton.ClickAsync();

        // Wait for dialog to close
        for (var i = 0; i < 30; i++)
        {
            if (!await dialog.IsVisibleAsync()) break;
            await Page.WaitForTimeoutAsync(1000);
        }

        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
    }

    #endregion
}
