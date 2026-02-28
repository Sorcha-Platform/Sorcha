// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Abstraction for checking whether a token has been revoked.
/// Register an implementation (e.g., Redis-backed) in DI to enable revocation checking.
/// When no implementation is registered, revocation checking is skipped.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>
    /// Returns true if the token identified by <paramref name="jti"/> has been revoked.
    /// </summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for JWT authentication across all Sorcha services.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Installation name - used to derive issuer and audience if not explicitly set.
    /// This should be unique per deployment (e.g., "localhost", "dev.sorcha.io", "prod.sorcha.io").
    /// </summary>
    public string? InstallationName { get; set; }

    /// <summary>
    /// JWT token issuer (iss claim).
    /// If not explicitly set, derived from InstallationName as "http://{InstallationName}".
    /// The tenant service is the authority that issues tokens with this issuer.
    /// </summary>
    public string Issuer { get; set; } = "https://tenant.sorcha.io";

    /// <summary>
    /// Valid audiences for tokens (aud claim).
    /// If not explicitly set, derived from InstallationName as ["http://{InstallationName}"].
    /// All services in an installation should accept tokens with this audience.
    /// </summary>
    public string[] Audience { get; set; } = ["https://api.sorcha.io"];

    /// <summary>
    /// Signing key for JWT tokens.
    /// If not provided in development, a key will be auto-generated.
    /// Production deployments MUST provide this via environment variable or Azure Key Vault.
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
    /// Clock skew tolerance for token validation in minutes.
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
/// Extension methods for configuring JWT authentication in Sorcha services.
/// Provides automatic key generation for development environments.
/// </summary>
public static class JwtAuthenticationExtensions
{
    // Development key file location - stored in user's local app data
    private static readonly string DevKeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sorcha",
        "dev-jwt-signing-key.txt");

    /// <summary>
    /// Adds JWT Bearer authentication with automatic key management.
    /// In Development: Auto-generates a signing key that's shared across services.
    /// In Production: Requires explicit configuration via environment variables or Azure Key Vault.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddJwtAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // Get installation name first (if provided)
        var installationName = configuration["JwtSettings:InstallationName"];

        // Apply installation name-based defaults if InstallationName is provided
        // and explicit Issuer/Audience are not set
        var issuerFromConfig = configuration["JwtSettings:Issuer"];
        var audienceFromConfig = configuration["JwtSettings:Audience:0"];

        // Create JWT settings with installation name-based defaults if applicable
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

        // Override with installation name-based values if:
        // 1. Installation name is provided
        // 2. No explicit issuer or audience configuration exists
        if (!string.IsNullOrWhiteSpace(installationName))
        {
            if (string.IsNullOrWhiteSpace(issuerFromConfig))
            {
                jwtSettings.Issuer = $"http://{installationName}";
            }

            if (string.IsNullOrWhiteSpace(audienceFromConfig))
            {
                jwtSettings.Audience = [$"http://{installationName}"];
            }
        }

        // Get or generate signing key
        var signingKey = GetOrGenerateSigningKey(configuration, environment.IsDevelopment());

        if (string.IsNullOrEmpty(signingKey))
        {
            throw new InvalidOperationException(
                "JWT SigningKey is required. In Development, a key is auto-generated. " +
                "In Production, set JwtSettings:SigningKey via environment variable or Azure Key Vault.");
        }

        // Register JwtSettings as a singleton for services that need to issue tokens
        builder.Services.AddSingleton(new JwtSettings
        {
            InstallationName = jwtSettings.InstallationName,
            Issuer = jwtSettings.Issuer,
            Audience = jwtSettings.Audience,
            SigningKey = signingKey,
            AccessTokenLifetimeMinutes = jwtSettings.AccessTokenLifetimeMinutes,
            RefreshTokenLifetimeHours = jwtSettings.RefreshTokenLifetimeHours,
            ServiceTokenLifetimeHours = jwtSettings.ServiceTokenLifetimeHours,
            ClockSkewMinutes = jwtSettings.ClockSkewMinutes,
            ValidateIssuer = jwtSettings.ValidateIssuer,
            ValidateAudience = jwtSettings.ValidateAudience,
            ValidateIssuerSigningKey = jwtSettings.ValidateIssuerSigningKey,
            ValidateLifetime = jwtSettings.ValidateLifetime
        });

        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        if (keyBytes.Length < 32)
        {
            Array.Resize(ref keyBytes, 32);
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = jwtSettings.ValidateIssuer,
                    ValidIssuer = jwtSettings.Issuer,

                    ValidateAudience = jwtSettings.ValidateAudience,
                    ValidAudiences = jwtSettings.Audience,

                    ValidateIssuerSigningKey = jwtSettings.ValidateIssuerSigningKey,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

                    ValidateLifetime = jwtSettings.ValidateLifetime,
                    ClockSkew = TimeSpan.FromMinutes(jwtSettings.ClockSkewMinutes),

                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },

                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };

                options.Events = new JwtBearerEvents
                {
                    // Handle SignalR WebSocket/SSE connections where token is in query string
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        // If the request is for a SignalR hub and has a token in the query string
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) &&
                            (path.StartsWithSegments("/hubs") ||
                             path.StartsWithSegments("/hub") ||
                             path.StartsWithSegments("/actionshub")))
                        {
                            // Extract the token from query string for SignalR
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },

                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtAuthentication");

                        logger.LogWarning(
                            "JWT authentication failed on {RequestPath}: {FailureReason} at {Timestamp}",
                            context.HttpContext.Request.Path,
                            context.Exception.Message,
                            DateTimeOffset.UtcNow);

                        return Task.CompletedTask;
                    },

                    OnTokenValidated = async context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtAuthentication");

                        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        var orgId = context.Principal?.FindFirst("org_id")?.Value;

                        // Check token revocation if a revocation store is registered (FR-006)
                        var revocationStore = context.HttpContext.RequestServices
                            .GetService<ITokenRevocationStore>();
                        if (revocationStore is not null)
                        {
                            var jti = context.Principal?.FindFirst("jti")?.Value;
                            if (!string.IsNullOrEmpty(jti))
                            {
                                try
                                {
                                    if (await revocationStore.IsRevokedAsync(jti, context.HttpContext.RequestAborted))
                                    {
                                        logger.LogWarning(
                                            "Token revoked on {RequestPath} for user {UserId} (jti: {Jti}) at {Timestamp}",
                                            context.HttpContext.Request.Path, userId, jti, DateTimeOffset.UtcNow);
                                        context.Fail("Token has been revoked");
                                        return;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Revocation check failure is non-fatal — allow the request
                                    logger.LogWarning(ex,
                                        "Revocation check failed for jti {Jti}, allowing request",
                                        jti);
                                }
                            }
                        }

                        logger.LogDebug(
                            "Token validated for user {UserId}, org {OrgId}",
                            userId, orgId);
                    }
                };
            });

        return builder;
    }

    /// <summary>
    /// Gets the signing key from configuration, environment, or generates one for development/testing.
    /// </summary>
    private static string? GetOrGenerateSigningKey(IConfiguration configuration, bool isDevelopment)
    {
        // Priority 1: Environment variable (highest priority for all environments)
        var envKey = Environment.GetEnvironmentVariable("JWTSETTINGS__SIGNINGKEY")
                  ?? Environment.GetEnvironmentVariable("JwtSettings__SigningKey");
        if (!string.IsNullOrEmpty(envKey))
        {
            return envKey;
        }

        // Priority 2: Configuration (appsettings, user secrets, etc.)
        var configKey = configuration["JwtSettings:SigningKey"];
        if (!string.IsNullOrEmpty(configKey))
        {
            return configKey;
        }

        // Priority 3: Auto-generate for development, testing, or when explicitly allowed
        // Detect test environment: check if WebApplicationFactory or test host is being used
        var isTestEnvironment = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name?.Contains("Microsoft.AspNetCore.Mvc.Testing") == true
                   || a.GetName().Name?.Contains("xunit") == true
                   || a.GetName().Name?.Contains("testhost") == true);

        // Also allow auto-generation via configuration (useful for CI/CD)
        var allowAutoGenerate = configuration.GetValue<bool>("JwtSettings:AllowAutoGenerateKey");

        if (isDevelopment || isTestEnvironment || allowAutoGenerate)
        {
            return GetOrCreateDevelopmentKey();
        }

        // Production without a key - will throw in calling code
        return null;
    }

    /// <summary>
    /// Gets or creates a development signing key that persists across service restarts.
    /// The key is stored in the user's local application data folder.
    /// </summary>
    private static string GetOrCreateDevelopmentKey()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Sorcha.Jwt");

        try
        {
            // Check if we already have a key
            if (File.Exists(DevKeyFilePath))
            {
                var existingKey = File.ReadAllText(DevKeyFilePath).Trim();
                if (!string.IsNullOrEmpty(existingKey) && existingKey.Length >= 32)
                {
                    return existingKey;
                }
            }

            // Generate a new secure key
            var keyBytes = new byte[32]; // 256 bits
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }
            var newKey = Convert.ToBase64String(keyBytes);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(DevKeyFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save the key
            File.WriteAllText(DevKeyFilePath, newKey);

            logger.LogInformation("Generated new development signing key at: {DevKeyFilePath}", DevKeyFilePath);

            return newKey;
        }
        catch (Exception ex)
        {
            // If we can't persist the key, generate one in memory
            // This means services might have different keys if started at different times
            logger.LogWarning(ex, "Could not persist development key — generating in-memory key. Services may not share tokens");

            var keyBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }
            return Convert.ToBase64String(keyBytes);
        }
    }

    /// <summary>
    /// Gets the symmetric security key for signing tokens.
    /// Use this in services that need to issue JWT tokens (e.g., Tenant Service).
    /// </summary>
    public static SymmetricSecurityKey GetSigningKey(this JwtSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SigningKey))
        {
            throw new InvalidOperationException("JWT SigningKey is not configured.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(settings.SigningKey);
        if (keyBytes.Length < 32)
        {
            Array.Resize(ref keyBytes, 32);
        }

        return new SymmetricSecurityKey(keyBytes);
    }

    /// <summary>
    /// Gets the signing credentials for creating JWT tokens.
    /// Use this in services that need to issue JWT tokens (e.g., Tenant Service).
    /// </summary>
    public static SigningCredentials GetSigningCredentials(this JwtSettings settings)
    {
        return new SigningCredentials(settings.GetSigningKey(), SecurityAlgorithms.HmacSha256);
    }
}
