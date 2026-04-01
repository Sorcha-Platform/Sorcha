// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Context for command execution containing global options and services.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// Profile name to use for this command (from --profile option).
    /// </summary>
    public string ProfileName { get; set; } = "dev";

    /// <summary>
    /// Output format (table, json, csv).
    /// </summary>
    public string OutputFormat { get; set; } = "table";

    /// <summary>
    /// Quiet mode - suppress non-essential output.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// Verbose mode - enable debug logging.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Machine-readable mode - wrap output in JSON envelope.
    /// </summary>
    public bool MachineReadable { get; set; }

    /// <summary>
    /// Configuration service.
    /// </summary>
    public IConfigurationService ConfigurationService { get; set; } = null!;

    /// <summary>
    /// Authentication service.
    /// </summary>
    public IAuthenticationService AuthenticationService { get; set; } = null!;

    /// <summary>
    /// HTTP client factory for creating service clients.
    /// </summary>
    public HttpClientFactory HttpClientFactory { get; set; } = null!;

    /// <summary>
    /// Logger instance.
    /// </summary>
    public ILogger Logger { get; set; } = null!;

    /// <summary>
    /// Gets the active profile for this command.
    /// </summary>
    public async Task<Profile> GetProfileAsync()
    {
        var profile = await ConfigurationService.GetProfileAsync(ProfileName);
        if (profile == null)
        {
            throw new InvalidOperationException(
                $"Profile '{ProfileName}' does not exist. Use 'sorcha config profile list' to see available profiles.");
        }
        return profile;
    }

    /// <summary>
    /// Gets a valid access token for the current profile.
    /// Returns null if not authenticated.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        return await AuthenticationService.GetAccessTokenAsync(ProfileName);
    }

    /// <summary>
    /// Checks if the user is authenticated for the current profile.
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync()
    {
        return await AuthenticationService.IsAuthenticatedAsync(ProfileName);
    }
}
