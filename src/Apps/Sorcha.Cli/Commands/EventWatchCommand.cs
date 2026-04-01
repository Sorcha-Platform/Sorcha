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
/// Parent command for real-time event streaming operations.
/// </summary>
public class EventsCommand : Command
{
    /// <summary>
    /// Initializes the events command with its subcommands.
    /// </summary>
    public EventsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("events", "Real-time event streaming\n\nExamples:\n  sorcha events watch --all\n  sorcha events watch --register <id>\n  sorcha events watch --blueprints\n  sorcha events watch --all --output json | jq .")
    {
        Subcommands.Add(new EventWatchCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Watches for real-time events from SignalR hubs and streams them to the console.
/// Supports filtering by register, blueprint events, or all events.
/// </summary>
public class EventWatchCommand : Command
{
    private static readonly JsonSerializerOptions JsonOptions = SorchaJsonOptions.Compact;

    /// <summary>
    /// Initializes the watch subcommand with its options.
    /// </summary>
    public EventWatchCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("watch", "Stream real-time events from the platform")
    {
        var registerOption = new Option<string?>("--register", "-r")
        {
            Description = "Filter events for a specific register ID"
        };

        var blueprintsOption = new Option<bool>("--blueprints", "-b")
        {
            Description = "Watch blueprint events only (BlueprintPublished, ActionCompleted)"
        };

        var allOption = new Option<bool>("--all", "-a")
        {
            Description = "Watch all events (register and blueprint)"
        };

        var roleOption = new Option<string?>("--role")
        {
            Description = "Filter events by participant role"
        };

        var sinceOption = new Option<DateTimeOffset?>("--since")
        {
            Description = "Only show events after this timestamp (ISO 8601). Note: filtering is applied client-side"
        };

        Options.Add(registerOption);
        Options.Add(blueprintsOption);
        Options.Add(allOption);
        Options.Add(roleOption);
        Options.Add(sinceOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var registerId = parseResult.GetValue(registerOption);
            var blueprintsOnly = parseResult.GetValue(blueprintsOption);
            var watchAll = parseResult.GetValue(allOption);
            var role = parseResult.GetValue(roleOption);
            var since = parseResult.GetValue(sinceOption);

            // Determine output format from global option
            var outputFormat = BaseCommand.OutputOption is not null
                ? parseResult.GetValue(BaseCommand.OutputOption) ?? "table"
                : "table";
            var isJsonMode = outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase);

            // Validate: at least one filter must be specified
            if (string.IsNullOrEmpty(registerId) && !blueprintsOnly && !watchAll)
            {
                ConsoleHelper.WriteError("Specify --register <id>, --blueprints, or --all to watch events.");
                return ExitCodes.ValidationError;
            }

            try
            {
                // Resolve profile and authentication
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "docker";
                var token = await authService.GetAccessTokenAsync(profileName);
                var gatewayUrl = profile?.GetGatewayUrl() ?? "http://localhost";

                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Please run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                await using var eventStream = new EventStreamService(gatewayUrl, token);

                if (!isJsonMode)
                {
                    ConsoleHelper.WriteInfo($"Connecting to {gatewayUrl}...");
                }

                await eventStream.ConnectAsync(cancellationToken);

                if (!isJsonMode)
                {
                    var filterDesc = !string.IsNullOrEmpty(registerId)
                        ? $"register {registerId}"
                        : blueprintsOnly
                            ? "blueprint events"
                            : "all events";
                    ConsoleHelper.WriteSuccess($"Connected. Watching {filterDesc}. Press Ctrl+C to stop.");
                    Console.WriteLine();
                    Console.WriteLine($"{"Timestamp",-24} {"Event Type",-25} {"Register",-38} {"Summary"}");
                    Console.WriteLine(new string('-', 110));
                }

                var eventCount = 0;

                try
                {
                    await foreach (var message in eventStream.StreamEventsAsync(
                        registerId: registerId,
                        blueprintsOnly: blueprintsOnly,
                        cancellationToken: cancellationToken))
                    {
                        // Apply --since filter
                        if (since.HasValue && message.Timestamp < since.Value.UtcDateTime)
                        {
                            continue;
                        }

                        eventCount++;

                        if (isJsonMode)
                        {
                            // One JSON object per line for piping to jq
                            var json = JsonSerializer.Serialize(message, JsonOptions);
                            Console.WriteLine(json);
                        }
                        else
                        {
                            var summary = FormatEventSummary(message);
                            var registerDisplay = message.RegisterId ?? "-";
                            if (registerDisplay.Length > 36)
                            {
                                registerDisplay = registerDisplay[..36] + "..";
                            }

                            Console.WriteLine(
                                $"{message.Timestamp:yyyy-MM-dd HH:mm:ss.fff}  {message.EventType,-25} {registerDisplay,-38} {summary}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown via Ctrl+C
                }

                if (!isJsonMode)
                {
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo($"Stream ended. {eventCount} event(s) received.");
                }

                return ExitCodes.Success;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Connection failed: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Error: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    /// <summary>
    /// Formats a brief summary of the event data for table display.
    /// </summary>
    private static string FormatEventSummary(EventStreamMessage message)
    {
        if (message.Data is null)
        {
            return "-";
        }

        if (message.Data is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            return message.EventType switch
            {
                "TransactionConfirmed" => TryGetJsonString(jsonElement, "transactionId", "Transaction confirmed"),
                "DocketSealed" => TryGetJsonString(jsonElement, "docketNumber", "Docket sealed"),
                "RegisterStatusChanged" => TryGetJsonString(jsonElement, "status", "Status changed"),
                "BlueprintPublished" => TryGetJsonString(jsonElement, "title", "Blueprint published"),
                "ActionCompleted" => TryGetJsonString(jsonElement, "actionName", "Action completed"),
                _ => message.EventType
            };
        }

        var dataString = message.Data.ToString() ?? "-";
        return dataString.Length > 60 ? dataString[..57] + "..." : dataString;
    }

    /// <summary>
    /// Tries to extract a string property from a JSON element for display.
    /// </summary>
    private static string TryGetJsonString(JsonElement element, string propertyName, string prefix)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString();
            return $"{prefix}: {value}";
        }

        // Try PascalCase variant
        var pascalName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(pascalName, out var pascalProp) && pascalProp.ValueKind == JsonValueKind.String)
        {
            var value = pascalProp.GetString();
            return $"{prefix}: {value}";
        }

        return prefix;
    }
}
