// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Platform management commands (system admin only).
/// </summary>
public class PlatformCommand : Command
{
    public PlatformCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("platform", "Platform management (system admin)\n\nExamples:\n  sorcha platform orgs\n  sorcha platform orgs --status active\n  sorcha platform settings")
    {
        Subcommands.Add(new PlatformOrgsCommand(clientFactory, authService, configService));
        Subcommands.Add(new PlatformSettingsCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists platform organizations.
/// </summary>
public class PlatformOrgsCommand : Command
{
    public PlatformOrgsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("orgs", "List all platform organizations")
    {
        var statusOption = new Option<string?>("--status") { Description = "Filter by status (active, suspended)" };
        Options.Add(statusOption);

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

                var status = parseResult.GetValue(statusOption);

                var client = await clientFactory.CreatePlatformServiceClientAsync(profileName);
                var orgs = await client.ListPlatformOrganizationsAsync(status, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, orgs);
                    return ExitCodes.Success;
                }

                if (orgs.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No organizations found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {orgs.Count} organization(s):");
                Console.WriteLine();

                Console.WriteLine($"{"ID",-38} {"Name",-25} {"Status",-12} {"Users",6} {"Created"}");
                Console.WriteLine(new string('-', 100));

                foreach (var org in orgs)
                {
                    var statusColor = org.Status.ToLowerInvariant() switch
                    {
                        "active" => ConsoleColor.Green,
                        "suspended" => ConsoleColor.Red,
                        _ => ConsoleColor.Gray
                    };

                    var originalColor = Console.ForegroundColor;
                    Console.Write($"{org.Id,-38} {org.Name,-25} ");
                    Console.ForegroundColor = statusColor;
                    Console.Write($"{org.Status,-12}");
                    Console.ForegroundColor = originalColor;
                    Console.WriteLine($" {org.UserCount,6} {org.CreatedAt:yyyy-MM-dd}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view platform organizations. System admin role required.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list platform organizations: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets platform settings.
/// </summary>
public class PlatformSettingsCommand : Command
{
    public PlatformSettingsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("settings", "Get platform settings")
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

                var client = await clientFactory.CreatePlatformServiceClientAsync(profileName);
                var settings = await client.GetPlatformSettingsAsync($"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, settings);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteInfo("Platform Settings:");
                Console.WriteLine($"  Public Org Enabled:     {settings.PublicOrgEnabled}");
                Console.WriteLine($"  Public Org ID:          {settings.PublicOrgId}");
                Console.WriteLine($"  Public Org Status:      {settings.PublicOrgStatus}");
                Console.WriteLine($"  Max Orgs Per User:      {settings.MaxOrgsPerUser}");
                Console.WriteLine($"  Updated At:             {settings.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view platform settings. System admin role required.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get platform settings: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
