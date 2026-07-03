// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring CORS policies across Sorcha services.
/// </summary>
public static class CorsExtensions
{
    /// <summary>
    /// Registers the shared Sorcha CORS default policy (SEC-001).
    /// <para>
    /// <b>Trust boundary:</b> in the standard deployment services are not directly exposed — the API
    /// Gateway (YARP) is the sole CORS enforcement point and each service port is reachable only
    /// in-cluster. In that model this policy is permissive (any origin/method/header) by design.
    /// </para>
    /// <para>
    /// <b>Defence-in-depth:</b> set <c>Cors:AllowedOrigins</c> (a string array in
    /// <c>appsettings.{Environment}.json</c>) to switch the service to an explicit origin allow-list —
    /// a second line of defence if a service port is ever exposed directly to the internet. With an
    /// allow-list, credentials are also permitted (safe only with explicit origins; the CORS spec
    /// forbids credentials alongside <c>AllowAnyOrigin</c>).
    /// </para>
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddSorchaCors<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                }
                else
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                }
            });
        });

        return builder;
    }
}
