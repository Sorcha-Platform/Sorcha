// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;

using Refit;

using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Trusted-list administration (Feature 181 US3). Operators import signed ETSI TS 119 612 trusted
/// lists; verifying services then resolve CA anchors from the imported snapshots for the external
/// EUDI trust rail. There is no scriptable path for this other than the admin UI.
/// </summary>
/// <remarks>
/// These are Tenant Service admin endpoints — sign in as an administrator
/// (<c>sorcha auth login</c>); a non-admin token gets a 403.
/// </remarks>
public class TrustCommand : Command
{
    public TrustCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("trust", "Manage imported trusted lists (ETSI TS 119 612, admin auth)\n\n"
            + "Examples:\n"
            + "  sorcha trust list\n"
            + "  sorcha trust get --id eu-lotl\n"
            + "  sorcha trust import --id eu-lotl --file lotl.xml\n"
            + "  sorcha trust import --id eu-lotl --url https://ec.europa.eu/.../lotl.xml\n"
            + "  sorcha trust delete --id eu-lotl")
    {
        Subcommands.Add(new TrustListCommand(clientFactory, authService, configService));
        Subcommands.Add(new TrustGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new TrustImportCommand(clientFactory, authService, configService));
        Subcommands.Add(new TrustDeleteCommand(clientFactory, authService, configService));
    }
}

/// <summary>Lists imported trusted-list snapshots.</summary>
public class TrustListCommand : Command
{
    public TrustListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List imported trusted-list snapshots")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profileName = (await configService.GetActiveProfileAsync())?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateTrustServiceClientAsync(profileName);
                var lists = await client.ListTrustListsAsync($"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, lists);
                    return ExitCodes.Success;
                }

                if (lists.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No trusted lists imported.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"{lists.Count} trusted-list snapshot(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Trust List",-24} {"Seq",5} {"Territory",-10} {"Anchors",7} {"Freshness",-10} {"Status",-10} {"Next update"}");
                Console.WriteLine(new string('-', 100));
                foreach (var l in lists)
                {
                    var next = l.NextUpdate?.ToString("yyyy-MM-dd") ?? "—";
                    Console.WriteLine($"{l.TrustListId,-24} {l.SequenceNumber,5} {l.SchemeTerritory ?? "—",-10} {l.AnchorCount,7} {l.Freshness,-10} {l.Status,-10} {next}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) { return TrustErrors.Handle(ex); }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list trusted lists: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>Shows a trusted-list snapshot with its anchors.</summary>
public class TrustGetCommand : Command
{
    private readonly Option<string> _idOption;

    public TrustGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Show a trusted-list snapshot and its anchors")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "The trust-list identifier, e.g. eu-lotl",
            Required = true
        };
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var trustListId = parseResult.GetValue(_idOption)!;
            try
            {
                var profileName = (await configService.GetActiveProfileAsync())?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateTrustServiceClientAsync(profileName);
                var detail = await client.GetTrustListAsync(trustListId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, detail);
                    return ExitCodes.Success;
                }

                var s = detail.Summary;
                ConsoleHelper.WriteSuccess($"Trusted list '{s.TrustListId}' (sequence {s.SequenceNumber})");
                Console.WriteLine();
                Console.WriteLine($"  Territory:     {s.SchemeTerritory ?? "—"}");
                Console.WriteLine($"  Operator:      {s.SchemeOperatorName ?? "—"}");
                Console.WriteLine($"  Issued:        {s.ListIssueDateTime:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Next update:   {(s.NextUpdate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—")}");
                Console.WriteLine($"  Freshness:     {s.Freshness}");
                Console.WriteLine($"  Status:        {s.Status}");
                Console.WriteLine($"  Signer:        {s.SignerCertSubject}");
                Console.WriteLine($"  Imported:      {s.ImportedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Extraction:    {detail.ExtractionSummary}");
                Console.WriteLine();
                Console.WriteLine($"  Anchors ({detail.Anchors.Count}):");
                foreach (var a in detail.Anchors)
                {
                    Console.WriteLine($"    - {a.SubjectDn}");
                    Console.WriteLine($"        {a.ServiceStatus}  valid {a.NotBefore:yyyy-MM-dd} → {a.NotAfter:yyyy-MM-dd}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"No trusted list found with id '{trustListId}'.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) { return TrustErrors.Handle(ex); }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get trusted list: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>Imports a trusted-list document, by file upload or by server-side URL fetch.</summary>
public class TrustImportCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string?> _fileOption;
    private readonly Option<string?> _urlOption;

    public TrustImportCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("import", "Import a trusted-list document (by --file upload or --url fetch-once)")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "The trust-list identifier to import under, e.g. eu-lotl",
            Required = true
        };

        _fileOption = new Option<string?>("--file", "-f")
        {
            Description = "Path to the trusted-list XML document to upload"
        };

        _urlOption = new Option<string?>("--url", "-u")
        {
            Description = "URL for the server to fetch the trusted-list document from (once)"
        };

        Options.Add(_idOption);
        Options.Add(_fileOption);
        Options.Add(_urlOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var trustListId = parseResult.GetValue(_idOption)!;
            var file = parseResult.GetValue(_fileOption);
            var url = parseResult.GetValue(_urlOption);

            if (string.IsNullOrWhiteSpace(file) == string.IsNullOrWhiteSpace(url))
            {
                ConsoleHelper.WriteError("Provide exactly one of --file or --url.");
                return ExitCodes.ValidationError;
            }

            if (!string.IsNullOrWhiteSpace(file) && !File.Exists(file))
            {
                ConsoleHelper.WriteError($"File not found: {file}");
                return ExitCodes.NotFound;
            }

            try
            {
                var profileName = (await configService.GetActiveProfileAsync())?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateTrustServiceClientAsync(profileName);

                TrustListSnapshotSummary result;
                if (!string.IsNullOrWhiteSpace(file))
                {
                    await using var stream = File.OpenRead(file);
                    var part = new StreamPart(stream, Path.GetFileName(file), "application/xml");
                    result = await client.ImportTrustListFileAsync(trustListId, part, $"Bearer {token}");
                }
                else
                {
                    result = await client.ImportTrustListUrlAsync(trustListId, url!, $"Bearer {token}");
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess(
                    $"Imported '{result.TrustListId}' sequence {result.SequenceNumber} "
                    + $"({result.AnchorCount} anchor(s), {result.Freshness}).");
                Console.WriteLine($"  Territory: {result.SchemeTerritory ?? "—"}");
                Console.WriteLine($"  Status:    {result.Status}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"The trusted list was rejected: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError(
                    "Sequence regression: the imported list's sequence number is not greater than "
                    + "the current active snapshot for this id.");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) { return TrustErrors.Handle(ex); }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to import trusted list: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>Deletes every version of a trusted-list snapshot.</summary>
public class TrustDeleteCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<bool> _confirmOption;

    public TrustDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete all versions of a trusted-list snapshot")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "The trust-list identifier to delete",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip the confirmation prompt"
        };

        Options.Add(_idOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var trustListId = parseResult.GetValue(_idOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning(
                        $"This deletes every version of trusted list '{trustListId}'. Verifying "
                        + "services will lose the anchors it provides.");
                    if (!ConsoleHelper.Confirm("Delete this trusted list?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Delete cancelled.");
                        return ExitCodes.Success;
                    }
                }

                var profileName = (await configService.GetActiveProfileAsync())?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateTrustServiceClientAsync(profileName);
                await client.DeleteTrustListAsync(trustListId, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Trusted list '{trustListId}' deleted.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"No trusted list found with id '{trustListId}'.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) { return TrustErrors.Handle(ex); }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Tenant Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to delete trusted list: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>Shared error mapping for the trust subcommands.</summary>
internal static class TrustErrors
{
    public static int Handle(ApiException ex)
    {
        switch (ex.StatusCode)
        {
            case HttpStatusCode.Forbidden:
                ConsoleHelper.WriteError(
                    "Trusted-list administration requires an administrator token. Sign in as an "
                    + "admin with 'sorcha auth login'.");
                return ExitCodes.AuthorizationError;
            case HttpStatusCode.Unauthorized:
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            default:
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
        }
    }
}
