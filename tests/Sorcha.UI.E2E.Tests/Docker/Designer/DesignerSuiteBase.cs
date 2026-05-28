// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// Shared base for the Feature 142 Blueprint Design Lifecycle Playwright suites
/// (rail gating, rehearsal walk, governed Go live, guided on-ramp, form authoring,
/// amend loop). Provides the <see cref="DesignerLifecyclePage"/> and an
/// authenticated navigation helper to the designer workspace.
/// </summary>
/// <remarks>
/// Abstract — NUnit does not run it directly. Concrete fixtures in this folder
/// MUST declare the suite categories so CI can filter them:
/// <code>
/// [Parallelizable(ParallelScope.Self)]
/// [TestFixture]
/// [Category("Docker")]
/// [Category("Designer")]
/// [Category("Lifecycle")]
/// [Category("Authenticated")]
/// public class LifecycleRailTests : DesignerSuiteBase { ... }
/// </code>
/// Per the <c>sorcha-ui</c> discipline: page object → Playwright test → component,
/// against Docker, with the base auto-checking console errors, network 5xx, and
/// MudBlazor CSS health on every authenticated navigation.
/// </remarks>
public abstract class DesignerSuiteBase : AuthenticatedDockerTestBase
{
    /// <summary>Shared page object for the rail-driven designer workspace.</summary>
    protected DesignerLifecyclePage Designer { get; private set; } = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        Designer = new DesignerLifecyclePage(Page);
    }

    /// <summary>
    /// Navigate to the designer workspace (optionally for a specific blueprint),
    /// re-authenticating inline if the token has not yet loaded, then wait for
    /// Blazor WASM hydration. By default the first-run guided overlay is dismissed
    /// after load so its modal scrim does not intercept pointer events on the rail;
    /// pass <paramref name="dismissFirstRunOverlay"/>=false to assert the overlay itself.
    /// </summary>
    protected async Task OpenDesignerAsync(string? blueprintId = null, bool dismissFirstRunOverlay = true)
    {
        var path = TestConstants.AuthenticatedRoutes.DesignerBlueprint
                   + (string.IsNullOrEmpty(blueprintId) ? string.Empty : "/" + blueprintId);
        await NavigateAuthenticatedAsync(path);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        if (dismissFirstRunOverlay && await Designer.FirstRunDismiss.CountAsync() > 0
            && await Designer.FirstRunDismiss.IsVisibleAsync())
        {
            await Designer.FirstRunDismiss.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
    }

    /// <summary>
    /// Navigates directly to the designer workspace for a specific blueprint with an explicit
    /// <c>?stage=</c> deep-link (e.g. <c>rehearse</c> / <c>golive</c>), re-authenticating inline if
    /// the token has not yet loaded, then waits for Blazor WASM hydration and dismisses the first-run
    /// overlay so its scrim does not intercept pointer events on the stage canvas.
    /// </summary>
    protected async Task OpenDesignerStageAsync(string blueprintId, string stage)
    {
        var path = $"{TestConstants.AuthenticatedRoutes.DesignerBlueprint}/{blueprintId}?stage={stage}";
        await NavigateAuthenticatedAsync(path);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        if (await Designer.FirstRunDismiss.CountAsync() > 0
            && await Designer.FirstRunDismiss.IsVisibleAsync())
        {
            await Designer.FirstRunDismiss.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }
    }

    /// <summary>
    /// Creates a minimal, valid two-participant / two-action blueprint via the REST API
    /// (login → <c>POST /api/blueprints</c>) and returns its id so a test can drive the designer
    /// stages against a loaded blueprint. Keeps blueprint provisioning off the slow UI authoring path
    /// — the lifecycle stages are what these suites assert. Marks the test inconclusive (rather than
    /// failing) if the live stack rejects login or blueprint creation, since that is an environment
    /// problem, not a designer-UI defect.
    /// </summary>
    /// <param name="title">Optional title; defaults to a unique "E2E Rehearsal" name.</param>
    protected async Task<string> CreateSmokeBlueprintAsync(string? title = null)
    {
        title ??= $"E2E Rehearsal {Guid.NewGuid():N}";

        using var http = new HttpClient { BaseAddress = new Uri(TestConstants.UiWebUrl) };

        // 1) Authenticate over HTTP to get a platform bearer token (admin carries the Designer role).
        string token;
        try
        {
            using var loginResponse = await http.PostAsJsonAsync(
                "/api/auth/login",
                new { email = TestConstants.TestEmail, password = TestConstants.TestPassword });

            var loginBody = await loginResponse.Content.ReadAsStringAsync();
            if (!loginResponse.IsSuccessStatusCode)
            {
                Assert.Inconclusive(
                    $"Could not obtain an API token to create a smoke blueprint ({(int)loginResponse.StatusCode}): {loginBody}");
            }

            using var loginDoc = JsonDocument.Parse(loginBody);
            token = loginDoc.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("No access_token in login response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Inconclusive($"Login request to the live stack failed: {ex.Message}");
            throw; // unreachable — Assert.Inconclusive throws.
        }

        // 2) POST a minimal valid blueprint and return its id.
        var blueprint = new
        {
            title,
            description = "Two-participant smoke blueprint for E2E.",
            version = 1,
            participants = new object[]
            {
                new { id = "applicant", name = "Applicant" },
                new { id = "reviewer", name = "Reviewer" },
            },
            actions = new object[]
            {
                new
                {
                    id = 0,
                    title = "Submit",
                    sender = "applicant",
                    isStartingAction = true,
                    dataSchemas = new object[]
                    {
                        new
                        {
                            type = "object",
                            properties = new { message = new { type = "string", minLength = 1 } },
                            required = new[] { "message" },
                        },
                    },
                    disclosures = new object[]
                    {
                        new { participantAddress = "reviewer", dataPointers = new[] { "/*" } },
                    },
                    routes = new object[]
                    {
                        new { id = "r", nextActionIds = new[] { 1 }, isDefault = true },
                    },
                },
                new
                {
                    id = 1,
                    title = "Review",
                    sender = "reviewer",
                    dataSchemas = new object[]
                    {
                        new
                        {
                            type = "object",
                            properties = new
                            {
                                decision = new { type = "string", @enum = new[] { "approved", "rejected" } },
                            },
                            required = new[] { "decision" },
                        },
                    },
                    disclosures = new object[]
                    {
                        new { participantAddress = "applicant", dataPointers = new[] { "/*" } },
                    },
                    routes = new object[]
                    {
                        new { id = "d", nextActionIds = Array.Empty<int>(), isDefault = true },
                    },
                },
            },
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/blueprints")
        {
            Content = JsonContent.Create(blueprint),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var createResponse = await http.SendAsync(createRequest);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        if (!createResponse.IsSuccessStatusCode)
        {
            Assert.Inconclusive(
                $"Could not create a smoke blueprint ({(int)createResponse.StatusCode}): {createBody}");
        }

        using var createDoc = JsonDocument.Parse(createBody);
        var id = createDoc.RootElement.GetProperty("id").GetString();
        Assert.That(id, Is.Not.Null.And.Not.Empty, "Created blueprint should return an id.");
        return id!;
    }
}
