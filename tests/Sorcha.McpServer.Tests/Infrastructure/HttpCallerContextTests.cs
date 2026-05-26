// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sorcha.McpServer.Infrastructure;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// Unit coverage for the HTTP-transport caller context (spec 139 US3): it must read the
/// current request's bearer + validated principal on every access (so a singleton yields
/// per-request values) and derive tier/roles/subject/org with the same logic as the stdio path.
/// </summary>
public class HttpCallerContextTests
{
    private const string Installation = "phaethon";

    private static (HttpCallerContext caller, TestHttpContextAccessor accessor) Build()
    {
        var accessor = new TestHttpContextAccessor();
        return (new HttpCallerContext(accessor), accessor);
    }

    private static HttpContext AuthenticatedContext(
        string? token,
        string audienceSuffix,
        IEnumerable<Claim>? extraClaims = null,
        bool authenticated = true)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Aud, $"{Installation}:{audienceSuffix}")
        };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        // Mark the identity authenticated by giving it an authentication type.
        var identity = new ClaimsIdentity(authenticated ? "jwt" : null, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaims(claims);

        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        if (token is not null)
        {
            ctx.Request.Headers.Authorization = $"Bearer {token}";
        }

        return ctx;
    }

    [Fact]
    public void RawToken_StripsBearerPrefix()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext("abc.def.ghi", "platform");

        caller.RawToken.Should().Be("abc.def.ghi");
    }

    [Fact]
    public void RawToken_NoHeader_IsNull()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext(token: null, "platform");

        caller.RawToken.Should().BeNull();
    }

    [Fact]
    public void Tier_PlatformAudience_ResolvesPlatform()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext("t", "platform");

        caller.Tier.Should().Be(Tier.Platform);
    }

    [Fact]
    public void Tier_ConsumerAudience_ResolvesConsumer()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext("t", "consumer");

        caller.Tier.Should().Be(Tier.Consumer);
    }

    [Fact]
    public void Roles_MappedToSorchaForm()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext("t", "platform", new[]
        {
            new Claim(ClaimTypes.Role, "administrator"),
            new Claim(ClaimTypes.Role, "sorcha:designer")
        });

        caller.Roles.Should().BeEquivalentTo(["sorcha:admin", "sorcha:designer"]);
    }

    [Fact]
    public void SubjectAndOrg_ReadFromClaims()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = AuthenticatedContext("t", "platform", new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "user-123"),
            new Claim("org_id", "org-abc")
        });

        caller.Subject.Should().Be("user-123");
        caller.OrganizationId.Should().Be("org-abc");
    }

    [Fact]
    public void IsAuthenticated_ReflectsPrincipalIdentity()
    {
        var (caller, accessor) = Build();

        accessor.HttpContext = AuthenticatedContext("t", "platform", authenticated: true);
        caller.IsAuthenticated.Should().BeTrue();

        accessor.HttpContext = AuthenticatedContext("t", "platform", authenticated: false);
        caller.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void NoHttpContext_YieldsUnauthenticatedEmptyContext()
    {
        var (caller, accessor) = Build();
        accessor.HttpContext = null;

        caller.IsAuthenticated.Should().BeFalse();
        caller.RawToken.Should().BeNull();
        caller.Tier.Should().BeNull();
        caller.Roles.Should().BeEmpty();
        caller.Subject.Should().BeNull();
        caller.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void EachAccess_ReflectsCurrentRequest()
    {
        // Proves the per-request semantics that justify a singleton registration: swapping the
        // ambient HttpContext changes the values without rebuilding the caller context.
        var (caller, accessor) = Build();

        accessor.HttpContext = AuthenticatedContext("first", "platform");
        caller.RawToken.Should().Be("first");
        caller.Tier.Should().Be(Tier.Platform);

        accessor.HttpContext = AuthenticatedContext("second", "consumer");
        caller.RawToken.Should().Be("second");
        caller.Tier.Should().Be(Tier.Consumer);
    }

    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
