// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.Tenant.Service.Extensions;

/// <summary>
/// Configuration for JWT authentication.
/// This configuration is used by Tenant Service for token issuance.
/// The shared JwtSettings from ServiceDefaults is used for token validation.
/// </summary>
public class JwtConfiguration
{
    /// <summary>
    /// Installation name driving the issuer + tier-audience namespace (spec 136). Default "sorcha".
    /// </summary>
    public string? InstallationName { get; set; }

    /// <summary>
    /// JWT token issuer (iss claim). Resolved via SorchaIssuer — no shared default (spec 136).
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The installation's four tier audiences, derived from SorchaAudiences (spec 136).
    /// </summary>
    public string[] Audiences { get; set; } = [];

    /// <summary>
    /// Signing key for development (production uses Azure Key Vault).
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Access token lifetime in minutes.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Refresh token lifetime in hours.
    /// </summary>
    public int RefreshTokenLifetimeHours { get; set; } = 24;

    /// <summary>
    /// Service token lifetime in hours.
    /// </summary>
    public int ServiceTokenLifetimeHours { get; set; } = 8;

    /// <summary>
    /// Clock skew tolerance for token validation.
    /// </summary>
    public int ClockSkewMinutes { get; set; } = 5;

    /// <summary>
    /// Whether to validate the issuer.
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// Whether to validate the audience.
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// Whether to validate the signing key.
    /// </summary>
    public bool ValidateIssuerSigningKey { get; set; } = true;

    /// <summary>
    /// Whether to validate token lifetime.
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;
}

/// <summary>
/// Extension methods for configuring JWT authorization in Tenant Service.
/// JWT authentication is configured via the shared ServiceDefaults.AddJwtAuthentication().
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Configures JwtConfiguration from the environment for token issuance.
    /// This ensures the TokenService uses the same key as the shared JWT authentication.
    /// </summary>
    public static IServiceCollection ConfigureJwtForTokenIssuance(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowDevLocalIssuerFallback)
    {
        services.Configure<JwtConfiguration>(options =>
        {
            var section = configuration.GetSection("JwtSettings");
            var installationName = configuration["JwtSettings:InstallationName"];
            var issuerFromConfig = configuration["JwtSettings:Issuer"];

            // Spec 136: issuer + tier audiences derive from the SAME single source of truth the
            // ServiceDefaults validation side uses, so the minted and validated values always agree.
            options.InstallationName = installationName;
            options.Issuer = SorchaIssuer.Resolve(issuerFromConfig, installationName, allowDevLocalIssuerFallback);
            options.Audiences = new SorchaAudiences(installationName).All.ToArray();

            options.SigningKey = configuration["JwtSettings:SigningKey"];
            options.AccessTokenLifetimeMinutes = section.GetValue("AccessTokenLifetimeMinutes", 60);
            options.RefreshTokenLifetimeHours = section.GetValue("RefreshTokenLifetimeHours", 24);
            options.ServiceTokenLifetimeHours = section.GetValue("ServiceTokenLifetimeHours", 8);
            options.ClockSkewMinutes = section.GetValue("ClockSkewMinutes", 5);
            options.ValidateIssuer = section.GetValue("ValidateIssuer", true);
            options.ValidateAudience = section.GetValue("ValidateAudience", true);
            options.ValidateIssuerSigningKey = section.GetValue("ValidateIssuerSigningKey", true);
            options.ValidateLifetime = section.GetValue("ValidateLifetime", true);
        });

        return services;
    }

    /// <summary>
    /// Adds authorization policies for Tenant Service.
    /// Note: Call builder.AddJwtAuthentication() from ServiceDefaults first.
    /// </summary>
    public static IServiceCollection AddTenantAuthorization(this IServiceCollection services)
    {
        // Register shared authorization policies (RequireAuthenticated, RequireService,
        // RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator, CanWriteDockets)
        services.AddSorchaAuthorizationPolicies();

        services.AddAuthorization(options =>
        {
            // Policy for auditors — SystemAdmin and Administrator also have audit access
            options.AddPolicy("RequireAuditor", policy =>
                policy.RequireRole("SystemAdmin", "Administrator", "Auditor"));

            // Policy for designers — SystemAdmin and Administrator also have designer access
            options.AddPolicy("RequireDesigner", policy =>
                policy.RequireRole("SystemAdmin", "Administrator", "Designer"));

            // Policy for public users (PassKey authenticated)
            options.AddPolicy("RequirePublicUser", policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser));

            // Policy for blockchain creation
            options.AddPolicy("CanCreateBlockchain", policy =>
                policy.RequireClaim("can_create_blockchain", "true"));

            // Policy for blueprint publishing
            options.AddPolicy("CanPublishBlueprint", policy =>
                policy.RequireClaim("can_publish_blueprint", "true"));

            // NOTE (Feature 147 / review LOW): RequireSystemAdmin is intentionally NOT re-registered
            // here. The shared, org-scoped definition from AddSorchaAuthorizationPolicies
            // (system-admin org membership AND SystemAdmin role) is authoritative. A previous
            // role-only override here dropped the org-scope (last-write-wins), letting a SystemAdmin
            // in any org clear platform-administration routes.
        });

        return services;
    }
}
