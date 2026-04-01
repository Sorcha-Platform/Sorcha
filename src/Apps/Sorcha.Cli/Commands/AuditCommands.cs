// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Audit log commands for querying and exporting audit entries.
/// </summary>
public class AuditCommand : Command
{
    public AuditCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("audit", "Query and export audit logs\n\nExamples:\n  sorcha audit list --org-id <id>\n  sorcha audit list --org-id <id> --since 2026-01-01 --action Login\n  sorcha audit export --org-id <id> --output audit.json")
    {
        Subcommands.Add(new AuditListCommand(clientFactory, authService, configService));
        Subcommands.Add(new AuditExportCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists audit log entries with optional filtering.
/// </summary>
public class AuditListCommand : Command
{
    public AuditListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List audit log entries\n\nExamples:\n  sorcha audit list --org-id <id>\n  sorcha audit list --org-id <id> --since 2026-01-01 --until 2026-03-31\n  sorcha audit list --org-id <id> --action Login --user admin@example.com")
    {
        var orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        var sinceOption = new Option<string?>("--since")
        {
            Description = "Filter entries from this date (ISO 8601, e.g., 2026-01-01)"
        };

        var untilOption = new Option<string?>("--until")
        {
            Description = "Filter entries until this date (ISO 8601, e.g., 2026-03-31)"
        };

        var actionOption = new Option<string?>("--action")
        {
            Description = "Filter by action type (e.g., Login, CreateOrg, UpdateUser)"
        };

        var userOption = new Option<string?>("--user")
        {
            Description = "Filter by user email"
        };

        var pageOption = new Option<int?>("--page")
        {
            Description = "Page number (1-based)"
        };

        var pageSizeOption = new Option<int?>("--page-size")
        {
            Description = "Number of entries per page"
        };

        Options.Add(orgIdOption);
        Options.Add(sinceOption);
        Options.Add(untilOption);
        Options.Add(actionOption);
        Options.Add(userOption);
        Options.Add(pageOption);
        Options.Add(pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var orgId = parseResult.GetValue(orgIdOption)!;
                var since = parseResult.GetValue(sinceOption);
                var until = parseResult.GetValue(untilOption);
                var action = parseResult.GetValue(actionOption);
                var user = parseResult.GetValue(userOption);
                var page = parseResult.GetValue(pageOption);
                var pageSize = parseResult.GetValue(pageSizeOption);

                var client = await clientFactory.CreateAuditServiceClientAsync(profileName);
                var response = await client.ListAuditEntriesAsync(
                    orgId, since, until, action, user, page, pageSize,
                    $"Bearer {token}");

                if (response.Entries.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No audit entries found matching the criteria.");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {response.Entries.Count} audit entries (Total: {response.TotalCount}, Page: {response.Page}/{((response.TotalCount + response.PageSize - 1) / Math.Max(response.PageSize, 1))}):");
                Console.WriteLine();
                Console.WriteLine($"{"Timestamp",-22} {"Action",-25} {"User",-30} {"Resource",-15} {"Details"}");
                Console.WriteLine(new string('-', 110));

                foreach (var entry in response.Entries)
                {
                    var timestamp = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var resourceInfo = !string.IsNullOrEmpty(entry.ResourceType)
                        ? $"{entry.ResourceType}"
                        : "-";
                    var details = !string.IsNullOrEmpty(entry.Details)
                        ? (entry.Details.Length > 30 ? entry.Details[..30] + "..." : entry.Details)
                        : "-";

                    Console.WriteLine($"{timestamp,-22} {entry.Action,-25} {entry.UserName,-30} {resourceInfo,-15} {details}");
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
                ConsoleHelper.WriteError($"Failed to list audit entries: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Exports audit log entries to a file in CSV or JSON format.
/// </summary>
public class AuditExportCommand : Command
{
    public AuditExportCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("export", "Export audit log entries to a file\n\nExamples:\n  sorcha audit export --org-id <id> --output audit.json\n  sorcha audit export --org-id <id> --output audit.csv --format csv\n  sorcha audit export --org-id <id> --output audit.json --since 2026-01-01")
    {
        var orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output file path",
            Required = true
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Export format (csv or json)",
            DefaultValueFactory = _ => "json"
        };

        var sinceOption = new Option<string?>("--since")
        {
            Description = "Filter entries from this date (ISO 8601)"
        };

        var untilOption = new Option<string?>("--until")
        {
            Description = "Filter entries until this date (ISO 8601)"
        };

        var actionOption = new Option<string?>("--action")
        {
            Description = "Filter by action type"
        };

        var userOption = new Option<string?>("--user")
        {
            Description = "Filter by user email"
        };

        Options.Add(orgIdOption);
        Options.Add(outputOption);
        Options.Add(formatOption);
        Options.Add(sinceOption);
        Options.Add(untilOption);
        Options.Add(actionOption);
        Options.Add(userOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var orgId = parseResult.GetValue(orgIdOption)!;
                var outputPath = parseResult.GetValue(outputOption)!;
                var format = parseResult.GetValue(formatOption)!.ToLowerInvariant();
                var since = parseResult.GetValue(sinceOption);
                var until = parseResult.GetValue(untilOption);
                var action = parseResult.GetValue(actionOption);
                var user = parseResult.GetValue(userOption);

                if (format != "csv" && format != "json")
                {
                    ConsoleHelper.WriteError("Invalid format. Use 'csv' or 'json'.");
                    return ExitCodes.ValidationError;
                }

                Console.WriteLine("Fetching audit entries...");

                var client = await clientFactory.CreateAuditServiceClientAsync(profileName);

                // Fetch all pages
                var allEntries = new List<AuditLogEntry>();
                var currentPage = 1;
                const int batchSize = 100;
                int totalCount;

                do
                {
                    var response = await client.ListAuditEntriesAsync(
                        orgId, since, until, action, user, currentPage, batchSize,
                        $"Bearer {token}");

                    allEntries.AddRange(response.Entries);
                    totalCount = response.TotalCount;
                    currentPage++;
                } while (allEntries.Count < totalCount);

                if (allEntries.Count == 0)
                {
                    ConsoleHelper.WriteWarning("No audit entries found matching the criteria. No file written.");
                    return ExitCodes.Success;
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string content;
                if (format == "csv")
                {
                    var lines = new List<string>
                    {
                        "Id,Timestamp,Action,UserId,UserName,ResourceType,ResourceId,Details"
                    };

                    foreach (var entry in allEntries)
                    {
                        var details = EscapeCsvField(entry.Details ?? string.Empty);
                        var userName = EscapeCsvField(entry.UserName);
                        lines.Add($"{entry.Id},{entry.Timestamp:O},{EscapeCsvField(entry.Action)},{entry.UserId},{userName},{entry.ResourceType ?? string.Empty},{entry.ResourceId ?? string.Empty},{details}");
                    }

                    content = string.Join(Environment.NewLine, lines);
                }
                else
                {
                    content = JsonSerializer.Serialize(allEntries, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                }

                await File.WriteAllTextAsync(outputPath, content, ct);

                ConsoleHelper.WriteSuccess($"Exported {allEntries.Count} audit entries to '{Path.GetFullPath(outputPath)}' ({format.ToUpperInvariant()})");
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
                ConsoleHelper.WriteError($"Failed to export audit entries: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    /// <summary>
    /// Escapes a field value for CSV output, wrapping in quotes if needed.
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}
