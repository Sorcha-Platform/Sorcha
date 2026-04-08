// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Auth;

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

            // Wallet operations (sign, encrypt, decrypt) - requires wallet ownership
            options.AddPolicy("CanUseWallet", policy =>
                policy.RequireAuthenticatedUser());

            // Persona crypto (internal S2S only) - requires a service token.
            // Used by Tenant Service to encrypt/decrypt a user's persona blob.
            // Routes under this policy must NOT be exposed through the API
            // Gateway — a gateway-config guard test asserts this.
            options.AddPolicy("RequirePersonaCrypto", policy =>
                policy.RequireAssertion(context =>
                {
                    var isService = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.TokenType &&
                        c.Value == TokenClaimConstants.TokenTypeService);
                    return isService;
                }));
        });

        return services;
    }
}
