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
/// Blueprint management commands.
/// </summary>
public class BlueprintCommand : Command
{
    public BlueprintCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("blueprint", "Manage blueprints (workflow definitions)")
    {
        Subcommands.Add(new BlueprintListCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintPublishCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintVersionsCommand(clientFactory, authService, configService));
        Subcommands.Add(new BlueprintInstancesCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all blueprints.
/// </summary>
public class BlueprintListCommand : Command
{
    public BlueprintListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all blueprints")
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

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);
                var blueprints = await client.ListBlueprintsAsync($"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(blueprints, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (blueprints == null || blueprints.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No blueprints found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {blueprints.Count} blueprint(s):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Title",-30} {"Version",-10} {"Status",-12} {"Actions",7} {"Created"}");
                Console.WriteLine(new string('-', 120));

                foreach (var bp in blueprints)
                {
                    Console.WriteLine($"{bp.Id,-38} {bp.Title,-30} {bp.Version,-10} {bp.Status,-12} {bp.ActionCount,7} {bp.CreatedAt:yyyy-MM-dd}");
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
                ConsoleHelper.WriteError($"Failed to list blueprints: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a blueprint by ID.
/// </summary>
public class BlueprintGetCommand : Command
{
    private readonly Option<string> _idOption;

    public BlueprintGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a blueprint by ID")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Blueprint ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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
                var blueprint = await client.GetBlueprintAsync(id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(blueprint, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Blueprint details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {blueprint.Id}");
                Console.WriteLine($"  Title:           {blueprint.Title}");
                Console.WriteLine($"  Description:     {blueprint.Description}");
                Console.WriteLine($"  Version:         {blueprint.Version}");
                Console.WriteLine($"  Status:          {blueprint.Status}");
                Console.WriteLine($"  Created:         {blueprint.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Updated:         {blueprint.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                if (blueprint.Participants.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  Participants ({blueprint.Participants.Count}):");
                    foreach (var p in blueprint.Participants)
                    {
                        Console.WriteLine($"    - {p.Name} ({p.Role})");
                    }
                }

                if (blueprint.Actions.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  Actions ({blueprint.Actions.Count}):");
                    foreach (var a in blueprint.Actions)
                    {
                        Console.WriteLine($"    [{a.Id}] {a.Title} ({a.Type}) -> {a.Participant}");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Blueprint '{id}' not found.");
                return ExitCodes.NotFound;
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
                ConsoleHelper.WriteError($"Failed to get blueprint: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new blueprint from a JSON file.
/// </summary>
public class BlueprintCreateCommand : Command
{
    private readonly Option<string> _fileOption;

    public BlueprintCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a blueprint from a JSON file")
    {
        _fileOption = new Option<string>("--file", "-f")
        {
            Description = "Path to blueprint JSON file",
            Required = true
        };

        Options.Add(_fileOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var filePath = parseResult.GetValue(_fileOption)!;

            try
            {
                if (!File.Exists(filePath))
                {
                    ConsoleHelper.WriteError($"File not found: {filePath}");
                    return ExitCodes.ValidationError;
                }

                var blueprintJson = await File.ReadAllTextAsync(filePath, ct);

                // Validate JSON
                try
                {
                    JsonDocument.Parse(blueprintJson);
                }
                catch (JsonException ex)
                {
                    ConsoleHelper.WriteError($"Invalid JSON in file: {ex.Message}");
                    return ExitCodes.ValidationError;
                }

                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);

                var request = new CreateBlueprintRequest
                {
                    BlueprintJson = blueprintJson
                };

                var blueprint = await client.CreateBlueprintAsync(request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(blueprint, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Blueprint created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:      {blueprint.Id}");
                Console.WriteLine($"  Title:   {blueprint.Title}");
                Console.WriteLine($"  Version: {blueprint.Version}");
                Console.WriteLine($"  Status:  {blueprint.Status}");
                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Use 'sorcha blueprint get --id {blueprint.Id}' to view details.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid blueprint: {ex.Content}");
                return ExitCodes.ValidationError;
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
                ConsoleHelper.WriteError($"Failed to create blueprint: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Publishes a blueprint to a register.
/// </summary>
public class BlueprintPublishCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _registerIdOption;

    public BlueprintPublishCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("publish", "Publish a blueprint to a register")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Blueprint ID",
            Required = true
        };

        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Target register ID",
            Required = true
        };

        Options.Add(_idOption);
        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
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

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);

                var request = new PublishBlueprintRequest
                {
                    BlueprintId = id,
                    RegisterId = registerId
                };

                ConsoleHelper.WriteInfo($"Publishing blueprint '{id}' to register '{registerId}'...");

                var response = await client.PublishBlueprintAsync(id, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Blueprint published successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Blueprint ID:    {response.BlueprintId}");
                Console.WriteLine($"  Register ID:     {response.RegisterId}");
                Console.WriteLine($"  Transaction ID:  {response.TransactionId}");
                Console.WriteLine($"  Status:          {response.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Blueprint '{id}' or register '{registerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid publish request: {ex.Content}");
                return ExitCodes.ValidationError;
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
                ConsoleHelper.WriteError($"Failed to publish blueprint: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a blueprint.
/// </summary>
public class BlueprintDeleteCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<bool> _confirmOption;

    public BlueprintDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a blueprint")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Blueprint ID",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_idOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("WARNING: This will delete the blueprint.");
                    if (!ConsoleHelper.Confirm($"Are you sure you want to delete blueprint '{id}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Deletion cancelled.");
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

                var client = await clientFactory.CreateBlueprintServiceClientAsync(profileName);
                await client.DeleteBlueprintAsync(id, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Blueprint '{id}' deleted successfully.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Blueprint '{id}' not found.");
                return ExitCodes.NotFound;
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
                ConsoleHelper.WriteError($"Failed to delete blueprint: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists versions of a blueprint.
/// </summary>
public class BlueprintVersionsCommand : Command
{
    private readonly Option<string> _idOption;

    public BlueprintVersionsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("versions", "List versions of a blueprint")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Blueprint ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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
                var versions = await client.ListBlueprintVersionsAsync(id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (versions == null || versions.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No versions found for blueprint '{id}'.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {versions.Count} version(s) for blueprint '{id}':");
                Console.WriteLine();
                Console.WriteLine($"{"Version",-12} {"Status",-12} {"Created",-20} {"Description"}");
                Console.WriteLine(new string('-', 80));

                foreach (var v in versions)
                {
                    Console.WriteLine($"{v.Version,-12} {v.Status,-12} {v.CreatedAt:yyyy-MM-dd HH:mm:ss,-20} {v.ChangeDescription}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Blueprint '{id}' not found.");
                return ExitCodes.NotFound;
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
                ConsoleHelper.WriteError($"Failed to list blueprint versions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists instances (running workflows) of a blueprint.
/// </summary>
public class BlueprintInstancesCommand : Command
{
    private readonly Option<string?> _blueprintIdOption;

    public BlueprintInstancesCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("instances", "List blueprint instances (running workflows)")
    {
        _blueprintIdOption = new Option<string?>("--blueprint-id", "-b")
        {
            Description = "Filter by blueprint ID (optional)"
        };

        Options.Add(_blueprintIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var blueprintId = parseResult.GetValue(_blueprintIdOption);

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
                var instances = await client.ListInstancesAsync(blueprintId, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(instances, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (instances == null || instances.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No instances found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {instances.Count} instance(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Instance ID",-38} {"Blueprint ID",-38} {"Status",-12} {"Current Action",14} {"Started"}");
                Console.WriteLine(new string('-', 120));

                foreach (var inst in instances)
                {
                    var currentAction = inst.CurrentActionId?.ToString() ?? "-";
                    Console.WriteLine($"{inst.InstanceId,-38} {inst.BlueprintId,-38} {inst.Status,-12} {currentAction,14} {inst.StartedAt:yyyy-MM-dd HH:mm}");
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
                ConsoleHelper.WriteError($"Failed to list instances: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
