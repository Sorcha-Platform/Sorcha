// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceDefaults.Auth;

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

    /// <summary>Consumer-tier audience required (citizen / wallet holder surfaces). Spec 136.</summary>
    public const string RequireConsumerAudience = "RequireConsumerAudience";

    /// <summary>Platform-tier audience required (admin / designer / org-management surfaces). Spec 136.</summary>
    public const string RequirePlatformAudience = "RequirePlatformAudience";

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

    /// <summary>System admin org member with Auditor+ role required.</summary>
    public const string RequirePlatformAuditor = "RequirePlatformAuditor";

    /// <summary>System admin org member with SystemAdmin role required.</summary>
    public const string RequireSystemAdmin = "RequireSystemAdmin";

    /// <summary>
    /// Service token required for pushing register-state observations into Register.Service
    /// (peer-height adverts, validator sealing progress). Feature 108. Currently mirrors
    /// RequireService; kept separate for future scope-based tightening.
    /// </summary>
    public const string CanReportRegisterObservation = "CanReportRegisterObservation";
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
    ///   <item><term>RequirePlatformAuditor</term><description>System admin org member with Auditor+ role required.</description></item>
    ///   <item><term>RequireSystemAdmin</term><description>System admin org member with SystemAdmin role required.</description></item>
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
        // Spec 136: the tier-audience policies need the installation's audience set at request time.
        // Resolve it from the configured InstallationName (default "sorcha" when absent / no config),
        // so issuance and validation share the single source of truth. Registered idempotently —
        // every service calls this once via its *Authorization extension.
        services.TryAddSingleton(sp =>
            new SorchaAudiences(sp.GetService<IConfiguration>()?["JwtSettings:InstallationName"]));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, TierAudienceAuthorizationHandler>());

        services.AddAuthorization(options =>
        {
            // 1. RequireAuthenticated — any authenticated user
            options.AddPolicy(AuthorizationPolicies.RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            // 2. RequireService — service-to-service token. Spec 136: in addition to the
            //    token_type marker, the token MUST carry this installation's :service audience,
            //    so a human token (or another installation's service token) is refused at the
            //    audience layer, not merely by a claim check.
            options.AddPolicy(AuthorizationPolicies.RequireService, policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService)
                      .AddRequirements(new TierAudienceRequirement(Tier.Service)));

            // 2b. Consumer / platform tier-audience policies (spec 136). Applied per endpoint to
            //     enforce consumer ⊥ platform isolation; the unclassified-endpoint default is platform.
            options.AddPolicy(AuthorizationPolicies.RequireConsumerAudience, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new TierAudienceRequirement(Tier.Consumer)));
            options.AddPolicy(AuthorizationPolicies.RequirePlatformAudience, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new TierAudienceRequirement(Tier.Platform)));

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

            // 5. RequireAdministrator — Administrator or SystemAdmin role
            options.AddPolicy(AuthorizationPolicies.RequireAdministrator, policy =>
                policy.RequireRole("SystemAdmin", "Administrator"));

            // 6. CanWriteDockets — service token required (Validator/Register docket writes).
            //    Currently mirrors RequireService; kept as a separate semantic policy so
            //    Register and Validator services can tighten it later (e.g. require a
            //    "dockets:write" scope) without affecting the general RequireService gate.
            options.AddPolicy(AuthorizationPolicies.CanWriteDockets, policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService)
                      .AddRequirements(new TierAudienceRequirement(Tier.Service)));

            // 7. RequirePlatformAuditor — system admin org member with Auditor+ role
            options.AddPolicy(AuthorizationPolicies.RequirePlatformAuditor, policy =>
                policy.RequireAssertion(context =>
                {
                    var orgId = context.User.FindFirst("org_id")?.Value;
                    var isSystemAdminOrg = orgId == "00000000-0000-0000-0000-000000000001";
                    var hasRole = context.User.IsInRole("SystemAdmin") || context.User.IsInRole("Auditor") || context.User.IsInRole("Administrator");
                    return isSystemAdminOrg && hasRole;
                }));

            // 8. RequireSystemAdmin — system admin org member with SystemAdmin role
            options.AddPolicy(AuthorizationPolicies.RequireSystemAdmin, policy =>
                policy.RequireAssertion(context =>
                {
                    var orgId = context.User.FindFirst("org_id")?.Value;
                    var isSystemAdminOrg = orgId == "00000000-0000-0000-0000-000000000001";
                    var isSystemAdmin = context.User.IsInRole("SystemAdmin");
                    return isSystemAdminOrg && isSystemAdmin;
                }));

            // 9. CanReportRegisterObservation — service token required for Feature 108 observation
            //    intake endpoints (peer height, validator sealing). Mirrors RequireService today;
            //    carved out so Register.Service can later require a narrower "observations:write"
            //    scope without broadening RequireService.
            options.AddPolicy(AuthorizationPolicies.CanReportRegisterObservation, policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService)
                      .AddRequirements(new TierAudienceRequirement(Tier.Service)));
        });

        return services;
    }

    /// <summary>
    /// True if the principal carries an <c>aud</c> claim equal to this installation's audience for
    /// <paramref name="tier"/>. A token may carry multiple <c>aud</c> claims; any match suffices.
    /// </summary>
    public static bool HasTierAudience(ClaimsPrincipal user, SorchaAudiences audiences, Tier tier)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(audiences);
        var expected = audiences.For(tier);
        return user.FindAll("aud").Any(c => c.Value == expected);
    }
}
