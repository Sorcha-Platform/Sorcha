// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Configuration management commands (example command using BaseCommand).
/// </summary>
public class ConfigCommand : BaseCommand
{
    public ConfigCommand()
        : base("config", "Manage CLI configuration and profiles\n\nExamples:\n  sorcha config list\n  sorcha config init --profile prod --url https://sorcha.example.com")
    {
        Subcommands.Add(new ConfigInitCommand());
        Subcommands.Add(new ProfileListCommand());
        Subcommands.Add(new ProfileSetActiveCommand());
    }

    protected override Task<int> ExecuteAsync(CommandContext context)
    {
        // Show help when config is called without subcommand
        WriteMessage(context, "Use 'sorcha config --help' to see available commands.");
        return Task.FromResult(ExitCodes.Success);
    }
}

/// <summary>
/// Lists all available profiles.
/// </summary>
public class ProfileListCommand : BaseCommand
{
    public ProfileListCommand()
        : base("list", "List all available profiles")
    {
    }

    protected override async Task<int> ExecuteAsync(CommandContext context)
    {
        var configService = new ConfigurationService();
        var profiles = await configService.ListProfilesAsync();
        var config = await configService.GetConfigurationAsync();

        var profileList = profiles.Select(p => new
        {
            p.Name,
            ServiceUrl = !string.IsNullOrWhiteSpace(p.ServiceUrl) ? p.ServiceUrl : p.GetTenantServiceUrl(),
            Active = !string.IsNullOrEmpty(config.ActiveProfile) && p.Name == config.ActiveProfile
        });

        WriteCollection(context, profileList);
        return ExitCodes.Success;
    }
}

/// <summary>
/// Sets the active profile.
/// </summary>
public class ProfileSetActiveCommand : Command
{
    public ProfileSetActiveCommand()
        : base("set-active", "Set the active profile")
    {
        var profileNameArgument = new Argument<string>("name")
        {
            Description = "Name of the profile to activate"
        };

        Arguments.Add(profileNameArgument);

        this.SetAction(async (parseResult, ct) =>
        {
            var profileName = parseResult.GetValue(profileNameArgument)!;
            var configService = new ConfigurationService();

            try
            {
                // Check if profile exists
                var profile = await configService.GetProfileAsync(profileName);
                if (profile == null)
                {
                    Console.Error.WriteLine($"Error: Profile '{profileName}' does not exist.");
                    Console.WriteLine("\nAvailable profiles:");

                    var profiles = await configService.ListProfilesAsync();
                    foreach (var p in profiles)
                    {
                        Console.WriteLine($"  - {p.Name}");
                    }

                    return ExitCodes.ValidationError;
                }

                // Set active profile
                await configService.SetActiveProfileAsync(profileName);
                Console.WriteLine($"✓ Active profile set to '{profileName}'");
                Console.WriteLine($"  Tenant Service URL: {profile.TenantServiceUrl}");
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to set active profile: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Initializes a new CLI configuration profile.
/// CLI-BOOTSTRAP-001: Initialize CLI configuration profile with service URLs.
/// </summary>
public class ConfigInitCommand : Command
{
    private readonly Option<string> _profileOption;
    private readonly Option<string?> _serviceUrlOption;
    private readonly Option<string?> _tenantUrlOption;
    private readonly Option<string?> _registerUrlOption;
    private readonly Option<string?> _walletUrlOption;
    private readonly Option<string?> _peerUrlOption;
    private readonly Option<string?> _authUrlOption;
    private readonly Option<string> _clientIdOption;
    private readonly Option<bool> _verifySslOption;
    private readonly Option<int> _timeoutOption;
    private readonly Option<bool> _checkConnectivityOption;
    private readonly Option<bool> _setActiveOption;

    public ConfigInitCommand()
        : base("init", "Initialize a new configuration profile")
    {
        _profileOption = new Option<string>("--profile", "-p")
        {
            Description = "Profile name",
            DefaultValueFactory = _ => "docker"
        };

        _serviceUrlOption = new Option<string?>("--service-url", "-s")
        {
            Description = "Base URL for all services (recommended - e.g., 'http://localhost')"
        };

        _tenantUrlOption = new Option<string?>("--tenant-url", "-t")
        {
            Description = "Tenant Service URL (optional override)"
        };

        _registerUrlOption = new Option<string?>("--register-url", "-r")
        {
            Description = "Register Service URL (optional override)"
        };

        _walletUrlOption = new Option<string?>("--wallet-url", "-w")
        {
            Description = "Wallet Service URL (optional override)"
        };

        _peerUrlOption = new Option<string?>("--peer-url")
        {
            Description = "Peer Service URL (optional override)"
        };

        _authUrlOption = new Option<string?>("--auth-url", "-a")
        {
            Description = "Auth Token URL (optional override)"
        };

        _clientIdOption = new Option<string>("--client-id", "-c")
        {
            Description = "Default client ID",
            DefaultValueFactory = _ => "sorcha-cli"
        };

        _verifySslOption = new Option<bool>("--verify-ssl")
        {
            Description = "Verify SSL certificates",
            DefaultValueFactory = _ => false
        };

        _timeoutOption = new Option<int>("--timeout")
        {
            Description = "Request timeout in seconds",
            DefaultValueFactory = _ => 30
        };

        _checkConnectivityOption = new Option<bool>("--check-connectivity")
        {
            Description = "Verify connectivity to service URLs",
            DefaultValueFactory = _ => true
        };

        _setActiveOption = new Option<bool>("--set-active")
        {
            Description = "Set as active profile",
            DefaultValueFactory = _ => true
        };

        Options.Add(_profileOption);
        Options.Add(_serviceUrlOption);
        Options.Add(_tenantUrlOption);
        Options.Add(_registerUrlOption);
        Options.Add(_walletUrlOption);
        Options.Add(_peerUrlOption);
        Options.Add(_authUrlOption);
        Options.Add(_clientIdOption);
        Options.Add(_verifySslOption);
        Options.Add(_timeoutOption);
        Options.Add(_checkConnectivityOption);
        Options.Add(_setActiveOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profileName = parseResult.GetValue(_profileOption)!;
                var serviceUrl = parseResult.GetValue(_serviceUrlOption);
                var tenantUrl = parseResult.GetValue(_tenantUrlOption);
                var registerUrl = parseResult.GetValue(_registerUrlOption);
                var walletUrl = parseResult.GetValue(_walletUrlOption);
                var peerUrl = parseResult.GetValue(_peerUrlOption);
                var authUrl = parseResult.GetValue(_authUrlOption);
                var clientId = parseResult.GetValue(_clientIdOption)!;
                var verifySsl = parseResult.GetValue(_verifySslOption);
                var timeout = parseResult.GetValue(_timeoutOption);
                var checkConnectivity = parseResult.GetValue(_checkConnectivityOption);
                var setActive = parseResult.GetValue(_setActiveOption);

                var configService = new ConfigurationService();

                // Validate that either base service URL or specific URLs are provided
                if (string.IsNullOrWhiteSpace(serviceUrl) &&
                    string.IsNullOrWhiteSpace(tenantUrl) &&
                    string.IsNullOrWhiteSpace(registerUrl) &&
                    string.IsNullOrWhiteSpace(walletUrl) &&
                    string.IsNullOrWhiteSpace(peerUrl))
                {
                    Console.Error.WriteLine("Error: Either --service-url or at least one specific service URL must be provided.");
                    return ExitCodes.ValidationError;
                }

                // Create profile
                var profile = new Profile
                {
                    Name = profileName,
                    ServiceUrl = serviceUrl,
                    TenantServiceUrl = tenantUrl,
                    RegisterServiceUrl = registerUrl,
                    WalletServiceUrl = walletUrl,
                    PeerServiceUrl = peerUrl,
                    AuthTokenUrl = authUrl,
                    DefaultClientId = clientId,
                    VerifySsl = verifySsl,
                    TimeoutSeconds = timeout
                };

                // Check if profile already exists
                var existingProfile = await configService.GetProfileAsync(profileName);
                var isUpdate = existingProfile != null;

                // Validate connectivity if requested
                var connectivityStatus = "skipped";
                if (checkConnectivity)
                {
                    Console.WriteLine($"Checking connectivity to service URLs...");
                    connectivityStatus = await CheckConnectivityAsync(profile);
                }

                // Save profile
                await configService.UpsertProfileAsync(profile);

                // Set as active if requested
                if (setActive)
                {
                    await configService.SetActiveProfileAsync(profileName);
                }

                // Output result
                Console.WriteLine($"✓ Profile '{profileName}' {(isUpdate ? "updated" : "created")}");

                if (!string.IsNullOrWhiteSpace(serviceUrl))
                {
                    Console.WriteLine($"  Base Service URL: {serviceUrl}");
                }

                Console.WriteLine($"  Tenant Service:   {profile.GetTenantServiceUrl()}{(string.IsNullOrWhiteSpace(tenantUrl) ? " (from base)" : "")}");
                Console.WriteLine($"  Register Service: {profile.GetRegisterServiceUrl()}{(string.IsNullOrWhiteSpace(registerUrl) ? " (from base)" : "")}");
                Console.WriteLine($"  Wallet Service:   {profile.GetWalletServiceUrl()}{(string.IsNullOrWhiteSpace(walletUrl) ? " (from base)" : "")}");
                Console.WriteLine($"  Peer Service:     {profile.GetPeerServiceUrl()}{(string.IsNullOrWhiteSpace(peerUrl) ? " (from base)" : "")}");
                Console.WriteLine($"  Auth Token URL:   {profile.GetAuthTokenUrl()}{(string.IsNullOrWhiteSpace(authUrl) ? " (from base)" : "")}");
                Console.WriteLine($"  Connectivity:     {connectivityStatus}");
                if (setActive)
                {
                    Console.WriteLine($"  Active profile:   {profileName}");
                }
                Console.WriteLine($"  Config file:      {configService.GetConfigFilePath()}");
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to initialize configuration: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    private static async Task<string> CheckConnectivityAsync(Profile profile)
    {
        var urls = new[]
        {
            ("Tenant", profile.GetTenantServiceUrl()),
            ("Register", profile.GetRegisterServiceUrl()),
            ("Wallet", profile.GetWalletServiceUrl()),
            ("Peer", profile.GetPeerServiceUrl())
        };

        var results = new List<(string Service, bool Success)>();

        HttpClient httpClient;
        if (!profile.VerifySsl)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }
        else
        {
            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        using (httpClient)
        {
            foreach (var (service, url) in urls)
            {
                try
                {
                    // Try to reach the health endpoint
                    var healthUrl = url.TrimEnd('/') + "/health";
                    var response = await httpClient.GetAsync(healthUrl);
                    results.Add((service, response.IsSuccessStatusCode));

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"    {service}: OK");
                    }
                    else
                    {
                        Console.WriteLine($"    {service}: HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    results.Add((service, false));
                    Console.WriteLine($"    {service}: {ex.Message}");
                }
            }
        }

        var successCount = results.Count(r => r.Success);
        var totalCount = results.Count;

        return successCount == totalCount ? "passed"
            : successCount > 0 ? "partial"
            : "failed";
    }
}
