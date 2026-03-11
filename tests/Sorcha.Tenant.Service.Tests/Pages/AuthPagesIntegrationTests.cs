// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Pages;

/// <summary>
/// Integration tests for server-rendered auth pages using WebApplicationFactory.
/// Verifies that Razor Pages return correct HTTP responses with expected HTML content.
/// </summary>
public class AuthPagesIntegrationTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public AuthPagesIntegrationTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    // ───── GET /auth/login ─────

    [Fact]
    public async Task GetLogin_ReturnsOk_WithLoginForm()
    {
        // Act
        var response = await _client.GetAsync("/auth/login");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sign In");
        html.Should().Contain("type=\"email\"");
        html.Should().Contain("type=\"password\"");
        html.Should().Contain("method=\"post\"");
    }

    [Fact]
    public async Task GetLogin_WithReturnUrl_PreservesReturnUrl()
    {
        // Act
        var response = await _client.GetAsync("/auth/login?returnUrl=/dashboard");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("/dashboard");
    }

    // ───── GET /auth/signup ─────

    [Fact]
    public async Task GetSignup_ReturnsOk_WithSignupForm()
    {
        // Act
        var response = await _client.GetAsync("/auth/signup");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Create Account");
        html.Should().Contain("Sign Up", "page title should contain Sign Up");
        html.Should().Contain("method=\"post\"");
        html.Should().Contain("type=\"email\"");
        html.Should().Contain("type=\"password\"");
    }

    // ───── GET /auth/logout ─────

    [Fact]
    public async Task GetLogout_ReturnsOk_WithConfirmationForm()
    {
        // Act
        var response = await _client.GetAsync("/auth/logout");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sign Out");
        html.Should().Contain("Are you sure you want to sign out?");
        html.Should().Contain("method=\"post\"");
    }

    // ───── GET /auth/error ─────

    [Fact]
    public async Task GetError_WithoutMessage_ReturnsOk_WithDefaultMessage()
    {
        // Act
        var response = await _client.GetAsync("/auth/error");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Authentication Error");
        html.Should().Contain("Something went wrong");
    }

    [Fact]
    public async Task GetError_WithMessage_ReturnsOk_WithCustomMessage()
    {
        // Act
        var response = await _client.GetAsync("/auth/error?message=Session+expired");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Session expired");
    }

    // ───── GET /auth/reset-password ─────

    [Fact]
    public async Task GetResetPassword_WithoutToken_ReturnsOk_WithEmailForm()
    {
        // Act
        var response = await _client.GetAsync("/auth/reset-password");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Reset Password");
        html.Should().Contain("Enter your email to receive a reset link");
        html.Should().Contain("type=\"email\"");
        html.Should().Contain("Send Reset Link");
    }

    [Fact]
    public async Task GetResetPassword_WithInvalidToken_ReturnsOk_WithErrorMessage()
    {
        // Act — the real PasswordResetService will fail to validate an invalid token
        var response = await _client.GetAsync("/auth/reset-password?token=invalid-token-xyz");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Reset Password");
        // Should show the reset mode (not the request mode)
        html.Should().Contain("Choose a new password");
    }

    // ───── GET /auth/verify-email ─────

    [Fact]
    public async Task GetVerifyEmail_WithValidToken_ReturnsOk_WithSuccessMessage()
    {
        // Arrange — the factory's mock IEmailVerificationService accepts "valid-verification-token"
        // Act
        var response = await _client.GetAsync("/auth/verify-email?token=valid-verification-token");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Email verified!");
        html.Should().Contain("successfully verified");
    }

    [Fact]
    public async Task GetVerifyEmail_WithInvalidToken_ReturnsOk_WithErrorMessage()
    {
        // Arrange — the factory's mock returns (false, "Invalid verification token.")
        // for "invalid-or-expired-token"
        var response = await _client.GetAsync("/auth/verify-email?token=invalid-or-expired-token");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Verification failed");
        html.Should().Contain("Invalid verification token.");
    }

    [Fact]
    public async Task GetVerifyEmail_WithoutToken_ReturnsOk_WithErrorMessage()
    {
        // Act
        var response = await _client.GetAsync("/auth/verify-email");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Verification failed");
        html.Should().Contain("No verification token was provided");
    }

    // ───── GET /auth/social-callback ─────

    [Fact]
    public async Task GetSocialCallback_WithoutParams_ReturnsOk_WithErrorMessage()
    {
        // Act — no provider, code, or state parameters
        var response = await _client.GetAsync("/auth/social/callback");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("missing provider");
    }

    [Fact]
    public async Task GetSocialCallback_WithProviderError_ReturnsOk_WithCancelledMessage()
    {
        // Act — provider returned an error (user denied access)
        var response = await _client.GetAsync("/auth/social/callback?error=access_denied");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("cancelled or denied");
    }

    // ───── POST /auth/login ─────

    [Fact]
    public async Task PostLogin_WithValidCredentials_ReturnsRedirectToApp()
    {
        // Arrange — get login page to extract antiforgery token
        var (antiforgeryToken, cookies) = await GetAntiforgeryTokenAsync("/auth/login");

        var formData = new Dictionary<string, string>
        {
            ["Email"] = "admin@test-org.sorcha.io",
            ["Password"] = "TestPassword123!",
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new FormUrlEncodedContent(formData)
        };
        request.Headers.Add("Cookie", cookies);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — successful login redirects to /app/#token=...
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location?.ToString();
        location.Should().NotBeNull();
        location.Should().StartWith("/app/#token=");
    }

    [Fact]
    public async Task PostLogin_WithInvalidCredentials_ReturnsOk_WithErrorMessage()
    {
        // Arrange
        var (antiforgeryToken, cookies) = await GetAntiforgeryTokenAsync("/auth/login");

        var formData = new Dictionary<string, string>
        {
            ["Email"] = "admin@test-org.sorcha.io",
            ["Password"] = "WrongPassword!",
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new FormUrlEncodedContent(formData)
        };
        request.Headers.Add("Cookie", cookies);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — failed login returns the page with error
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sign In");
        // Should contain the error alert div
        html.Should().Contain("alert-danger");
    }

    [Fact]
    public async Task PostLogin_WithMissingEmail_ReturnsOk_WithValidationError()
    {
        // Arrange
        var (antiforgeryToken, cookies) = await GetAntiforgeryTokenAsync("/auth/login");

        var formData = new Dictionary<string, string>
        {
            ["Email"] = "",
            ["Password"] = "TestPassword123!",
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new FormUrlEncodedContent(formData)
        };
        request.Headers.Add("Cookie", cookies);

        // Act
        var response = await _client.SendAsync(request);

        // Assert — validation failure returns the page
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Sign In");
    }

    // ───── POST /auth/logout ─────

    [Fact]
    public async Task PostLogout_ReturnsOk_WithSignedOutMessage()
    {
        // Arrange
        var (antiforgeryToken, cookies) = await GetAntiforgeryTokenAsync("/auth/logout");

        var formData = new Dictionary<string, string>
        {
            ["RefreshToken"] = "some-refresh-token",
            ["__RequestVerificationToken"] = antiforgeryToken
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout")
        {
            Content = new FormUrlEncodedContent(formData)
        };
        request.Headers.Add("Cookie", cookies);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("signed out", "page should confirm sign out");
    }

    // ───── Helpers ─────

    /// <summary>
    /// Fetches a page via GET and extracts the antiforgery token and cookies.
    /// </summary>
    private async Task<(string Token, string Cookies)> GetAntiforgeryTokenAsync(string url)
    {
        var getResponse = await _client.GetAsync(url);
        getResponse.EnsureSuccessStatusCode();

        var html = await getResponse.Content.ReadAsStringAsync();

        // Extract __RequestVerificationToken from the form
        var match = Regex.Match(html,
            @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""" +
            @"|" +
            @"type=""hidden""\s+name=""__RequestVerificationToken""\s+value=""([^""]+)""" +
            @"|" +
            @"value=""([^""]+)""\s+[^>]*name=""__RequestVerificationToken""");

        var token = match.Groups.Cast<Group>()
            .Skip(1)
            .FirstOrDefault(g => g.Success)?.Value;

        token.Should().NotBeNullOrEmpty(
            $"antiforgery token should be present in the HTML at {url}");

        // Collect cookies from Set-Cookie headers
        var cookies = string.Join("; ",
            getResponse.Headers
                .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .SelectMany(h => h.Value)
                .Select(c => c.Split(';')[0]));

        return (token!, cookies);
    }
}
