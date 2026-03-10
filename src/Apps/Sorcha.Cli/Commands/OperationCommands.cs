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
/// Container command for encryption operation commands.
/// </summary>
public class OperationCommand : Command
{
    public OperationCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("operation", "Encryption operation management")
    {
        Subcommands.Add(new OperationStatusCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets the status of an encryption operation by ID.
/// </summary>
public class OperationStatusCommand : Command
{
    public OperationStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get encryption operation status")
    {
        var operationIdArg = new Argument<string>("operationId")
        {
            Description = "The operation ID to check"
        };

        Arguments.Add(operationIdArg);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var operationId = parseResult.GetValue(operationIdArg)!;

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
                var status = await client.GetOperationStatusAsync(operationId, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Table output
                Console.WriteLine($"{"Field",-22} {"Value"}");
                Console.WriteLine(new string('-', 60));
                Console.WriteLine($"{"Operation ID",-22} {status.OperationId}");
                Console.WriteLine($"{"Stage",-22} {status.Stage}");
                Console.WriteLine($"{"Progress",-22} {status.PercentComplete}%");
                Console.WriteLine($"{"Recipients",-22} {status.ProcessedRecipients} / {status.RecipientCount}");

                if (!string.IsNullOrEmpty(status.ErrorMessage))
                {
                    Console.WriteLine($"{"Error",-22} {status.ErrorMessage}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Operation not found: {operationId}");
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
                ConsoleHelper.WriteError($"Failed to get operation status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
