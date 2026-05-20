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
/// Validator service management commands.
/// </summary>
public class ValidatorCommand : Command
{
    public ValidatorCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("validator", "Manage the validator service\n\nExamples:\n  sorcha validator status\n  sorcha validator start\n  sorcha validator metrics")
    {
        Subcommands.Add(new ValidatorStatusCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorStartCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorStopCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorProcessCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorConsentCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorMetricsCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorThresholdCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorRegisterCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorCountCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorAuditCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorSuspendCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorReactivateCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorRevokeCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorSequenceCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Shared helpers for the validator roster commands.
/// </summary>
internal static class ValidatorRosterHelpers
{
    public static async Task<(string? token, string profileName)> ResolveTokenAsync(
        IAuthenticationService authService, IConfigurationService configService)
    {
        var profile = await configService.GetActiveProfileAsync();
        var profileName = profile?.Name ?? "dev";
        var token = await authService.GetAccessTokenAsync(profileName);
        return (token, profileName);
    }

    public static int HandleApiException(ApiException ex, string subject)
    {
        switch (ex.StatusCode)
        {
            case HttpStatusCode.NotFound:
                ConsoleHelper.WriteError($"{subject} not found.");
                return ExitCodes.NotFound;
            case HttpStatusCode.Unauthorized:
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            case HttpStatusCode.Forbidden:
                ConsoleHelper.WriteError("You do not have permission to perform this validator operation.");
                return ExitCodes.AuthorizationError;
            case HttpStatusCode.BadRequest:
                ConsoleHelper.WriteError("The request was rejected as invalid.");
                if (ex.Content != null) ConsoleHelper.WriteError($"Details: {ex.Content}");
                return ExitCodes.ValidationError;
            default:
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null) ConsoleHelper.WriteError($"Details: {ex.Content}");
                return ExitCodes.GeneralError;
        }
    }
}

/// <summary>
/// Self-registers a validator for a register.
/// </summary>
public class ValidatorRegisterCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<string> _publicKeyOption;
    private readonly Option<string> _grpcEndpointOption;

    public ValidatorRegisterCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("register", "Self-register a validator for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _validatorIdOption = new Option<string>("--validator-id", "-v") { Description = "Validator ID", Required = true };
        _publicKeyOption = new Option<string>("--public-key", "-k") { Description = "Validator docket-signing public key", Required = true };
        _grpcEndpointOption = new Option<string>("--grpc-endpoint", "-g") { Description = "Validator gRPC endpoint URL", Required = true };
        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_publicKeyOption);
        Options.Add(_grpcEndpointOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var publicKey = parseResult.GetValue(_publicKeyOption)!;
            var grpcEndpoint = parseResult.GetValue(_grpcEndpointOption)!;
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to register a validator.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var request = new RegisterValidatorRequest
                {
                    RegisterId = registerId,
                    ValidatorId = validatorId,
                    PublicKey = publicKey,
                    GrpcEndpoint = grpcEndpoint
                };
                var response = await client.RegisterValidatorAsync(request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Validator '{response.ValidatorId}' {response.Status}.");
                Console.WriteLine($"  Register ID:    {response.RegisterId}");
                Console.WriteLine($"  Order index:    {response.OrderIndex}");
                Console.WriteLine($"  Transaction ID: {response.TransactionId}");
                if (!string.IsNullOrEmpty(response.Message))
                    Console.WriteLine($"  Message:        {response.Message}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Register '{registerId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to register validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets the active validator count for a register.
/// </summary>
public class ValidatorCountCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorCountCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("count", "Get the active validator count for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query validator count.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.GetValidatorCountAsync(registerId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Validator count:");
                Console.WriteLine($"  Active:   {response.ActiveCount} (min {response.MinValidators}, max {response.MaxValidators})");
                Console.WriteLine($"  Quorum:   {(response.HasQuorum ? "yes" : "no")}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Register '{registerId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get validator count: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets the validator roster audit trail for a register.
/// </summary>
public class ValidatorAuditCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string?> _validatorIdOption;
    private readonly Option<int?> _limitOption;
    private readonly Option<int?> _offsetOption;

    public ValidatorAuditCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("audit", "Show the validator roster audit trail for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _validatorIdOption = new Option<string?>("--validator-id", "-v") { Description = "Filter to a single validator" };
        _limitOption = new Option<int?>("--limit", "-l") { Description = "Maximum entries to return" };
        _offsetOption = new Option<int?>("--offset", "-o") { Description = "Entries to skip" };
        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_limitOption);
        Options.Add(_offsetOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption);
            var limit = parseResult.GetValue(_limitOption);
            var offset = parseResult.GetValue(_offsetOption);
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query the validator audit trail.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.GetValidatorAuditAsync(registerId, validatorId, limit, offset, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Validator audit trail ({response.Total} total):");
                Console.WriteLine();
                if (response.Entries.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No audit entries.");
                    return ExitCodes.Success;
                }
                Console.WriteLine($"{"Validator",-30} {"From",-12} {"To",-12} {"By",-20} {"When"}");
                Console.WriteLine(new string('-', 100));
                foreach (var e in response.Entries)
                {
                    Console.WriteLine($"{e.ValidatorId,-30} {e.PreviousStatus,-12} {e.NewStatus,-12} {e.PerformedBy,-20} {e.Timestamp:yyyy-MM-dd HH:mm:ss}");
                }
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Register '{registerId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get validator audit trail: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Suspends an active validator.
/// </summary>
public class ValidatorSuspendCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<string> _reasonOption;
    private readonly Option<string?> _byOption;

    public ValidatorSuspendCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("suspend", "Suspend an active validator")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _validatorIdOption = new Option<string>("--validator-id", "-v") { Description = "Validator ID", Required = true };
        _reasonOption = new Option<string>("--reason") { Description = "Reason for suspension", Required = true };
        _byOption = new Option<string?>("--by") { Description = "Actor performing the suspension (default: current user)" };
        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_reasonOption);
        Options.Add(_byOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var reason = parseResult.GetValue(_reasonOption)!;
            var by = parseResult.GetValue(_byOption);
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to suspend a validator.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var request = new SuspendValidatorRequest { SuspendedBy = by ?? string.Empty, Reason = reason };
                var response = await client.SuspendValidatorAsync(registerId, validatorId, request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }
                ConsoleHelper.WriteSuccess($"Validator '{response.ValidatorId}' is now {response.Status}.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Validator '{validatorId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to suspend validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Reactivates a suspended validator.
/// </summary>
public class ValidatorReactivateCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<string?> _notesOption;
    private readonly Option<string?> _byOption;

    public ValidatorReactivateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("reactivate", "Reactivate a suspended validator")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _validatorIdOption = new Option<string>("--validator-id", "-v") { Description = "Validator ID", Required = true };
        _notesOption = new Option<string?>("--notes") { Description = "Optional notes" };
        _byOption = new Option<string?>("--by") { Description = "Actor performing the reactivation (default: current user)" };
        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_notesOption);
        Options.Add(_byOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var notes = parseResult.GetValue(_notesOption);
            var by = parseResult.GetValue(_byOption);
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to reactivate a validator.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var request = new ReactivateValidatorRequest { ReactivatedBy = by ?? string.Empty, Notes = notes };
                var response = await client.ReactivateValidatorAsync(registerId, validatorId, request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }
                ConsoleHelper.WriteSuccess($"Validator '{response.ValidatorId}' is now {response.Status}.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Validator '{validatorId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to reactivate validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Permanently revokes a validator.
/// </summary>
public class ValidatorRevokeCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<string> _reasonOption;
    private readonly Option<string?> _byOption;

    public ValidatorRevokeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("revoke", "Permanently revoke a validator")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _validatorIdOption = new Option<string>("--validator-id", "-v") { Description = "Validator ID", Required = true };
        _reasonOption = new Option<string>("--reason") { Description = "Reason for revocation", Required = true };
        _byOption = new Option<string?>("--by") { Description = "Actor performing the revocation (default: current user)" };
        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_reasonOption);
        Options.Add(_byOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var reason = parseResult.GetValue(_reasonOption)!;
            var by = parseResult.GetValue(_byOption);
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to revoke a validator.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var request = new RevokeValidatorRequest { RevokedBy = by ?? string.Empty, Reason = reason };
                var response = await client.RevokeValidatorAsync(registerId, validatorId, request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }
                ConsoleHelper.WriteSuccess($"Validator '{response.ValidatorId}' is now {response.Status}.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Validator '{validatorId}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to revoke validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a wallet's sequence numbers for a register.
/// </summary>
public class ValidatorSequenceCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _walletOption;

    public ValidatorSequenceCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("sequence", "Get a wallet's sequence numbers for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        _walletOption = new Option<string>("--wallet", "-w") { Description = "Wallet address", Required = true };
        Options.Add(_registerIdOption);
        Options.Add(_walletOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            try
            {
                var (token, profileName) = await ValidatorRosterHelpers.ResolveTokenAsync(authService, configService);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query a wallet sequence.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.GetValidatorSequenceAsync(registerId, wallet, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }
                ConsoleHelper.WriteSuccess("Wallet sequence:");
                Console.WriteLine($"  Wallet:   {response.WalletAddress}");
                Console.WriteLine($"  Last:     {response.LastSequenceNumber}");
                Console.WriteLine($"  Next:     {response.NextSequenceNumber}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) { return ValidatorRosterHelpers.HandleApiException(ex, $"Wallet '{wallet}'"); }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get wallet sequence: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets the current validator service status.
/// </summary>
public class ValidatorStatusCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get validator service status\n\nNote: --register-id is required (breaking change from v1.0.x)")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var registerId = parseResult.GetValue(_registerIdOption)!;
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var status = await client.GetStatusAsync(registerId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, status);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Validator service status:");
                Console.WriteLine();
                Console.WriteLine($"  Status:              {status.Status}");
                Console.WriteLine($"  Running:             {(status.IsRunning ? "Yes" : "No")}");
                Console.WriteLine($"  Registers Monitored: {status.RegistersMonitored}");
                Console.WriteLine($"  Total Validations:   {status.TotalValidations}");
                Console.WriteLine($"  Failed Validations:  {status.FailedValidations}");
                Console.WriteLine($"  Consensus Protocol:  {status.ConsensusProtocol}");
                Console.WriteLine($"  Uptime:              {status.Uptime}");

                if (status.LastValidationAt.HasValue)
                {
                    Console.WriteLine($"  Last Validation:     {status.LastValidationAt:yyyy-MM-dd HH:mm:ss}");
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
                ConsoleHelper.WriteError("You do not have permission to view validator status.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get validator status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Starts the validator service.
/// </summary>
public class ValidatorStartCommand : Command
{
    public ValidatorStartCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("start", "Start the validator service")
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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                ConsoleHelper.WriteInfo("Starting validator service...");

                var response = await client.StartAsync($"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Validator service: {response.Status}");
                if (!string.IsNullOrEmpty(response.Message))
                {
                    Console.WriteLine($"  {response.Message}");
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
                ConsoleHelper.WriteError("You do not have permission to start the validator service.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteWarning("Validator service is already running.");
                return ExitCodes.Success;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to start validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Stops the validator service.
/// </summary>
public class ValidatorStopCommand : Command
{
    private readonly Option<bool> _confirmOption;

    public ValidatorStopCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("stop", "Stop the validator service")
    {
        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("WARNING: Stopping the validator will halt transaction validation.");
                    if (!ConsoleHelper.Confirm("Are you sure you want to stop the validator?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Stop cancelled.");
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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                ConsoleHelper.WriteInfo("Stopping validator service...");

                var response = await client.StopAsync($"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Validator service: {response.Status}");
                if (!string.IsNullOrEmpty(response.Message))
                {
                    Console.WriteLine($"  {response.Message}");
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
                ConsoleHelper.WriteError("You do not have permission to stop the validator service.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteWarning("Validator service is already stopped.");
                return ExitCodes.Success;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to stop validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Triggers processing of pending transactions for a register.
/// </summary>
public class ValidatorProcessCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorProcessCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("process", "Process pending transactions for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID to process",
            Required = true
        };

        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;

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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                ConsoleHelper.WriteInfo($"Processing pending transactions for register '{registerId}'...");

                var result = await client.ProcessRegisterAsync(registerId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Processing complete!");
                Console.WriteLine();
                Console.WriteLine($"  Register ID:            {result.RegisterId}");
                Console.WriteLine($"  Transactions Processed: {result.TransactionsProcessed}");
                Console.WriteLine($"  Transactions Validated: {result.TransactionsValidated}");
                Console.WriteLine($"  Transactions Rejected:  {result.TransactionsRejected}");
                Console.WriteLine($"  Processed At:           {result.ProcessedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to process transactions.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to process transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

// ===== Consent Commands =====

/// <summary>
/// Validator consent management commands.
/// </summary>
public class ValidatorConsentCommand : Command
{
    public ValidatorConsentCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("consent", "Manage validator consent/registration")
    {
        Subcommands.Add(new ValidatorConsentPendingCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorConsentApproveCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorConsentRejectCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorConsentRefreshCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists pending validator registrations for a register.
/// </summary>
public class ValidatorConsentPendingCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorConsentPendingCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("pending", "List pending validator registrations")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;

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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.GetPendingValidatorsAsync(registerId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess($"Pending validators for register '{registerId}':");
                Console.WriteLine();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0)
                    {
                        ConsoleHelper.WriteInfo("No pending validators.");
                        return ExitCodes.Success;
                    }

                    Console.WriteLine($"{"Validator ID",-38} {"Address",-45} {"Requested At"}");
                    Console.WriteLine(new string('-', 100));

                    foreach (var item in root.EnumerateArray())
                    {
                        Console.WriteLine($"{ValidatorJsonHelper.GetString(item, "validatorId"),-38} {ValidatorJsonHelper.GetString(item, "address"),-45} {ValidatorJsonHelper.GetString(item, "requestedAt")}");
                    }
                }
                else
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        Console.WriteLine($"  {prop.Name,-25} {prop.Value}");
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
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get pending validators: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Approves a pending validator registration.
/// </summary>
public class ValidatorConsentApproveCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<bool> _confirmOption;

    public ValidatorConsentApproveCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("approve", "Approve a pending validator registration")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _validatorIdOption = new Option<string>("--validator-id", "-v")
        {
            Description = "Validator ID to approve",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    if (!ConsoleHelper.Confirm($"Approve validator '{validatorId}' for register '{registerId}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Approval cancelled.");
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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.ApproveValidatorAsync(registerId, validatorId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                ConsoleHelper.WriteSuccess($"Validator '{validatorId}' approved for register '{registerId}'.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to approve validators.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to approve validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Rejects a pending validator registration.
/// </summary>
public class ValidatorConsentRejectCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _validatorIdOption;
    private readonly Option<string?> _reasonOption;
    private readonly Option<bool> _confirmOption;

    public ValidatorConsentRejectCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("reject", "Reject a pending validator registration")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _validatorIdOption = new Option<string>("--validator-id", "-v")
        {
            Description = "Validator ID to reject",
            Required = true
        };

        _reasonOption = new Option<string?>("--reason")
        {
            Description = "Reason for rejection"
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_validatorIdOption);
        Options.Add(_reasonOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var validatorId = parseResult.GetValue(_validatorIdOption)!;
            var reason = parseResult.GetValue(_reasonOption);
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    if (!ConsoleHelper.Confirm($"Reject validator '{validatorId}' for register '{registerId}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Rejection cancelled.");
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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.RejectValidatorAsync(registerId, validatorId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                ConsoleHelper.WriteSuccess($"Validator '{validatorId}' rejected for register '{registerId}'.");
                if (!string.IsNullOrEmpty(reason))
                {
                    Console.WriteLine($"  Reason: {reason}");
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
                ConsoleHelper.WriteError("You do not have permission to reject validators.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to reject validator: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Refreshes the validator list for a register.
/// </summary>
public class ValidatorConsentRefreshCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorConsentRefreshCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("refresh", "Refresh the validator list for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;

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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                ConsoleHelper.WriteInfo($"Refreshing validators for register '{registerId}'...");

                var response = await client.RefreshValidatorsAsync(registerId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Validator list refreshed for register '{registerId}'.");
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
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to refresh validators: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

// ===== Metrics Commands =====

/// <summary>
/// Validator metrics commands.
/// </summary>
public class ValidatorMetricsCommand : Command
{
    public ValidatorMetricsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("metrics", "View validator metrics")
    {
        // Default action for 'validator metrics' (aggregated)
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            return await FetchAndDisplayMetricsAsync(
                clientFactory, authService, configService,
                "aggregated", parseResult, ct,
                async (client, auth) => await client.GetAggregatedMetricsAsync(auth));
        });

        Subcommands.Add(new ValidatorMetricsSubCommand("validation", "View validation metrics",
            clientFactory, authService, configService,
            async (client, auth) => await client.GetValidationMetricsAsync(auth)));

        Subcommands.Add(new ValidatorMetricsSubCommand("consensus", "View consensus metrics",
            clientFactory, authService, configService,
            async (client, auth) => await client.GetConsensusMetricsAsync(auth)));

        Subcommands.Add(new ValidatorMetricsSubCommand("pools", "View pool metrics",
            clientFactory, authService, configService,
            async (client, auth) => await client.GetPoolMetricsAsync(auth)));

        Subcommands.Add(new ValidatorMetricsSubCommand("caches", "View cache metrics",
            clientFactory, authService, configService,
            async (client, auth) => await client.GetCacheMetricsAsync(auth)));

        Subcommands.Add(new ValidatorMetricsSubCommand("config", "View configuration metrics",
            clientFactory, authService, configService,
            async (client, auth) => await client.GetConfigMetricsAsync(auth)));
    }

    internal static async Task<int> FetchAndDisplayMetricsAsync(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService,
        string metricsType,
        ParseResult parseResult,
        CancellationToken ct,
        Func<IValidatorServiceClient, string, Task<HttpResponseMessage>> apiCall)
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

            var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
            var response = await apiCall(client, $"Bearer {token}");
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                return ExitCodes.GeneralError;
            }

            var outputFormat = OutputHelper.GetOutputFormat(parseResult);
            if (OutputHelper.IsStructuredFormat(outputFormat))
            {
                Console.WriteLine(content);
                return ExitCodes.Success;
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            ConsoleHelper.WriteSuccess($"Validator {metricsType} metrics:");
            Console.WriteLine();

            DisplayJsonProperties(root, indent: "  ");

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
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"Failed to get {metricsType} metrics: {ex.Message}");
            return ExitCodes.GeneralError;
        }
    }

    /// <summary>
    /// Recursively displays JSON properties in a table-like format.
    /// </summary>
    internal static void DisplayJsonProperties(JsonElement element, string indent)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    Console.WriteLine($"{indent}{prop.Name}:");
                    DisplayJsonProperties(prop.Value, indent + "  ");
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"{indent}{prop.Name}: [{prop.Value.GetArrayLength()} items]");
                }
                else
                {
                    Console.WriteLine($"{indent}{prop.Name,-30} {prop.Value}");
                }
            }
        }
        else
        {
            Console.WriteLine($"{indent}{element}");
        }
    }
}

/// <summary>
/// Generic metrics sub-command that delegates to a specific API call.
/// </summary>
public class ValidatorMetricsSubCommand : Command
{
    public ValidatorMetricsSubCommand(
        string name,
        string description,
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService,
        Func<IValidatorServiceClient, string, Task<HttpResponseMessage>> apiCall)
        : base(name, description)
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            return await ValidatorMetricsCommand.FetchAndDisplayMetricsAsync(
                clientFactory, authService, configService,
                name, parseResult, ct, apiCall);
        });
    }
}

// ===== Threshold Commands =====

/// <summary>
/// Validator threshold signing commands.
/// </summary>
public class ValidatorThresholdCommand : Command
{
    public ValidatorThresholdCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("threshold", "Manage threshold signing")
    {
        Subcommands.Add(new ValidatorThresholdStatusCommand(clientFactory, authService, configService));
        Subcommands.Add(new ValidatorThresholdSetupCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets the threshold signing status.
/// </summary>
public class ValidatorThresholdStatusCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public ValidatorThresholdStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get threshold signing status")
    {
        _registerIdOption = new Option<string>("--register-id", "-r") { Description = "Register ID", Required = true };
        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var registerId = parseResult.GetValue(_registerIdOption)!;
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);
                var response = await client.GetThresholdStatusAsync(registerId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess("Threshold signing status:");
                Console.WriteLine();

                ValidatorMetricsCommand.DisplayJsonProperties(root, "  ");

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
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get threshold status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Sets up threshold signing for a register.
/// </summary>
public class ValidatorThresholdSetupCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<int> _thresholdOption;
    private readonly Option<int> _totalValidatorsOption;
    private readonly Option<string> _validatorIdsOption;
    private readonly Option<bool> _confirmOption;

    public ValidatorThresholdSetupCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("setup", "Set up threshold signing for a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _thresholdOption = new Option<int>("--threshold", "-t")
        {
            Description = "Signing threshold (minimum signatures required)",
            Required = true
        };

        _totalValidatorsOption = new Option<int>("--total-validators", "-n")
        {
            Description = "Total number of validators",
            Required = true
        };

        _validatorIdsOption = new Option<string>("--validator-ids")
        {
            Description = "Comma-separated list of validator IDs",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_thresholdOption);
        Options.Add(_totalValidatorsOption);
        Options.Add(_validatorIdsOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var threshold = parseResult.GetValue(_thresholdOption);
            var totalValidators = parseResult.GetValue(_totalValidatorsOption);
            var validatorIdsRaw = parseResult.GetValue(_validatorIdsOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            var validatorIds = validatorIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("Threshold signing setup:");
                    Console.WriteLine($"  Register ID:      {registerId}");
                    Console.WriteLine($"  Threshold:        {threshold} of {totalValidators}");
                    Console.WriteLine($"  Validator IDs:    {string.Join(", ", validatorIds)}");

                    if (!ConsoleHelper.Confirm("Proceed with threshold setup?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Threshold setup cancelled.");
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

                var client = await clientFactory.CreateValidatorServiceClientAsync(profileName);

                var request = new ThresholdSetupRequest
                {
                    RegisterId = registerId,
                    Threshold = threshold,
                    TotalValidators = totalValidators,
                    ValidatorIds = validatorIds
                };

                var response = await client.SetupThresholdAsync(request, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Threshold signing configured!");
                Console.WriteLine();

                using var doc = JsonDocument.Parse(content);
                ValidatorMetricsCommand.DisplayJsonProperties(doc.RootElement, "  ");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to configure threshold signing.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to set up threshold signing: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Helper to safely extract a string from a JsonElement (file-local).
/// </summary>
file static class ValidatorJsonHelper
{
    public static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ToString();
        }
        return "-";
    }
}
