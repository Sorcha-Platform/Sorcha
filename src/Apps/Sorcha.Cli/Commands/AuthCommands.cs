// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Authentication management commands.
/// </summary>
public class AuthCommand : Command
{
    public AuthCommand(
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("auth", "Manage authentication and login sessions")
    {
        Subcommands.Add(new AuthLoginCommand(authService, configService));
        Subcommands.Add(new AuthLogoutCommand(authService, configService));
        Subcommands.Add(new AuthStatusCommand(authService, configService));
    }
}

/// <summary>
/// Login command for user and service principal authentication.
/// </summary>
public class AuthLoginCommand : Command
{
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configService;

    private readonly Option<string?> _usernameOption;
    private readonly Option<string?> _passwordOption;
    private readonly Option<string?> _clientIdOption;
    private readonly Option<string?> _clientSecretOption;
    private readonly Option<bool> _interactiveOption;
    private readonly Option<string?> _profileOption;

    public AuthLoginCommand(
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("login", "Authenticate as a user or service principal")
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "Username for user authentication"
        };

        _passwordOption = new Option<string?>("--password", "-p")
        {
            Description = "Password for user authentication (INSECURE - use interactive mode instead)"
        };

        _clientIdOption = new Option<string?>("--client-id", "-c")
        {
            Description = "Client ID for service principal authentication"
        };

        _clientSecretOption = new Option<string?>("--client-secret", "-s")
        {
            Description = "Client secret for service principal authentication (INSECURE - use interactive mode instead)"
        };

        _interactiveOption = new Option<bool>("--interactive", "-i")
        {
            Description = "Use interactive login (prompts for credentials securely)",
            DefaultValueFactory = _ => true
        };

        _profileOption = new Option<string?>("--profile")
        {
            Description = "Profile to authenticate with (defaults to active profile)"
        };

        Options.Add(_usernameOption);
        Options.Add(_passwordOption);
        Options.Add(_clientIdOption);
        Options.Add(_clientSecretOption);
        Options.Add(_interactiveOption);
        Options.Add(_profileOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var username = parseResult.GetValue(_usernameOption);
            var password = parseResult.GetValue(_passwordOption);
            var clientId = parseResult.GetValue(_clientIdOption);
            var clientSecret = parseResult.GetValue(_clientSecretOption);
            var interactive = parseResult.GetValue(_interactiveOption);
            var profileName = parseResult.GetValue(_profileOption);

            try
            {
                // Determine profile
                if (string.IsNullOrEmpty(profileName))
                {
                    var activeProfile = await _configService.GetActiveProfileAsync();
                    profileName = activeProfile?.Name ?? "dev";
                }

                ConsoleHelper.WriteInfo($"Authenticating with profile: {profileName}");

                // Determine authentication type
                bool isServicePrincipal = !string.IsNullOrEmpty(clientId) || !string.IsNullOrEmpty(clientSecret);

                if (isServicePrincipal)
                {
                    await LoginServicePrincipalAsync(clientId, clientSecret, interactive, profileName);
                }
                else
                {
                    await LoginUserAsync(username, password, interactive, profileName);
                }

                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Authentication failed: {ex.Message}");
                return ExitCodes.AuthenticationError;
            }
        });
    }

    private async Task LoginUserAsync(string? username, string? password, bool interactive, string profileName)
    {
        // Get credentials
        if (interactive || string.IsNullOrEmpty(username))
        {
            ConsoleHelper.WriteWarning("Interactive User Login");
            username = ConsoleHelper.ReadLine("Username", username);
        }

        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        if (interactive || string.IsNullOrEmpty(password))
        {
            if (!string.IsNullOrEmpty(password))
            {
                ConsoleHelper.WriteWarning("Warning: Password provided as command-line argument is INSECURE!");
                ConsoleHelper.WriteWarning("  Use interactive mode to avoid exposing credentials in process list.");
            }
            password = ConsoleHelper.ReadPassword("Password: ");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Password is required.");
        }

        // Authenticate
        var request = new LoginRequest
        {
            Username = username,
            Password = password
        };

        ConsoleHelper.WriteInfo($"Authenticating user: {username}");
        var response = await _authService.LoginAsync(request, profileName);

        ConsoleHelper.WriteSuccess("Authentication successful!");
        ConsoleHelper.WriteInfo($"Access token expires in {response.ExpiresIn} seconds");

        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            ConsoleHelper.WriteInfo("Refresh token received (will auto-refresh)");
        }
    }

    private async Task LoginServicePrincipalAsync(string? clientId, string? clientSecret, bool interactive, string profileName)
    {
        // Get credentials
        if (interactive || string.IsNullOrEmpty(clientId))
        {
            ConsoleHelper.WriteWarning("Interactive Service Principal Login");
            clientId = ConsoleHelper.ReadLine("Client ID", clientId);
        }

        if (string.IsNullOrEmpty(clientId))
        {
            throw new InvalidOperationException("Client ID is required.");
        }

        if (interactive || string.IsNullOrEmpty(clientSecret))
        {
            if (!string.IsNullOrEmpty(clientSecret))
            {
                ConsoleHelper.WriteWarning("Warning: Client secret provided as command-line argument is INSECURE!");
                ConsoleHelper.WriteWarning("  Use interactive mode to avoid exposing credentials in process list.");
            }
            clientSecret = ConsoleHelper.ReadPassword("Client Secret: ");
        }

        if (string.IsNullOrEmpty(clientSecret))
        {
            throw new InvalidOperationException("Client secret is required.");
        }

        // Authenticate
        var request = new ServicePrincipalLoginRequest
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };

        ConsoleHelper.WriteInfo($"Authenticating service principal: {clientId}");
        var response = await _authService.LoginServicePrincipalAsync(request, profileName);

        ConsoleHelper.WriteSuccess("Service principal authentication successful!");
        ConsoleHelper.WriteInfo($"Access token expires in {response.ExpiresIn} seconds ({response.ExpiresIn / 3600.0:F1} hours)");
    }
}

/// <summary>
/// Logout command to clear cached authentication tokens.
/// </summary>
public class AuthLogoutCommand : Command
{
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configService;

    private readonly Option<bool> _allOption;
    private readonly Option<string?> _profileOption;

    public AuthLogoutCommand(
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("logout", "Clear cached authentication tokens")
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _allOption = new Option<bool>("--all", "-a")
        {
            Description = "Clear tokens for all profiles (default: current profile only)"
        };

        _profileOption = new Option<string?>("--profile")
        {
            Description = "Profile to logout from (defaults to active profile)"
        };

        Options.Add(_allOption);
        Options.Add(_profileOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var all = parseResult.GetValue(_allOption);
            var profileName = parseResult.GetValue(_profileOption);

            try
            {
                if (all)
                {
                    if (ConsoleHelper.Confirm("Clear authentication tokens for ALL profiles?", defaultYes: false))
                    {
                        await _authService.LogoutAllAsync();
                        ConsoleHelper.WriteSuccess("Logged out from all profiles");
                    }
                    else
                    {
                        ConsoleHelper.WriteInfo("Logout cancelled");
                    }
                }
                else
                {
                    // Determine profile
                    if (string.IsNullOrEmpty(profileName))
                    {
                        var activeProfile = await _configService.GetActiveProfileAsync();
                        profileName = activeProfile?.Name ?? "dev";
                    }

                    await _authService.LogoutAsync(profileName);
                    ConsoleHelper.WriteSuccess($"Logged out from profile: {profileName}");
                }

                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Logout failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Status command to check authentication status.
/// </summary>
public class AuthStatusCommand : Command
{
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configService;

    private readonly Option<string?> _profileOption;

    public AuthStatusCommand(
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Check authentication status for the current profile")
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _profileOption = new Option<string?>("--profile")
        {
            Description = "Profile to check status for (defaults to active profile)"
        };

        Options.Add(_profileOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var profileName = parseResult.GetValue(_profileOption);

            try
            {
                // Determine profile
                if (string.IsNullOrEmpty(profileName))
                {
                    var activeProfile = await _configService.GetActiveProfileAsync();
                    profileName = activeProfile?.Name ?? "dev";
                }

                var isAuthenticated = await _authService.IsAuthenticatedAsync(profileName);
                var token = await _authService.GetAccessTokenAsync(profileName);

                Console.WriteLine();
                Console.WriteLine($"Profile: {profileName}");
                Console.WriteLine($"Status: {(isAuthenticated ? "Authenticated" : "Not authenticated")}");

                if (isAuthenticated && !string.IsNullOrEmpty(token))
                {
                    // Try to decode token expiration (JWT)
                    try
                    {
                        var parts = token.Split('.');
                        if (parts.Length == 3)
                        {
                            var payload = System.Text.Json.JsonDocument.Parse(
                                Convert.FromBase64String(PadBase64(parts[1])));

                            if (payload.RootElement.TryGetProperty("exp", out var expElement))
                            {
                                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                                var remaining = exp - DateTimeOffset.UtcNow;

                                if (remaining.TotalSeconds > 0)
                                {
                                    Console.WriteLine($"Token expires: {exp:u} ({FormatTimeRemaining(remaining)} remaining)");
                                }
                                else
                                {
                                    ConsoleHelper.WriteWarning("Token has expired! Re-authentication recommended.");
                                }
                            }

                            if (payload.RootElement.TryGetProperty("sub", out var subElement))
                            {
                                Console.WriteLine($"Subject: {subElement.GetString()}");
                            }

                            if (payload.RootElement.TryGetProperty("token_type", out var typeElement))
                            {
                                Console.WriteLine($"Type: {typeElement.GetString()}");
                            }
                        }
                    }
                    catch (FormatException)
                    {
                        // Not valid base64 - token may not be a JWT
                    }
                    catch (JsonException)
                    {
                        // Not valid JSON payload - token format unrecognized
                    }
                }
                else
                {
                    ConsoleHelper.WriteInfo($"Run 'sorcha auth login --profile {profileName}' to authenticate");
                }

                Console.WriteLine();
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to check status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    private static string PadBase64(string base64)
    {
        // JWT uses base64url encoding without padding
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 += new string('=', 4 - padding);
        }
        return base64.Replace('-', '+').Replace('_', '/');
    }

    private static string FormatTimeRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return $"{remaining.TotalDays:F1} days";
        }
        if (remaining.TotalHours >= 1)
        {
            return $"{remaining.TotalHours:F1} hours";
        }
        if (remaining.TotalMinutes >= 1)
        {
            return $"{remaining.TotalMinutes:F0} minutes";
        }
        return $"{remaining.TotalSeconds:F0} seconds";
    }
}
