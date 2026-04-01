// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Health check command for checking service status.
/// </summary>
public class HealthCommand : Command
{
    private static readonly string[] KnownServices = ["blueprint", "register", "tenant", "wallet", "validator", "peer", "gateway"];

    public HealthCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("health", "Check service health status\n\nExamples:\n  sorcha health\n  sorcha health --service register\n  sorcha health --service tenant")
    {
        var serviceOption = new Option<string?>("--service") { Description = "Specific service to check (blueprint, register, tenant, wallet, validator, peer, gateway)" };
        Options.Add(serviceOption);

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

                var serviceName = parseResult.GetValue(serviceOption);

                // Try the admin health endpoint first for aggregate status
                try
                {
                    var adminClient = await clientFactory.CreateAdminServiceClientAsync(profileName);
                    var health = await adminClient.GetHealthAsync($"Bearer {token}");

                    var services = health.Services;

                    // Filter to specific service if requested
                    if (!string.IsNullOrEmpty(serviceName))
                    {
                        services = services
                            .Where(s => s.Service.Contains(serviceName, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (services.Count == 0)
                        {
                            ConsoleHelper.WriteWarning($"No health data found for service '{serviceName}'.");
                            ConsoleHelper.WriteInfo($"Available services: {string.Join(", ", KnownServices)}");
                            return ExitCodes.NotFound;
                        }
                    }

                    var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                    if (OutputHelper.IsStructuredFormat(outputFormat))
                    {
                        var result = new
                        {
                            health.OverallStatus,
                            health.CheckedAt,
                            Services = services
                        };
                        OutputHelper.WriteSingle(parseResult, result);
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

                    if (services.Count > 0)
                    {
                        Console.WriteLine($"{"Service",-25} {"Status",-12} {"Response (ms)",13} {"Error"}");
                        Console.WriteLine(new string('-', 80));

                        foreach (var service in services)
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
                            Console.WriteLine($" {service.ResponseTimeMs,13}");
                        }
                    }

                    return ExitCodes.Success;
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                    return ExitCodes.AuthenticationError;
                }
                catch (HttpRequestException ex)
                {
                    ConsoleHelper.WriteError($"Cannot reach API Gateway: {ex.Message}");
                    ConsoleHelper.WriteInfo("Ensure services are running. Use 'docker-compose up -d' or 'dotnet run --project src/Apps/Sorcha.AppHost'.");
                    return ExitCodes.NetworkError;
                }
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Health check failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
