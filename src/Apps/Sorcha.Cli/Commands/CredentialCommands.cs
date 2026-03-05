// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Verifiable credential management commands.
/// </summary>
public class CredentialCommand : Command
{
    public CredentialCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("credential", "Manage verifiable credentials")
    {
        Subcommands.Add(new CredentialListCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialIssueCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialPresentCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialVerifyCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialRevokeCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialStatusCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialSuspendCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialReinstateCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialRefreshCommand(clientFactory, authService, configService));
        Subcommands.Add(new CredentialStatusListCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all credentials.
/// </summary>
public class CredentialListCommand : Command
{
    public CredentialListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all credentials")
    {
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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var credentials = await client.ListCredentialsAsync($"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (credentials == null || credentials.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No credentials found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {credentials.Count} credential(s):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Type",-25} {"Subject",-30} {"Status",-12} {"Issued"}");
                Console.WriteLine(new string('-', 120));

                foreach (var cred in credentials)
                {
                    Console.WriteLine($"{cred.Id,-38} {cred.Type,-25} {cred.Subject,-30} {cred.Status,-12} {cred.IssuedAt:yyyy-MM-dd}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list credentials: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a credential by ID.
/// </summary>
public class CredentialGetCommand : Command
{
    private readonly Option<string> _idOption;

    public CredentialGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a credential by ID")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var credential = await client.GetCredentialAsync(id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(credential, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Credential details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:          {credential.Id}");
                Console.WriteLine($"  Type:        {credential.Type}");
                Console.WriteLine($"  Issuer:      {credential.Issuer}");
                Console.WriteLine($"  Subject:     {credential.Subject}");
                Console.WriteLine($"  Status:      {credential.Status}");
                Console.WriteLine($"  Issued:      {credential.IssuedAt:yyyy-MM-dd HH:mm:ss}");

                if (credential.ExpiresAt.HasValue)
                {
                    Console.WriteLine($"  Expires:     {credential.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                }

                if (credential.Claims.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Claims:");
                    foreach (var (key, value) in credential.Claims)
                    {
                        Console.WriteLine($"    {key}: {value}");
                    }
                }

                if (!string.IsNullOrEmpty(credential.Proof))
                {
                    Console.WriteLine();
                    Console.WriteLine($"  Proof:       {credential.Proof[..Math.Min(64, credential.Proof.Length)]}...");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Issues a new verifiable credential.
/// </summary>
public class CredentialIssueCommand : Command
{
    private readonly Option<string> _typeOption;
    private readonly Option<string> _subjectOption;
    private readonly Option<string> _walletOption;
    private readonly Option<string> _claimsOption;
    private readonly Option<int?> _expiresInDaysOption;

    public CredentialIssueCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("issue", "Issue a new verifiable credential")
    {
        _typeOption = new Option<string>("--type", "-t")
        {
            Description = "Credential type (e.g., IdentityCredential, MembershipCredential)",
            Required = true
        };

        _subjectOption = new Option<string>("--subject", "-s")
        {
            Description = "Subject identifier (participant or wallet address)",
            Required = true
        };

        _walletOption = new Option<string>("--wallet", "-w")
        {
            Description = "Issuer wallet address for signing",
            Required = true
        };

        _claimsOption = new Option<string>("--claims", "-c")
        {
            Description = "Claims as JSON object (e.g., '{\"name\":\"John\",\"role\":\"admin\"}')",
            Required = true
        };

        _expiresInDaysOption = new Option<int?>("--expires-in-days", "-e")
        {
            Description = "Number of days until credential expires"
        };

        Options.Add(_typeOption);
        Options.Add(_subjectOption);
        Options.Add(_walletOption);
        Options.Add(_claimsOption);
        Options.Add(_expiresInDaysOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var type = parseResult.GetValue(_typeOption)!;
            var subject = parseResult.GetValue(_subjectOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            var claimsJson = parseResult.GetValue(_claimsOption)!;
            var expiresInDays = parseResult.GetValue(_expiresInDaysOption);

            try
            {
                // Parse claims JSON
                Dictionary<string, string> claims;
                try
                {
                    claims = JsonSerializer.Deserialize<Dictionary<string, string>>(claimsJson) ?? new();
                }
                catch (JsonException ex)
                {
                    ConsoleHelper.WriteError($"Invalid claims JSON: {ex.Message}");
                    return ExitCodes.ValidationError;
                }

                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);

                var request = new IssueCredentialRequest
                {
                    Type = type,
                    Subject = subject,
                    WalletAddress = wallet,
                    Claims = claims,
                    ExpiresInDays = expiresInDays
                };

                var credential = await client.IssueCredentialAsync(request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(credential, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Credential issued successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:      {credential.Id}");
                Console.WriteLine($"  Type:    {credential.Type}");
                Console.WriteLine($"  Subject: {credential.Subject}");
                Console.WriteLine($"  Issuer:  {credential.Issuer}");
                Console.WriteLine($"  Status:  {credential.Status}");
                Console.WriteLine($"  Issued:  {credential.IssuedAt:yyyy-MM-dd HH:mm:ss}");

                if (credential.ExpiresAt.HasValue)
                {
                    Console.WriteLine($"  Expires: {credential.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to issue credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Presents a credential to a verifier.
/// </summary>
public class CredentialPresentCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _verifierOption;
    private readonly Option<string?> _selectedClaimsOption;

    public CredentialPresentCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("present", "Present a credential to a verifier")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        _verifierOption = new Option<string>("--verifier", "-v")
        {
            Description = "Verifier wallet address",
            Required = true
        };

        _selectedClaimsOption = new Option<string?>("--claims", "-c")
        {
            Description = "Comma-separated list of claims to present (selective disclosure)"
        };

        Options.Add(_idOption);
        Options.Add(_verifierOption);
        Options.Add(_selectedClaimsOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var verifier = parseResult.GetValue(_verifierOption)!;
            var selectedClaimsStr = parseResult.GetValue(_selectedClaimsOption);

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);

                var selectedClaims = selectedClaimsStr?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                var request = new PresentCredentialRequest
                {
                    CredentialId = id,
                    VerifierAddress = verifier,
                    SelectedClaims = selectedClaims
                };

                var response = await client.PresentCredentialAsync(id, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Credential presented successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Presentation ID: {response.PresentationId}");
                Console.WriteLine($"  Status:          {response.Status}");

                if (response.VerifiedAt.HasValue)
                {
                    Console.WriteLine($"  Verified At:     {response.VerifiedAt:yyyy-MM-dd HH:mm:ss}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to present credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Verifies a credential.
/// </summary>
public class CredentialVerifyCommand : Command
{
    private readonly Option<string> _idOption;

    public CredentialVerifyCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("verify", "Verify a credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var result = await client.VerifyCredentialAsync(id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (result.IsValid)
                {
                    ConsoleHelper.WriteSuccess("Credential is VALID.");
                }
                else
                {
                    ConsoleHelper.WriteError("Credential is INVALID.");
                }

                Console.WriteLine();
                Console.WriteLine($"  Credential ID: {result.CredentialId}");
                Console.WriteLine($"  Status:        {result.Status}");
                Console.WriteLine($"  Verified At:   {result.VerifiedAt:yyyy-MM-dd HH:mm:ss}");

                if (result.Errors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Errors:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"    - {error}");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to verify credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Revokes a credential.
/// </summary>
public class CredentialRevokeCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<bool> _confirmOption;

    public CredentialRevokeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("revoke", "Revoke a credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_idOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("WARNING: Revoking a credential is irreversible.");
                    if (!ConsoleHelper.Confirm($"Are you sure you want to revoke credential '{id}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Revocation cancelled.");
                        return ExitCodes.Success;
                    }
                }

                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                await client.RevokeCredentialAsync(id, $"Bearer {token}");

                ConsoleHelper.WriteSuccess($"Credential '{id}' revoked successfully.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to revoke credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets the status of a credential.
/// </summary>
public class CredentialStatusCommand : Command
{
    private readonly Option<string> _idOption;

    public CredentialStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get the status of a credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var status = await client.GetCredentialStatusAsync(id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Credential status:");
                Console.WriteLine();
                Console.WriteLine($"  Credential ID: {status.CredentialId}");
                Console.WriteLine($"  Status:        {status.Status}");
                Console.WriteLine($"  Revoked:       {(status.IsRevoked ? "Yes" : "No")}");

                if (status.IsRevoked)
                {
                    Console.WriteLine($"  Revoked At:    {status.RevokedAt:yyyy-MM-dd HH:mm:ss}");
                    if (!string.IsNullOrEmpty(status.RevokedReason))
                    {
                        Console.WriteLine($"  Reason:        {status.RevokedReason}");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get credential status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Suspends an active credential.
/// </summary>
public class CredentialSuspendCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _walletOption;
    private readonly Option<string?> _reasonOption;

    public CredentialSuspendCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("suspend", "Suspend an active credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        _walletOption = new Option<string>("--wallet", "-w")
        {
            Description = "Issuer wallet address",
            Required = true
        };

        _reasonOption = new Option<string?>("--reason", "-r")
        {
            Description = "Reason for suspension"
        };

        Options.Add(_idOption);
        Options.Add(_walletOption);
        Options.Add(_reasonOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            var reason = parseResult.GetValue(_reasonOption);

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var response = await client.SuspendCredentialAsync(id, new LifecycleCredentialRequest
                {
                    IssuerWallet = wallet,
                    Reason = reason
                }, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Credential '{id}' suspended successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Credential ID: {response.CredentialId}");
                Console.WriteLine($"  New Status:    {response.NewStatus}");
                Console.WriteLine($"  Performed By:  {response.PerformedBy}");
                Console.WriteLine($"  Reason:        {response.Reason}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to suspend credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Reinstates a suspended credential.
/// </summary>
public class CredentialReinstateCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _walletOption;
    private readonly Option<string?> _reasonOption;

    public CredentialReinstateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("reinstate", "Reinstate a suspended credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        _walletOption = new Option<string>("--wallet", "-w")
        {
            Description = "Issuer wallet address",
            Required = true
        };

        _reasonOption = new Option<string?>("--reason", "-r")
        {
            Description = "Reason for reinstatement"
        };

        Options.Add(_idOption);
        Options.Add(_walletOption);
        Options.Add(_reasonOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            var reason = parseResult.GetValue(_reasonOption);

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var response = await client.ReinstateCredentialAsync(id, new LifecycleCredentialRequest
                {
                    IssuerWallet = wallet,
                    Reason = reason
                }, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Credential '{id}' reinstated successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Credential ID: {response.CredentialId}");
                Console.WriteLine($"  New Status:    {response.NewStatus}");
                Console.WriteLine($"  Performed By:  {response.PerformedBy}");
                Console.WriteLine($"  Reason:        {response.Reason}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to reinstate credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Refreshes an expired credential.
/// </summary>
public class CredentialRefreshCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _walletOption;
    private readonly Option<int?> _expiresInDaysOption;

    public CredentialRefreshCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("refresh", "Refresh an expired credential")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Credential ID",
            Required = true
        };

        _walletOption = new Option<string>("--wallet", "-w")
        {
            Description = "Issuer wallet address",
            Required = true
        };

        _expiresInDaysOption = new Option<int?>("--expires-in-days", "-e")
        {
            Description = "Number of days until the refreshed credential expires"
        };

        Options.Add(_idOption);
        Options.Add(_walletOption);
        Options.Add(_expiresInDaysOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            var expiresInDays = parseResult.GetValue(_expiresInDaysOption);

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

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var response = await client.RefreshCredentialAsync(id, new RefreshCredentialRequest
                {
                    IssuerWallet = wallet,
                    NewExpiryDuration = expiresInDays.HasValue ? $"P{expiresInDays}D" : null
                }, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Credential '{id}' refreshed successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Old Credential ID: {response.OldCredentialId}");
                Console.WriteLine($"  New Credential ID: {response.NewCredentialId}");
                Console.WriteLine($"  New Expires At:    {response.NewExpiresAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Credential '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to refresh credential: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Status list operations for W3C Bitstring Status Lists.
/// </summary>
public class CredentialStatusListCommand : Command
{
    public CredentialStatusListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status-list", "Status list operations")
    {
        Subcommands.Add(new CredentialStatusListGetCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets a W3C Bitstring Status List by ID.
/// </summary>
public class CredentialStatusListGetCommand : Command
{
    private readonly Option<string> _idOption;

    public CredentialStatusListGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a W3C Bitstring Status List")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Status list ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var listId = parseResult.GetValue(_idOption)!;

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var client = await clientFactory.CreateCredentialServiceClientAsync(profileName);
                var response = await client.GetStatusListAsync(listId);

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Status list details:");
                Console.WriteLine();
                Console.WriteLine($"  List ID:    {response.Id}");
                Console.WriteLine($"  Purpose:    {response.Purpose}");
                Console.WriteLine($"  Issuer:     {response.Issuer}");
                Console.WriteLine($"  Valid From:  {response.ValidFrom:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Status list '{listId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get status list: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
