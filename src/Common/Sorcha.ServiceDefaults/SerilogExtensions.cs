// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
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
    /// <para>
    /// This method requires <see cref="WebApplicationBuilder"/> specifically because
    /// Serilog's <c>UseSerilog</c> extension targets <see cref="IHostBuilder"/>,
    /// which is only accessible via <see cref="WebApplicationBuilder.Host"/>.
    /// </para>
    /// <para>
    /// <b>Serilog owns stdout, so the default console provider is removed first.</b>
    /// <see cref="WebApplicationBuilder"/> registers <c>ConsoleLoggerProvider</c> by default, and
    /// <c>writeToProviders: true</c> forwards every event to the registered providers — so each
    /// event was written to stdout TWICE, by two writers: <c>ConsoleLoggerProvider</c>'s two lines
    /// (a <c>info: Category[0]</c> header plus an indented message with quoted structured values)
    /// and Serilog's single <c>[HH:mm:ss LVL] Category Message</c> line. Three physical lines per
    /// event, interleaved non-deterministically because two writers race for the stream.
    /// </para>
    /// <para>
    /// That made <c>docker logs … | grep -c</c> report double the real event count — which reads as
    /// a regression in any counter an operator greps for — and it multiplied log volume on nodes
    /// whose Docker <c>json-file</c> driver has no rotation configured.
    /// </para>
    /// </remarks>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        RemoveDefaultConsoleLoggerProvider(builder.Services);

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
    /// Drops the default <c>ConsoleLoggerProvider</c> registration so Serilog is the only writer to
    /// stdout, while every other provider — crucially OpenTelemetry — keeps receiving events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Removing the registration is the only thing that works, and that was established by
    /// running it, not by reading the API.</b> The obvious spelling,
    /// <c>builder.Logging.AddFilter&lt;ConsoleLoggerProvider&gt;(null, LogLevel.None)</c>, has
    /// <i>no effect</i>: with <c>writeToProviders: true</c> Serilog fans out to the providers
    /// itself, so MEL's filter pipeline — where <c>AddFilter</c> rules are applied — is never
    /// consulted. A probe over all three candidates measured 3 lines as-is, 3 lines with the filter,
    /// and 1 line with the removal.
    /// </para>
    /// <para>
    /// <b>And <c>builder.Logging.ClearProviders()</c> is NOT an acceptable substitute here.</b>
    /// Every Sorcha service calls <c>AddServiceDefaults()</c> — which registers the OpenTelemetry
    /// logging provider — BEFORE <c>AddSerilogLogging()</c>, so clearing would silently take OTel
    /// with it and the Aspire dashboard would simply stop receiving logs, with nothing failing.
    /// That is the entire reason <c>writeToProviders: true</c> is set.
    /// </para>
    /// <para>
    /// A host that does not call this method keeps its console provider, which is correct: whichever
    /// console is the only one survives. <c>Sorcha.Haip.Service</c> is currently such a host.
    /// </para>
    /// </remarks>
    private static void RemoveDefaultConsoleLoggerProvider(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(ILoggerProvider)
                && descriptor.ImplementationType == typeof(ConsoleLoggerProvider))
            {
                services.RemoveAt(i);
            }
        }
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
