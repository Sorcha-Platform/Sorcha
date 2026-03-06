// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Spectre.Console;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Container command for action execution commands.
/// </summary>
public class ActionCommand : Command
{
    /// <summary>
    /// Initializes the action command with its subcommands.
    /// </summary>
    public ActionCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("action", "Blueprint action execution")
    {
        Subcommands.Add(new ActionExecuteCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Executes a blueprint action, optionally waiting for async encryption operations.
/// </summary>
public class ActionExecuteCommand : Command
{
    /// <summary>
    /// Initializes the execute subcommand with all required and optional options.
    /// </summary>
    public ActionExecuteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("execute", "Execute a blueprint action")
    {
        var blueprintOption = new Option<string>("--blueprint")
        {
            Description = "Blueprint ID",
            Required = true
        };

        var actionOption = new Option<string>("--action")
        {
            Description = "Action ID within the blueprint",
            Required = true
        };

        var instanceOption = new Option<string>("--instance")
        {
            Description = "Blueprint instance ID",
            Required = true
        };

        var walletOption = new Option<string>("--wallet")
        {
            Description = "Sender wallet address",
            Required = true
        };

        var registerOption = new Option<string>("--register")
        {
            Description = "Register address",
            Required = true
        };

        var payloadOption = new Option<string>("--payload")
        {
            Description = "JSON string of payload data"
        };

        var noWaitOption = new Option<bool>("--no-wait")
        {
            Description = "Don't wait for async operations to complete",
            DefaultValueFactory = _ => false
        };

        Options.Add(blueprintOption);
        Options.Add(actionOption);
        Options.Add(instanceOption);
        Options.Add(walletOption);
        Options.Add(registerOption);
        Options.Add(payloadOption);
        Options.Add(noWaitOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var blueprintId = parseResult.GetValue(blueprintOption)!;
            var actionId = parseResult.GetValue(actionOption)!;
            var instanceId = parseResult.GetValue(instanceOption)!;
            var walletAddress = parseResult.GetValue(walletOption)!;
            var registerAddress = parseResult.GetValue(registerOption)!;
            var payloadJson = parseResult.GetValue(payloadOption);
            var noWait = parseResult.GetValue(noWaitOption);

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

                var request = new ActionExecuteCliRequest
                {
                    BlueprintId = blueprintId,
                    ActionId = actionId,
                    InstanceId = instanceId,
                    SenderWallet = walletAddress,
                    RegisterAddress = registerAddress
                };

                // Parse optional payload
                if (!string.IsNullOrEmpty(payloadJson))
                {
                    try
                    {
                        request.PayloadData = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);
                    }
                    catch (JsonException ex)
                    {
                        ConsoleHelper.WriteError($"Invalid payload JSON: {ex.Message}");
                        return ExitCodes.ValidationError;
                    }
                }

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);
                var response = await client.SubmitActionAsync(
                    instanceId, actionId, request, $"Bearer {token}");

                // Handle synchronous completion
                if (!response.IsAsync || response.IsComplete)
                {
                    ConsoleHelper.WriteSuccess($"Action completed. Transaction ID: {response.TransactionId}");
                    return ExitCodes.Success;
                }

                // Async operation — no-wait mode (T036)
                if (noWait)
                {
                    ConsoleHelper.WriteSuccess($"Action submitted. Operation ID: {response.OperationId}");
                    ConsoleHelper.WriteInfo("Check status with: sorcha operation status " + response.OperationId);
                    return ExitCodes.Success;
                }

                // Async operation — blocking mode with progress (T035)
                return await WaitForOperationAsync(client, token, response.OperationId!, ct);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Resource not found: {ex.Content}");
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
                ConsoleHelper.WriteError($"Failed to execute action: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    /// <summary>
    /// Polls the operation status with a Spectre.Console progress display until completion or failure.
    /// </summary>
    private static async Task<int> WaitForOperationAsync(
        IBlueprintServiceClient client,
        string token,
        string operationId,
        CancellationToken ct)
    {
        var exitCode = ExitCodes.Success;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[cyan]Encrypting ({operationId})[/]", maxValue: 100);

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var status = await client.GetOperationStatusAsync(operationId, $"Bearer {token}");

                        task.Description = $"[cyan]{status.Stage}[/]";
                        task.Value = status.PercentComplete;

                        if (status.PercentComplete >= 100)
                        {
                            task.StopTask();
                            break;
                        }

                        if (!string.IsNullOrEmpty(status.ErrorMessage))
                        {
                            task.Description = $"[red]Failed: {status.ErrorMessage}[/]";
                            task.StopTask();
                            exitCode = ExitCodes.GeneralError;
                            break;
                        }
                    }
                    catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Operation may have completed and been cleaned up
                        task.Value = 100;
                        task.StopTask();
                        break;
                    }

                    await Task.Delay(2000, ct);
                }
            });

        if (exitCode == ExitCodes.Success)
        {
            ConsoleHelper.WriteSuccess($"Encryption operation completed: {operationId}");
        }
        else
        {
            ConsoleHelper.WriteError($"Encryption operation failed: {operationId}");
        }

        return exitCode;
    }
}
