// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Service for managing CLI configuration and profiles.
/// Configuration is stored in ~/.sorcha/config.json
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private static string GetConfigDirectory()
    {
        // Allow override via environment variable for testing
        var overrideDir = Environment.GetEnvironmentVariable("SORCHA_CONFIG_DIR");
        if (!string.IsNullOrEmpty(overrideDir))
        {
            return overrideDir;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sorcha");
    }

    private string ConfigDirectory => GetConfigDirectory();
    private string ConfigFilePath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <inheritdoc/>
    public string GetConfigFilePath() => ConfigFilePath;

    /// <inheritdoc/>
    public async Task EnsureConfigDirectoryAsync()
    {
        if (!Directory.Exists(ConfigDirectory))
        {
            Directory.CreateDirectory(ConfigDirectory);

            // Set restrictive permissions on Unix systems
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ConfigDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // Create default config if it doesn't exist (without calling SaveConfigurationAsync to avoid recursion)
        if (!File.Exists(ConfigFilePath))
        {
            var config = CreateDefaultConfiguration();
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(ConfigFilePath, json);

            // Small delay to ensure file handle is released
            await Task.Delay(100);

            // Set restrictive permissions on Unix systems
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(ConfigFilePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<CliConfiguration> GetConfigurationAsync()
    {
        await EnsureConfigDirectoryAsync();

        if (!File.Exists(ConfigFilePath))
        {
            return CreateDefaultConfiguration();
        }

        var json = await File.ReadAllTextAsync(ConfigFilePath);
        var config = JsonSerializer.Deserialize<CliConfiguration>(json, JsonOptions);

        return config ?? CreateDefaultConfiguration();
    }

    /// <inheritdoc/>
    public async Task SaveConfigurationAsync(CliConfiguration configuration)
    {
        await EnsureConfigDirectoryAsync();

        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        await File.WriteAllTextAsync(ConfigFilePath, json);

        // Set restrictive permissions on Unix systems
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ConfigFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <inheritdoc/>
    public async Task<Profile?> GetProfileAsync(string name)
    {
        var config = await GetConfigurationAsync();
        return config.Profiles.TryGetValue(name, out var profile) ? profile : null;
    }

    /// <inheritdoc/>
    public async Task<Profile?> GetActiveProfileAsync()
    {
        var config = await GetConfigurationAsync();
        return config.GetActiveProfile();
    }

    /// <inheritdoc/>
    public async Task SetActiveProfileAsync(string name)
    {
        var config = await GetConfigurationAsync();

        if (!config.Profiles.ContainsKey(name))
        {
            throw new InvalidOperationException($"Profile '{name}' does not exist.");
        }

        config.ActiveProfile = name;
        await SaveConfigurationAsync(config);
    }

    /// <inheritdoc/>
    public async Task UpsertProfileAsync(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(profile));
        }

        var config = await GetConfigurationAsync();
        config.Profiles[profile.Name] = profile;

        // If this is the first profile, make it active
        if (config.Profiles.Count == 1 || string.IsNullOrEmpty(config.ActiveProfile))
        {
            config.ActiveProfile = profile.Name;
        }

        await SaveConfigurationAsync(config);
    }

    /// <inheritdoc/>
    public async Task DeleteProfileAsync(string name)
    {
        var config = await GetConfigurationAsync();

        if (!config.Profiles.Remove(name))
        {
            throw new InvalidOperationException($"Profile '{name}' does not exist.");
        }

        // If we deleted the active profile, clear it or set to another profile
        if (config.ActiveProfile == name)
        {
            config.ActiveProfile = config.Profiles.Keys.FirstOrDefault();
        }

        await SaveConfigurationAsync(config);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Profile>> ListProfilesAsync()
    {
        var config = await GetConfigurationAsync();
        return config.Profiles.Values;
    }

    /// <summary>
    /// Creates a default configuration with a single Docker profile for local development.
    /// Additional profiles can be added using 'sorcha config init'.
    /// </summary>
    private static CliConfiguration CreateDefaultConfiguration()
    {
        return new CliConfiguration
        {
            ActiveProfile = "docker",
            DefaultOutputFormat = "table",
            VerboseLogging = false,
            QuietMode = false,
            Profiles = new Dictionary<string, Profile>
            {
                ["docker"] = new Profile
                {
                    Name = "docker",
                    ServiceUrl = "http://localhost",
                    TenantServiceUrl = null,  // Derived from ServiceUrl via API Gateway
                    RegisterServiceUrl = null,  // Derived from ServiceUrl via API Gateway
                    PeerServiceUrl = null,  // Derived from ServiceUrl via API Gateway
                    WalletServiceUrl = null,  // Derived from ServiceUrl via API Gateway
                    AuthTokenUrl = "http://localhost/api/service-auth/token",
                    // Internal-only endpoint (#1397/#1406) — only reachable when the CLI runs
                    // inside the Sorcha trust network. Set explicitly so 'sorcha auth login
                    // --client-id ...' fails with a clear error rather than the stale public route.
                    ServiceAuthTokenUrl = "http://localhost/api/internal/service-auth/token",
                    DefaultClientId = "sorcha-cli",
                    VerifySsl = false,
                    TimeoutSeconds = 30
                }
            }
        };
    }
}
