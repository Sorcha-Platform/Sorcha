// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;

// Placed in Microsoft.Extensions.Hosting so callers get these extension methods automatically
// without needing an additional using directive — a standard pattern for service-defaults libraries.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Well-known authorization policy names shared across all Sorcha services.
/// Use these constants instead of inline magic strings when applying policies to endpoints.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Any authenticated user.</summary>
    public const string RequireAuthenticated = "RequireAuthenticated";

    /// <summary>Service-to-service token required.</summary>
    public const string RequireService = "RequireService";

    /// <summary>Token must carry a non-empty org_id claim.</summary>
    public const string RequireOrganizationMember = "RequireOrganizationMember";

    /// <summary>Service token acting on behalf of a user (delegated authority).</summary>
    public const string RequireDelegatedAuthority = "RequireDelegatedAuthority";

    /// <summary>Administrator role required.</summary>
    public const string RequireAdministrator = "RequireAdministrator";

    /// <summary>
    /// Service token required for docket writes (Validator/Register).
    /// Currently mirrors RequireService; kept as a separate semantic policy for future scope-based tightening.
    /// </summary>
    public const string CanWriteDockets = "CanWriteDockets";
}

/// <summary>
/// Extension methods for registering shared authorization policies across all Sorcha services.
/// These policies cover common concerns (authentication, service tokens, organization membership,
/// delegated authority, administrator role, and docket writes). Individual services add their
/// own domain-specific policies on top of these shared ones.
/// </summary>
public static class AuthorizationPolicyExtensions
{
    /// <summary>
    /// Registers the standard set of Sorcha authorization policies that are shared across all services.
    /// Call this before adding service-specific policies. Policies registered:
    /// <list type="bullet">
    ///   <item><term>RequireAuthenticated</term><description>Any authenticated user.</description></item>
    ///   <item><term>RequireService</term><description>Service-to-service token required.</description></item>
    ///   <item><term>RequireOrganizationMember</term><description>Token must carry a non-empty org_id claim.</description></item>
    ///   <item><term>RequireDelegatedAuthority</term><description>Service token acting on behalf of a user.</description></item>
    ///   <item><term>RequireAdministrator</term><description>Administrator role required.</description></item>
    ///   <item><term>CanWriteDockets</term><description>Service token required for docket writes. Currently mirrors RequireService; exists as a separate semantic policy for future scope-based tightening.</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddSorchaAuthorizationPolicies<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSorchaAuthorizationPolicies();
        return builder;
    }

    /// <summary>
    /// Registers the standard set of Sorcha authorization policies on an <see cref="IServiceCollection"/>.
    /// This overload is used by individual service authorization extension methods that operate
    /// on <see cref="IServiceCollection"/> rather than <see cref="IHostApplicationBuilder"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSorchaAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // 1. RequireAuthenticated — any authenticated user
            options.AddPolicy(AuthorizationPolicies.RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            // 2. RequireService — service-to-service token
            options.AddPolicy(AuthorizationPolicies.RequireService, policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

            // 3. RequireOrganizationMember — token must carry a non-empty org_id
            options.AddPolicy(AuthorizationPolicies.RequireOrganizationMember, policy =>
                policy.RequireAssertion(context =>
                    context.User.Claims.Any(c => c.Type == TokenClaimConstants.OrgId && !string.IsNullOrEmpty(c.Value))));

            // 4. RequireDelegatedAuthority — service acting on behalf of a user
            options.AddPolicy(AuthorizationPolicies.RequireDelegatedAuthority, policy =>
                policy.RequireAssertion(context =>
                {
                    var isService = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.TokenType &&
                        c.Value == TokenClaimConstants.TokenTypeService);
                    var hasDelegatedUserId = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.DelegatedUserId && !string.IsNullOrEmpty(c.Value));
                    return isService && hasDelegatedUserId;
                }));

            // 5. RequireAdministrator — Administrator role
            options.AddPolicy(AuthorizationPolicies.RequireAdministrator, policy =>
                policy.RequireRole("Administrator"));

            // 6. CanWriteDockets — service token required (Validator/Register docket writes).
            //    Currently mirrors RequireService; kept as a separate semantic policy so
            //    Register and Validator services can tighten it later (e.g. require a
            //    "dockets:write" scope) without affecting the general RequireService gate.
            options.AddPolicy(AuthorizationPolicies.CanWriteDockets, policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));
        });

        return services;
    }
}
