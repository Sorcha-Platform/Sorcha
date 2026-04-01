// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Base class for all CLI commands.
/// Provides common functionality for output formatting, error handling, and authentication.
/// </summary>
public abstract class BaseCommand : Command
{
    // Global options - these will be set by Program.cs
    public static Option<string>? ProfileOption { get; set; }
    public static Option<string>? OutputOption { get; set; }
    public static Option<bool>? QuietOption { get; set; }
    public static Option<bool>? VerboseOption { get; set; }
    public static Option<bool>? MachineReadableOption { get; set; }

    // Config service - set by Program.cs for profile resolution
    public static IConfigurationService? ConfigService { get; set; }

    protected BaseCommand(string name, string? description = null)
        : base(name, description)
    {
        this.SetAction(HandleCommandAsync);
    }

    /// <summary>
    /// Handles command execution with error handling and output formatting.
    /// </summary>
    private async Task<int> HandleCommandAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        try
        {
            var commandContext = BuildCommandContext(parseResult);
            return await ExecuteAsync(commandContext);
        }
        catch (UnauthorizedAccessException ex)
        {
            WriteError(parseResult, "Authentication required. Please login first.", ex);
            return ExitCodes.AuthenticationError;
        }
        catch (InvalidOperationException ex)
        {
            WriteError(parseResult, ex.Message, ex);
            return ExitCodes.GeneralError;
        }
        catch (ArgumentException ex)
        {
            WriteError(parseResult, $"Invalid argument: {ex.Message}", ex);
            return ExitCodes.ValidationError;
        }
        catch (HttpRequestException ex)
        {
            WriteError(parseResult, $"Network error: {ex.Message}", ex);
            return ExitCodes.NetworkError;
        }
        catch (Exception ex)
        {
            WriteError(parseResult, $"Unexpected error: {ex.Message}", ex);
            return ExitCodes.GeneralError;
        }
    }

    /// <summary>
    /// Executes the command logic. Must be implemented by derived classes.
    /// </summary>
    /// <param name="context">Command context with global options and services</param>
    /// <returns>Exit code</returns>
    protected abstract Task<int> ExecuteAsync(CommandContext context);

    /// <summary>
    /// Builds the command context from parse result.
    /// Override this method to extract command-specific options.
    /// </summary>
    protected virtual CommandContext BuildCommandContext(ParseResult parseResult)
    {
        // Get profile from option, or fall back to active profile from config
        var profileName = ProfileOption != null ? parseResult.GetValue(ProfileOption) : null;

        if (string.IsNullOrEmpty(profileName) && ConfigService != null)
        {
            var activeProfile = ConfigService.GetActiveProfileAsync().GetAwaiter().GetResult();
            profileName = activeProfile?.Name;
        }

        return new CommandContext
        {
            ProfileName = profileName ?? "docker", // Default to docker if no config
            OutputFormat = (OutputOption != null ? parseResult.GetValue(OutputOption) : null) ?? "table",
            Quiet = QuietOption != null && parseResult.GetValue(QuietOption),
            Verbose = VerboseOption != null && parseResult.GetValue(VerboseOption),
            MachineReadable = MachineReadableOption != null && parseResult.GetValue(MachineReadableOption)
        };
    }

    /// <summary>
    /// Gets the appropriate output formatter based on context.
    /// </summary>
    protected IOutputFormatter GetFormatter(CommandContext context)
    {
        if (context.MachineReadable)
        {
            return new MachineReadableFormatter(Name);
        }

        return context.OutputFormat.ToLowerInvariant() switch
        {
            "json" => new JsonOutputFormatter(),
            "csv" => new CsvOutputFormatter(),
            "yaml" => new YamlOutputFormatter(),
            "table" => new TableOutputFormatter(),
            _ => new TableOutputFormatter()
        };
    }

    /// <summary>
    /// Writes output to console based on format.
    /// </summary>
    protected void WriteOutput<T>(CommandContext context, T data) where T : class
    {
        if (context.Quiet)
        {
            return;
        }

        var formatter = GetFormatter(context);
        var output = formatter.FormatSingle(data);
        Console.WriteLine(output);
    }

    /// <summary>
    /// Writes a collection to console based on format.
    /// </summary>
    protected void WriteCollection<T>(CommandContext context, IEnumerable<T> data) where T : class
    {
        if (context.Quiet)
        {
            return;
        }

        var formatter = GetFormatter(context);
        var output = formatter.FormatCollection(data);
        Console.WriteLine(output);
    }

    /// <summary>
    /// Writes a simple message to console.
    /// </summary>
    protected void WriteMessage(CommandContext context, string message)
    {
        if (context.Quiet)
        {
            return;
        }

        var formatter = GetFormatter(context);
        var output = formatter.FormatMessage(message);
        Console.WriteLine(output);
    }

    /// <summary>
    /// Writes an error message to stderr.
    /// </summary>
    protected void WriteError(ParseResult parseResult, string message, Exception? exception = null)
    {
        var verbose = VerboseOption != null && parseResult.GetValue(VerboseOption);

        Console.Error.WriteLine($"Error: {message}");

        if (verbose && exception != null)
        {
            Console.Error.WriteLine($"Details: {exception}");
        }
    }

    /// <summary>
    /// Ensures the user is authenticated for the current profile.
    /// Throws UnauthorizedAccessException if not authenticated.
    /// </summary>
    protected async Task EnsureAuthenticatedAsync(CommandContext context)
    {
        var isAuthenticated = await context.IsAuthenticatedAsync();
        if (!isAuthenticated)
        {
            throw new UnauthorizedAccessException(
                $"Not authenticated for profile '{context.ProfileName}'. Please run 'sorcha auth login' first.");
        }
    }
}
