// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;

using Sorcha.Blueprint.Service.Authorization;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Blueprint.Service.Extensions;

/// <summary>
/// Extension methods for configuring authorization policies in Blueprint Service.
/// JWT authentication is configured via the shared ServiceDefaults.AddJwtAuthentication().
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds authorization policies for Blueprint Service.
    /// Note: Call builder.AddJwtAuthentication() from ServiceDefaults first.
    /// </summary>
    public static IServiceCollection AddBlueprintAuthorization(this IServiceCollection services)
    {
        // Register shared authorization policies (RequireAuthenticated, RequireService,
        // RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator, CanWriteDockets)
        services.AddSorchaAuthorizationPolicies();

        // Feature 147 / review H2: blueprint authoring must exclude consumer-tier tokens (which carry
        // an org_id under Feature 136). The tier gate lives in the policy itself (via the handler) so it
        // cannot be omitted per-endpoint.
        services.AddSingleton<IAuthorizationHandler, BlueprintManagementAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Blueprint management (create, update, delete) — service-tier caller OR platform-tier
            // org member. The previous `org_id OR service` assertion admitted consumer/citizen tokens
            // (which carry org_id); the requirement folds in the platform-audience check so the gap
            // is closed for every endpoint using this policy, current and future (review H2).
            options.AddPolicy("CanManageBlueprints", policy =>
                policy.AddRequirements(new BlueprintManagementRequirement()));

            // Blueprint execution - any authenticated user
            options.AddPolicy("CanExecuteBlueprints", policy =>
                policy.RequireAuthenticatedUser());

            // Blueprint publishing - requires specific claim or admin role.
            //
            // SystemAdmin is accepted alongside Administrator, matching the shared
            // AuthorizationPolicies.RequireAdministrator (RequireRole("SystemAdmin", "Administrator"))
            // and the Register Service's CanManageRegisters. This is NOT a privilege grant: a
            // SystemAdmin already clears every other admin gate in the platform, including the
            // register-creation policy that governs the ledger a blueprint is published to. The
            // omission was accidental — the line read IsInRole("RequireAdministrator") (a policy
            // name used as a role name) until 96bdc0cd2 narrowed it to the single literal
            // "Administrator" while fixing that bug; SystemAdmin was never considered.
            //
            // The consequence was not a plain 403. A SystemAdmin's refusal here is DETERMINISTIC,
            // and the MCP server's IServiceAvailabilityTracker marks a service unavailable after
            // three consecutive failures — so three publish attempts silently disabled EVERY
            // Blueprint MCP tool for a cooldown, reporting "Blueprint service is currently
            // unavailable", which was false.
            options.AddPolicy("CanPublishBlueprints", policy =>
                policy.RequireAssertion(context =>
                {
                    var canPublish = context.User.Claims.Any(c => c.Type == "can_publish_blueprint" && c.Value == "true");
                    var isAdmin = context.User.IsInRole("Administrator") || context.User.IsInRole("SystemAdmin");
                    return canPublish || isAdmin;
                }));
        });

        return services;
    }
}
