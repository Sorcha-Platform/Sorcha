// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sorcha.McpServer.Services;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Per-request <see cref="ICallerContext"/> for the Streamable HTTP transport (spec 139 US3).
/// <para>
/// Every member reads the <em>current</em> <see cref="HttpContext"/> from the injected
/// <see cref="IHttpContextAccessor"/> on access, so a single shared (singleton) registration
/// still yields per-request values. This deliberately avoids registering the caller context as
/// scoped — the <see cref="CallerTokenForwardingHandler"/> is a pooled <see cref="DelegatingHandler"/>
/// and capturing a scoped dependency in it would surface a captive-dependency / cross-request
/// token-bleed hazard.
/// </para>
/// <para>
/// The bearer is the request's validated <c>Authorization: Bearer</c> value; identity claims come
/// from the already-validated <see cref="HttpContext.User"/> (the ASP.NET Core JWT bearer
/// middleware validated it against the installation issuer + tier audiences before dispatch).
/// </para>
/// </summary>
public sealed class HttpCallerContext : ICallerContext
{
    private const string BearerPrefix = "Bearer ";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Creates the per-request caller context backed by the supplied accessor.
    /// </summary>
    public HttpCallerContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext? Context => _httpContextAccessor.HttpContext;

    private ClaimsPrincipal? User => Context?.User;

    /// <inheritdoc />
    public string? RawToken
    {
        get
        {
            var header = Context?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                return null;
            }

            return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                ? header[BearerPrefix.Length..].Trim()
                : header.Trim();
        }
    }

    /// <inheritdoc />
    public Tier? Tier
    {
        get
        {
            var user = User;
            if (user is null)
            {
                return null;
            }

            // The audience claim survives validation as "aud" — match the same tier-suffix
            // logic the stdio path (McpSessionService) uses.
            var audiences = user.FindAll(JwtRegisteredClaimNames.Aud).Select(c => c.Value)
                .Concat(user.FindAll("aud").Select(c => c.Value));
            return TierResolution.Resolve(audiences);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles
    {
        get
        {
            var user = User;
            if (user is null)
            {
                return [];
            }

            // The bearer middleware maps role claims to ClaimTypes.Role (RoleClaimType); also
            // accept the raw claim names defensively, then normalise to the sorcha:* MCP form.
            var raw = user.Claims
                .Where(c => c.Type == ClaimTypes.Role
                            || c.Type == "role"
                            || c.Type == "roles"
                            || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                .Select(c => c.Value)
                .Distinct();

            return MapToMcpRoles(raw);
        }
    }

    /// <inheritdoc />
    public string? OrganizationId
    {
        get
        {
            var user = User;
            if (user is null)
            {
                return null;
            }

            return user.FindFirst("org_id")?.Value
                ?? user.FindFirst("tenant_id")?.Value
                ?? user.FindFirst("tid")?.Value;
        }
    }

    /// <inheritdoc />
    public string? Subject
    {
        get
        {
            var user = User;
            if (user is null)
            {
                return null;
            }

            return user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    /// <summary>
    /// Maps standard role names to the Sorcha MCP role format. Delegates to the single-home
    /// <see cref="McpRoleNormalizer"/> (mirrors the stdio path in <see cref="Sorcha.McpServer.Services.McpSessionService"/>).
    /// </summary>
    private static List<string> MapToMcpRoles(IEnumerable<string> roles) =>
        McpRoleNormalizer.NormalizeAll(roles);
}
