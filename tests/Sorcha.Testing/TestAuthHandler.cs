// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.Testing;

/// <summary>
/// Shared authentication handler used by Sorcha integration test factories.
/// Requires an Authorization header to be present; default claims and the
/// default role are configurable per-service via <see cref="TestAuthHandlerOptions"/>.
/// Standard override headers supported:
///   X-Test-Role          — overrides/sets the ClaimTypes.Role claim.
///   X-Test-User-Id       — overrides ClaimTypes.NameIdentifier.
///   X-Test-Token-Type    — sets the "token_type" claim (e.g. "user" or "service").
///   X-Test-Organization-Id — overrides the "organization_id" and "org_id" claims.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public const string SchemeName = "TestScheme";

    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>(Options.DefaultClaims);

        if (Request.Headers.TryGetValue("X-Test-Role", out var roleHeader))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleHeader.ToString()));
        }
        else if (!string.IsNullOrEmpty(Options.DefaultRole))
        {
            claims.Add(new Claim(ClaimTypes.Role, Options.DefaultRole));
        }

        if (Request.Headers.TryGetValue("X-Test-User-Id", out var userIdHeader))
        {
            claims.RemoveAll(c => c.Type == ClaimTypes.NameIdentifier);
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdHeader.ToString()));
        }

        if (Request.Headers.TryGetValue("X-Test-Organization-Id", out var orgIdHeader))
        {
            var orgId = orgIdHeader.ToString();
            claims.RemoveAll(c => c.Type is "organization_id" or "org_id");
            claims.Add(new Claim("organization_id", orgId));
            claims.Add(new Claim("org_id", orgId));
        }

        if (Request.Headers.TryGetValue("X-Test-Token-Type", out var tokenTypeHeader))
        {
            claims.RemoveAll(c => c.Type == "token_type");
            claims.Add(new Claim("token_type", tokenTypeHeader.ToString()));
        }

        foreach (var (header, claimType) in Options.HeaderClaimMappings)
        {
            if (Request.Headers.TryGetValue(header, out var headerValue))
            {
                claims.Add(new Claim(claimType, headerValue.ToString()));
            }
        }

        // Spec 136 tier audiences. An explicit X-Test-Audience (comma-separated) sets the aud claim(s)
        // for tier-isolation tests; otherwise inject all four tier audiences for the default
        // installation so pre-spec-136 tests authenticate at any tier-gated endpoint unchanged.
        if (Request.Headers.TryGetValue("X-Test-Audience", out var audHeader))
        {
            claims.RemoveAll(c => c.Type == "aud");
            foreach (var aud in audHeader.ToString()
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("aud", aud));
            }
        }
        else if (!claims.Any(c => c.Type == "aud"))
        {
            // Resolve the SAME SorchaAudiences the tier-audience policies use (registered from the
            // host's configured InstallationName), so injected audiences always match the validator.
            var audiences = Context.RequestServices.GetService<SorchaAudiences>()
                ?? new SorchaAudiences(installationName: null);
            foreach (var aud in audiences.All)
            {
                claims.Add(new Claim("aud", aud));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
