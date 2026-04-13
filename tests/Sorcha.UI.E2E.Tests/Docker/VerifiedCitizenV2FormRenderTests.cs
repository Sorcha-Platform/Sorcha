// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// End-to-end regression coverage for the Feature 103 (Verified Citizen v2)
/// form renderer bug discovered in wave 10. The v2 blueprint references four
/// Sorcha core identity primitives (PersonName/v1, DateOfBirth/v1,
/// EmailAddress/v1, PostalAddress/v1) via <c>$ref</c>, and its wizard pages
/// use untitled section wrappers to group the referenced fields.
/// </summary>
/// <remarks>
/// <para>
/// Before wave 10 the form rendered page headings and descriptions but
/// ZERO input fields because two overlapping bugs silently ate the schema:
/// </para>
/// <list type="number">
///   <item><description>
///     <c>SchemaLayoutParser.TryParseSections</c> dropped sections without
///     a <c>title</c>. The v2 wizard pages use untitled "invisible wrapper"
///     sections because the page title already labels the grouping.
///   </description></item>
///   <item><description>
///     <c>FormSchemaService.InferControlFromSchema</c> mapped object-typed
///     properties to a single TextLine control with no children, so even
///     if Bug A were fixed the nested primitive fields would never render.
///   </description></item>
/// </list>
/// <para>
/// This test drives the UI end-to-end against Docker, using the walkthrough
/// state file written by <c>walkthroughs/HaipVerifiedCitizen/setup.ps1</c>
/// to locate the citizen account and the published blueprint. If the
/// walkthrough hasn't been run, the fixture calls <see cref="Assert.Ignore"/>
/// with a clear reason so CI runs without the walkthrough still pass.
/// </para>
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("Workflows")]
[Category("FormRenderer")]
[Category("Feature103")]
[Parallelizable(ParallelScope.Self)]
public class VerifiedCitizenV2FormRenderTests : DockerTestBase
{
    private static readonly string WalkthroughStatePath = Path.Combine(
        GetRepoRoot(),
        "walkthroughs",
        "HaipVerifiedCitizen",
        "state.json");

    private string _citizenEmail = string.Empty;
    private string _citizenPassword = string.Empty;
    private string _blueprintId = string.Empty;
    private string _registerId = string.Empty;

    // Don't auto-fail on console errors — the MudBlazor form renderer emits
    // authorization-failure info messages for admin-only endpoints that are
    // unrelated to the form itself.
    protected override bool AssertNoConsoleErrors => false;

    [OneTimeSetUp]
    public void LoadWalkthroughState()
    {
        if (!File.Exists(WalkthroughStatePath))
        {
            Assert.Ignore(
                $"HaipVerifiedCitizen walkthrough state not found at '{WalkthroughStatePath}'. " +
                "Run `pwsh walkthroughs/HaipVerifiedCitizen/setup.ps1 -Force` before this test.");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(WalkthroughStatePath));
        var root = doc.RootElement;

        _blueprintId = root.GetProperty("blueprintId").GetString() ?? string.Empty;
        _registerId = root.GetProperty("registerId").GetString() ?? string.Empty;

        var citizen = root.GetProperty("roles").GetProperty("citizen");
        _citizenEmail = citizen.GetProperty("email").GetString() ?? string.Empty;
        _citizenPassword = citizen.GetProperty("password").GetString() ?? string.Empty;

        if (string.IsNullOrEmpty(_blueprintId) ||
            string.IsNullOrEmpty(_registerId) ||
            string.IsNullOrEmpty(_citizenEmail))
        {
            Assert.Ignore("Walkthrough state is missing required fields; re-run setup.ps1.");
        }
    }

    [Test]
    public async Task VerifiedCitizenV2_Page1AndPage2_RenderNestedPrimitiveFields()
    {
        // Primary regression test for wave 10 Bug A + Bug B. Covers:
        //   Page 1: PersonName/v1 (4 string fields inlined from $ref)
        //   Page 2: DateOfBirth/v1 (1 date field inlined from $ref)
        // Does NOT test the DatePicker fill flow — that's UI plumbing
        // unrelated to the Feature 103 form-render regression.
        await LoginAsCitizenAsync();

        var submissionUrl = $"{TestConstants.UiWebUrl}/app/new-submission/{_registerId}/{_blueprintId}";
        await Page.GotoAsync(submissionUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // MudTextField renders the schema title as the accessible-name
        // attribute on the <input>, with a trailing "*" on required fields.

        // ---- Page 1: Your Name (PersonName/v1) ----
        await Expect(Page.Locator("h6:has-text('Your Name')")).ToBeVisibleAsync();
        var givenName = Page.GetByRole(AriaRole.Textbox, new() { Name = "Given Name*" });
        var middleName = Page.GetByRole(AriaRole.Textbox, new() { Name = "Middle Name" });
        var familyName = Page.GetByRole(AriaRole.Textbox, new() { Name = "Family Name*" });
        var fullName = Page.GetByRole(AriaRole.Textbox, new() { Name = "Full Name" });

        await Expect(givenName).ToBeVisibleAsync();
        await Expect(middleName).ToBeVisibleAsync();
        await Expect(familyName).ToBeVisibleAsync();
        await Expect(fullName).ToBeVisibleAsync();

        // Fill the required fields and advance. If IsRequired has
        // regressed and inherited the root-level marker, this will fail
        // because middle/full name would block the NEXT button.
        await givenName.FillAsync("Alice");
        await familyName.FillAsync("O'Brien");
        await Page.GetByRole(AriaRole.Button, new() { Name = "NEXT: DATE OF BIRTH" }).ClickAsync();

        // ---- Page 2: Date of Birth (DateOfBirth/v1) ----
        await Expect(Page.Locator("h6:has-text('Date of Birth')")).ToBeVisibleAsync();
        var dob = Page.GetByRole(AriaRole.Textbox, new() { Name = "Date of Birth*" });
        await Expect(dob).ToBeVisibleAsync();
    }

    // NOTE: Pages 3 (Contact / EmailAddress/v1) and 4 (Address /
    // PostalAddress/v1) are NOT covered by an E2E advance-through test
    // because the MudDatePicker on page 2 requires interactive popup
    // driving to accept a date — the Playwright Fill action fails
    // against the read-only picker input. Field-rendering coverage for
    // pages 3-4 is provided by the unit tests in
    // FormSchemaServiceTests.AutoGenerateForm_ObjectWithDateChild_EmitsDateTimeLeaf
    // and AutoGenerateForm_ObjectWithPostcodeChild_DispatchesToPostcodeLookup
    // which exercise the same recursion path that rendered them in
    // manual testing.

    [Test]
    public async Task VerifiedCitizenV2_FirstPage_HasCorrectRequiredMarkers()
    {
        await LoginAsCitizenAsync();

        var submissionUrl = $"{TestConstants.UiWebUrl}/app/new-submission/{_registerId}/{_blueprintId}";
        await Page.GotoAsync(submissionUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // PersonName/v1 declares `required: ["givenName", "familyName"]` on
        // the NESTED schema. Before wave 10's IsRequired fix, every nested
        // field inherited the root-level `required: ["name"]` marker, which
        // was wrong.
        //
        // MudTextField renders required fields with a trailing "*" suffix
        // on the accessible name ("Given Name*"), so the accessible-name
        // lookup itself encodes the required state.
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Given Name*" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Family Name*" })).ToBeVisibleAsync();

        // Middle Name and Full Name are optional per PersonName/v1 and
        // MUST render WITHOUT the trailing asterisk.
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Middle Name" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Full Name" })).ToBeVisibleAsync();

        // Negative check: the required-marked versions MUST NOT exist for
        // the optional fields. If they did, IsRequired has regressed back
        // to inheriting the root-level marker.
        Assert.That(
            await Page.GetByRole(AriaRole.Textbox, new() { Name = "Middle Name*" }).CountAsync(),
            Is.Zero,
            "Middle Name is optional per PersonName/v1 but is rendering with a required marker");
        Assert.That(
            await Page.GetByRole(AriaRole.Textbox, new() { Name = "Full Name*" }).CountAsync(),
            Is.Zero,
            "Full Name is optional per PersonName/v1 but is rendering with a required marker");
    }

    private async Task LoginAsCitizenAsync()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}/auth/login",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

        var emailInput = Page.Locator("input[placeholder='you@example.com']");
        await emailInput.WaitForAsync(new() { Timeout = 15000 });
        await emailInput.FillAsync(_citizenEmail);
        await Page.Locator("input[placeholder='Enter password']").FillAsync(_citizenPassword);

        // The sign-in button is a MudButton rendered as <button> with text
        // "Sign In". Match by the button role and name.
        var signInButton = Page.Locator("button:has-text('Sign In')").First;
        await signInButton.WaitForAsync(new() { Timeout = 15000 });
        await signInButton.ClickAsync();

        // Wait for the URL to transition into /app/*. Default timeout is
        // generous because the first auth also triggers Blazor WASM hydration.
        await Page.WaitForURLAsync(url => url.Contains("/app"), new() { Timeout = 30000 });
    }

    private static string GetRepoRoot()
    {
        // Walk up from the test DLL location until we find the solution file
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "sorcha.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
