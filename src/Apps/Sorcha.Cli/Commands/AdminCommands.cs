// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Administrative operations commands.
/// </summary>
public class AdminCommand : Command
{
    public AdminCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("admin", "Administrative operations\n\nExamples:\n  sorcha admin health\n  sorcha admin alerts")
    {
        Subcommands.Add(new AdminHealthCommand(clientFactory, authService, configService));
        Subcommands.Add(new AdminAlertsCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets the health status of all services.
/// </summary>
public class AdminHealthCommand : Command
{
    public AdminHealthCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("health", "Check health of all services")
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

                var client = await clientFactory.CreateAdminServiceClientAsync(profileName);
                var health = await client.GetHealthAsync($"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, health);
                    return ExitCodes.Success;
                }

                // Display overall status
                if (health.OverallStatus.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleHelper.WriteSuccess($"Overall status: {health.OverallStatus}");
                }
                else if (health.OverallStatus.Equals("Degraded", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleHelper.WriteWarning($"Overall status: {health.OverallStatus}");
                }
                else
                {
                    ConsoleHelper.WriteError($"Overall status: {health.OverallStatus}");
                }

                Console.WriteLine($"  Checked at: {health.CheckedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();

                if (health.Services.Count > 0)
                {
                    Console.WriteLine($"{"Service",-25} {"Status",-12} {"Response (ms)",13} {"Version",-15}");
                    Console.WriteLine(new string('-', 70));

                    foreach (var service in health.Services)
                    {
                        var statusColor = service.Status.ToLowerInvariant() switch
                        {
                            "healthy" => ConsoleColor.Green,
                            "degraded" => ConsoleColor.Yellow,
                            _ => ConsoleColor.Red
                        };

                        var originalColor = Console.ForegroundColor;
                        Console.Write($"{service.Service,-25} ");
                        Console.ForegroundColor = statusColor;
                        Console.Write($"{service.Status,-12}");
                        Console.ForegroundColor = originalColor;
                        Console.WriteLine($" {service.ResponseTimeMs,13} {service.Version,-15}");
                    }
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
                ConsoleHelper.WriteError($"Failed to check health: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists system alerts.
/// </summary>
public class AdminAlertsCommand : Command
{
    private readonly Option<string?> _severityOption;

    public AdminAlertsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("alerts", "List system alerts")
    {
        _severityOption = new Option<string?>("--severity", "-s")
        {
            Description = "Filter by severity (Critical, Warning, Info)"
        };

        Options.Add(_severityOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var severity = parseResult.GetValue(_severityOption);

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

                var client = await clientFactory.CreateAdminServiceClientAsync(profileName);
                var alerts = await client.ListAlertsAsync(severity, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, alerts);
                    return ExitCodes.Success;
                }

                if (alerts == null || alerts.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No alerts found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {alerts.Count} alert(s):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Severity",-10} {"Source",-20} {"Created",-20} {"Message"}");
                Console.WriteLine(new string('-', 130));

                foreach (var alert in alerts)
                {
                    var severityColor = alert.Severity.ToLowerInvariant() switch
                    {
                        "critical" => ConsoleColor.Red,
                        "warning" => ConsoleColor.Yellow,
                        _ => ConsoleColor.Cyan
                    };

                    var originalColor = Console.ForegroundColor;
                    Console.Write($"{alert.Id,-38} ");
                    Console.ForegroundColor = severityColor;
                    Console.Write($"{alert.Severity,-10}");
                    Console.ForegroundColor = originalColor;
                    Console.WriteLine($" {alert.Source,-20} {alert.CreatedAt:yyyy-MM-dd HH:mm,-20} {alert.Message}");
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
                ConsoleHelper.WriteError("You do not have permission to view alerts.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list alerts: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

