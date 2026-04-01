// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Reflection;
using Spectre.Console;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Branding;

/// <summary>
/// ASCII art banner and version display for the Sorcha CLI.
/// </summary>
public static class SorchaBanner
{
    private const string Banner = """
      ____                 _
     / ___|  ___  _ __ ___| |__   __ _
     \___ \ / _ \| '__/ __| '_ \ / _` |
      ___) | (_) | | | (__| | | | (_| |
     |____/ \___/|_|  \___|_| |_|\__,_|
    """;

    /// <summary>
    /// Renders the branded banner with version and auth status.
    /// </summary>
    public static void Render(IConfigurationService? configService = null)
    {
        AnsiConsole.MarkupLine("[bold blue]" + Banner.Replace("[", "[[").Replace("]", "]]") + "[/]");

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var infoVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        AnsiConsole.MarkupLine($"  [dim]Distributed Ledger Platform[/]  [green]v{infoVersion ?? version?.ToString() ?? "1.0.0"}[/]");

        if (configService != null)
        {
            try
            {
                var profile = configService.GetActiveProfileAsync().GetAwaiter().GetResult();
                if (profile != null)
                {
                    AnsiConsole.MarkupLine($"  [dim]Profile:[/] [yellow]{profile.Name}[/]  [dim]API:[/] [yellow]{profile.ServiceUrl ?? "not configured"}[/]");
                }
            }
            catch
            {
                // Silently ignore config errors in banner
            }
        }

        AnsiConsole.WriteLine();
    }
}
