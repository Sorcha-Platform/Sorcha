// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Schema management commands.
/// </summary>
public class SchemaCommand : Command
{
    public SchemaCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("schema", "Schema management operations")
    {
        Subcommands.Add(new SchemaProvidersCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Schema provider management commands.
/// </summary>
public class SchemaProvidersCommand : Command
{
    public SchemaProvidersCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("providers", "Manage schema providers")
    {
        Subcommands.Add(new SchemaProvidersListCommand(clientFactory, authService, configService));
        Subcommands.Add(new SchemaProvidersRefreshCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all schema providers.
/// </summary>
public class SchemaProvidersListCommand : Command
{
    public SchemaProvidersListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all schema providers")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);
                var providers = await client.GetSchemaProvidersAsync($"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(providers, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (providers == null || providers.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No schema providers found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {providers.Count} schema provider(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Name",-25} {"Status",-12} {"Schemas",8} {"Type",-10} {"Last Fetch",-20}");
                Console.WriteLine(new string('-', 80));

                foreach (var provider in providers)
                {
                    var statusColor = provider.HealthStatus.ToLowerInvariant() switch
                    {
                        "healthy" => ConsoleColor.Green,
                        "degraded" => ConsoleColor.Yellow,
                        _ => ConsoleColor.Red
                    };

                    var lastFetch = provider.LastSuccessfulFetch.HasValue
                        ? provider.LastSuccessfulFetch.Value.ToString("yyyy-MM-dd HH:mm")
                        : "Never";

                    var originalColor = Console.ForegroundColor;
                    Console.Write($"{provider.ProviderName,-25} ");
                    Console.ForegroundColor = statusColor;
                    Console.Write($"{provider.HealthStatus,-12}");
                    Console.ForegroundColor = originalColor;
                    Console.WriteLine($" {provider.SchemaCount,8} {provider.ProviderType,-10} {lastFetch,-20}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach API Gateway: {ex.Message}");
                ConsoleHelper.WriteInfo("Ensure the services are running. Try 'docker-compose up -d'.");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list schema providers: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Refreshes a schema provider by name.
/// </summary>
public class SchemaProvidersRefreshCommand : Command
{
    private readonly Option<string> _nameOption;

    public SchemaProvidersRefreshCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("refresh", "Refresh a schema provider")
    {
        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Name of the schema provider to refresh",
            Required = true
        };

        Options.Add(_nameOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);
                var provider = await client.RefreshSchemaProviderAsync(name, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Refresh triggered for '{name}'.");
                Console.WriteLine();
                Console.WriteLine($"  Name:         {provider.ProviderName}");
                Console.WriteLine($"  Status:       {provider.HealthStatus}");
                Console.WriteLine($"  Schemas:      {provider.SchemaCount}");
                Console.WriteLine($"  Type:         {provider.ProviderType}");
                Console.WriteLine($"  Last Fetch:   {(provider.LastSuccessfulFetch.HasValue ? provider.LastSuccessfulFetch.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Provider '{name}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to refresh schema providers.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach API Gateway: {ex.Message}");
                ConsoleHelper.WriteInfo("Ensure the services are running. Try 'docker-compose up -d'.");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to refresh schema provider: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
