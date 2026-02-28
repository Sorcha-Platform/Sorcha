// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.Register.Service.IntegrationTests.Fixtures;

/// <summary>
/// Test authentication handler for integration tests.
/// Automatically authenticates requests with test claims.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for authorization header
        if (!Context.Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, "test@sorcha.io"),
            new("organization_id", "test-org-id"),
            new("org_id", "test-org-id")
        };

        // Check for role override header
        if (Context.Request.Headers.TryGetValue("X-Test-Role", out var roleHeader))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleHeader.ToString()));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "User"));
        }

        // Check for user ID override header
        if (Context.Request.Headers.TryGetValue("X-Test-User-Id", out var userIdHeader))
        {
            claims.RemoveAll(c => c.Type == ClaimTypes.NameIdentifier);
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdHeader.ToString()));
        }

        // Check for organization ID override header
        if (Context.Request.Headers.TryGetValue("X-Test-Organization-Id", out var orgIdHeader))
        {
            claims.RemoveAll(c => c.Type == "organization_id");
            claims.Add(new Claim("organization_id", orgIdHeader.ToString()));
        }

        // Check for token type override header (for service token testing)
        if (Context.Request.Headers.TryGetValue("X-Test-Token-Type", out var tokenTypeHeader))
        {
            claims.Add(new Claim("token_type", tokenTypeHeader.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
