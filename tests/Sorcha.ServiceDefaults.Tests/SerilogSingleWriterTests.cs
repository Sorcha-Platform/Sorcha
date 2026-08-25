// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Serilog must be the ONLY writer to stdout, and it must not be the only provider.
/// </summary>
/// <remarks>
/// <para>
/// <c>writeToProviders: true</c> forwards every event to the registered <see cref="ILoggerProvider"/>s,
/// and <see cref="WebApplicationBuilder"/> registers a console provider by default — so each event
/// reached stdout twice, from two writers, as three physical lines. On n1 that made
/// <c>docker logs … | grep -c</c> report double the real event count, which reads as a regression in
/// whatever counter an operator is grepping for.
/// </para>
/// <para>
/// The two assertions are a pair on purpose. Silencing the duplicate is trivially achieved by
/// <c>ClearProviders()</c>, which also removes the OpenTelemetry provider — and nothing would fail:
/// the Aspire dashboard would just stop receiving logs. So "one line" alone is not the property;
/// "one line, and OTel still fed" is.
/// </para>
/// </remarks>
public class SerilogSingleWriterTests
{
    private static (string Console, IReadOnlyList<string> Providers) LogOneEvent(bool withSerilog)
    {
        var original = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.AddServiceDefaults();
            if (withSerilog)
            {
                builder.AddSerilogLogging();
            }

            var app = builder.Build();
            var providers = app.Services.GetServices<ILoggerProvider>()
                .Select(p => p.GetType().Name).ToList();

            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Probe.Category")
                .LogInformation("SENTINEL {Value}", "abc");

            // Deterministic flush — the console processor drains on dispose, so this does not race.
            app.Services.GetRequiredService<ILoggerFactory>().Dispose();
            Serilog.Log.CloseAndFlush();

            return (buffer.ToString(), providers);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static IReadOnlyList<string> SentinelLines(string consoleOutput) =>
        consoleOutput.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Contains("SENTINEL", StringComparison.Ordinal)
                     || l.Contains("Probe.Category", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void OneEvent_ReachesStdoutExactlyOnce()
    {
        var (consoleOutput, _) = LogOneEvent(withSerilog: true);

        var lines = SentinelLines(consoleOutput);

        lines.Should().ContainSingle(
            "one event must produce one line; the default console provider adds a 'info: Category[0]' "
            + "header plus an indented message, so a regression here shows up as 3");
        lines[0].Should().Contain("[").And.Contain("INF] Probe.Category SENTINEL abc",
            "the surviving writer must be Serilog — every runbook and log grep in this repo assumes "
            + "its [HH:mm:ss LVL] Category Message shape");
    }

    [Fact]
    public void TheOpenTelemetryProvider_SurvivesTheDeduplication()
    {
        var (_, providers) = LogOneEvent(withSerilog: true);

        providers.Should().NotContain(nameof(ConsoleLoggerProvider),
            "Serilog owns stdout");
        providers.Should().Contain(p => p.Contains("OpenTelemetry", StringComparison.Ordinal),
            "AddServiceDefaults registers OTel BEFORE AddSerilogLogging runs, so a blunt "
            + "ClearProviders() would take it with the console provider and the Aspire dashboard "
            + "would silently stop receiving logs — which is the whole reason writeToProviders is set");
    }

    [Fact]
    public void AHostThatDoesNotUseSerilog_KeepsItsConsoleProvider()
    {
        // Whichever console is the ONLY one survives. Sorcha.Haip.Service does not call
        // AddSerilogLogging, and must keep printing.
        var (consoleOutput, providers) = LogOneEvent(withSerilog: false);

        providers.Should().Contain(nameof(ConsoleLoggerProvider));
        SentinelLines(consoleOutput).Should().NotBeEmpty(
            "removing the console provider unconditionally would silence such a host entirely");
    }
}
