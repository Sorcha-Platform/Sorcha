// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using Sorcha.Agent.Commands;

namespace Sorcha.Agent;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var rootCommand = new RootCommand("Sorcha Agent - Autonomous actor for walkthrough execution");
            rootCommand.Subcommands.Add(new RunCommand());
            rootCommand.Subcommands.Add(new ValidateCommand());

            return await rootCommand.Parse(args).InvokeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            if (args.Contains("--verbose"))
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return ExitCodes.GeneralError;
        }
    }
}
