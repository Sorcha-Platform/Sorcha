// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;

// Placed in Microsoft.Extensions.Hosting so callers get these extension methods automatically
// without needing an additional using directive — a standard pattern for service-defaults libraries.
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Extension methods for configuring OpenAPI document generation and Scalar interactive API documentation
/// across all Sorcha services with consistent metadata and theming.
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>
    /// Registers OpenAPI document generation with standard Sorcha metadata (contact, license, version).
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="title">The API title displayed in the OpenAPI document.</param>
    /// <param name="description">The API description displayed in the OpenAPI document.</param>
    /// <param name="configureOptions">Optional callback to configure additional OpenAPI options such as operation transformers.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddSorchaOpenApi<TBuilder>(this TBuilder builder, string title, string description,
        Action<Microsoft.AspNetCore.OpenApi.OpenApiOptions>? configureOptions = null)
        where TBuilder : IHostApplicationBuilder
    {
        var version = ResolveAssemblyInformationalVersion();

        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = title;
                document.Info.Version = version;
                document.Info.Description = description;

                if (document.Info.Contact == null)
                {
                    document.Info.Contact = new();
                }
                document.Info.Contact.Name = "Sorcha Contributors";
                document.Info.Contact.Url = new Uri("https://github.com/Sorcha-Platform/Sorcha");

                if (document.Info.License == null)
                {
                    document.Info.License = new();
                }
                document.Info.License.Name = "MIT License";
                document.Info.License.Url = new Uri("https://opensource.org/licenses/MIT");

                return Task.CompletedTask;
            });

            // Spec 117 FR-010 — register the x-status operation transformer for every service
            // so any endpoint marked with [OpenApiStatus("partial")] surfaces in the served document.
            options.AddOperationTransformer<OpenApiStatusOperationTransformer>();

            // Apply any additional configuration (e.g., operation transformers for examples)
            configureOptions?.Invoke(options);
        });

        return builder;
    }

    /// <summary>
    /// Returns the entry assembly's informational version, stripped of any "+commitHash" suffix.
    /// Falls back to "1.0.0" when no entry assembly or attribute is available (rare; e.g. unit-test hosts).
    /// Single source of truth for FR-046 — both OpenAPI info.version and MCP manifest version must read from this.
    /// </summary>
    private static string ResolveAssemblyInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        var plusIdx = raw.IndexOf('+');
        return plusIdx > 0 ? raw[..plusIdx] : raw;
    }

    /// <summary>
    /// Maps the OpenAPI endpoint and Scalar interactive API documentation UI with optional custom routing and access control.
    /// Scalar is enabled in Development environment or when <c>OpenApi:ExposeScalar</c>
    /// configuration is set to <c>true</c>, allowing production deployments to opt-in.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="title">The title displayed in the Scalar UI.</param>
    /// <param name="routePattern">Custom Scalar UI route pattern. Defaults to <c>/scalar/{documentName}</c>.</param>
    /// <param name="openApiRoutePattern">Custom OpenAPI spec route for Scalar to load. When null, uses the default spec endpoint.</param>
    /// <param name="authorizationPolicy">Optional authorization policy name to apply to the Scalar endpoint.</param>
    /// <param name="theme">The Scalar UI theme. Defaults to <see cref="ScalarTheme.Purple"/>.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapSorchaOpenApiUi(
        this WebApplication app,
        string title,
        string? routePattern = null,
        string? openApiRoutePattern = null,
        string? authorizationPolicy = null,
        ScalarTheme theme = ScalarTheme.Purple)
    {
        app.MapOpenApi();

        var exposeScalar = app.Configuration.GetValue<bool>("OpenApi:ExposeScalar", false);

        if (app.Environment.IsDevelopment() || exposeScalar)
        {
            IEndpointConventionBuilder scalarEndpoint;

            if (routePattern is not null)
            {
                scalarEndpoint = app.MapScalarApiReference(routePattern, options =>
                {
                    options
                        .WithTitle(title)
                        .WithTheme(theme)
                        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);

                    if (openApiRoutePattern is not null)
                    {
                        options.WithOpenApiRoutePattern(openApiRoutePattern);
                    }
                });
            }
            else
            {
                scalarEndpoint = app.MapScalarApiReference(options =>
                {
                    options
                        .WithTitle(title)
                        .WithTheme(theme)
                        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);

                    if (openApiRoutePattern is not null)
                    {
                        options.WithOpenApiRoutePattern(openApiRoutePattern);
                    }
                });
            }

            if (authorizationPolicy is not null)
            {
                scalarEndpoint.RequireAuthorization(authorizationPolicy);
            }
        }

        return app;
    }
}
