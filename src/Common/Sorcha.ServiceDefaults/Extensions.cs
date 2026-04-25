// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Helpers;
using Sorcha.ServiceDefaults.Storage;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Adds common .NET Aspire service defaults: OpenTelemetry, health checks, service discovery, and HTTP resilience.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        builder.Services.AddStorageRegistration();

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry with logging, metrics, and tracing for ASP.NET Core, HTTP, and runtime instrumentation.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                // Add custom meters for Peer Service
                metrics.AddMeter("Sorcha.Peer.Service");

                // Feature 111 — Blueprint Service presentation lifecycle metrics
                metrics.AddMeter("Sorcha.Blueprint.Service.Presentation");

                // Feature 113 — Storage provider registration audit
                metrics.AddMeter(Sorcha.ServiceDefaults.Storage.StorageRegistrationMetrics.MeterName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();

                // Add custom activity sources for Peer Service
                tracing.AddSource("Sorcha.Peer.Service");
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds default health checks including a self-check tagged as "live" for liveness probes.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps health check endpoints: <c>/health</c> for readiness and <c>/alive</c> for liveness probes.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints are required for production monitoring and orchestration.
        // Security should be handled at the network level (firewall, ingress rules, etc.)
        // See https://aka.ms/dotnet/aspire/healthchecks for details.

        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }

    /// <summary>
    /// Adds OWASP-recommended security headers to all HTTP responses.
    /// Implements SEC-004 security hardening requirements.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // Prevent clickjacking attacks
            context.Response.Headers["X-Frame-Options"] = "DENY";

            // Prevent MIME type sniffing
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // Enable XSS filter
            context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

            // Referrer policy - only send origin for cross-origin requests
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Content Security Policy - strict default with allowances for APIs
            // Note: Adjust this CSP based on your specific needs (especially for UI apps)
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self'; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'";

            // Permissions Policy - restrict browser features
            context.Response.Headers["Permissions-Policy"] =
                "geolocation=(), " +
                "microphone=(), " +
                "camera=(), " +
                "payment=(), " +
                "usb=(), " +
                "magnetometer=(), " +
                "gyroscope=(), " +
                "accelerometer=()";

            await next();
        });

        return app;
    }

    /// <summary>
    /// Enables HTTPS enforcement including HSTS header and HTTPS redirection.
    /// Implements SEC-001 HTTPS enforcement requirements.
    /// HTTPS enforcement is only applied in production to avoid certificate issues in development.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <param name="forceInDevelopment">Force HTTPS in development (default: false)</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseHttpsEnforcement(this WebApplication app, bool forceInDevelopment = false)
    {
        // Only enable HTTPS enforcement in production environments to prevent certificate issues in development/Docker
        if (!app.Environment.IsDevelopment() || forceInDevelopment)
        {
            // HSTS (HTTP Strict-Transport-Security)
            // max-age: 1 year, includeSubDomains, preload for submission to browser preload lists
            app.Use(async (context, next) =>
            {
                context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
                await next();
            });

            // Enable HTTPS redirection only when HTTPS is configured
            app.UseHttpsRedirection();
        }

        return app;
    }

    /// <summary>
    /// Adds security headers optimized for API services (less restrictive CSP).
    /// Use this for services that don't serve HTML/UI content.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseApiSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // Prevent clickjacking attacks
            context.Response.Headers["X-Frame-Options"] = "DENY";

            // Prevent MIME type sniffing
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // Referrer policy
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            // Check if this is a path that needs relaxed CSP (UI apps, documentation, landing page)
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/design", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/app", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/explorer", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/not-found", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/manifest.json", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/icon-", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                // UI apps (Blazor WASM, Scalar) require scripts and styles to function
                // Blazor WebAssembly specifically needs 'unsafe-eval' for .NET runtime
                // Allow connections to localhost on any port for Aspire development scenarios
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' 'unsafe-inline' 'unsafe-eval' blob:; " +
                    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                    "img-src 'self' data: https:; " +
                    "font-src 'self' data: https://fonts.gstatic.com; " +
                    "connect-src 'self' https://localhost:* http://localhost:* wss://localhost:* ws://localhost:* https://www.schemastore.org https://json.schemastore.org; " +
                    "worker-src 'self' blob:; " +
                    "manifest-src 'self'; " +
                    "frame-ancestors 'none'";
            }
            else
            {
                // API-optimized CSP (no script/style restrictions)
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; frame-ancestors 'none'";
            }

            // Permissions Policy - restrict all browser features for APIs
            context.Response.Headers["Permissions-Policy"] =
                "geolocation=(), " +
                "microphone=(), " +
                "camera=(), " +
                "payment=(), " +
                "usb=(), " +
                "magnetometer=(), " +
                "gyroscope=(), " +
                "accelerometer=()";

            await next();
        });

        return app;
    }

    /// <summary>
    /// Adds rate limiting services with all standard policies driven by <see cref="RateLimitSettings"/>.
    /// Bind the "RateLimiting" section of appsettings.json to override defaults.
    /// Default values are very relaxed for pre-release development; tighten in production config.
    /// Implements SEC-002 API rate limiting requirements.
    /// </summary>
    /// <param name="builder">The host application builder</param>
    /// <param name="configure">Optional configuration action applied after standard policies</param>
    /// <returns>The builder for chaining</returns>
    public static TBuilder AddRateLimiting<TBuilder>(
        this TBuilder builder,
        Action<RateLimiterOptions>? configure = null) where TBuilder : IHostApplicationBuilder
    {
        // Bind settings from configuration — falls back to coded defaults if section is absent
        var settings = new RateLimitSettings();
        builder.Configuration.GetSection(RateLimitSettings.SectionName).Bind(settings);

        // Register with startup validation so misconfiguration fails fast
        builder.Services.AddOptions<RateLimitSettings>()
            .BindConfiguration(RateLimitSettings.SectionName)
            .Validate(s =>
                s.ApiPermitLimit > 0
                && s.AuthenticationPermitLimit > 0
                && s.StrictTokenLimit > 0
                && s.StrictTokensPerPeriod > 0
                && s.StrictReplenishmentPeriodSeconds > 0
                && s.HeavyPermitLimit > 0
                && s.RelaxedPermitLimit > 0
                && s.TotpPermitLimit > 0
                && s.PlatformAuthPermitLimit > 0
                && s.McpPerUserRequestsPerMinute > 0
                && s.McpPerTenantRequestsPerMinute > 0
                && s.McpAdminToolsRequestsPerMinute > 0
                && s.NotificationRealTimePerMinute > 0,
                "All RateLimiting permit/token limits must be positive")
            .ValidateOnStart();

        builder.Services.AddRateLimiter(options =>
        {
            // Default rejection status code
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Add response headers for rate limit info
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers["Retry-After"] = "60";
                context.HttpContext.Response.Headers["X-RateLimit-Policy"] = context.Lease.TryGetMetadata(
                    MetadataName.ReasonPhrase, out var reason) ? reason : "rate_limit_exceeded";

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers["Retry-After"] = ((int)retryAfter.TotalSeconds).ToString();
                }

                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many requests\",\"message\":\"Rate limit exceeded. Please try again later.\"}",
                    cancellationToken);
            };

            // Default API policy: Fixed window per IP
            options.AddPolicy(RateLimitPolicies.Api, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.ApiPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.ApiQueueLimit
                });
            });

            // Authentication policy: Sliding window per IP
            options.AddPolicy(RateLimitPolicies.Authentication, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetSlidingWindowLimiter(clientIp, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = settings.AuthenticationPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6, // 10-second segments
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.AuthenticationQueueLimit
                });
            });

            // Strict policy: Token bucket per IP
            options.AddPolicy(RateLimitPolicies.Strict, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetTokenBucketLimiter(clientIp, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.StrictTokenLimit,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(settings.StrictReplenishmentPeriodSeconds),
                    TokensPerPeriod = settings.StrictTokensPerPeriod,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.StrictQueueLimit
                });
            });

            // Heavy operations policy: Concurrency limiter (global)
            options.AddPolicy(RateLimitPolicies.HeavyOperations, _ =>
            {
                return RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = settings.HeavyPermitLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.HeavyQueueLimit
                });
            });

            // Relaxed policy: Fixed window per IP (health checks, metrics)
            options.AddPolicy(RateLimitPolicies.Relaxed, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.RelaxedPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.RelaxedQueueLimit
                });
            });

            // TOTP validation policy: Fixed window per IP
            options.AddPolicy(RateLimitPolicies.TotpValidation, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.TotpPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.TotpQueueLimit
                });
            });

            // Platform auth policy: Fixed window per IP (social login, registration, passkeys)
            options.AddPolicy(RateLimitPolicies.PlatformAuth, context =>
            {
                var clientIp = GetClientIdentifier(context);
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PlatformAuthPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = settings.PlatformAuthQueueLimit
                });
            });

            // Apply custom configuration if provided
            configure?.Invoke(options);
        });

        return builder;
    }

    /// <summary>
    /// Applies the rate limiting middleware with default API policy.
    /// Must be called after UseRouting() and before UseEndpoints().
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseRateLimiting(this WebApplication app)
    {
        app.UseRateLimiter();
        return app;
    }

    /// <summary>
    /// Gets a client identifier for rate limiting partitioning.
    /// Uses X-Forwarded-For header if behind a proxy, otherwise uses remote IP.
    /// </summary>
    private static string GetClientIdentifier(HttpContext context) =>
        ClientIpHelper.GetClientIp(context);

    /// <summary>
    /// Adds input validation services with configurable options.
    /// Implements SEC-003 OWASP input validation requirements.
    /// </summary>
    /// <param name="builder">The host application builder</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The builder for chaining</returns>
    public static TBuilder AddInputValidation<TBuilder>(
        this TBuilder builder,
        Action<InputValidationOptions>? configure = null) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<InputValidationOptions>(options =>
        {
            configure?.Invoke(options);
        });

        return builder;
    }

    /// <summary>
    /// Applies the input validation middleware for OWASP protection.
    /// Should be called early in the pipeline, after UseRouting but before other middleware.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <returns>The web application for chaining</returns>
    public static WebApplication UseInputValidation(this WebApplication app)
    {
        app.UseMiddleware<InputValidationMiddleware>();
        return app;
    }
}

/// <summary>
/// Well-known rate limiting policy names (SEC-002).
/// All limits are driven by <see cref="RateLimitSettings"/> — override via appsettings.json.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Default API policy: fixed window per IP.</summary>
    public const string Api = "api";

    /// <summary>Authentication policy: sliding window per IP (login, token, password reset).</summary>
    public const string Authentication = "authentication";

    /// <summary>Strict policy: token bucket per IP (wallet operations, sensitive endpoints).</summary>
    public const string Strict = "strict";

    /// <summary>Heavy operations policy: concurrency limiter (bulk imports, file processing).</summary>
    public const string HeavyOperations = "heavy";

    /// <summary>Relaxed policy: fixed window per IP (health checks, metrics).</summary>
    public const string Relaxed = "relaxed";

    /// <summary>TOTP/2FA validation policy: fixed window per IP.</summary>
    public const string TotpValidation = "totp-validate";

    /// <summary>Platform auth policy: fixed window per IP (social login, registration, passkeys).</summary>
    public const string PlatformAuth = "platform-auth";
}
