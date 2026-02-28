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
/// Cross-register query commands.
/// </summary>
public class QueryCommand : Command
{
    public QueryCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("query", "Query transactions across registers")
    {
        Subcommands.Add(new QueryWalletCommand(clientFactory, authService, configService));
        Subcommands.Add(new QuerySenderCommand(clientFactory, authService, configService));
        Subcommands.Add(new QueryBlueprintCommand(clientFactory, authService, configService));
        Subcommands.Add(new QueryStatsCommand(clientFactory, authService, configService));
        Subcommands.Add(new QueryODataCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Queries transactions by wallet address.
/// </summary>
public class QueryWalletCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public QueryWalletCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("wallet", "Query transactions by wallet address (sender or recipient)")
    {
        _addressOption = new Option<string>("--address", "-a")
        {
            Description = "Wallet address",
            Required = true
        };

        _pageOption = new Option<int?>("--page", "-p")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size", "-s")
        {
            Description = "Number of transactions per page (default: 50)"
        };

        Options.Add(_addressOption);
        Options.Add(_pageOption);
        Options.Add(_pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var page = parseResult.GetValue(_pageOption);
            var pageSize = parseResult.GetValue(_pageSizeOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query transactions.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var response = await client.QueryByWalletAsync(address, page, pageSize, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (response.Items == null || response.Items.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No transactions found for wallet '{address}'.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.TotalCount} transaction(s) for wallet '{address}':");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"TX ID",-66} {"Register",-34} {"Block",8} {"Timestamp"}");
                Console.WriteLine(new string('-', 130));

                foreach (var tx in response.Items)
                {
                    var timestamp = tx.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var block = tx.DocketNumber?.ToString() ?? "-";
                    Console.WriteLine($"{tx.TxId,-66} {tx.RegisterId,-34} {block,8} {timestamp}");
                }

                // Show pagination info
                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Page {response.Page} of {response.TotalPages} (Total: {response.TotalCount})");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to query transactions.");
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
                ConsoleHelper.WriteError($"Failed to query transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Queries transactions by sender address.
/// </summary>
public class QuerySenderCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public QuerySenderCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("sender", "Query transactions by sender wallet address")
    {
        _addressOption = new Option<string>("--address", "-a")
        {
            Description = "Sender wallet address",
            Required = true
        };

        _pageOption = new Option<int?>("--page", "-p")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size", "-s")
        {
            Description = "Number of transactions per page (default: 50)"
        };

        Options.Add(_addressOption);
        Options.Add(_pageOption);
        Options.Add(_pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var page = parseResult.GetValue(_pageOption);
            var pageSize = parseResult.GetValue(_pageSizeOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query transactions.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var response = await client.QueryBySenderAsync(address, page, pageSize, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (response.Items == null || response.Items.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No transactions found from sender '{address}'.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.TotalCount} transaction(s) from sender '{address}':");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"TX ID",-66} {"Register",-34} {"Block",8} {"Timestamp"}");
                Console.WriteLine(new string('-', 130));

                foreach (var tx in response.Items)
                {
                    var timestamp = tx.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var block = tx.DocketNumber?.ToString() ?? "-";
                    Console.WriteLine($"{tx.TxId,-66} {tx.RegisterId,-34} {block,8} {timestamp}");
                }

                // Show pagination info
                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Page {response.Page} of {response.TotalPages} (Total: {response.TotalCount})");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to query transactions.");
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
                ConsoleHelper.WriteError($"Failed to query transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Queries transactions by blueprint ID.
/// </summary>
public class QueryBlueprintCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public QueryBlueprintCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("blueprint", "Query transactions by blueprint ID")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Blueprint ID",
            Required = true
        };

        _pageOption = new Option<int?>("--page", "-p")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size", "-s")
        {
            Description = "Number of transactions per page (default: 50)"
        };

        Options.Add(_idOption);
        Options.Add(_pageOption);
        Options.Add(_pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var blueprintId = parseResult.GetValue(_idOption)!;
            var page = parseResult.GetValue(_pageOption);
            var pageSize = parseResult.GetValue(_pageSizeOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query transactions.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var response = await client.QueryByBlueprintAsync(blueprintId, page, pageSize, $"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (response.Items == null || response.Items.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No transactions found for blueprint '{blueprintId}'.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.TotalCount} transaction(s) for blueprint '{blueprintId}':");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"TX ID",-66} {"Register",-34} {"Sender",-40}");
                Console.WriteLine(new string('-', 145));

                foreach (var tx in response.Items)
                {
                    Console.WriteLine($"{tx.TxId,-66} {tx.RegisterId,-34} {tx.SenderWallet,-40}");
                }

                // Show pagination info
                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Page {response.Page} of {response.TotalPages} (Total: {response.TotalCount})");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to query transactions.");
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
                ConsoleHelper.WriteError($"Failed to query transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets query statistics.
/// </summary>
public class QueryStatsCommand : Command
{
    public QueryStatsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("stats", "Get platform query statistics")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to get query statistics.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var stats = await client.GetQueryStatsAsync($"Bearer {token}");

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Platform statistics:");
                Console.WriteLine();
                Console.WriteLine($"  Total Transactions: {stats.TotalTransactions:N0}");
                Console.WriteLine($"  Total Registers:    {stats.TotalRegisters:N0}");
                Console.WriteLine($"  Total Dockets:      {stats.TotalDockets:N0}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view query statistics.");
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
                ConsoleHelper.WriteError($"Failed to get query statistics: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Executes OData queries.
/// </summary>
public class QueryODataCommand : Command
{
    private readonly Option<string> _resourceOption;
    private readonly Option<string?> _filterOption;
    private readonly Option<string?> _orderbyOption;
    private readonly Option<int?> _topOption;
    private readonly Option<int?> _skipOption;
    private readonly Option<string?> _selectOption;
    private readonly Option<bool?> _countOption;

    public QueryODataCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("odata", "Execute an OData query")
    {
        _resourceOption = new Option<string>("--resource", "-r")
        {
            Description = "OData resource (e.g., Transactions, Registers, Dockets)",
            Required = true
        };

        _filterOption = new Option<string?>("--filter", "-f")
        {
            Description = "OData $filter expression"
        };

        _orderbyOption = new Option<string?>("--orderby", "-o")
        {
            Description = "OData $orderby expression"
        };

        _topOption = new Option<int?>("--top", "-t")
        {
            Description = "OData $top (max items to return)"
        };

        _skipOption = new Option<int?>("--skip", "-s")
        {
            Description = "OData $skip (items to skip)"
        };

        _selectOption = new Option<string?>("--select")
        {
            Description = "OData $select (fields to return)"
        };

        _countOption = new Option<bool?>("--count", "-c")
        {
            Description = "Include total count ($count=true)"
        };

        Options.Add(_resourceOption);
        Options.Add(_filterOption);
        Options.Add(_orderbyOption);
        Options.Add(_topOption);
        Options.Add(_skipOption);
        Options.Add(_selectOption);
        Options.Add(_countOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var resource = parseResult.GetValue(_resourceOption)!;
            var filter = parseResult.GetValue(_filterOption);
            var orderby = parseResult.GetValue(_orderbyOption);
            var top = parseResult.GetValue(_topOption);
            var skip = parseResult.GetValue(_skipOption);
            var select = parseResult.GetValue(_selectOption);
            var count = parseResult.GetValue(_countOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to execute OData queries.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var response = await client.QueryODataAsync(resource, filter, orderby, top, skip, select, count, $"Bearer {token}");

                // Read response content
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"OData query failed: {response.StatusCode}");
                    if (!string.IsNullOrEmpty(content))
                    {
                        ConsoleHelper.WriteError($"Details: {content}");
                    }
                    return ExitCodes.GeneralError;
                }

                // Pretty print JSON
                try
                {
                    var json = JsonDocument.Parse(content);
                    Console.WriteLine(JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (JsonException)
                {
                    // If not valid JSON, just print raw content
                    Console.WriteLine(content);
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to execute OData queries.");
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
                ConsoleHelper.WriteError($"Failed to execute OData query: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
