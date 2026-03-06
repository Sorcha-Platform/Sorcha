// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Cli.Commands;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli;

/// <summary>
/// Entry point for the Sorcha CLI administrative tool.
/// </summary>
internal class Program
{
    /// <summary>
    /// Main entry point for the CLI application.
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>Exit code (0 = success, non-zero = error)</returns>
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Build service collection
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Build the root command with dependency injection
            var rootCommand = BuildRootCommand(serviceProvider);

            // Invoke the command
            return await rootCommand.Parse(args).InvokeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            if (args.Contains("--verbose") || args.Contains("-v"))
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return ExitCodes.GeneralError;
        }
    }

    /// <summary>
    /// Configures services for dependency injection.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration service
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        // HTTP client for API calls
        services.AddHttpClient("SorchaApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-CLI/1.0");
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient("SorchaApi");
        });

        // Token cache with encryption
        services.AddSingleton(sp =>
        {
            IEncryptionProvider encryptionProvider;

            if (OperatingSystem.IsWindows())
            {
                encryptionProvider = new WindowsDpapiEncryption();
            }
            else if (OperatingSystem.IsMacOS())
            {
                encryptionProvider = new MacOsKeychainEncryption();
            }
            else
            {
                encryptionProvider = new LinuxEncryption();
            }

            return new TokenCache(encryptionProvider);
        });

        // Authentication service
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // HTTP client factory for service clients (Tenant, Register, Wallet)
        services.AddSingleton<HttpClientFactory>();

        // Logging (optional, for debugging)
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    }

    /// <summary>
    /// Builds the root command with all subcommands.
    /// </summary>
    private static RootCommand BuildRootCommand(ServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("Sorcha CLI - Administrative tool for managing Sorcha distributed ledger platform");

        // Global options
        var profileOption = new Option<string>("--profile", "-p")
        {
            Description = "Configuration profile to use (uses active profile from config if not specified)"
        };

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output format (table, json, csv)",
            DefaultValueFactory = _ => "table"
        };

        var quietOption = new Option<bool>("--quiet", "-q")
        {
            Description = "Suppress non-essential output"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose logging"
        };

        rootCommand.Options.Add(profileOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(quietOption);
        rootCommand.Options.Add(verboseOption);

        // Get config service for profile resolution
        var configService = serviceProvider.GetRequiredService<IConfigurationService>();

        // Set global options for BaseCommand
        BaseCommand.ProfileOption = profileOption;
        BaseCommand.OutputOption = outputOption;
        BaseCommand.QuietOption = quietOption;
        BaseCommand.VerboseOption = verboseOption;
        BaseCommand.ConfigService = configService;

        // Get services from DI container
        var authService = serviceProvider.GetRequiredService<IAuthenticationService>();
        var clientFactory = serviceProvider.GetRequiredService<HttpClientFactory>();

        // Subcommands with dependency injection
        // Bootstrap command (Sprint 5)
        rootCommand.Subcommands.Add(new BootstrapCommand(clientFactory, configService));

        // Sprint 2: Tenant Service commands
        rootCommand.Subcommands.Add(new OrganizationCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new UserCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new ServicePrincipalCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new AuthCommand(authService, configService));

        // Sprint 3: Register, Transaction & Wallet commands
        rootCommand.Subcommands.Add(new RegisterCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new TransactionCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new WalletCommand(clientFactory, authService, configService));

        // Docket and Query commands
        rootCommand.Subcommands.Add(new DocketCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new QueryCommand(clientFactory, authService, configService));

        // Sprint 4: Peer Service commands
        rootCommand.Subcommands.Add(new PeerCommand(clientFactory, authService, configService));

        // Sprint 5: Blueprint, Participant, Credential, Validator, Admin commands
        rootCommand.Subcommands.Add(new BlueprintCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new ParticipantCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new CredentialCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new ValidatorCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new AdminCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new SchemaCommand(clientFactory, authService, configService));
        rootCommand.Subcommands.Add(new OperationCommand(clientFactory, authService, configService));

        // Configuration management commands
        rootCommand.Subcommands.Add(new ConfigCommand());

        // Version command
        var versionCommand = new Command("version", "Display CLI version information");
        versionCommand.SetAction((parseResult, ct) =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var fileVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>()?.Version;
            var infoVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            Console.WriteLine($"Sorcha CLI v{infoVersion ?? version?.ToString() ?? "1.0.0"}");
            Console.WriteLine($"Assembly Version: {version}");
            Console.WriteLine($"File Version: {fileVersion}");
            Console.WriteLine($".NET Runtime: {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Platform: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            return Task.FromResult(0);
        });
        rootCommand.Subcommands.Add(versionCommand);

        return rootCommand;
    }
}
