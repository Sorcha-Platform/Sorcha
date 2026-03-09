// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Auth;

namespace Sorcha.Tenant.Service.Extensions;

/// <summary>
/// Configuration for JWT authentication.
/// This configuration is used by Tenant Service for token issuance.
/// The shared JwtSettings from ServiceDefaults is used for token validation.
/// </summary>
public class JwtConfiguration
{
    /// <summary>
    /// JWT token issuer (iss claim).
    /// </summary>
    public string Issuer { get; set; } = "https://tenant.sorcha.io";

    /// <summary>
    /// Valid audiences for tokens (aud claim).
    /// </summary>
    public string[] Audiences { get; set; } = ["https://api.sorcha.io"];

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
        IConfiguration configuration)
    {
        services.Configure<JwtConfiguration>(options =>
        {
            var section = configuration.GetSection("JwtSettings");
            var installationName = configuration["JwtSettings:InstallationName"];

            // Derive issuer from installation name if not explicitly set
            var issuerFromConfig = configuration["JwtSettings:Issuer"];
            if (!string.IsNullOrWhiteSpace(issuerFromConfig))
            {
                options.Issuer = issuerFromConfig;
            }
            else if (!string.IsNullOrWhiteSpace(installationName))
            {
                options.Issuer = $"http://{installationName}";
            }
            else
            {
                options.Issuer = "https://tenant.sorcha.io";
            }

            options.SigningKey = configuration["JwtSettings:SigningKey"];
            options.AccessTokenLifetimeMinutes = section.GetValue("AccessTokenLifetimeMinutes", 60);
            options.RefreshTokenLifetimeHours = section.GetValue("RefreshTokenLifetimeHours", 24);
            options.ServiceTokenLifetimeHours = section.GetValue("ServiceTokenLifetimeHours", 8);
            options.ClockSkewMinutes = section.GetValue("ClockSkewMinutes", 5);
            options.ValidateIssuer = section.GetValue("ValidateIssuer", true);
            options.ValidateAudience = section.GetValue("ValidateAudience", true);
            options.ValidateIssuerSigningKey = section.GetValue("ValidateIssuerSigningKey", true);
            options.ValidateLifetime = section.GetValue("ValidateLifetime", true);

            // Handle audiences - can be:
            // - Array: JwtSettings:Audience:0, JwtSettings:Audience:1
            // - Single value: JwtSettings:Audience
            // - Environment variable array: JwtSettings__Audience__0
            // - Derived from InstallationName if not set
            var audienceSection = configuration.GetSection("JwtSettings:Audience");
            var audienceChildren = audienceSection.GetChildren().ToList();

            if (audienceChildren.Count > 0)
            {
                // Array of audiences
                options.Audiences = audienceChildren.Select(c => c.Value!).Where(v => !string.IsNullOrEmpty(v)).ToArray();
            }
            else
            {
                // Single audience value
                var singleAudience = configuration["JwtSettings:Audience"];
                if (!string.IsNullOrEmpty(singleAudience))
                {
                    options.Audiences = [singleAudience];
                }
                else if (!string.IsNullOrWhiteSpace(installationName))
                {
                    // Derive from installation name
                    options.Audiences = [$"http://{installationName}"];
                }
                else
                {
                    // Fallback to default
                    options.Audiences = ["https://api.sorcha.io"];
                }
            }
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

            // Policy for system administrators only (platform-level operations)
            options.AddPolicy("RequireSystemAdmin", policy =>
                policy.RequireRole("SystemAdmin"));
        });

        return services;
    }
}
