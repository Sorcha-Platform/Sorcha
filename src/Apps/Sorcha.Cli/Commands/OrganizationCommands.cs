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
/// Organization management commands.
/// </summary>
public class OrganizationCommand : Command
{
    public OrganizationCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("org", "Manage organizations\n\nExamples:\n  sorcha org list\n  sorcha org get --id <org-id>\n  sorcha org create --name \"My Org\" --subdomain myorg")
    {
        Subcommands.Add(new OrgListCommand(clientFactory, authService, configService));
        Subcommands.Add(new OrgGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new OrgCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new OrgUpdateCommand(clientFactory, authService, configService));
        Subcommands.Add(new OrgDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new OrgWalletCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all organizations.
/// </summary>
public class OrgListCommand : Command
{
    public OrgListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all organizations")
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
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Tenant Service client
                var client = await clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                var response = await client.ListOrganizationsAsync($"Bearer {token}");

                // Display results
                if (response?.Organizations == null || response.Organizations.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No organizations found.");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, response.Organizations);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.Organizations.Count} organization(s) (Total: {response.TotalCount}):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Name",-30} {"Subdomain",-20} {"Status",-10}");
                Console.WriteLine(new string('-', 100));
                foreach (var org in response.Organizations)
                {
                    Console.WriteLine($"{org.Id,-38} {org.Name,-30} {org.Subdomain,-20} {org.Status,-10}");
                }

                return ExitCodes.Success;
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
                ConsoleHelper.WriteError($"Failed to list organizations: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets an organization by ID.
/// </summary>
public class OrgGetCommand : Command
{
    private readonly Option<string> _idOption;

    public OrgGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get an organization by ID")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Organization ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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
                var org = await client.GetOrganizationAsync(id, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, org);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Organization details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:          {org.Id}");
                Console.WriteLine($"  Name:        {org.Name}");
                Console.WriteLine($"  Subdomain:   {org.Subdomain}");
                Console.WriteLine($"  Status:      {org.Status}");
                Console.WriteLine($"  Created:     {org.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                if (org.Branding != null)
                {
                    Console.WriteLine($"  Branding:");
                    if (!string.IsNullOrEmpty(org.Branding.LogoUrl))
                        Console.WriteLine($"    Logo:      {org.Branding.LogoUrl}");
                    if (!string.IsNullOrEmpty(org.Branding.PrimaryColor))
                        Console.WriteLine($"    Primary:   {org.Branding.PrimaryColor}");
                    if (!string.IsNullOrEmpty(org.Branding.CompanyTagline))
                        Console.WriteLine($"    Tagline:   {org.Branding.CompanyTagline}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{id}' not found.");
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
                ConsoleHelper.WriteError($"Failed to get organization: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new organization.
/// </summary>
public class OrgCreateCommand : Command
{
    private readonly Option<string> _nameOption;
    private readonly Option<string> _subdomainOption;

    public OrgCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a new organization")
    {
        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Organization name",
            Required = true
        };

        _subdomainOption = new Option<string>("--subdomain", "-s")
        {
            Description = "Unique subdomain (3-50 alphanumeric characters with hyphens)",
            Required = true
        };

        Options.Add(_nameOption);
        Options.Add(_subdomainOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;
            var subdomain = parseResult.GetValue(_subdomainOption)!;

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
                var request = new CreateOrganizationRequest
                {
                    Name = name,
                    Subdomain = subdomain
                };

                // Call API
                var org = await client.CreateOrganizationAsync(request, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, org);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Organization created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:          {org.Id}");
                Console.WriteLine($"  Name:        {org.Name}");
                Console.WriteLine($"  Subdomain:   {org.Subdomain}");
                Console.WriteLine($"  Status:      {org.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"Organization with that name or subdomain already exists.");
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
                ConsoleHelper.WriteError($"Failed to create organization: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Updates an organization.
/// </summary>
public class OrgUpdateCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string?> _nameOption;
    private readonly Option<OrganizationStatus?> _statusOption;

    public OrgUpdateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("update", "Update an organization")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Organization ID",
            Required = true
        };

        _nameOption = new Option<string?>("--name", "-n")
        {
            Description = "Organization name"
        };

        _statusOption = new Option<OrganizationStatus?>("--status", "-s")
        {
            Description = "Organization status (Active, Suspended, Inactive)"
        };

        Options.Add(_idOption);
        Options.Add(_nameOption);
        Options.Add(_statusOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var name = parseResult.GetValue(_nameOption);
            var status = parseResult.GetValue(_statusOption);

            try
            {
                // Validate that at least one field is provided
                if (string.IsNullOrEmpty(name) && !status.HasValue)
                {
                    ConsoleHelper.WriteError("At least one field (--name or --status) must be provided.");
                    return ExitCodes.ValidationError;
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

                // Build request
                var request = new UpdateOrganizationRequest
                {
                    Name = name,
                    Status = status
                };

                // Call API
                var org = await client.UpdateOrganizationAsync(id, request, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, org);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Organization updated successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:          {org.Id}");
                Console.WriteLine($"  Name:        {org.Name}");
                Console.WriteLine($"  Subdomain:   {org.Subdomain}");
                Console.WriteLine($"  Status:      {org.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
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
                ConsoleHelper.WriteError($"Failed to update organization: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes an organization.
/// </summary>
public class OrgDeleteCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<bool> _confirmOption;

    public OrgDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete an organization")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Organization ID",
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
                // Confirm deletion
                if (!confirm)
                {
                    if (!ConsoleHelper.Confirm($"Are you sure you want to delete organization '{id}'?", defaultYes: false))
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
                await client.DeleteOrganizationAsync(id, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Organization '{id}' deleted successfully.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{id}' not found.");
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
                ConsoleHelper.WriteError($"Failed to delete organization: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
