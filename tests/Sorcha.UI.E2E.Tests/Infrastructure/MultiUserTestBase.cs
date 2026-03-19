// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.PageObjects;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for multi-user E2E tests that require switching between
/// different authenticated users during a single test flow.
/// Manages separate browser contexts per user with independent auth state.
/// </summary>
public abstract class MultiUserTestBase : DockerTestBase
{
    private readonly Dictionary<string, IBrowserContext> _userContexts = new();
    private readonly Dictionary<string, IPage> _userPages = new();
    private readonly Dictionary<string, string> _userTokens = new();

    /// <summary>
    /// Shared HttpClient for API-driven setup (org creation, user provisioning, etc.).
    /// Uses the API Gateway base URL. Initialized eagerly so it's available in OneTimeSetUp.
    /// </summary>
    protected HttpClient ApiClient { get; private set; } = new()
    {
        BaseAddress = new Uri(TestConstants.ApiGatewayUrl),
        Timeout = TimeSpan.FromSeconds(120)
    };

    /// <summary>
    /// JSON serializer options matching Sorcha API conventions.
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Extended timeout for long-running test operations (ms).
    /// </summary>
    protected const int LongOperationTimeout = 30_000;

    /// <summary>
    /// Timeout for action processing including validator pipeline (ms).
    /// </summary>
    protected const int ActionProcessingTimeout = 60_000;

    /// <summary>
    /// Timeout for waiting for the full page + Blazor WASM to load (ms).
    /// </summary>
    protected const int FullPageLoadTimeout = 20_000;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
    }

    [TearDown]
    public override async Task BaseTearDown()
    {
        // NOTE: Do NOT clear _userContexts, _userPages, or _userTokens here.
        // For ordered sequential tests (NonParallelizable), state must persist
        // across tests. Cleanup happens in OneTimeTearDown or when the fixture ends.
        await base.BaseTearDown();
    }

    /// <summary>
    /// Call from [OneTimeTearDown] to dispose all browser contexts and clear state.
    /// </summary>
    protected async Task DisposeAllUserContextsAsync()
    {
        foreach (var (_, context) in _userContexts)
        {
            try { await context.CloseAsync(); } catch { /* ignore */ }
        }
        _userContexts.Clear();
        _userPages.Clear();
        _userTokens.Clear();
        ApiClient?.Dispose();
    }

    #region API Authentication

    /// <summary>
    /// Obtains a JWT token for the given credentials via the service-auth endpoint.
    /// </summary>
    protected async Task<string> GetTokenAsync(string email, string password, int maxRetries = 3)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = email,
                ["password"] = password,
                ["client_id"] = "sorcha-cli"
            });

            var response = await ApiClient.PostAsync("/api/service-auth/token", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("No access_token in response");
            }

            // Check if account is locked (401 with empty body or lock message)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt < maxRetries)
            {
                TestContext.Out.WriteLine(
                    $"Token request for {email} returned 401 (attempt {attempt}/{maxRetries}). " +
                    $"Possible account lockout. Waiting 15s before retry...");
                await Task.Delay(15_000);
                continue;
            }

            throw new InvalidOperationException(
                $"Token request failed for {email} ({response.StatusCode}): {body}");
        }

        throw new InvalidOperationException($"Token request failed for {email} after {maxRetries} attempts");
    }

    /// <summary>
    /// Sets the API client authorization header to the given token.
    /// </summary>
    protected void SetApiToken(string token)
    {
        ApiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Gets the stored token for a named user.
    /// </summary>
    protected string GetUserToken(string userName)
    {
        return _userTokens.TryGetValue(userName, out var token)
            ? token
            : throw new InvalidOperationException($"No token stored for user '{userName}'");
    }

    /// <summary>
    /// Stores a token for a named user.
    /// </summary>
    protected void StoreUserToken(string userName, string token)
    {
        _userTokens[userName] = token;
    }

    #endregion

    #region Browser Context Management

    /// <summary>
    /// Logs in as the specified user via the UI login page and creates a dedicated
    /// browser context. The context is cached for reuse within the test.
    /// </summary>
    protected async Task<IPage> LoginAsUserAsync(string userName, string email, string password)
    {
        if (_userPages.TryGetValue(userName, out var existingPage))
        {
            return existingPage;
        }

        // Create a new browser context for this user
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // Navigate to login page
        await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Login}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for form
        var emailInput = page.Locator("input[type='email']").First;
        await emailInput.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

        // Fill and submit
        await emailInput.FillAsync(email);
        await page.Locator("input[type='password']").First.FillAsync(password);
        await page.Locator("button:has-text('Sign In')").First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Wait for redirect away from login
        try
        {
            await page.WaitForURLAsync(
                url => !url.Contains("/auth/login"),
                new() { Timeout = TestConstants.PageLoadTimeout * 2 });
        }
        catch (TimeoutException)
        {
            // Token bounce — check if token was issued
            if (!page.Url.Contains("token"))
            {
                throw new InvalidOperationException(
                    $"Login failed for {userName} ({email}). URL: {page.Url}");
            }
        }

        // Wait for Blazor to process the token
        await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        _userContexts[userName] = context;
        _userPages[userName] = page;

        return page;
    }

    /// <summary>
    /// Gets the cached page for a previously logged-in user.
    /// </summary>
    protected IPage GetUserPage(string userName)
    {
        return _userPages.TryGetValue(userName, out var page)
            ? page
            : throw new InvalidOperationException(
                $"No browser context for user '{userName}'. Call LoginAsUserAsync first.");
    }

    /// <summary>
    /// Navigates a user's page to a path and waits for Blazor hydration.
    /// </summary>
    protected async Task NavigateUserAsync(string userName, string path)
    {
        var page = GetUserPage(userName);
        var url = $"{TestConstants.UiWebUrl}{path}";
        await page.GotoAsync(url);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await MudBlazorHelpers.WaitForBlazorAsync(page, FullPageLoadTimeout);
        await MudBlazorHelpers.WaitForTranslationsAsync(page, TestConstants.PageLoadTimeout);
    }

    /// <summary>
    /// Captures a screenshot from a specific user's page.
    /// </summary>
    protected async Task CaptureUserScreenshotAsync(string userName, string suffix)
    {
        if (!_userPages.TryGetValue(userName, out var page)) return;

        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            var testName = TestContext.CurrentContext.Test.Name;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{testName}_{userName}_{suffix}_{timestamp}.png";

            foreach (var c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');

            var filePath = Path.Combine(ScreenshotDirectory, fileName);
            await page.ScreenshotAsync(new() { Path = filePath, FullPage = true });

            TestContext.AddTestAttachment(filePath, $"Screenshot: {userName} - {suffix}");
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Failed to capture screenshot for {userName}: {ex.Message}");
        }
    }

    #endregion

    #region API Helper Methods

    /// <summary>
    /// Makes an authenticated API POST request with JSON body.
    /// </summary>
    protected async Task<HttpResponseMessage> ApiPostAsync(string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        return await ApiClient.SendAsync(request);
    }

    /// <summary>
    /// Makes an authenticated API GET request.
    /// </summary>
    protected async Task<HttpResponseMessage> ApiGetAsync(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await ApiClient.SendAsync(request);
    }

    /// <summary>
    /// Makes an authenticated API PUT request with JSON body.
    /// </summary>
    protected async Task<HttpResponseMessage> ApiPutAsync(string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        return await ApiClient.SendAsync(request);
    }

    /// <summary>
    /// Makes an authenticated API DELETE request.
    /// </summary>
    protected async Task<HttpResponseMessage> ApiDeleteAsync(string path, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await ApiClient.SendAsync(request);
    }

    /// <summary>
    /// Reads a JSON response and returns the parsed JsonElement.
    /// </summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"API call failed ({response.StatusCode}): {body}");
        }
        return JsonDocument.Parse(body).RootElement;
    }

    /// <summary>
    /// Reads a JSON response, allowing specific non-success status codes.
    /// Returns (statusCode, body) tuple.
    /// </summary>
    protected static async Task<(int StatusCode, string Body)> ReadResponseAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    #endregion
}
