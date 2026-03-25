// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sorcha.Tenant.Service.IntegrationTests.Fixtures;

/// <summary>
/// Test authentication handler that supports both mock authentication and real JWT tokens.
/// - For "test-token" or "test-service-token": uses seeded test user data
/// - For real JWT tokens: extracts claims from the token
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    // Must match the secret key used in TenantServiceWebApplicationFactory configuration
    private const string TestSecretKey = "test-signing-key-for-integration-tests-minimum-32-characters-required";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if we should authenticate (based on Authorization header presence)
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            // Return no result for endpoints that allow anonymous access
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authHeaderValue = authHeader.ToString();
        if (!authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authHeaderValue["Bearer ".Length..].Trim();

        // Check for test tokens - use mock authentication
        if (token is "test-token" or "test-service-token")
        {
            return Task.FromResult(AuthenticateResult.Success(CreateMockTicket()));
        }

        // Try to parse as a real JWT token
        var jwtResult = TryParseJwtToken(token);
        if (jwtResult != null)
        {
            return Task.FromResult(AuthenticateResult.Success(jwtResult));
        }

        // Fall back to mock authentication for any other token
        return Task.FromResult(AuthenticateResult.Success(CreateMockTicket()));
    }

    private AuthenticationTicket CreateMockTicket()
    {
        // Check for test header to determine role
        var isAdmin = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader) &&
                      roleHeader.ToString().Contains("Administrator");
        var isService = Request.Headers.TryGetValue("X-Test-Role", out var serviceRoleHeader) &&
                        serviceRoleHeader.ToString().Contains("Service");

        // Use provided user ID or default
        var userId = Request.Headers.TryGetValue("X-Test-User-Id", out var userIdHeader)
            ? userIdHeader.ToString()
            : isAdmin
                ? TestDataSeeder.TestAdminUserId.ToString()
                : TestDataSeeder.TestMemberUserId.ToString();

        var email = isAdmin ? TestDataSeeder.TestAdminEmail : TestDataSeeder.TestMemberEmail;
        var displayName = isAdmin ? TestDataSeeder.TestAdminName : TestDataSeeder.TestMemberName;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, email),
            new("sub", userId),
            new("org_id", TestDataSeeder.TestOrganizationId.ToString()),
            new("org_name", TestDataSeeder.TestOrganizationName),
            new("token_type", isService ? "service" : "user")
        };

        // Add appropriate role
        if (isService)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Service"));
        }
        else if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "Consumer"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, SchemeName);
    }

    private AuthenticationTicket? TryParseJwtToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(TestSecretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false, // Don't validate expiry in tests
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return new AuthenticationTicket(principal, SchemeName);
        }
        catch
        {
            // Token is not a valid JWT - return null to fall back to mock
            return null;
        }
    }
}
