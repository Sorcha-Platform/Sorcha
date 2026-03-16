// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.Validator.Service.IntegrationTests.Fixtures;

/// <summary>
/// Test authentication handler for Validator Service integration tests.
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
            new(ClaimTypes.NameIdentifier, "test-validator-id"),
            new(ClaimTypes.Name, "Test Validator"),
            new(ClaimTypes.Email, "validator@sorcha.io"),
            new("organization_id", "test-org-id"),
            new("org_id", "test-org-id"),
            new("validator_id", "test-validator-id"),
            new("wallet_address", "test-wallet-address"),
            new("token_type", "service")
        };

        // Check for role override header
        if (Context.Request.Headers.TryGetValue("X-Test-Role", out var roleHeader))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleHeader.ToString()));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "Validator"));
        }

        // Check for validator ID override header
        if (Context.Request.Headers.TryGetValue("X-Test-Validator-Id", out var validatorIdHeader))
        {
            claims.RemoveAll(c => c.Type == "validator_id");
            claims.Add(new Claim("validator_id", validatorIdHeader.ToString()));
        }

        // Check for register ID override header
        if (Context.Request.Headers.TryGetValue("X-Test-Register-Id", out var registerIdHeader))
        {
            claims.Add(new Claim("register_id", registerIdHeader.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
