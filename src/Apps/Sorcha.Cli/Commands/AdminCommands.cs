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
/// Administrative operations commands.
/// </summary>
public class AdminCommand : Command
{
    public AdminCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("admin", "Administrative operations")
    {
        Subcommands.Add(new AdminHealthCommand(clientFactory, authService, configService));
        Subcommands.Add(new AdminAlertsCommand(clientFactory, authService, configService));
        Subcommands.Add(new AdminEventsCommand(clientFactory, authService, configService));
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(alerts, new JsonSerializerOptions { WriteIndented = true }));
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

/// <summary>
/// Container command for system event operations.
/// </summary>
public class AdminEventsCommand : Command
{
    public AdminEventsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("events", "Manage system events")
    {
        Subcommands.Add(new AdminEventsListCommand(clientFactory, authService, configService));
        Subcommands.Add(new AdminEventsDeleteCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists system events with optional filtering.
/// </summary>
public class AdminEventsListCommand : Command
{
    private readonly Option<string?> _severityOption;
    private readonly Option<string?> _sinceOption;
    private readonly Option<int> _pageOption;

    public AdminEventsListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List system events")
    {
        _severityOption = new Option<string?>("--severity", "-s")
        {
            Description = "Filter by severity (Info, Warning, Error, Critical)"
        };

        _sinceOption = new Option<string?>("--since")
        {
            Description = "Filter events since ISO 8601 date (e.g. 2026-01-01)"
        };

        _pageOption = new Option<int>("--page", "-p")
        {
            Description = "Page number",
            DefaultValueFactory = _ => 1
        };

        Options.Add(_severityOption);
        Options.Add(_sinceOption);
        Options.Add(_pageOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var severity = parseResult.GetValue(_severityOption);
            var since = parseResult.GetValue(_sinceOption);
            var page = parseResult.GetValue(_pageOption);

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
                var response = await client.ListEventsAsync(severity, page, 20, since, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (response.Events.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No events found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.TotalCount} event(s) (page {response.Page}):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Severity",-10} {"Type",-20} {"Source",-15} {"Timestamp",-20} {"Message"}");
                Console.WriteLine(new string('-', 155));

                foreach (var evt in response.Events)
                {
                    var severityColor = evt.Severity.ToLowerInvariant() switch
                    {
                        "critical" => ConsoleColor.Red,
                        "error" => ConsoleColor.Red,
                        "warning" => ConsoleColor.Yellow,
                        _ => ConsoleColor.Cyan
                    };

                    var message = evt.Message.Length > 50 ? evt.Message[..50] + "..." : evt.Message;

                    var originalColor = Console.ForegroundColor;
                    Console.Write($"{evt.Id,-38} ");
                    Console.ForegroundColor = severityColor;
                    Console.Write($"{evt.Severity,-10}");
                    Console.ForegroundColor = originalColor;
                    Console.WriteLine($" {evt.Type,-20} {evt.Source,-15} {evt.Timestamp:yyyy-MM-dd HH:mm,-20} {message}");
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
                ConsoleHelper.WriteError("You do not have permission to view events.");
                return ExitCodes.AuthorizationError;
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
                ConsoleHelper.WriteError($"Failed to list events: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a system event by ID.
/// </summary>
public class AdminEventsDeleteCommand : Command
{
    private readonly Option<string> _idOption;

    public AdminEventsDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a system event")
    {
        _idOption = new Option<string>("--id")
        {
            Description = "The event ID to delete",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var eventId = parseResult.GetValue(_idOption)!;

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
                await client.DeleteEventAsync(eventId, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Event '{eventId}' deleted successfully.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Permission denied. You do not have access to delete events.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Event not found: {eventId}");
                return ExitCodes.GeneralError;
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
                ConsoleHelper.WriteError($"Failed to delete event: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
