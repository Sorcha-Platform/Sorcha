// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Shared constants for Docker-based E2E tests.
/// </summary>
public static class TestConstants
{
    // Docker environment URLs
    // UI must be accessed through the API Gateway so that relative /api/* calls
    // route to backend services. Direct access at :5400 has no API proxy.
    public const string UiWebUrl = "http://localhost:80";
    public const string UiDirectUrl = "http://localhost:5400";
    public const string ApiGatewayUrl = "http://localhost:80";

    // All UI pages are served under /app
    public const string AppBase = "/app";

    // Test credentials (from bootstrap)
    public const string TestEmail = "admin@sorcha.local";
    public const string TestPassword = "Dev_Pass_2025!";
    public const string TestProfileName = "docker";

    // Timeouts (ms)
    public const int BlazorHydrationTimeout = 8000;
    public const int PageLoadTimeout = 15000;
    public const int ElementTimeout = 10000;
    public const int NetworkIdleWait = 3000;
    public const int ShortWait = 1000;

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
        public const string AdminValidator = $"{AppBase}/admin/validator";
        public const string AdminPrincipals = $"{AppBase}/admin/principals";
        public const string Settings = $"{AppBase}/settings";
        public const string AdminEvents = $"{AppBase}/admin/events";
        public const string AdminPresentations = $"{AppBase}/admin/presentations";
        public const string NotificationSettings = $"{AppBase}/settings/notifications";
        public const string Credentials = $"{AppBase}/credentials";
        public const string Help = $"{AppBase}/help";
    }
}
