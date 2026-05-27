// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Shared constants for Docker-based E2E tests.
/// </summary>
public static class TestConstants
{
    // Docker environment URLs (overridable via environment variables)
    // UI must be accessed through the API Gateway so that relative /api/* calls
    // route to backend services. Direct access at :5400 has no API proxy.
    public const string DefaultUiWebUrl = "http://localhost:80";
    public const string DefaultUiDirectUrl = "http://localhost:5400";
    public const string DefaultApiGatewayUrl = "http://localhost:80";

    public static string UiWebUrl =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_BASE_URL") ?? DefaultUiWebUrl;

    public static string UiDirectUrl =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_DIRECT_URL") ?? DefaultUiDirectUrl;

    public static string ApiGatewayUrl =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_API_URL")
        ?? Environment.GetEnvironmentVariable("SORCHA_E2E_BASE_URL") ?? DefaultApiGatewayUrl;

    // All UI pages are served under /app
    public const string AppBase = "/app";

    // Citizen Wallet PWA (Feature 114) — mounted at /wallet/ via gateway path-rewrite.
    // The PWA's own routes are root-relative inside that mount (`@page "/"` → /wallet/).
    public const string CitizenWalletBase = "/wallet/";
    public const string CitizenWalletAudience = "sorcha:citizen-wallet";

    // Test credentials (overridable via environment variables)
    public const string DefaultTestEmail = "admin@sorcha.local";
    public const string DefaultTestPassword = "Dev_Pass_2025!";
    public const string DefaultTestProfileName = "docker";

    public static string TestEmail =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_EMAIL") ?? DefaultTestEmail;

    public static string TestPassword =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_PASSWORD") ?? DefaultTestPassword;

    public static string TestProfileName =>
        Environment.GetEnvironmentVariable("SORCHA_E2E_PROFILE") ?? DefaultTestProfileName;

    // Timeouts (ms)
    public const int BlazorHydrationTimeout = 8000;
    public const int PageLoadTimeout = 15000;
    public const int ElementTimeout = 10000;
    public const int NetworkIdleWait = 3000;
    public const int ShortWait = 1000;

    // Long-running test timeouts (ms)
    public const int LongRunningTestTimeout = 1_200_000; // 20 minutes
    public const int ActionProcessingTimeout = 60_000;    // 1 minute for validator pipeline
    public const int MultiUserLoginTimeout = 30_000;      // 30 seconds per user login

    // Council E2E test credentials (overridable via environment variables)
    public static class CouncilTestUsers
    {
        public const string DefaultStaffPassword = "Council_Staff_2026!";
        public const string DefaultCitizenPassword = "Citizen_Pass_2026!";

        public const string CouncilAdminEmail = "council.admin@ashwick.test";
        public const string IdDeptEmail = "id.dept@ashwick.test";
        public const string ServiceDeptEmail = "service.dept@ashwick.test";
        public const string ReturnDeptEmail = "return.dept@ashwick.test";
        public const string CitizenEmail = "jane.citizen@email.test";
        public const string CitizenDisplayName = "Jane Citizen";
        public const string CouncilOrgName = "Ashwick Council";
        public const string CouncilSubdomain = "ashwick";

        public static string StaffPassword =>
            Environment.GetEnvironmentVariable("SORCHA_E2E_COUNCIL_STAFF_PASSWORD") ?? DefaultStaffPassword;

        public static string CitizenPassword =>
            Environment.GetEnvironmentVariable("SORCHA_E2E_COUNCIL_CITIZEN_PASSWORD") ?? DefaultCitizenPassword;
    }

    /// <summary>
    /// Console error patterns that are expected from Blazor WASM and should not fail tests.
    /// </summary>
    public static readonly string[] KnownConsoleErrorPatterns =
    [
        "WASM",
        "Blazor",
        "dotnet",
        "favicon",
        "404",
        "Content Security Policy",
        "CSP",
        "fonts.googleapis",
        "schemastore",
        // Blazor WASM boot resources
        "_framework",
        "blazor.webassembly.js",
        // Browser extension noise
        "chrome-extension",
        "moz-extension",
        // HTTP resource load errors (pre-existing auth token propagation issues)
        "401",
        "Unauthorized",
        "Failed to load resource",
        // Static file MIME type issues (development environment)
        "MIME type",
        "clipboard.js",
        "strict MIME type checking",
    ];

    /// <summary>
    /// Network response status codes that should be treated as failures.
    /// </summary>
    public static readonly int[] FailureStatusCodes = [500, 502, 503, 504];

    /// <summary>
    /// Routes that are publicly accessible (no auth required).
    /// </summary>
    public static class PublicRoutes
    {
        public const string Landing = "/";
        public const string Login = "/auth/login";
        public const string Logout = "/auth/logout";
        public const string Signup = "/auth/signup";
    }

    /// <summary>
    /// Routes that require authentication.
    /// </summary>
    public static class AuthenticatedRoutes
    {
        public const string Dashboard = $"{AppBase}/dashboard";
        public const string Designer = $"{AppBase}/designer";
        public const string DesignerBlueprint = $"{AppBase}/designer/blueprint";
        public const string Blueprints = $"{AppBase}/blueprints";
        public const string Templates = $"{AppBase}/templates";
        public const string Schemas = $"{AppBase}/schemas";
        public const string Wallets = $"{AppBase}/wallets";
        public const string WalletCreate = $"{AppBase}/wallets/create";
        public const string WalletCreateFirstLogin = $"{AppBase}/wallets/create?first-login=true";
        public const string WalletRecover = $"{AppBase}/wallets/recover";
        public const string MyWallet = $"{AppBase}/wallets";
        public const string MyActions = $"{AppBase}/my-actions";
        public const string MyWorkflows = $"{AppBase}/my-workflows";
        public const string MyTransactions = $"{AppBase}/my-transactions";
        public const string Registers = $"{AppBase}/registers";
        public const string Participants = $"{AppBase}/participants";
        public const string Admin = $"{AppBase}/admin";
        public const string AdminHealth = $"{AppBase}/admin/health";
        public const string AdminPeers = $"{AppBase}/admin/peers";
        public const string AdminOrganizations = $"{AppBase}/admin/organizations";
        public const string AdminOrganizationDetail = $"{AppBase}/admin/organizations/{{0}}";
        public const string AdminUserDetail = $"{AppBase}/admin/organizations/{{0}}/users/{{1}}";
        public const string AdminValidator = $"{AppBase}/admin/validator";
        public const string AdminPrincipals = $"{AppBase}/admin/principals";
        public const string Settings = $"{AppBase}/settings";
        public const string AdminEvents = $"{AppBase}/admin/events";
        public const string AdminPresentations = $"{AppBase}/admin/presentations";
        public const string NotificationSettings = $"{AppBase}/settings/notifications";
        public const string Credentials = $"{AppBase}/credentials";
        public const string Help = $"{AppBase}/help";
        public const string MyDevices = $"{AppBase}/my-devices";
    }
}
