// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;

using Refit;

using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Workflow-instance repair commands (Feature 145 US4). A workflow instance is a deterministic
/// projection of the sealed register; these commands verify and, if needed, rebuild that projection
/// from the ledger. There is no UI equivalent — this is the operator repair path.
/// </summary>
/// <remarks>
/// Both subcommands call service-tier <c>/api/internal/*</c> endpoints, so they require a
/// service-principal token: <c>sorcha auth login --client-id … --client-secret …</c>. A user token
/// gets a 403.
/// </remarks>
public class InstanceCommand : Command
{
    public InstanceCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("instance", "Verify and repair workflow-instance projections (operator, service-principal auth)\n\n"
            + "Examples:\n"
            + "  sorcha instance parity --register-id <reg> --instance-id <inst>\n"
            + "  sorcha instance rebuild --register-id <reg> --instance-id <inst>")
    {
        Subcommands.Add(new InstanceParityCommand(clientFactory, authService, configService));
        Subcommands.Add(new InstanceRebuildCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Checks whether an instance's materialized view matches a fresh rebuild from the ledger.
/// Read-only — writes nothing.
/// </summary>
public class InstanceParityCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _instanceIdOption;

    public InstanceParityCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("parity", "Check whether an instance's stored state matches a ledger rebuild (read-only)")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "The register the instance lives on",
            Required = true
        };

        _instanceIdOption = new Option<string>("--instance-id", "-i")
        {
            Description = "The workflow instance id",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_instanceIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var instanceId = parseResult.GetValue(_instanceIdOption)!;

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

                var client = await clientFactory.CreateInstanceServiceClientAsync(profileName);
                var result = await client.CheckParityAsync(registerId, instanceId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                if (result.InSync)
                {
                    ConsoleHelper.WriteSuccess($"Instance '{instanceId}' is in sync with the ledger.");
                    Console.WriteLine($"  State: {result.MaterializedState}");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteWarning($"Instance '{instanceId}' has DIVERGED from the ledger.");
                Console.WriteLine();
                Console.WriteLine($"  Materialized state: {result.MaterializedState}");
                Console.WriteLine($"  Ledger rebuild:     {result.RebuiltState}");
                if (!string.IsNullOrEmpty(result.Detail))
                {
                    Console.WriteLine($"  Detail:             {result.Detail}");
                }

                Console.WriteLine();
                ConsoleHelper.WriteInfo(
                    "Run 'sorcha instance rebuild --register-id " + registerId + " --instance-id "
                    + instanceId + "' to overwrite the materialized view with the ledger rebuild.");

                // Divergence is a real, actionable condition — exit non-zero so scripts notice.
                return ExitCodes.GeneralError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError(
                    "This endpoint requires a service-principal token. Run "
                    + "'sorcha auth login --client-id <id> --client-secret <secret>'.");
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
                ConsoleHelper.WriteError($"Cannot reach Blueprint Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to check parity: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Rebuilds an instance's projection from the ledger and overwrites the materialized view.
/// </summary>
public class InstanceRebuildCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _instanceIdOption;
    private readonly Option<bool> _confirmOption;

    public InstanceRebuildCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("rebuild", "Rebuild an instance projection from the ledger, overwriting the stored view")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "The register the instance lives on",
            Required = true
        };

        _instanceIdOption = new Option<string>("--instance-id", "-i")
        {
            Description = "The workflow instance id",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip the confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_instanceIdOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var instanceId = parseResult.GetValue(_instanceIdOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning(
                        $"This overwrites the materialized view of instance '{instanceId}' with a "
                        + "fresh rebuild from the ledger.");
                    if (!ConsoleHelper.Confirm("Rebuild this instance?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Rebuild cancelled.");
                        return ExitCodes.Success;
                    }
                }

                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateInstanceServiceClientAsync(profileName);
                var rebuilt = await client.RebuildAsync(registerId, instanceId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, rebuilt);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Instance '{rebuilt.Id}' rebuilt from the ledger.");
                Console.WriteLine();
                Console.WriteLine($"  Blueprint:        {rebuilt.BlueprintId} (v{rebuilt.BlueprintVersion})");
                Console.WriteLine($"  Register:         {rebuilt.RegisterId}");
                Console.WriteLine($"  State:            {rebuilt.State}");
                Console.WriteLine($"  Current actions:  {(rebuilt.CurrentActionIds.Count > 0 ? string.Join(", ", rebuilt.CurrentActionIds) : "(none — terminal)")}");
                Console.WriteLine($"  Completed:        {rebuilt.CompletedActionCount}");
                if (!string.IsNullOrEmpty(rebuilt.LastTransactionId))
                {
                    Console.WriteLine($"  Last tx:          {rebuilt.LastTransactionId}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError(
                    $"No sealed transactions found for instance '{instanceId}' on register "
                    + $"'{registerId}' — there is nothing to rebuild.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError(
                    "This endpoint requires a service-principal token. Run "
                    + "'sorcha auth login --client-id <id> --client-secret <secret>'.");
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
                ConsoleHelper.WriteError($"Cannot reach Blueprint Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to rebuild instance: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
