// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Shared Serilog configuration for all Sorcha services.
/// Enriches logs with machine name, thread ID, and application name,
/// then forwards through OpenTelemetry to Aspire Dashboard.
/// </summary>
public static class SerilogExtensions
{
    /// <summary>
    /// Configures Serilog as the logging provider with structured enrichment.
    /// Uses <c>writeToProviders: true</c> to preserve the OpenTelemetry logging provider,
    /// so enriched logs flow through both Console and OTLP.
    /// </summary>
    /// <remarks>
    /// This method requires <see cref="WebApplicationBuilder"/> specifically because
    /// Serilog's <c>UseSerilog</c> extension targets <see cref="IHostBuilder"/>,
    /// which is only accessible via <see cref="WebApplicationBuilder.Host"/>.
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, config) => config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"),
            writeToProviders: true);

        return builder;
    }

    /// <summary>
    /// Adds Serilog HTTP request logging middleware with enriched diagnostic context.
    /// </summary>
    public static WebApplication UseSerilogLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("RemoteIpAddress",
                    httpContext.Connection.RemoteIpAddress?.ToString());
            };
        });

        return app;
    }
}
