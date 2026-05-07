// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.Tenant.Service.Tests.Infrastructure;

/// <summary>
/// Test authentication handler for integration tests.
/// Reads user identity from test headers.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if test headers are present
        if (!Request.Headers.ContainsKey("X-Test-User-Id"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers["X-Test-User-Id"].ToString();
        var role = Request.Headers["X-Test-Role"].ToString();
        var organizationId = Request.Headers["X-Test-Organization-Id"].ToString();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, $"test-user-{userId}"),
            new Claim(ClaimTypes.Email, $"test{userId}@test.com"),
            new Claim("sub", userId),
            new Claim("email", $"test{userId}@test.com")
        };

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        if (!string.IsNullOrEmpty(organizationId))
        {
            claims.Add(new Claim("organization_id", organizationId));
            claims.Add(new Claim("org_id", organizationId));
        }

        // Feature 118 — endpoints scoped to the authenticated platform user
        // (e.g., /api/me/inbox/*) read the `platform_user_id` claim. Tests
        // pass it via X-Test-Platform-User-Id when needed. Existing endpoints
        // that fall back to `sub` (NameIdentifier) keep working when the
        // header is absent.
        var platformUserId = Request.Headers["X-Test-Platform-User-Id"].ToString();
        if (!string.IsNullOrEmpty(platformUserId))
        {
            claims.Add(new Claim("platform_user_id", platformUserId));
        }

        // Service-principal tests pass X-Test-Token-Type=service to satisfy
        // the RequireService authorization policy used by /api/internal/* endpoints.
        var tokenType = Request.Headers["X-Test-Token-Type"].ToString();
        if (!string.IsNullOrEmpty(tokenType))
        {
            claims.Add(new Claim("token_type", tokenType));
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
