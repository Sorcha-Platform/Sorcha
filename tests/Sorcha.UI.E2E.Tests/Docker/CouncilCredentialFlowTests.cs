// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;
using Sorcha.UI.E2E.Tests.PageObjects.WorkflowPages;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// End-to-end integration test exercising the full Sorcha platform through a realistic
/// civic scenario: a citizen obtains a digital ID credential from Eastbourne Council,
/// then uses that credential to access a council service (foodbank support).
///
/// This is an exploratory/diagnostic test. The first pass identifies UI and backend
/// issues — failures are catalogued as issues, not necessarily fixed immediately.
///
/// Flow 1: Digital ID Credential Issuance (citizen → id-dept → id-dept)
/// Flow 2: Service Request with Credential Presentation (citizen → service-dept → return-dept)
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("LongRunning")]
[Category("E2E")]
[NonParallelizable]
public class CouncilCredentialFlowTests : MultiUserTestBase
{
    // Disable automatic console/network error assertions — we catalogue issues manually
    protected override bool AssertNoConsoleErrors => false;
    protected override bool AssertNoNetworkFailures => false;
    protected override bool ValidateLayoutHealth => false;

    #region Test State — shared across ordered tests

    // Admin credentials (from bootstrap)
    private const string AdminEmail = TestConstants.TestEmail;
    private const string AdminPassword = TestConstants.TestPassword;

    // Council org
    private string _councilOrgId = null!;
    private const string CouncilOrgName = "Eastbourne Council";
    private const string CouncilSubdomain = "eastbourne";

    // Council staff users
    private const string CouncilAdminEmail = "council.admin@eastbourne.test";
    private const string IdDeptEmail = "id.dept@eastbourne.test";
    private const string ServiceDeptEmail = "service.dept@eastbourne.test";
    private const string ReturnDeptEmail = "return.dept@eastbourne.test";
    private const string StaffPassword = "Council_Staff_2026!";

    // Citizen
    private const string CitizenEmail = "jane.citizen@email.test";
    private const string CitizenPassword = "Citizen_Pass_2026!";
    private const string CitizenDisplayName = "Jane Citizen";

    // Wallet addresses (populated during setup)
    private string _councilAdminWallet = null!;
    private string _idDeptWallet = null!;
    private string _serviceDeptWallet = null!;
    private string _returnDeptWallet = null!;
    private string _citizenWallet = null!;

    // Participant IDs
    private string _idDeptParticipantId = null!;
    private string _serviceDeptParticipantId = null!;
    private string _returnDeptParticipantId = null!;
    private string _citizenParticipantId = null!;

    // Register
    private string _registerId = null!;

    // Blueprint IDs (populated after publishing)
    private string _idBlueprintId = null!;
    private string _serviceBlueprintId = null!;

    // Workflow instance IDs
    private string _idInstanceId = null!;
    private string _serviceInstanceId = null!;

    // Credential reference from Flow 1
    private string _credentialRef = null!;

    // Issue catalogue
    private readonly List<string> _issues = [];

    #endregion

    #region Setup & Teardown

    [OneTimeSetUp]
    public async Task SetupPlatformAndCouncil()
    {
        TestContext.Out.WriteLine("=== Council Credential Flow: Platform Setup ===");

        try
        {
            // Step 1: Get admin token
            TestContext.Out.WriteLine("Getting admin token...");
            var adminToken = await GetTokenAsync(AdminEmail, AdminPassword);
            StoreUserToken("admin", adminToken);
            SetApiToken(adminToken);
            TestContext.Out.WriteLine("Admin token obtained.");

            // Step 2: Enable public organization (required for self-registration)
            TestContext.Out.WriteLine("Enabling public organization...");
            await EnablePublicOrgAsync(adminToken);

            // Step 3: Pre-register council admin as PlatformUser via public org self-registration.
            // This ensures the org provisioner finds them by email and directly adds them
            // to the new org, avoiding the invitation-email path (no SMTP in Docker).
            TestContext.Out.WriteLine("Pre-registering council admin user...");
            await RegisterUserViaPublicOrgAsync(CouncilAdminEmail, "Council Admin", StaffPassword);

            // Step 4: Create Eastbourne Council organization (provisioner finds council admin, skips invite)
            TestContext.Out.WriteLine("Creating Eastbourne Council organization...");
            _councilOrgId = await CreateOrganizationAsync(adminToken, CouncilOrgName, CouncilSubdomain);
            TestContext.Out.WriteLine($"Council org created: {_councilOrgId}");

            // Step 5: Create remaining council staff users
            TestContext.Out.WriteLine("Creating council staff users...");
            await CreateStaffUsersAsync(adminToken);

            // Step 5: Create wallets for staff users
            TestContext.Out.WriteLine("Creating wallets for council staff...");
            await CreateStaffWalletsAsync();

            // Step 6: Register participants and link wallets
            TestContext.Out.WriteLine("Registering participants and linking wallets...");
            await RegisterStaffParticipantsAsync();

            // Step 7: Create register
            TestContext.Out.WriteLine("Creating council register...");
            await CreateRegisterAsync();

            // Step 8: Publish blueprints
            TestContext.Out.WriteLine("Publishing blueprints...");
            await PublishBlueprintsAsync();

            TestContext.Out.WriteLine("=== Platform Setup Complete ===");
        }
        catch (Exception ex)
        {
            LogIssue("SETUP-001", "Platform Setup Failed", "Blocker", "Setup",
                "OneTimeSetUp", "Platform should be configured",
                $"Setup failed: {ex.Message}", ex.ToString());
            throw;
        }
    }

    [OneTimeTearDown]
    public async Task CaptureResults()
    {
        TestContext.Out.WriteLine("\n=== Council Credential Flow: Results ===");
        TestContext.Out.WriteLine($"Total issues found: {_issues.Count}");

        foreach (var issue in _issues)
        {
            TestContext.Out.WriteLine(issue);
        }

        // Capture final screenshots from any active user pages
        foreach (var userName in new[] { "citizen", "id-dept", "service-dept", "return-dept" })
        {
            try
            {
                await CaptureUserScreenshotAsync(userName, "final-state");
            }
            catch
            {
                // User may not have been logged in
            }
        }

        TestContext.Out.WriteLine("=== End Results ===");

        // Clean up all browser contexts and API client
        await DisposeAllUserContextsAsync();
    }

    #endregion

    #region Test 1: Citizen Registration

    [Test, Order(1)]
    public async Task CitizenRegistration_SignupAndCreateWallet()
    {
        TestContext.Out.WriteLine("\n--- Test 1: Citizen Registration ---");

        // Step 1: Navigate to signup page
        var signupPage = new SignupPage(Page);
        await signupPage.NavigateAsync();

        var formLoaded = await signupPage.WaitForFormAsync();
        if (!formLoaded)
        {
            LogIssue("CIT-001", "Signup page did not load", "Blocker", "UI",
                "CitizenRegistration", "Signup page should load with form",
                "Page did not render or form not found");
            await CaptureScreenshotAsync("signup-not-loaded");
            Assert.Fail("Signup page did not load");
        }

        await CaptureScreenshotAsync("signup-loaded");

        // Step 2: Register via email tab — log issues but don't fail (API fallback available)
        var uiSignupSucceeded = false;
        TestContext.Out.WriteLine("Attempting email signup...");
        try
        {
            await signupPage.SignUpWithEmailAsync(CitizenDisplayName, CitizenEmail, CitizenPassword);
            await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
            await CaptureScreenshotAsync("signup-submitted");

            // Check for success or error
            var error = await signupPage.GetErrorMessageAsync();
            if (error != null)
            {
                LogIssue("CIT-003", "Signup returned error", "Major", "Tenant",
                    "CitizenRegistration", "Signup should succeed",
                    $"Error: {error}");
            }

            var success = await signupPage.GetSuccessMessageAsync();
            TestContext.Out.WriteLine($"Signup result - Success: {success}, Error: {error}");
            uiSignupSucceeded = success != null || Page.Url.Contains("/dashboard")
                || Page.Url.Contains("/login"); // Redirect to login = success (verify email)
        }
        catch (Exception ex)
        {
            LogIssue("CIT-002", "Signup form submission failed", "Major", "UI",
                "CitizenRegistration", "Form should submit successfully",
                $"Exception: {ex.Message}");
            await CaptureScreenshotAsync("signup-submission-failed");
        }

        // Step 3: API fallback registration if UI didn't work
        if (!uiSignupSucceeded)
        {
            TestContext.Out.WriteLine("UI signup incomplete — using API fallback registration...");
            await RegisterCitizenViaApiAsync();
        }

        // Step 5: Login as citizen via UI (non-fatal — API token already stored)
        TestContext.Out.WriteLine("Logging in as citizen via UI...");
        try
        {
            var citizenPage = await LoginAsUserAsync("citizen", CitizenEmail, CitizenPassword);
            await CaptureUserScreenshotAsync("citizen", "post-login");

            var url = citizenPage.Url;
            TestContext.Out.WriteLine($"Citizen post-login URL: {url}");

            if (url.Contains("/auth/login"))
            {
                LogIssue("CIT-004", "Citizen UI login stuck on login page", "Major", "UI",
                    "CitizenRegistration", "Citizen should be redirected to dashboard",
                    $"URL: {url}. Possible org selection required for multi-org users.");
            }
        }
        catch (Exception ex)
        {
            LogIssue("CIT-005", "Citizen UI login exception", "Major", "UI",
                "CitizenRegistration", "UI login should succeed",
                $"Exception: {ex.Message}. API token is available — continuing with API operations.");
        }

        // Step 6: Create wallet via API (UI wallet creation is complex — use API for reliability)
        TestContext.Out.WriteLine("Creating citizen wallet via API...");
        _citizenWallet = await CreateWalletViaApiAsync("citizen", "Citizen Wallet");
        TestContext.Out.WriteLine($"Citizen wallet: {_citizenWallet}");

        // Step 7: Register as participant
        TestContext.Out.WriteLine("Registering citizen as participant...");
        _citizenParticipantId = await SelfRegisterParticipantAsync("citizen", CitizenDisplayName);
        TestContext.Out.WriteLine($"Citizen participant ID: {_citizenParticipantId}");

        // Step 8: Navigate to dashboard and verify
        var page = GetUserPage("citizen");
        await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(page, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("citizen", "dashboard");
        TestContext.Out.WriteLine("--- Test 1 Complete ---");
    }

    #endregion

    #region Test 2: Flow 1 — Citizen Applies for ID

    [Test, Order(2)]
    public async Task Flow1_CitizenAppliesForId()
    {
        TestContext.Out.WriteLine("\n--- Test 2: Citizen Applies for ID ---");

        // Start a blueprint instance via API (UI may not have "Start Workflow" button)
        TestContext.Out.WriteLine("Starting Council ID Application instance via API...");
        var citizenToken = GetUserToken("citizen");

        try
        {
            _idInstanceId = await StartBlueprintInstanceAsync(citizenToken, _idBlueprintId);
            TestContext.Out.WriteLine($"Instance started: {_idInstanceId}");
        }
        catch (Exception ex)
        {
            LogIssue("F1-001", "Failed to start ID blueprint instance", "Blocker", "Blueprint",
                "Flow1_CitizenAppliesForId", "Should start blueprint instance",
                $"Exception: {ex.Message}");
            Assert.Fail($"Failed to start instance: {ex.Message}");
        }

        // Submit Action 0: Apply for ID
        TestContext.Out.WriteLine("Submitting Action 0: Apply for ID...");
        var applicationData = new Dictionary<string, object>
        {
            ["fullName"] = "Jane Citizen",
            ["dateOfBirth"] = "1990-05-15",
            ["address"] = "42 High Street, Eastbourne, BN21 1AA",
            ["councilReferenceId"] = "EC-2026-001234"
        };

        try
        {
            await ExecuteActionAsync(citizenToken, _idInstanceId, _idBlueprintId,
                actionId: 0, _citizenWallet, _registerId, applicationData);
            TestContext.Out.WriteLine("Action 0 submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F1-002", "Failed to submit ID application action", "Blocker", "Blueprint",
                "Flow1_CitizenAppliesForId", "Action 0 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 0 failed: {ex.Message}");
        }

        // Navigate to My Actions to verify the submission is visible
        try
        {
            var citizenPage = GetUserPage("citizen");
            await citizenPage.GotoAsync(
                $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyWorkflows}");
            await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

            await CaptureUserScreenshotAsync("citizen", "my-workflows-after-apply");

            // Check if workflow is visible
            var workflowCards = citizenPage.Locator(".mud-card, .mud-table-row");
            var count = await workflowCards.CountAsync();
            TestContext.Out.WriteLine($"Workflow cards/rows visible: {count}");

            if (count == 0)
            {
                LogIssue("F1-003", "No workflows visible after submission", "Major", "UI",
                    "Flow1_CitizenAppliesForId",
                    "My Workflows should show the started instance",
                    "No workflow cards or table rows found");
            }
        }
        catch (Exception ex)
        {
            LogIssue("F1-004", "My Workflows page error", "Minor", "UI",
                "Flow1_CitizenAppliesForId", "Should display workflows",
                $"Exception: {ex.Message}");
        }

        TestContext.Out.WriteLine("--- Test 2 Complete ---");
    }

    #endregion

    #region Test 3: Flow 1 — ID Dept Verifies Citizen

    [Test, Order(3)]
    public async Task Flow1_IDDeptVerifiesCitizen()
    {
        TestContext.Out.WriteLine("\n--- Test 3: ID Dept Verifies Citizen ---");

        // Login as ID Dept
        TestContext.Out.WriteLine("Logging in as ID Dept...");
        try
        {
            await LoginAsUserAsync("id-dept", IdDeptEmail, StaffPassword);
            await CaptureUserScreenshotAsync("id-dept", "post-login");
        }
        catch (Exception ex)
        {
            LogIssue("F1-005", "ID Dept login failed", "Blocker", "Tenant",
                "Flow1_IDDeptVerifiesCitizen", "ID Dept should log in",
                $"Exception: {ex.Message}");
            Assert.Fail($"ID Dept login failed: {ex.Message}");
        }

        // Navigate to My Actions
        var idDeptPage = GetUserPage("id-dept");
        await idDeptPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}");
        await idDeptPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(idDeptPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("id-dept", "my-actions");

        // Check for pending actions
        var actionCards = idDeptPage.Locator(
            ".mud-card, [data-testid='action-card'], [data-testid='pending-action']");
        var actionCount = await actionCards.CountAsync();
        TestContext.Out.WriteLine($"Pending action cards visible: {actionCount}");

        if (actionCount == 0)
        {
            LogIssue("F1-006", "No pending actions for ID Dept", "Major", "UI",
                "Flow1_IDDeptVerifiesCitizen",
                "My Actions should show pending verification action",
                "No action cards found — may need wallet-address-based filtering");
        }

        // Submit Action 1: Verify Identity via API
        TestContext.Out.WriteLine("Submitting Action 1: Verify Identity via API...");
        var idDeptToken = GetUserToken("id-dept");
        var verificationData = new Dictionary<string, object>
        {
            ["decision"] = "approved",
            ["verificationMethod"] = "document",
            ["verifiedAt"] = DateTime.UtcNow.ToString("O"),
            ["notes"] = "Identity verified via government-issued photo ID"
        };

        try
        {
            await ExecuteActionAsync(idDeptToken, _idInstanceId, _idBlueprintId,
                actionId: 1, _idDeptWallet, _registerId, verificationData);
            TestContext.Out.WriteLine("Action 1 submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F1-007", "Failed to submit verification action", "Blocker", "Blueprint",
                "Flow1_IDDeptVerifiesCitizen", "Action 1 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 1 failed: {ex.Message}");
        }

        await CaptureUserScreenshotAsync("id-dept", "after-verification");
        TestContext.Out.WriteLine("--- Test 3 Complete ---");
    }

    #endregion

    #region Test 4: Flow 1 — ID Dept Issues Credential

    [Test, Order(4)]
    public async Task Flow1_IDDeptIssuesCredential()
    {
        TestContext.Out.WriteLine("\n--- Test 4: ID Dept Issues Credential ---");

        var idDeptToken = GetUserToken("id-dept");
        _credentialRef = $"EC-ID-{DateTime.UtcNow:yyyyMMdd}-001";

        var issuanceData = new Dictionary<string, object>
        {
            ["credentialNumber"] = _credentialRef,
            ["validFrom"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };

        TestContext.Out.WriteLine($"Issuing credential: {_credentialRef}");

        try
        {
            await ExecuteActionAsync(idDeptToken, _idInstanceId, _idBlueprintId,
                actionId: 2, _idDeptWallet, _registerId, issuanceData);
            TestContext.Out.WriteLine("Action 2 (Issue Credential) submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F1-008", "Failed to issue credential", "Blocker", "Blueprint",
                "Flow1_IDDeptIssuesCredential", "Action 2 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 2 failed: {ex.Message}");
        }

        // Verify the workflow instance is complete
        try
        {
            var instanceStatus = await GetInstanceStatusAsync(idDeptToken, _idInstanceId);
            TestContext.Out.WriteLine($"Instance status: {instanceStatus}");
        }
        catch (Exception ex)
        {
            LogIssue("F1-009", "Could not verify instance status", "Minor", "Blueprint",
                "Flow1_IDDeptIssuesCredential", "Should be able to check status",
                $"Exception: {ex.Message}");
        }

        TestContext.Out.WriteLine("--- Test 4 Complete ---");
    }

    #endregion

    #region Test 5: Flow 1 — Citizen Views Credential

    [Test, Order(5)]
    public async Task Flow1_CitizenViewsCredential()
    {
        TestContext.Out.WriteLine("\n--- Test 5: Citizen Views Credential ---");

        var citizenPage = GetUserPage("citizen");
        var credentialsPage = new CredentialsPage(citizenPage);

        // Navigate to credentials page
        await citizenPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}");
        await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("citizen", "credentials-page");

        // Check for credentials
        var pageLoaded = await credentialsPage.WaitForPageAsync(FullPageLoadTimeout);
        if (!pageLoaded)
        {
            LogIssue("F1-010", "Credentials page did not load", "Major", "UI",
                "Flow1_CitizenViewsCredential",
                "Credentials page should load",
                "Page content did not appear within timeout");
        }

        var hasError = await credentialsPage.HasServiceErrorAsync();
        if (hasError)
        {
            LogIssue("F1-011", "Credentials page shows service error", "Major", "UI",
                "Flow1_CitizenViewsCredential",
                "No service errors expected",
                "Service error displayed on credentials page");
        }

        var credentialCount = await credentialsPage.GetCredentialCountAsync();
        TestContext.Out.WriteLine($"Credentials displayed: {credentialCount}");

        if (credentialCount == 0)
        {
            var isEmpty = await credentialsPage.IsEmptyStateVisibleAsync();
            LogIssue("F1-012", "No credentials displayed for citizen", "Major", "UI",
                "Flow1_CitizenViewsCredential",
                "Citizen should see their issued credential",
                $"Empty state visible: {isEmpty}. Credential issuance may not be fully wired end-to-end.");
        }
        else
        {
            var firstCredText = await credentialsPage.GetFirstCredentialTextAsync();
            TestContext.Out.WriteLine($"First credential text: {firstCredText}");
        }

        TestContext.Out.WriteLine("--- Test 5 Complete ---");
    }

    #endregion

    #region Test 6: Flow 2 — Citizen Requests Service

    [Test, Order(6)]
    public async Task Flow2_CitizenRequestsService()
    {
        TestContext.Out.WriteLine("\n--- Test 6: Citizen Requests Service ---");

        var citizenToken = GetUserToken("citizen");

        // Start service request blueprint instance
        TestContext.Out.WriteLine("Starting Council Service Request instance...");
        try
        {
            _serviceInstanceId = await StartBlueprintInstanceAsync(citizenToken, _serviceBlueprintId);
            TestContext.Out.WriteLine($"Service instance started: {_serviceInstanceId}");
        }
        catch (Exception ex)
        {
            LogIssue("F2-001", "Failed to start service request instance", "Blocker", "Blueprint",
                "Flow2_CitizenRequestsService", "Should start instance",
                $"Exception: {ex.Message}");
            Assert.Fail($"Failed to start service instance: {ex.Message}");
        }

        // Submit Action 0: Request Service
        var requestData = new Dictionary<string, object>
        {
            ["serviceType"] = "foodbank",
            ["credentialRef"] = _credentialRef ?? "EC-ID-UNKNOWN",
            ["justification"] = "Family of four, primary earner recently made redundant. " +
                "Requesting emergency foodbank access for 4 weeks."
        };

        try
        {
            await ExecuteActionAsync(citizenToken, _serviceInstanceId, _serviceBlueprintId,
                actionId: 0, _citizenWallet, _registerId, requestData);
            TestContext.Out.WriteLine("Action 0 (Request Service) submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F2-002", "Failed to submit service request", "Blocker", "Blueprint",
                "Flow2_CitizenRequestsService", "Action 0 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 0 failed: {ex.Message}");
        }

        // Verify on citizen's My Workflows page
        try
        {
            var citizenPage = GetUserPage("citizen");
            await citizenPage.GotoAsync(
                $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyWorkflows}");
            await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

            await CaptureUserScreenshotAsync("citizen", "my-workflows-after-service-request");
        }
        catch (Exception ex)
        {
            LogIssue("F2-003", "My Workflows navigation failed", "Minor", "UI",
                "Flow2_CitizenRequestsService", "Should show workflows",
                $"Exception: {ex.Message}");
        }

        TestContext.Out.WriteLine("--- Test 6 Complete ---");
    }

    #endregion

    #region Test 7: Flow 2 — Service Dept Assesses

    [Test, Order(7)]
    public async Task Flow2_ServiceDeptAssesses()
    {
        TestContext.Out.WriteLine("\n--- Test 7: Service Dept Assesses ---");

        // Login as Service Dept
        TestContext.Out.WriteLine("Logging in as Service Dept...");
        try
        {
            await LoginAsUserAsync("service-dept", ServiceDeptEmail, StaffPassword);
            await CaptureUserScreenshotAsync("service-dept", "post-login");
        }
        catch (Exception ex)
        {
            LogIssue("F2-004", "Service Dept login failed", "Blocker", "Tenant",
                "Flow2_ServiceDeptAssesses", "Service Dept should log in",
                $"Exception: {ex.Message}");
            Assert.Fail($"Service Dept login failed: {ex.Message}");
        }

        // Navigate to My Actions
        var serviceDeptPage = GetUserPage("service-dept");
        await serviceDeptPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}");
        await serviceDeptPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(serviceDeptPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("service-dept", "my-actions");

        // Submit Action 1: Assess Request via API
        var serviceDeptToken = GetUserToken("service-dept");
        var assessmentData = new Dictionary<string, object>
        {
            ["decision"] = "approved",
            ["priority"] = "standard",
            ["assessmentNotes"] = "Credential verified. Foodbank access approved for 4-week period."
        };

        try
        {
            await ExecuteActionAsync(serviceDeptToken, _serviceInstanceId, _serviceBlueprintId,
                actionId: 1, _serviceDeptWallet, _registerId, assessmentData);
            TestContext.Out.WriteLine("Action 1 (Assess Request) submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F2-005", "Failed to submit assessment", "Blocker", "Blueprint",
                "Flow2_ServiceDeptAssesses", "Action 1 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 1 failed: {ex.Message}");
        }

        await CaptureUserScreenshotAsync("service-dept", "after-assessment");
        TestContext.Out.WriteLine("--- Test 7 Complete ---");
    }

    #endregion

    #region Test 8: Flow 2 — Return Dept Fulfils

    [Test, Order(8)]
    public async Task Flow2_ReturnDeptFulfils()
    {
        TestContext.Out.WriteLine("\n--- Test 8: Return Dept Fulfils ---");

        // Login as Return Dept
        TestContext.Out.WriteLine("Logging in as Return Dept...");
        try
        {
            await LoginAsUserAsync("return-dept", ReturnDeptEmail, StaffPassword);
            await CaptureUserScreenshotAsync("return-dept", "post-login");
        }
        catch (Exception ex)
        {
            LogIssue("F2-006", "Return Dept login failed", "Blocker", "Tenant",
                "Flow2_ReturnDeptFulfils", "Return Dept should log in",
                $"Exception: {ex.Message}");
            Assert.Fail($"Return Dept login failed: {ex.Message}");
        }

        // Navigate to My Actions
        var returnDeptPage = GetUserPage("return-dept");
        await returnDeptPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyActions}");
        await returnDeptPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(returnDeptPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("return-dept", "my-actions");

        // Submit Action 2: Fulfil Service via API
        var returnDeptToken = GetUserToken("return-dept");
        var fulfilmentData = new Dictionary<string, object>
        {
            ["fulfilmentDate"] = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
            ["deliveryMethod"] = "collection",
            ["referenceNumber"] = $"FB-{DateTime.UtcNow:yyyyMMdd}-001",
            ["completionNotes"] = "Foodbank parcel ready for collection at Eastbourne Community Centre. " +
                "Collection window: 9am-5pm weekdays."
        };

        try
        {
            await ExecuteActionAsync(returnDeptToken, _serviceInstanceId, _serviceBlueprintId,
                actionId: 2, _returnDeptWallet, _registerId, fulfilmentData);
            TestContext.Out.WriteLine("Action 2 (Fulfil Service) submitted successfully.");
        }
        catch (Exception ex)
        {
            LogIssue("F2-007", "Failed to submit fulfilment", "Blocker", "Blueprint",
                "Flow2_ReturnDeptFulfils", "Action 2 should execute",
                $"Exception: {ex.Message}");
            Assert.Fail($"Action 2 failed: {ex.Message}");
        }

        // Verify the workflow instance is complete
        try
        {
            var instanceStatus = await GetInstanceStatusAsync(returnDeptToken, _serviceInstanceId);
            TestContext.Out.WriteLine($"Service instance status: {instanceStatus}");
        }
        catch (Exception ex)
        {
            LogIssue("F2-008", "Could not verify service instance status", "Minor", "Blueprint",
                "Flow2_ReturnDeptFulfils", "Should be able to check status",
                $"Exception: {ex.Message}");
        }

        await CaptureUserScreenshotAsync("return-dept", "after-fulfilment");
        TestContext.Out.WriteLine("--- Test 8 Complete ---");
    }

    #endregion

    #region Test 9: Flow 2 — Citizen Confirms Fulfilment

    [Test, Order(9)]
    public async Task Flow2_CitizenConfirmsFulfilment()
    {
        TestContext.Out.WriteLine("\n--- Test 9: Citizen Confirms Fulfilment ---");

        var citizenPage = GetUserPage("citizen");

        // Navigate to My Workflows
        await citizenPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyWorkflows}");
        await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("citizen", "final-my-workflows");

        // Check workflow count
        var workflowCards = citizenPage.Locator(".mud-card, .mud-table-row");
        var count = await workflowCards.CountAsync();
        TestContext.Out.WriteLine($"Final workflow count: {count}");

        if (count < 2)
        {
            LogIssue("F2-009", "Not all workflows visible", "Major", "UI",
                "Flow2_CitizenConfirmsFulfilment",
                "Citizen should see both workflow instances",
                $"Expected at least 2, found {count}");
        }

        // Navigate to My Transactions to see the full ledger trail
        await citizenPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.MyTransactions}");
        await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("citizen", "final-my-transactions");

        var txRows = citizenPage.Locator(".mud-table-row, .mud-card");
        var txCount = await txRows.CountAsync();
        TestContext.Out.WriteLine($"Transaction rows visible: {txCount}");

        // Check credentials page one more time
        await citizenPage.GotoAsync(
            $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Credentials}");
        await citizenPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(citizenPage, FullPageLoadTimeout);

        await CaptureUserScreenshotAsync("citizen", "final-credentials");

        // Summary
        TestContext.Out.WriteLine("\n=== Flow Summary ===");
        TestContext.Out.WriteLine($"Council Org ID: {_councilOrgId}");
        TestContext.Out.WriteLine($"Register ID: {_registerId}");
        TestContext.Out.WriteLine($"ID Blueprint: {_idBlueprintId}");
        TestContext.Out.WriteLine($"Service Blueprint: {_serviceBlueprintId}");
        TestContext.Out.WriteLine($"ID Instance: {_idInstanceId}");
        TestContext.Out.WriteLine($"Service Instance: {_serviceInstanceId}");
        TestContext.Out.WriteLine($"Credential Ref: {_credentialRef}");
        TestContext.Out.WriteLine($"Total Issues: {_issues.Count}");
        TestContext.Out.WriteLine("--- Test 9 Complete ---");
    }

    #endregion

    #region API Setup Helpers

    private async Task EnablePublicOrgAsync(string adminToken)
    {
        try
        {
            var response = await ApiPutAsync("/api/platform/settings/public-org",
                new { enabled = true }, adminToken);
            var (status, body) = await ReadResponseAsync(response);
            TestContext.Out.WriteLine($"Enable public org: {status} - {body}");
        }
        catch (Exception ex)
        {
            LogIssue("SETUP-002", "Failed to enable public org", "Major", "Tenant",
                "Setup", "Public org should be enabled",
                $"Exception: {ex.Message}");
        }
    }

    private async Task RegisterUserViaPublicOrgAsync(string email, string displayName, string password)
    {
        var response = await ApiPostAsync("/api/auth/register", new
        {
            orgSubdomain = "public",
            email,
            password,
            displayName
        });

        var (status, body) = await ReadResponseAsync(response);
        TestContext.Out.WriteLine($"Register {email}: {status}");

        if (status >= 400 && status != 409)
        {
            LogIssue($"SETUP-REG-{email}", $"Failed to register {email}",
                "Major", "Tenant", "Setup",
                $"{email} should be registered",
                $"Status {status}: {body}");
        }

        // Auto-verify email (no SMTP in Docker test environment)
        await AutoVerifyEmailAsync(email);
    }

    private async Task<string> CreateOrganizationAsync(string adminToken, string name, string subdomain)
    {
        var response = await ApiPostAsync("/api/platform/organizations",
            new { name, subdomain, adminEmail = CouncilAdminEmail }, adminToken);
        var json = await ReadJsonAsync(response);

        // Try common property names for the org ID
        if (json.TryGetProperty("id", out var idProp))
            return idProp.GetString()!;
        if (json.TryGetProperty("organizationId", out var orgIdProp))
            return orgIdProp.GetString()!;

        // Fallback: if creation returned 409 (already exists), try to find it
        var listResponse = await ApiGetAsync("/api/platform/organizations", adminToken);
        var listJson = await ReadJsonAsync(listResponse);

        if (listJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var org in listJson.EnumerateArray())
            {
                var orgName = org.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (orgName == name && org.TryGetProperty("id", out var oid))
                    return oid.GetString()!;
            }
        }

        // Handle paginated response
        if (listJson.TryGetProperty("items", out var items) ||
            listJson.TryGetProperty("data", out items) ||
            listJson.TryGetProperty("organizations", out items))
        {
            foreach (var org in items.EnumerateArray())
            {
                var orgName = org.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (orgName == name && org.TryGetProperty("id", out var oid))
                    return oid.GetString()!;
            }
        }

        throw new InvalidOperationException(
            $"Could not determine org ID from response: {json.GetRawText()}");
    }

    private async Task CreateStaffUsersAsync(string adminToken)
    {
        // Strategy: Register all staff via public org self-registration (so they have
        // passwords and can get tokens), then add them to the council org via admin API.
        var staffUsers = new[]
        {
            (Email: IdDeptEmail, DisplayName: "ID Department", TokenKey: "id-dept", Roles: new[] { "Member" }),
            (Email: ServiceDeptEmail, DisplayName: "Service Department", TokenKey: "service-dept", Roles: new[] { "Member" }),
            (Email: ReturnDeptEmail, DisplayName: "Fulfilment Department", TokenKey: "return-dept", Roles: new[] { "Member" }),
        };

        // Phase 1: Register as PlatformUsers via public org
        foreach (var user in staffUsers)
        {
            await RegisterUserViaPublicOrgAsync(user.Email, user.DisplayName, StaffPassword);
        }

        // Phase 2: Add to council org via admin API
        foreach (var user in staffUsers)
        {
            try
            {
                var response = await ApiPostAsync(
                    $"/api/organizations/{_councilOrgId}/users",
                    new
                    {
                        email = user.Email,
                        displayName = user.DisplayName,
                        externalIdpUserId = $"{user.DisplayName.ToLower().Replace(' ', '-')}-{Guid.NewGuid().ToString()[..8]}",
                        roles = user.Roles
                    },
                    adminToken);

                var (status, body) = await ReadResponseAsync(response);
                TestContext.Out.WriteLine($"Create user {user.Email}: {status}");

                if (status == 409)
                {
                    TestContext.Out.WriteLine($"User {user.Email} already exists (409).");
                }
                else if (status >= 400)
                {
                    LogIssue($"SETUP-USR-{user.Email}", $"Failed to create user {user.Email}",
                        "Major", "Tenant", "Setup",
                        $"User {user.Email} should be created",
                        $"Status {status}: {body}");
                }
            }
            catch (Exception ex)
            {
                LogIssue($"SETUP-USR-{user.Email}", $"Exception creating user {user.Email}",
                    "Major", "Tenant", "Setup",
                    $"User {user.Email} should be created",
                    $"Exception: {ex.Message}");
            }
        }

        // Phase 3: Get tokens for council admin + all staff users.
        // Token login may issue a token for the public org — users may need to switch org.
        var allStaff = new[] { (Email: CouncilAdminEmail, TokenKey: "council-admin") }
            .Concat(staffUsers.Select(s => (s.Email, s.TokenKey)));

        foreach (var user in allStaff)
        {
            try
            {
                var token = await GetTokenAsync(user.Email, StaffPassword);
                StoreUserToken(user.TokenKey, token);
                TestContext.Out.WriteLine($"Token obtained for {user.Email} → {user.TokenKey}");

                // Try switching to council org if the token is for a different org
                try
                {
                    var switchResponse = await ApiPostAsync("/api/auth/switch-org",
                        new { organizationId = _councilOrgId }, token);
                    var (switchStatus, switchBody) = await ReadResponseAsync(switchResponse);
                    if (switchStatus == 200 && !string.IsNullOrWhiteSpace(switchBody))
                    {
                        using var switchDoc = JsonDocument.Parse(switchBody);
                        if (switchDoc.RootElement.TryGetProperty("accessToken", out var at) ||
                            switchDoc.RootElement.TryGetProperty("access_token", out at))
                        {
                            StoreUserToken(user.TokenKey, at.GetString()!);
                            TestContext.Out.WriteLine($"Switched {user.Email} to council org");
                        }
                    }
                }
                catch
                {
                    // Org switch not available or failed — proceed with original token
                }
            }
            catch (Exception ex)
            {
                LogIssue($"SETUP-TOK-{user.TokenKey}", $"Failed to get token for {user.Email}",
                    "Major", "Tenant", "Setup",
                    $"Should obtain token for {user.Email}",
                    $"Exception: {ex.Message}");
            }
        }
    }

    private async Task CreateStaffWalletsAsync()
    {
        var walletAssignments = new (string UserKey, string Name, Action<string> SetWallet)[]
        {
            ("council-admin", "Council Admin Wallet", w => _councilAdminWallet = w),
            ("id-dept", "ID Dept Wallet", w => _idDeptWallet = w),
            ("service-dept", "Service Dept Wallet", w => _serviceDeptWallet = w),
            ("return-dept", "Return Dept Wallet", w => _returnDeptWallet = w),
        };

        foreach (var (userKey, name, setWallet) in walletAssignments)
        {
            try
            {
                var address = await CreateWalletViaApiAsync(userKey, name);
                setWallet(address);
                TestContext.Out.WriteLine($"Wallet for {userKey}: {address}");
            }
            catch (Exception ex)
            {
                LogIssue($"SETUP-WAL-{userKey}", $"Failed to create wallet for {userKey}",
                    "Major", "Wallet", "Setup",
                    $"Wallet should be created for {userKey}",
                    $"Exception: {ex.Message}");
            }
        }
    }

    private async Task RegisterStaffParticipantsAsync()
    {
        var participants = new (string UserKey, string DisplayName, Action<string> SetId)[]
        {
            ("id-dept", "ID Department", id => _idDeptParticipantId = id),
            ("service-dept", "Service Department", id => _serviceDeptParticipantId = id),
            ("return-dept", "Fulfilment Department", id => _returnDeptParticipantId = id),
        };

        foreach (var (userKey, displayName, setId) in participants)
        {
            try
            {
                var participantId = await SelfRegisterParticipantAsync(userKey, displayName);
                setId(participantId);
                TestContext.Out.WriteLine($"Participant for {userKey}: {participantId}");
            }
            catch (Exception ex)
            {
                LogIssue($"SETUP-PART-{userKey}", $"Failed to register participant for {userKey}",
                    "Major", "Tenant", "Setup",
                    $"Participant should be registered for {userKey}",
                    $"Exception: {ex.Message}");
            }
        }
    }

    private async Task CreateRegisterAsync()
    {
        var adminToken = GetUserToken("admin");

        try
        {
            // Initiate register
            var initiateResponse = await ApiPostAsync("/api/registers/initiate",
                new
                {
                    name = "Eastbourne Council Register",
                    description = "Register for Eastbourne Council digital services"
                }, adminToken);

            var initiateJson = await ReadJsonAsync(initiateResponse);
            _registerId = initiateJson.TryGetProperty("registerId", out var rid)
                ? rid.GetString()!
                : initiateJson.TryGetProperty("id", out var id)
                    ? id.GetString()!
                    : throw new InvalidOperationException(
                        $"No registerId in response: {initiateJson.GetRawText()}");

            TestContext.Out.WriteLine($"Register initiated: {_registerId}");

            // Finalize register (simplified — full flow needs attestation signing)
            var finalizeResponse = await ApiPostAsync(
                $"/api/registers/finalize",
                new { registerId = _registerId }, adminToken);

            var (finalizeStatus, finalizeBody) = await ReadResponseAsync(finalizeResponse);
            TestContext.Out.WriteLine($"Register finalize: {finalizeStatus}");

            if (finalizeStatus >= 400)
            {
                LogIssue("SETUP-REG", "Register finalization issue", "Major", "Register",
                    "Setup", "Register should finalize",
                    $"Status {finalizeStatus}: {finalizeBody}. " +
                    "May need attestation signing — using register ID as-is.");
            }
        }
        catch (Exception ex)
        {
            // Fallback: use a fixed register ID
            _registerId = $"council-register-{DateTime.UtcNow:yyyyMMddHHmmss}";
            LogIssue("SETUP-REG", "Register creation failed", "Major", "Register",
                "Setup", "Register should be created",
                $"Exception: {ex.Message}. Using fallback ID: {_registerId}");
        }
    }

    /// <summary>
    /// Resolves a path relative to the repository root (walks up from test bin to find .git).
    /// </summary>
    private static string RepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir != null
            ? Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar))
            : relativePath;
    }

    private async Task PublishBlueprintsAsync()
    {
        var adminToken = GetUserToken("admin");

        // Publish Council ID Application blueprint
        _idBlueprintId = await PublishBlueprintAsync(adminToken,
            RepoPath("blueprints/templates/council-id-application-template.json"),
            "council-id-app",
            new Dictionary<string, string>
            {
                ["citizen"] = _citizenWallet ?? "pending-citizen-wallet",
                ["id-dept"] = _idDeptWallet ?? "pending-id-dept-wallet"
            });
        TestContext.Out.WriteLine($"ID Blueprint published: {_idBlueprintId}");

        // Publish Council Service Request blueprint
        _serviceBlueprintId = await PublishBlueprintAsync(adminToken,
            RepoPath("blueprints/templates/council-service-request-template.json"),
            "council-svc-req",
            new Dictionary<string, string>
            {
                ["citizen"] = _citizenWallet ?? "pending-citizen-wallet",
                ["service-dept"] = _serviceDeptWallet ?? "pending-service-dept-wallet",
                ["return-dept"] = _returnDeptWallet ?? "pending-return-dept-wallet"
            });
        TestContext.Out.WriteLine($"Service Blueprint published: {_serviceBlueprintId}");
    }

    private async Task<string> PublishBlueprintAsync(string token, string templatePath,
        string idPrefix, Dictionary<string, string> walletMappings)
    {
        // Load template
        var templateJson = await File.ReadAllTextAsync(templatePath);
        using var templateDoc = JsonDocument.Parse(templateJson);
        var template = templateDoc.RootElement.GetProperty("template");

        // Build blueprint with wallet addresses patched in
        var blueprintId = $"{idPrefix}-{DateTime.UtcNow:yyyyMMddHHmmss}";

        // Reconstruct participants with wallet addresses
        var participants = new List<object>();
        foreach (var participant in template.GetProperty("participants").EnumerateArray())
        {
            var pid = participant.GetProperty("id").GetString()!;
            var pObj = new Dictionary<string, object>
            {
                ["id"] = pid,
                ["name"] = participant.GetProperty("name").GetString()!,
                ["organisation"] = participant.GetProperty("organisation").GetString()!,
            };
            if (participant.TryGetProperty("description", out var desc))
                pObj["description"] = desc.GetString()!;
            if (walletMappings.TryGetValue(pid, out var wallet))
                pObj["walletAddress"] = wallet;
            participants.Add(pObj);
        }

        // Reconstruct actions
        var actions = new List<object>();
        foreach (var action in template.GetProperty("actions").EnumerateArray())
        {
            // Pass through the raw JSON for each action
            actions.Add(JsonSerializer.Deserialize<object>(action.GetRawText(), JsonOptions)!);
        }

        var blueprint = new Dictionary<string, object>
        {
            ["id"] = blueprintId,
            ["title"] = template.GetProperty("title").GetString()!,
            ["description"] = template.TryGetProperty("description", out var d)
                ? d.GetString()! : "",
            ["version"] = 1,
            ["metadata"] = new Dictionary<string, string>
            {
                ["category"] = "credentials",
                ["author"] = "E2E Test"
            },
            ["participants"] = participants,
            ["actions"] = actions
        };

        // Create blueprint
        var createResponse = await ApiPostAsync("/api/blueprints", blueprint, token);
        var (createStatus, createBody) = await ReadResponseAsync(createResponse);
        TestContext.Out.WriteLine($"Create blueprint {idPrefix}: {createStatus}");

        if (createStatus >= 400)
        {
            throw new InvalidOperationException(
                $"Blueprint creation failed ({createStatus}): {createBody}");
        }

        // Extract the server-generated ID from the creation response
        if (!string.IsNullOrWhiteSpace(createBody))
        {
            using var createDoc = JsonDocument.Parse(createBody);
            if (createDoc.RootElement.TryGetProperty("id", out var idProp))
                blueprintId = idProp.GetString() ?? blueprintId;
            else if (createDoc.RootElement.TryGetProperty("blueprintId", out var bpIdProp))
                blueprintId = bpIdProp.GetString() ?? blueprintId;
        }
        TestContext.Out.WriteLine($"Blueprint server ID: {blueprintId}");

        // Publish blueprint
        var publishResponse = await ApiPostAsync(
            $"/api/blueprints/{blueprintId}/publish", null, token);
        var (pubStatus, pubBody) = await ReadResponseAsync(publishResponse);
        TestContext.Out.WriteLine($"Publish blueprint {blueprintId}: {pubStatus}");

        if (pubStatus >= 400)
        {
            LogIssue($"SETUP-BP-{idPrefix}", "Blueprint publish failed", "Major", "Blueprint",
                "Setup", "Blueprint should publish",
                $"Status {pubStatus}: {pubBody}");
        }

        return blueprintId;
    }

    #endregion

    #region API Action Helpers

    private async Task RegisterCitizenViaApiAsync()
    {
        try
        {
            // Self-register via API
            var response = await ApiPostAsync("/api/auth/register", new
            {
                email = CitizenEmail,
                password = CitizenPassword,
                displayName = CitizenDisplayName,
                orgSubdomain = "public"
            });
            var (status, body) = await ReadResponseAsync(response);
            TestContext.Out.WriteLine($"API citizen registration: {status} - {body}");

            // Auto-verify email via Docker psql (no SMTP in test environment)
            await AutoVerifyEmailAsync(CitizenEmail);

            // Get citizen token
            var token = await GetTokenAsync(CitizenEmail, CitizenPassword);
            StoreUserToken("citizen", token);
            TestContext.Out.WriteLine("Citizen token obtained.");
        }
        catch (Exception ex)
        {
            LogIssue("CIT-API-002", "API registration exception", "Blocker", "Tenant",
                "CitizenRegistration", "API registration should succeed",
                $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Auto-verifies a user's email by updating the PlatformUsers table directly.
    /// Only for test environments — no SMTP server is available in Docker.
    /// </summary>
    private static async Task AutoVerifyEmailAsync(string email)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"exec sorcha-postgres psql -U sorcha -d sorcha_tenant -c " +
                    $"\"UPDATE \\\"PlatformUsers\\\" SET \\\"EmailVerified\\\" = true, " +
                    $"\\\"EmailVerifiedAt\\\" = NOW() WHERE \\\"Email\\\" = '{email}';\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        TestContext.Out.WriteLine($"Auto-verify email {email}: {output.Trim()}");
    }

    private async Task<string> CreateWalletViaApiAsync(string userKey, string walletName)
    {
        var token = GetUserToken(userKey);

        var response = await ApiPostAsync("/api/v1/wallets",
            new
            {
                name = walletName,
                algorithm = "ED25519",
                wordCount = 12
            }, token);

        var json = await ReadJsonAsync(response);

        // Try common property names (may be nested under "wallet")
        if (json.TryGetProperty("wallet", out var walletObj))
        {
            if (walletObj.TryGetProperty("address", out var wa))
                return wa.GetString()!;
        }
        if (json.TryGetProperty("address", out var addr))
            return addr.GetString()!;
        if (json.TryGetProperty("walletAddress", out var wa2))
            return wa2.GetString()!;

        throw new InvalidOperationException(
            $"No wallet address in response: {json.GetRawText()}");
    }

    private async Task<string> SelfRegisterParticipantAsync(string userKey, string displayName,
        string? orgIdOverride = null)
    {
        var token = GetUserToken(userKey);

        // Decode org_id from token to use the correct org for self-registration
        var orgId = orgIdOverride;
        if (orgId == null)
        {
            try
            {
                var payload = token.Split('.')[1];
                // Pad base64
                var padded = payload + new string('=', (4 - payload.Length % 4) % 4);
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                using var doc = JsonDocument.Parse(json);
                orgId = doc.RootElement.GetProperty("org_id").GetString();
            }
            catch { /* fallback below */ }
        }
        orgId ??= _councilOrgId;

        // Try the /me/organizations/{orgId}/self-register endpoint
        var response = await ApiPostAsync(
            $"/api/me/organizations/{orgId}/self-register?displayName={Uri.EscapeDataString(displayName)}",
            null, token);

        var (status, body) = await ReadResponseAsync(response);

        if (status >= 400)
        {
            // Try alternative endpoint pattern
            response = await ApiPostAsync(
                $"/api/me/register-participant?displayName={Uri.EscapeDataString(displayName)}",
                null, token);
            (status, body) = await ReadResponseAsync(response);
        }

        if (status >= 400 && status != 409)
        {
            throw new InvalidOperationException(
                $"Participant self-registration failed ({status}): {body}");
        }

        if (status == 409)
        {
            TestContext.Out.WriteLine($"Participant {displayName} already registered (409).");
        }

        // Extract participant ID
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("participantId", out var pid))
                return pid.GetString()!;
            if (doc.RootElement.TryGetProperty("id", out var id))
                return id.GetString()!;
        }

        // If 409, try to get existing participant ID
        var profileResponse = await ApiGetAsync("/api/me/participant-profiles", token);
        var profileJson = await ReadJsonAsync(profileResponse);

        if (profileJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var profile in profileJson.EnumerateArray())
            {
                if (profile.TryGetProperty("participantId", out var pid))
                    return pid.GetString()!;
                if (profile.TryGetProperty("id", out var id))
                    return id.GetString()!;
            }
        }

        return $"unknown-participant-{userKey}";
    }

    private async Task<string> StartBlueprintInstanceAsync(string token, string blueprintId)
    {
        var response = await ApiPostAsync(
            "/api/instances",
            new { blueprintId, registerId = _registerId }, token);

        var json = await ReadJsonAsync(response);

        if (json.TryGetProperty("instanceId", out var iid))
            return iid.GetString()!;
        if (json.TryGetProperty("id", out var id))
            return id.GetString()!;

        throw new InvalidOperationException(
            $"No instanceId in response: {json.GetRawText()}");
    }

    private async Task ExecuteActionAsync(string token, string instanceId, string blueprintId,
        int actionId, string senderWallet, string registerAddress,
        Dictionary<string, object> payloadData)
    {
        var body = new Dictionary<string, object>
        {
            ["blueprintId"] = blueprintId,
            ["actionId"] = actionId,
            ["instanceId"] = instanceId,
            ["senderWallet"] = senderWallet,
            ["registerAddress"] = registerAddress,
            ["payloadData"] = payloadData
        };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/instances/{instanceId}/actions/{actionId}/execute");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Delegation-Token", token);
        request.Content = System.Net.Http.Json.JsonContent.Create(body, options: JsonOptions);

        var response = await ApiClient.SendAsync(request);
        var (status, responseBody) = await ReadResponseAsync(response);

        if (status >= 400)
        {
            throw new InvalidOperationException(
                $"Action execution failed ({status}): {responseBody}");
        }

        TestContext.Out.WriteLine($"Action {actionId} executed: {status}");

        // Wait for validator processing
        await Task.Delay(3000);
    }

    private async Task<string> GetInstanceStatusAsync(string token, string instanceId)
    {
        var response = await ApiGetAsync($"/api/instances/{instanceId}", token);
        var (status, body) = await ReadResponseAsync(response);

        if (status >= 400)
        {
            return $"Error ({status})";
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("status", out var s))
            return s.GetString() ?? "unknown";
        if (doc.RootElement.TryGetProperty("state", out var st))
            return st.GetString() ?? "unknown";

        return "unknown";
    }

    #endregion

    #region Issue Logging

    private void LogIssue(string id, string title, string severity, string component,
        string step, string expected, string actual, string? details = null)
    {
        var issue = $"""

            ## ISSUE-{id}: {title}
            - **Severity:** {severity}
            - **Component:** {component}
            - **Step:** {step}
            - **Expected:** {expected}
            - **Actual:** {actual}
            {(details != null ? $"- **Details:** {details}" : "")}
            """;

        _issues.Add(issue);
        TestContext.Out.WriteLine($"[ISSUE-{id}] ({severity}) {title}: {actual}");
    }

    #endregion
}
