// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Auth;

namespace Sorcha.Register.Service.Extensions;

/// <summary>
/// Extension methods for configuring authorization policies in Register Service.
/// JWT authentication is configured via the shared ServiceDefaults.AddJwtAuthentication().
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds authorization policies for Register Service.
    /// Note: Call builder.AddJwtAuthentication() from ServiceDefaults first.
    /// </summary>
    public static IServiceCollection AddRegisterAuthorization(this IServiceCollection services)
    {
        // Register shared authorization policies (RequireAuthenticated, RequireService,
        // RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator, CanWriteDockets)
        services.AddSorchaAuthorizationPolicies();

        services.AddAuthorization(options =>
        {
            // Register management (create, configure) — requires org admin or service token
            options.AddPolicy("CanManageRegisters", policy =>
                policy.RequireAssertion(context =>
                {
                    // Service tokens can always manage registers
                    var isService = context.User.Claims.Any(c => c.Type == TokenClaimConstants.TokenType && c.Value == TokenClaimConstants.TokenTypeService);
                    if (isService) return true;

                    // User tokens require org_id claim + Administrator or SystemAdmin role
                    var hasOrgId = context.User.Claims.Any(c => c.Type == TokenClaimConstants.OrgId && !string.IsNullOrEmpty(c.Value));
                    var isAdmin = context.User.IsInRole("Administrator") || context.User.IsInRole("SystemAdmin");
                    return hasOrgId && isAdmin;
                }));

            // System register creation — requires SystemAdmin org + SystemAdmin role
            options.AddPolicy("CanCreateSystemRegisters", policy =>
                policy.RequireAssertion(context =>
                {
                    var orgId = context.User.FindFirst(TokenClaimConstants.OrgId)?.Value;
                    var isSystemAdminOrg = orgId == "00000000-0000-0000-0000-000000000001";
                    var isSystemAdmin = context.User.IsInRole("SystemAdmin");
                    return isSystemAdminOrg && isSystemAdmin;
                }));

            // Transaction submission (write operations)
            options.AddPolicy("CanSubmitTransactions", policy =>
                policy.RequireAuthenticatedUser());

            // Transaction reading (query operations)
            options.AddPolicy("CanReadTransactions", policy =>
                policy.RequireAuthenticatedUser());
        });

        return services;
    }
}
