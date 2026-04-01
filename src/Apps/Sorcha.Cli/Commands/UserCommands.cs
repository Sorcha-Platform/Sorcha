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
/// User management commands.
/// </summary>
public class UserCommand : Command
{
    public UserCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("user", "Manage users within organizations\n\nExamples:\n  sorcha user list\n  sorcha user create --email user@example.com --first-name John --last-name Doe\n  sorcha user get --id <user-id>")
    {
        Subcommands.Add(new UserListCommand(clientFactory, authService, configService));
        Subcommands.Add(new UserGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new UserCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new UserUpdateCommand(clientFactory, authService, configService));
        Subcommands.Add(new UserDeleteCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all users in an organization.
/// </summary>
public class UserListCommand : Command
{
    private readonly Option<string> _orgIdOption;

    public UserListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all users in an organization")
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
                var users = await client.ListUsersAsync(orgId, $"Bearer {token}");

                // Display results
                if (users == null || users.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No users found.");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, users);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {users.Count} user(s) in organization '{orgId}':");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-30} {"Username",-20} {"Email",-30} {"Active",-8} {"Roles",-20}");
                Console.WriteLine(new string('-', 110));
                foreach (var user in users)
                {
                    var roles = user.Roles != null && user.Roles.Count > 0 ? string.Join(", ", user.Roles) : "(none)";
                    var active = user.IsActive ? "Yes" : "No";
                    Console.WriteLine($"{user.Id,-30} {user.Username,-20} {user.Email,-30} {active,-8} {roles,-20}");
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
                ConsoleHelper.WriteError($"Failed to list users: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a user by ID.
/// </summary>
public class UserGetCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _userIdOption;

    public UserGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a user by ID")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _userIdOption = new Option<string>("--user-id", "-u")
        {
            Description = "User ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_userIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var userId = parseResult.GetValue(_userIdOption)!;

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
                var user = await client.GetUserAsync(orgId, userId, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, user);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("User details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {user.Id}");
                Console.WriteLine($"  Organization ID: {user.OrganizationId}");
                Console.WriteLine($"  Username:        {user.Username}");
                Console.WriteLine($"  Email:           {user.Email}");
                if (!string.IsNullOrEmpty(user.FirstName) || !string.IsNullOrEmpty(user.LastName))
                {
                    var fullName = $"{user.FirstName ?? ""} {user.LastName ?? ""}".Trim();
                    Console.WriteLine($"  Name:            {fullName}");
                }
                Console.WriteLine($"  Active:          {(user.IsActive ? "Yes" : "No")}");
                if (user.Roles != null && user.Roles.Count > 0)
                {
                    Console.WriteLine($"  Roles:           {string.Join(", ", user.Roles)}");
                }
                Console.WriteLine($"  Created:         {user.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                if (user.UpdatedAt.HasValue)
                {
                    Console.WriteLine($"  Updated:         {user.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"User '{userId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to get user: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new user in an organization.
/// </summary>
public class UserCreateCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _usernameOption;
    private readonly Option<string> _emailOption;
    private readonly Option<string> _passwordOption;
    private readonly Option<string?> _firstNameOption;
    private readonly Option<string?> _lastNameOption;
    private readonly Option<string?> _rolesOption;

    public UserCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a new user in an organization")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _usernameOption = new Option<string>("--username", "-u")
        {
            Description = "Username",
            Required = true
        };

        _emailOption = new Option<string>("--email", "-e")
        {
            Description = "Email address",
            Required = true
        };

        _passwordOption = new Option<string>("--password", "-p")
        {
            Description = "Password",
            Required = true
        };

        _firstNameOption = new Option<string?>("--first-name", "-f")
        {
            Description = "First name"
        };

        _lastNameOption = new Option<string?>("--last-name", "-l")
        {
            Description = "Last name"
        };

        _rolesOption = new Option<string?>("--roles", "-r")
        {
            Description = "Comma-separated list of roles (e.g., Admin,User)"
        };

        Options.Add(_orgIdOption);
        Options.Add(_usernameOption);
        Options.Add(_emailOption);
        Options.Add(_passwordOption);
        Options.Add(_firstNameOption);
        Options.Add(_lastNameOption);
        Options.Add(_rolesOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var username = parseResult.GetValue(_usernameOption)!;
            var email = parseResult.GetValue(_emailOption)!;
            var password = parseResult.GetValue(_passwordOption)!;
            var firstName = parseResult.GetValue(_firstNameOption);
            var lastName = parseResult.GetValue(_lastNameOption);
            var roles = parseResult.GetValue(_rolesOption);

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
                var request = new CreateUserRequest
                {
                    Username = username,
                    Email = email,
                    Password = password,
                    FirstName = firstName,
                    LastName = lastName,
                    Roles = !string.IsNullOrEmpty(roles)
                        ? roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : null
                };

                // Call API
                var user = await client.CreateUserAsync(orgId, request, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, user);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"User created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {user.Id}");
                Console.WriteLine($"  Organization ID: {user.OrganizationId}");
                Console.WriteLine($"  Username:        {user.Username}");
                Console.WriteLine($"  Email:           {user.Email}");
                if (!string.IsNullOrEmpty(user.FirstName) || !string.IsNullOrEmpty(user.LastName))
                {
                    var fullName = $"{user.FirstName ?? ""} {user.LastName ?? ""}".Trim();
                    Console.WriteLine($"  Name:            {fullName}");
                }
                if (user.Roles != null && user.Roles.Count > 0)
                {
                    Console.WriteLine($"  Roles:           {string.Join(", ", user.Roles)}");
                }

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
                ConsoleHelper.WriteError($"User with that username or email already exists.");
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
                ConsoleHelper.WriteError($"Failed to create user: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Updates a user.
/// </summary>
public class UserUpdateCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _userIdOption;
    private readonly Option<string?> _emailOption;
    private readonly Option<string?> _firstNameOption;
    private readonly Option<string?> _lastNameOption;
    private readonly Option<bool?> _isActiveOption;
    private readonly Option<string?> _rolesOption;

    public UserUpdateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("update", "Update a user")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _userIdOption = new Option<string>("--user-id", "-u")
        {
            Description = "User ID",
            Required = true
        };

        _emailOption = new Option<string?>("--email", "-e")
        {
            Description = "New email address"
        };

        _firstNameOption = new Option<string?>("--first-name", "-f")
        {
            Description = "New first name"
        };

        _lastNameOption = new Option<string?>("--last-name", "-l")
        {
            Description = "New last name"
        };

        _isActiveOption = new Option<bool?>("--active", "-a")
        {
            Description = "Set active status (true/false)"
        };

        _rolesOption = new Option<string?>("--roles", "-r")
        {
            Description = "Comma-separated list of roles (replaces existing roles)"
        };

        Options.Add(_orgIdOption);
        Options.Add(_userIdOption);
        Options.Add(_emailOption);
        Options.Add(_firstNameOption);
        Options.Add(_lastNameOption);
        Options.Add(_isActiveOption);
        Options.Add(_rolesOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var userId = parseResult.GetValue(_userIdOption)!;
            var email = parseResult.GetValue(_emailOption);
            var firstName = parseResult.GetValue(_firstNameOption);
            var lastName = parseResult.GetValue(_lastNameOption);
            var isActive = parseResult.GetValue(_isActiveOption);
            var roles = parseResult.GetValue(_rolesOption);

            try
            {
                // Validate that at least one field is provided
                if (string.IsNullOrEmpty(email) && string.IsNullOrEmpty(firstName) &&
                    string.IsNullOrEmpty(lastName) && !isActive.HasValue && string.IsNullOrEmpty(roles))
                {
                    ConsoleHelper.WriteError("At least one field (--email, --first-name, --last-name, --active, or --roles) must be provided.");
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
                var request = new UpdateUserRequest
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    IsActive = isActive,
                    Roles = !string.IsNullOrEmpty(roles)
                        ? roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                        : null
                };

                // Call API
                var user = await client.UpdateUserAsync(orgId, userId, request, $"Bearer {token}");

                // Display results
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, user);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"User updated successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:       {user.Id}");
                Console.WriteLine($"  Username: {user.Username}");
                Console.WriteLine($"  Email:    {user.Email}");
                if (!string.IsNullOrEmpty(user.FirstName) || !string.IsNullOrEmpty(user.LastName))
                {
                    var fullName = $"{user.FirstName ?? ""} {user.LastName ?? ""}".Trim();
                    Console.WriteLine($"  Name:     {fullName}");
                }
                Console.WriteLine($"  Active:   {(user.IsActive ? "Yes" : "No")}");
                if (user.Roles != null && user.Roles.Count > 0)
                {
                    Console.WriteLine($"  Roles:    {string.Join(", ", user.Roles)}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"User '{userId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to update user: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a user.
/// </summary>
public class UserDeleteCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _userIdOption;
    private readonly Option<bool> _confirmOption;

    public UserDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a user")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _userIdOption = new Option<string>("--user-id", "-u")
        {
            Description = "User ID",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_orgIdOption);
        Options.Add(_userIdOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var userId = parseResult.GetValue(_userIdOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                // Confirm deletion
                if (!confirm)
                {
                    if (!ConsoleHelper.Confirm($"Are you sure you want to delete user '{userId}' from organization '{orgId}'?", defaultYes: false))
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
                await client.DeleteUserAsync(orgId, userId, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"User '{userId}' deleted successfully.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"User '{userId}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to delete user: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
