// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
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
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddSorchaOpenApi<TBuilder>(this TBuilder builder, string title, string description)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = title;
                document.Info.Version = "1.0.0";
                document.Info.Description = description;

                if (document.Info.Contact == null)
                {
                    document.Info.Contact = new();
                }
                document.Info.Contact.Name = "Sorcha Contributors";
                document.Info.Contact.Url = new Uri("https://github.com/sorcha-platform/sorcha");

                if (document.Info.License == null)
                {
                    document.Info.License = new();
                }
                document.Info.License.Name = "MIT License";
                document.Info.License.Url = new Uri("https://opensource.org/licenses/MIT");

                return Task.CompletedTask;
            });
        });

        return builder;
    }

    /// <summary>
    /// Maps the OpenAPI endpoint and Scalar interactive API documentation UI (development only).
    /// Both the raw OpenAPI JSON and the Scalar UI are restricted to the development environment.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="title">The title displayed in the Scalar UI.</param>
    /// <param name="theme">The Scalar UI theme. Defaults to <see cref="ScalarTheme.Purple"/>.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapSorchaOpenApiUi(this WebApplication app, string title, ScalarTheme theme = ScalarTheme.Purple)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle(title)
                    .WithTheme(theme)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            });
        }

        return app;
    }
}
