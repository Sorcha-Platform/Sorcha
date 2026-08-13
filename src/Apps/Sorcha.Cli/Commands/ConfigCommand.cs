// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Text.Json;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Configuration management commands (example command using BaseCommand).
/// </summary>
public class ConfigCommand : BaseCommand
{
    public ConfigCommand(IConfigurationService configService, IAuthenticationService authService)
        : base("config", "Manage CLI configuration and profiles\n\nExamples:\n  sorcha config list\n  sorcha config init --profile prod --service-url https://sorcha.example.com\n  sorcha config view\n  sorcha config validate\n  sorcha config export --output config.json")
    {
        Subcommands.Add(new ConfigInitCommand());
        Subcommands.Add(new ProfileListCommand(configService));
        Subcommands.Add(new ProfileSetActiveCommand());
        Subcommands.Add(new ConfigViewCommand(configService, authService));
        Subcommands.Add(new ConfigValidateCommand(configService));
        Subcommands.Add(new ConfigExportCommand());
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
    private readonly IConfigurationService _configService;

    public ProfileListCommand(IConfigurationService configService)
        : base("list", "List all available profiles")
    {
        _configService = configService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context)
    {
        var profiles = await _configService.ListProfilesAsync();
        var config = await _configService.GetConfigurationAsync();

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
    private readonly Option<string?> _serviceAuthUrlOption;
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
            Description = "Auth Token URL for browser/human grants — password, refresh_token (optional override)"
        };

        _serviceAuthUrlOption = new Option<string?>("--service-auth-url")
        {
            Description = "Service-principal (client_credentials) token URL — internal-only Tenant "
                + "Service route, not reachable through the public API Gateway (#1397/#1406). "
                + "Optional override."
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
        Options.Add(_serviceAuthUrlOption);
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
                var serviceAuthUrl = parseResult.GetValue(_serviceAuthUrlOption);
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
                    ServiceAuthTokenUrl = serviceAuthUrl,
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
                Console.WriteLine($"  Service Auth URL: {profile.GetServiceAuthTokenUrl()}{(string.IsNullOrWhiteSpace(serviceAuthUrl) ? " (derived — internal-only)" : "")}");
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
            Console.Error.WriteLine("[WARN] SSL certificate verification is disabled — do not use in production.");
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

/// <summary>
/// Displays the current configuration including profile, API URLs, and auth status.
/// </summary>
public class ConfigViewCommand : BaseCommand
{
    private readonly IConfigurationService _configService;
    private readonly IAuthenticationService _authService;

    public ConfigViewCommand(IConfigurationService configService, IAuthenticationService authService)
        : base("view", "Display current configuration details\n\nExamples:\n  sorcha config view\n  sorcha config view --output json")
    {
        _configService = configService;
        _authService = authService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context)
    {
        var config = await _configService.GetConfigurationAsync();
        var profile = await _configService.GetActiveProfileAsync();

        if (profile == null)
        {
            ConsoleHelper.WriteWarning("No active profile configured. Run 'sorcha config init' first.");
            return ExitCodes.ConfigurationError;
        }

        // Check auth status
        var isAuthenticated = await _authService.IsAuthenticatedAsync(profile.Name);
        string? tokenExpiry = null;

        if (isAuthenticated)
        {
            var token = await _authService.GetAccessTokenAsync(profile.Name);
            if (!string.IsNullOrEmpty(token))
            {
                // Decode JWT to get expiry
                tokenExpiry = TryGetTokenExpiry(token);
            }
        }

        var configView = new
        {
            ProfileName = profile.Name,
            BaseServiceUrl = profile.ServiceUrl ?? "(not set)",
            TenantServiceUrl = profile.GetTenantServiceUrl(),
            RegisterServiceUrl = profile.GetRegisterServiceUrl(),
            WalletServiceUrl = profile.GetWalletServiceUrl(),
            PeerServiceUrl = profile.GetPeerServiceUrl(),
            BlueprintServiceUrl = profile.GetBlueprintServiceUrl(),
            ValidatorServiceUrl = profile.GetValidatorServiceUrl(),
            GatewayUrl = profile.GetGatewayUrl(),
            AuthTokenUrl = profile.GetAuthTokenUrl(),
            ServiceAuthTokenUrl = profile.GetServiceAuthTokenUrl(),
            VerifySsl = profile.VerifySsl,
            TimeoutSeconds = profile.TimeoutSeconds,
            Authenticated = isAuthenticated,
            TokenExpiry = tokenExpiry ?? "(not authenticated)",
            ConfigFile = _configService.GetConfigFilePath()
        };

        var outputFormat = context.OutputFormat;
        if (OutputHelper.IsStructuredFormat(outputFormat))
        {
            var formatter = OutputHelper.GetFormatter(outputFormat);
            Console.WriteLine(formatter.FormatSingle(configView));
            return ExitCodes.Success;
        }

        // Table-style display
        Console.WriteLine($"Profile:            {profile.Name}");
        Console.WriteLine($"Base Service URL:   {profile.ServiceUrl ?? "(not set)"}");
        Console.WriteLine();
        Console.WriteLine("Service URLs:");
        Console.WriteLine($"  Tenant:           {profile.GetTenantServiceUrl()}");
        Console.WriteLine($"  Register:         {profile.GetRegisterServiceUrl()}");
        Console.WriteLine($"  Wallet:           {profile.GetWalletServiceUrl()}");
        Console.WriteLine($"  Peer:             {profile.GetPeerServiceUrl()}");
        Console.WriteLine($"  Blueprint:        {profile.GetBlueprintServiceUrl()}");
        Console.WriteLine($"  Validator:        {profile.GetValidatorServiceUrl()}");
        Console.WriteLine($"  Gateway:          {profile.GetGatewayUrl()}");
        Console.WriteLine($"  Auth Token:       {profile.GetAuthTokenUrl()}");
        Console.WriteLine($"  Service Auth:     {profile.GetServiceAuthTokenUrl()} (internal-only, #1397)");
        Console.WriteLine();
        Console.WriteLine("Settings:");
        Console.WriteLine($"  Verify SSL:       {profile.VerifySsl}");
        Console.WriteLine($"  Timeout:          {profile.TimeoutSeconds}s");
        Console.WriteLine();
        Console.WriteLine("Authentication:");

        if (isAuthenticated)
        {
            ConsoleHelper.WriteSuccess($"Authenticated (expires: {tokenExpiry ?? "unknown"})");
        }
        else
        {
            ConsoleHelper.WriteWarning("Not authenticated. Run 'sorcha auth login' to authenticate.");
        }

        Console.WriteLine();
        Console.WriteLine($"Config file:        {_configService.GetConfigFilePath()}");

        return ExitCodes.Success;
    }

    /// <summary>
    /// Attempts to decode the JWT token expiry without full validation.
    /// </summary>
    private static string? TryGetTokenExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            // Decode the payload (base64url)
            var payload = parts[1];
            // Pad to multiple of 4
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                var exp = expElement.GetInt64();
                var expiry = DateTimeOffset.FromUnixTimeSeconds(exp);
                return expiry.ToString("yyyy-MM-dd HH:mm:ss UTC");
            }
        }
        catch
        {
            // Ignore decode errors
        }

        return null;
    }

}

/// <summary>
/// Validates connectivity to each Sorcha service by checking health endpoints.
/// </summary>
public class ConfigValidateCommand : BaseCommand
{
    private readonly IConfigurationService _configService;

    public ConfigValidateCommand(IConfigurationService configService)
        : base("validate", "Check connectivity to all configured services\n\nExamples:\n  sorcha config validate\n  sorcha config validate --output json")
    {
        _configService = configService;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context)
    {
        var profile = await _configService.GetActiveProfileAsync();

        if (profile == null)
        {
            ConsoleHelper.WriteWarning("No active profile configured. Run 'sorcha config init' first.");
            return ExitCodes.ConfigurationError;
        }

        Console.WriteLine($"Validating connectivity for profile '{profile.Name}'...");
        Console.WriteLine();

        var services = new[]
        {
            ("Tenant", profile.GetTenantServiceUrl()),
            ("Register", profile.GetRegisterServiceUrl()),
            ("Wallet", profile.GetWalletServiceUrl()),
            ("Peer", profile.GetPeerServiceUrl()),
            ("Blueprint", profile.GetBlueprintServiceUrl()),
            ("Validator", profile.GetValidatorServiceUrl()),
            ("Gateway", profile.GetGatewayUrl())
        };

        HttpClient httpClient;
        if (!profile.VerifySsl)
        {
            Console.Error.WriteLine("[WARN] SSL certificate verification is disabled — do not use in production.");
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }
        else
        {
            httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        var results = new List<object>();
        var allHealthy = true;

        using (httpClient)
        {
            foreach (var (serviceName, baseUrl) in services)
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    results.Add(new { Service = serviceName, Status = "Not Configured", ResponseTimeMs = (long?)null, Error = "URL not set" });
                    allHealthy = false;
                    continue;
                }

                var healthUrl = baseUrl.TrimEnd('/') + "/api/health";
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    var response = await httpClient.GetAsync(healthUrl);
                    stopwatch.Stop();

                    var status = response.IsSuccessStatusCode ? "Healthy" : $"HTTP {(int)response.StatusCode}";

                    results.Add(new { Service = serviceName, Status = status, ResponseTimeMs = (long?)stopwatch.ElapsedMilliseconds, Error = (string?)null });

                    if (!response.IsSuccessStatusCode)
                    {
                        allHealthy = false;
                    }
                }
                catch (TaskCanceledException)
                {
                    stopwatch.Stop();
                    results.Add(new { Service = serviceName, Status = "Timeout", ResponseTimeMs = (long?)stopwatch.ElapsedMilliseconds, Error = "Request timed out" });
                    allHealthy = false;
                }
                catch (HttpRequestException ex)
                {
                    stopwatch.Stop();
                    results.Add(new { Service = serviceName, Status = "Error", ResponseTimeMs = (long?)stopwatch.ElapsedMilliseconds, Error = ex.Message });
                    allHealthy = false;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    results.Add(new { Service = serviceName, Status = "Error", ResponseTimeMs = (long?)stopwatch.ElapsedMilliseconds, Error = ex.Message });
                    allHealthy = false;
                }
            }
        }

        var outputFormat = context.OutputFormat;
        if (OutputHelper.IsStructuredFormat(outputFormat))
        {
            var formatter = OutputHelper.GetFormatter(outputFormat);
            Console.WriteLine(formatter.FormatCollection(results));
            return allHealthy ? ExitCodes.Success : ExitCodes.NetworkError;
        }

        // Table display
        Console.WriteLine($"{"Service",-15} {"Status",-18} {"Response (ms)",13}  {"Error"}");
        Console.WriteLine(new string('-', 75));

        foreach (dynamic result in results)
        {
            string service = result.Service;
            string status = result.Status;
            long? responseTimeMs = result.ResponseTimeMs;
            string? error = result.Error;

            var statusColor = status switch
            {
                "Healthy" => ConsoleColor.Green,
                "Timeout" => ConsoleColor.Yellow,
                "Not Configured" => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            var originalColor = Console.ForegroundColor;
            Console.Write($"{service,-15} ");
            Console.ForegroundColor = statusColor;
            Console.Write($"{status,-18}");
            Console.ForegroundColor = originalColor;
            Console.Write($" {(responseTimeMs.HasValue ? responseTimeMs.Value.ToString() : "-"),13}");
            Console.WriteLine(!string.IsNullOrEmpty(error) ? $"  {error}" : string.Empty);
        }

        Console.WriteLine();

        if (allHealthy)
        {
            ConsoleHelper.WriteSuccess("All services are reachable.");
        }
        else
        {
            ConsoleHelper.WriteWarning("Some services are not reachable. Check the errors above.");
        }

        return allHealthy ? ExitCodes.Success : ExitCodes.NetworkError;
    }
}

/// <summary>
/// Exports the current configuration to a file in JSON or YAML format.
/// </summary>
public class ConfigExportCommand : Command
{
    public ConfigExportCommand()
        : base("export", "Export current configuration to a file\n\nExamples:\n  sorcha config export --output config.json\n  sorcha config export --output config.yaml --format yaml")
    {
        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output file path",
            Required = true
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Export format (json or yaml)",
            DefaultValueFactory = _ => "json"
        };

        Options.Add(outputOption);
        Options.Add(formatOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var outputPath = parseResult.GetValue(outputOption)!;
                var format = parseResult.GetValue(formatOption)!.ToLowerInvariant();

                if (format != "json" && format != "yaml")
                {
                    ConsoleHelper.WriteError("Invalid format. Use 'json' or 'yaml'.");
                    return ExitCodes.ValidationError;
                }

                var configService = new ConfigurationService();
                var config = await configService.GetConfigurationAsync();
                var profile = await configService.GetActiveProfileAsync();

                // Build export object (exclude sensitive data like tokens)
                var exportData = new
                {
                    ActiveProfile = config.ActiveProfile,
                    Profiles = (await configService.ListProfilesAsync()).Select(p => new
                    {
                        p.Name,
                        p.ServiceUrl,
                        p.TenantServiceUrl,
                        p.RegisterServiceUrl,
                        p.WalletServiceUrl,
                        p.PeerServiceUrl,
                        p.BlueprintServiceUrl,
                        p.ValidatorServiceUrl,
                        p.GatewayUrl,
                        p.AuthTokenUrl,
                        p.ServiceAuthTokenUrl,
                        p.DefaultClientId,
                        p.VerifySsl,
                        p.TimeoutSeconds,
                        p.CustomSettings
                    })
                };

                string content;
                if (format == "yaml")
                {
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .DisableAliases()
                        .Build();
                    content = serializer.Serialize(exportData);
                }
                else
                {
                    content = JsonSerializer.Serialize(exportData, SorchaJsonOptions.Default);
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(outputPath, content, ct);

                ConsoleHelper.WriteSuccess($"Configuration exported to '{Path.GetFullPath(outputPath)}' ({format.ToUpperInvariant()})");
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to export configuration: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
