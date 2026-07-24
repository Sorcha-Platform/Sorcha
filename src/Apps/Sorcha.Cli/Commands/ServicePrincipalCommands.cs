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
/// Service principal management commands.
/// </summary>
public class ServicePrincipalCommand : Command
{
    public ServicePrincipalCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("principal", "Manage service principals within organizations\n\nExamples:\n  sorcha principal list\n  sorcha principal create --name \"API Client\" --org-id <id>")
    {
        Subcommands.Add(new PrincipalListCommand(clientFactory, authService, configService));
        Subcommands.Add(new PrincipalGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new PrincipalCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new PrincipalDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new PrincipalRotateSecretCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all service principals in an organization.
/// </summary>
public class PrincipalListCommand : Command
{
    private readonly Option<string> _orgIdOption;

    public PrincipalListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all service principals in an organization")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        Options.Add(_orgIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                var principals = await client.ListServicePrincipalsAsync(orgId, $"Bearer {token}");

                // Display results
                if (principals == null || principals.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No service principals found.");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, principals);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {principals.Count} service principal(s) in organization '{orgId}':");
                Console.WriteLine();
                Console.WriteLine($"{"Client ID",-40} {"Service Name",-30} {"Status",-10} {"Scopes",-20}");
                Console.WriteLine(new string('-', 102));
                foreach (var sp in principals)
                {
                    var scopes = sp.Scopes is { Length: > 0 } ? string.Join(", ", sp.Scopes) : "(none)";
                    Console.WriteLine($"{sp.ClientId,-40} {sp.ServiceName,-30} {sp.Status,-10} {scopes,-20}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{orgId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Token may be expired. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list service principals: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a service principal by client ID.
/// </summary>
public class PrincipalGetCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _clientIdOption;

    public PrincipalGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a service principal by client ID")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _clientIdOption = new Option<string>("--client-id", "-c")
        {
            Description = "Service principal client ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_clientIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var clientId = parseResult.GetValue(_clientIdOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                var sp = await client.GetServicePrincipalAsync(orgId, clientId, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, sp);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Service principal details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {sp.Id}");
                Console.WriteLine($"  Client ID:       {sp.ClientId}");
                Console.WriteLine($"  Service Name:    {sp.ServiceName}");
                Console.WriteLine($"  Status:          {sp.Status}");
                if (sp.Scopes is { Length: > 0 })
                {
                    Console.WriteLine($"  Scopes:          {string.Join(", ", sp.Scopes)}");
                }

                Console.WriteLine($"  Created:         {sp.CreatedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Service principal '{clientId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to get service principal: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new service principal in an organization.
/// </summary>
public class PrincipalCreateCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _nameOption;
    private readonly Option<string?> _descriptionOption;
    private readonly Option<string?> _scopesOption;

    public PrincipalCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a new service principal in an organization")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Service principal name",
            Required = true
        };

        _descriptionOption = new Option<string?>("--description", "-d")
        {
            Description = "Service principal description"
        };

        _scopesOption = new Option<string?>("--scopes", "-s")
        {
            Description = "Comma-separated list of scopes/permissions (e.g., read,write,admin)"
        };

        Options.Add(_orgIdOption);
        Options.Add(_nameOption);
        Options.Add(_descriptionOption);
        Options.Add(_scopesOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var name = parseResult.GetValue(_nameOption)!;
            var description = parseResult.GetValue(_descriptionOption);
            var scopes = parseResult.GetValue(_scopesOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Build request
                var request = new CreateServicePrincipalRequest
                {
                    Name = name,
                    Description = description,
                    Scopes = !string.IsNullOrEmpty(scopes)
                        ? scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : null
                };

                // Call API
                var response = await client.CreateServicePrincipalAsync(orgId, request, $"Bearer {token}");

                // Display results with security warning
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Service principal created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {response.ServicePrincipal.Id}");
                Console.WriteLine($"  Client ID:       {response.ServicePrincipal.ClientId}");
                Console.WriteLine($"  Service Name:    {response.ServicePrincipal.ServiceName}");
                Console.WriteLine($"  Status:          {response.ServicePrincipal.Status}");
                if (response.ServicePrincipal.Scopes is { Length: > 0 })
                {
                    Console.WriteLine($"  Scopes:          {string.Join(", ", response.ServicePrincipal.Scopes)}");
                }
                Console.WriteLine();
                ConsoleHelper.WriteWarning("IMPORTANT: Save these credentials securely!");
                Console.WriteLine($"  Client Secret:   {response.ClientSecret}");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("The client secret will NEVER be displayed again.");
                ConsoleHelper.WriteWarning("Store it in a secure location (e.g., Azure Key Vault, AWS Secrets Manager).");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{orgId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"Service principal with that name already exists.");
                return ExitCodes.GeneralError;
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
                ConsoleHelper.WriteError($"Failed to create service principal: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a service principal.
/// </summary>
public class PrincipalDeleteCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _clientIdOption;
    private readonly Option<bool> _confirmOption;

    public PrincipalDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a service principal")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _clientIdOption = new Option<string>("--client-id", "-c")
        {
            Description = "Service principal client ID",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_orgIdOption);
        Options.Add(_clientIdOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var clientId = parseResult.GetValue(_clientIdOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                // Confirm deletion
                if (!confirm)
                {
                    if (!ConsoleHelper.Confirm($"Are you sure you want to delete service principal '{clientId}' from organization '{orgId}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Deletion cancelled.");
                        return ExitCodes.Success;
                    }
                }

                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                await client.DeleteServicePrincipalAsync(orgId, clientId, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Service principal '{clientId}' deleted successfully.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Service principal '{clientId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to delete service principal: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Rotates the client secret for a service principal.
/// </summary>
public class PrincipalRotateSecretCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _clientIdOption;

    public PrincipalRotateSecretCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("rotate-secret", "Rotate the client secret for a service principal")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _clientIdOption = new Option<string>("--client-id", "-c")
        {
            Description = "Service principal client ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_clientIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var clientId = parseResult.GetValue(_clientIdOption)!;

            try
            {
                // Warn about secret rotation
                ConsoleHelper.WriteWarning("WARNING: The old client secret will be immediately invalidated!");
                if (!ConsoleHelper.Confirm($"Are you sure you want to rotate the secret for service principal '{clientId}'?", defaultYes: false))
                {
                    ConsoleHelper.WriteInfo("Secret rotation cancelled.");
                    return ExitCodes.Success;
                }

                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                var response = await client.RotateSecretAsync(orgId, clientId, $"Bearer {token}");

                // Display results with security warning
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Client secret rotated successfully!");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("IMPORTANT: Save the new client secret securely!");
                Console.WriteLine($"  New Client Secret: {response.NewClientSecret}");
                Console.WriteLine();

                // Defensive: the rotation has already happened server-side by the time we get here,
                // so an empty secret means the operator has just lost access to this principal.
                // Say so loudly rather than printing a blank line and claiming success.
                if (string.IsNullOrWhiteSpace(response.NewClientSecret))
                {
                    ConsoleHelper.WriteError(
                        "The server did not return a new client secret. The old secret is no longer "
                        + "valid, so this principal now has no usable credential — re-register it.");
                    return ExitCodes.GeneralError;
                }

                ConsoleHelper.WriteWarning(
                    string.IsNullOrWhiteSpace(response.Warning)
                        ? "The new client secret will NEVER be displayed again."
                        : response.Warning);
                ConsoleHelper.WriteWarning("Update all applications using this service principal immediately.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Service principal '{clientId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to rotate secret: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
