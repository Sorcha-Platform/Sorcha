// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.Register.Models;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Docket inspection commands.
/// </summary>
public class DocketCommand : Command
{
    public DocketCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("docket", "Inspect dockets (sealed blocks) in registers")
    {
        Subcommands.Add(new DocketListCommand(clientFactory, authService, configService));
        Subcommands.Add(new DocketGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new DocketTransactionsCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all dockets in a register.
/// </summary>
public class DocketListCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public DocketListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all dockets in a register")
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
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to list dockets.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var dockets = await client.ListDocketsAsync(registerId, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(dockets, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (dockets == null || dockets.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No dockets found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {dockets.Count} docket(s) in register '{registerId}':");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"ID",8} {"Hash",-66} {"State",-12} {"TX Count",10} {"Timestamp"}");
                Console.WriteLine(new string('-', 120));

                foreach (var docket in dockets)
                {
                    var hash = docket.Hash.Length > 64 ? docket.Hash[..64] + "..." : docket.Hash;
                    var timestamp = docket.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss");
                    Console.WriteLine($"{docket.Id,8} {hash,-66} {docket.State,-12} {docket.TransactionIds.Count,10} {timestamp}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view dockets in this register.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list dockets: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a docket by ID.
/// </summary>
public class DocketGetCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<ulong> _docketIdOption;

    public DocketGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a docket by ID")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _docketIdOption = new Option<ulong>("--docket-id", "-d")
        {
            Description = "Docket ID (block height)",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_docketIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var docketId = parseResult.GetValue(_docketIdOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to get a docket.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var docket = await client.GetDocketAsync(registerId, docketId, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(docket, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Docket details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {docket.Id}");
                Console.WriteLine($"  Register ID:     {docket.RegisterId}");
                Console.WriteLine($"  State:           {docket.State}");
                Console.WriteLine($"  Hash:            {docket.Hash}");
                Console.WriteLine($"  Previous Hash:   {(string.IsNullOrEmpty(docket.PreviousHash) ? "(genesis)" : docket.PreviousHash)}");
                Console.WriteLine($"  Transaction Count: {docket.TransactionIds.Count}");
                Console.WriteLine($"  Timestamp:       {docket.TimeStamp:yyyy-MM-dd HH:mm:ss}");

                if (!string.IsNullOrEmpty(docket.Votes))
                {
                    Console.WriteLine($"  Votes:           {docket.Votes}");
                }

                if (docket.MetaData != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Metadata:");
                    if (!string.IsNullOrEmpty(docket.MetaData.BlueprintId))
                        Console.WriteLine($"    Blueprint ID:  {docket.MetaData.BlueprintId}");
                }

                if (docket.TransactionIds.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Transaction IDs:");
                    foreach (var txId in docket.TransactionIds)
                    {
                        Console.WriteLine($"    - {txId}");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Docket '{docketId}' not found in register '{registerId}'.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view this docket.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get docket: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists transactions in a docket.
/// </summary>
public class DocketTransactionsCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<ulong> _docketIdOption;

    public DocketTransactionsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("transactions", "List transactions in a docket")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _docketIdOption = new Option<ulong>("--docket-id", "-d")
        {
            Description = "Docket ID (block height)",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_docketIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var docketId = parseResult.GetValue(_docketIdOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to list docket transactions.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var transactions = await client.GetDocketTransactionsAsync(registerId, docketId, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(transactions, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (transactions == null || transactions.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No transactions found in docket {docketId}.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {transactions.Count} transaction(s) in docket {docketId}:");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"TX ID",-66} {"Sender",-40} {"Payloads",8} {"Timestamp"}");
                Console.WriteLine(new string('-', 140));

                foreach (var tx in transactions)
                {
                    var timestamp = tx.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss");
                    Console.WriteLine($"{tx.TxId,-66} {tx.SenderWallet,-40} {tx.PayloadCount,8} {timestamp}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Docket '{docketId}' not found in register '{registerId}'.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view transactions in this docket.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list docket transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
