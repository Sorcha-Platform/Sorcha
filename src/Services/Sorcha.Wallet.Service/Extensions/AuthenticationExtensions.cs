// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;

using Sorcha.ServiceClients.Auth;
using Sorcha.Wallet.Service.Authorization;

namespace Sorcha.Wallet.Service.Extensions;

/// <summary>
/// Extension methods for configuring authorization policies in Wallet Service.
/// JWT authentication is configured via the shared ServiceDefaults.AddJwtAuthentication().
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Adds authorization policies for Wallet Service.
    /// Note: Call builder.AddJwtAuthentication() from ServiceDefaults first.
    /// </summary>
    public static IServiceCollection AddWalletAuthorization(this IServiceCollection services)
    {
        // Register shared authorization policies (RequireAuthenticated, RequireService,
        // RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator, CanWriteDockets)
        services.AddSorchaAuthorizationPolicies();

        // Feature 147 / review H1: the system-wallet recover operation accepts either a service-tier
        // caller or a platform-tier administrator (resolved against this installation's audiences).
        services.AddSingleton<IAuthorizationHandler, SystemWalletRecoveryAuthorizationHandler>();

        services.AddAuthorization(options =>
        {
            // Wallet management (create, read wallet list)
            options.AddPolicy("CanManageWallets", policy =>
                policy.RequireAssertion(context =>
                {
                    var hasOrgId = context.User.Claims.Any(c => c.Type == TokenClaimConstants.OrgId && !string.IsNullOrEmpty(c.Value));
                    var isService = context.User.Claims.Any(c => c.Type == TokenClaimConstants.TokenType && c.Value == TokenClaimConstants.TokenTypeService);
                    return hasOrgId || isService;
                }));

            // System-wallet recovery (BIP39 import that seats a validator docket-signing wallet).
            // Feature 147 / review H1: service-tier caller OR platform-tier administrator.
            // Logic lives in SystemWalletRecoveryAuthorizationHandler so the audience tier is
            // resolved from the single source of truth at request time.
            options.AddPolicy("CanRecoverSystemWallet", policy =>
                policy.RequireAuthenticatedUser()
                      .AddRequirements(new SystemWalletRecoveryRequirement()));

            // Wallet operations (sign, encrypt, decrypt) - requires wallet ownership
            options.AddPolicy("CanUseWallet", policy =>
                policy.RequireAuthenticatedUser());

            // Persona crypto (internal S2S only) — requires a service token
            // that additionally carries the `persona:crypto` scope. Only the
            // Tenant Service is issued this scope, so a stray Blueprint /
            // Register / Peer service token cannot reach these endpoints
            // even if it leaked. Routes under this policy must NOT be
            // exposed through the API Gateway.
            options.AddPolicy("RequirePersonaCrypto", policy =>
                policy.RequireAssertion(context =>
                {
                    var isService = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.TokenType &&
                        c.Value == TokenClaimConstants.TokenTypeService);
                    if (!isService) return false;

                    // Accept either a space-delimited `scope` claim or a
                    // repeated `scp` claim, depending on how the service
                    // token was issued.
                    var hasPersonaCryptoScope = context.User.Claims
                        .Where(c => c.Type == TokenClaimConstants.Scope || c.Type == "scp")
                        .SelectMany(c => (c.Value ?? string.Empty).Split(
                            [' '], StringSplitOptions.RemoveEmptyEntries))
                        .Any(s => string.Equals(s, "persona:crypto", StringComparison.Ordinal));

                    return hasPersonaCryptoScope;
                }));
        });

        return services;
    }
}
