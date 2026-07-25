// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Spectre.Console;
using Sorcha.Cli.Branding;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Getting-started guide that walks through auth, org setup, and first register creation.
/// </summary>
public class HelpCommand : BaseCommand
{
    private readonly IConfigurationService _configService;

    public HelpCommand(IConfigurationService configService)
        : base("help", "Getting started guide for the Sorcha CLI")
    {
        _configService = configService;
    }

    protected override Task<int> ExecuteAsync(CommandContext context)
    {
        SorchaBanner.Render(_configService);

        AnsiConsole.MarkupLine("[bold underline]Getting Started with Sorcha CLI[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]1. Configure your profile[/]");
        AnsiConsole.MarkupLine("   [dim]Set the API endpoint for your Sorcha deployment:[/]");
        AnsiConsole.MarkupLine("   sorcha config set api-url https://your-deployment.example.com");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]2. Authenticate[/]");
        AnsiConsole.MarkupLine("   [dim]Log in with your credentials:[/]");
        AnsiConsole.MarkupLine("   sorcha auth login --username admin@example.com --password <password>");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]3. List your organizations[/]");
        AnsiConsole.MarkupLine("   sorcha org list");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]4. Create a wallet[/]");
        AnsiConsole.MarkupLine("   [dim]Create an ED25519 signing wallet:[/]");
        AnsiConsole.MarkupLine("   sorcha wallet create --name \"My Wallet\" --algorithm ED25519");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]5. Create a register[/]");
        AnsiConsole.MarkupLine("   [dim]Create a new register with a blueprint:[/]");
        AnsiConsole.MarkupLine("   sorcha register create --name \"My Register\" --owner-wallet <wallet-addr>");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold yellow]6. Submit a transaction[/]");
        AnsiConsole.MarkupLine("   sorcha tx submit --register-id <id> --wallet <addr> --type <type> --payload '{\"key\":\"value\"}' --signature <sig>");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[dim]Use --help on any command for detailed usage. Use --output json|csv|yaml to change format.[/]");

        return Task.FromResult(ExitCodes.Success);
    }
}
