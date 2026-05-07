// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Configuration;
using Sorcha.ServiceClients.Http.Hub;

namespace Sorcha.Agent.Commands;

/// <summary>
/// The "validate" command — checks actor configuration before running.
/// </summary>
public class ValidateCommand : Command
{
    public ValidateCommand() : base("validate", "Check actor configuration")
    {
        var configOption = new Option<string>("--config")
        {
            Description = "Path to actor definition JSON file",
            Required = true
        };
        var stateOption = new Option<string?>("--state")
        {
            Description = "Path to state.json for placeholder resolution"
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Show detailed check results"
        };

        Options.Add(configOption);
        Options.Add(stateOption);
        Options.Add(verboseOption);

        this.SetAction(async (parseResult, cancellationToken) =>
        {
            var configPath = parseResult.GetValue(configOption)!;
            var statePath = parseResult.GetValue(stateOption);
            var verbose = parseResult.GetValue(verboseOption);

            return await ExecuteAsync(configPath, statePath, verbose, cancellationToken);
        });
    }

    private static async Task<int> ExecuteAsync(
        string configPath,
        string? statePath,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        string? actorName = null;

        // Check 1: JSON schema validation + deserialization
        Console.WriteLine($"Validating actor config...");

        var loadResult = ActorDefinitionLoader.Load(configPath, statePath);

        if (!loadResult.IsSuccess)
        {
            // Distinguish between variable resolution failures and structural failures
            var hasUnresolved = loadResult.Errors.Any(e => e.Contains("Unresolved"));
            var hasStructural = loadResult.Errors.Any(e => !e.Contains("Unresolved"));

            if (hasStructural)
            {
                Console.WriteLine("  [FAIL] JSON schema validation");
                foreach (var error in loadResult.Errors.Where(e => !e.Contains("Unresolved")))
                    Console.WriteLine($"         {error}");
                failed++;
            }
            else
            {
                Console.WriteLine("  [PASS] JSON schema valid");
                passed++;
            }

            if (hasUnresolved)
            {
                Console.WriteLine("  [FAIL] Variable resolution");
                foreach (var error in loadResult.Errors.Where(e => e.Contains("Unresolved")))
                    Console.WriteLine($"         {error}");
                failed++;

                Console.WriteLine("  [SKIP] Authentication (requires resolved variables)");
                Console.WriteLine("  [SKIP] SignalR hub (requires authentication)");
                skipped += 2;

                PrintSummary(passed, failed, skipped);
                return ExitCodes.ConfigurationError;
            }

            PrintSummary(passed, failed, skipped);
            return ExitCodes.ValidationError;
        }

        Console.WriteLine("  [PASS] JSON schema valid");
        passed++;
        Console.WriteLine("  [PASS] Variables resolved");
        passed++;

        var definition = loadResult.Definition!;
        actorName = definition.Actor.Name;
        Console.WriteLine($"  Actor: \"{actorName}\"");

        // Check 3: Credential connectivity
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(definition.Connection.GatewayUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.None);
        });

        var authService = new AgentAuthService(
            httpClient, definition.Connection, loggerFactory.CreateLogger<AgentAuthService>());

        try
        {
            await authService.AuthenticateAsync(cancellationToken);
            Console.WriteLine("  [PASS] Authentication successful");
            passed++;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  [FAIL] Authentication: {ex.Message}");
            failed++;

            Console.WriteLine("  [SKIP] SignalR hub (requires authentication)");
            skipped++;

            PrintSummary(passed, failed, skipped);
            return ExitCodes.AuthenticationError;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] Authentication: {ex.Message}");
            failed++;

            Console.WriteLine("  [SKIP] SignalR hub (requires authentication)");
            skipped++;

            PrintSummary(passed, failed, skipped);
            return ExitCodes.NetworkError;
        }

        // Check 4: SignalR hub reachability
        if (definition.Inbox.SignalR?.Enabled ?? false)
        {
            try
            {
                var hubUrl = $"{definition.Connection.GatewayUrl.TrimEnd('/')}/hubs/blueprint";
                var connection = SorchaHubConnectionBuilder.Build(hubUrl, authService.TokenProviderAsync);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                await connection.StartAsync(cts.Token);
                await connection.DisposeAsync();

                Console.WriteLine("  [PASS] SignalR hub reachable");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [FAIL] SignalR hub: {ex.Message}");
                failed++;
            }
        }
        else
        {
            Console.WriteLine("  [SKIP] SignalR hub (not enabled)");
            skipped++;
        }

        PrintSummary(passed, failed, skipped);
        return failed > 0 ? ExitCodes.ValidationError : ExitCodes.Success;
    }

    private static void PrintSummary(int passed, int failed, int skipped)
    {
        Console.WriteLine();
        if (failed == 0)
        {
            Console.WriteLine("All checks passed.");
        }
        else
        {
            Console.WriteLine($"{failed} check(s) failed, {skipped} skipped.");
        }
    }
}
