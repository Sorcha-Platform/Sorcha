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
/// Participant identity management commands.
/// </summary>
public class ParticipantCommand : Command
{
    public ParticipantCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("participant", "Manage participant identities")
    {
        Subcommands.Add(new ParticipantRegisterCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantListCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantUpdateCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantSearchCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantWalletLinkCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantSuspendCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantReactivateCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantPublishCommand(clientFactory, authService, configService));
        Subcommands.Add(new ParticipantUnpublishCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Registers a new participant in an organization.
/// </summary>
public class ParticipantRegisterCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _userIdOption;
    private readonly Option<string> _displayNameOption;

    public ParticipantRegisterCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("register", "Register a new participant in an organization")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _userIdOption = new Option<string>("--user-id", "-u")
        {
            Description = "User ID",
            Required = true
        };

        _displayNameOption = new Option<string>("--display-name", "-n")
        {
            Description = "Display name for the participant",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_userIdOption);
        Options.Add(_displayNameOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var userId = parseResult.GetValue(_userIdOption)!;
            var displayName = parseResult.GetValue(_displayNameOption)!;

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);

                var request = new RegisterParticipantRequest
                {
                    UserId = userId,
                    DisplayName = displayName
                };

                var participant = await client.RegisterParticipantAsync(orgId, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(participant, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Participant registered successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {participant.Id}");
                Console.WriteLine($"  User ID:         {participant.UserId}");
                Console.WriteLine($"  Organization:    {participant.OrganizationId}");
                Console.WriteLine($"  Display Name:    {participant.DisplayName}");
                Console.WriteLine($"  Status:          {participant.Status}");
                Console.WriteLine($"  Created:         {participant.CreatedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid request: {ex.Content}");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError("A participant with this user ID already exists in this organization.");
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
                ConsoleHelper.WriteError($"Failed to register participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists participants in an organization.
/// </summary>
public class ParticipantListCommand : Command
{
    private readonly Option<string> _orgIdOption;

    public ParticipantListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List participants in an organization")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        Options.Add(_orgIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);
                var participants = await client.ListParticipantsAsync(orgId, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(participants, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (participants == null || participants.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No participants found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {participants.Count} participant(s):");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Display Name",-30} {"Status",-12} {"Wallets",7} {"Created"}");
                Console.WriteLine(new string('-', 105));

                foreach (var p in participants)
                {
                    Console.WriteLine($"{p.Id,-38} {p.DisplayName,-30} {p.Status,-12} {p.WalletLinks.Count,7} {p.CreatedAt:yyyy-MM-dd}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view participants in this organization.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list participants: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a participant by ID.
/// </summary>
public class ParticipantGetCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _idOption;

    public ParticipantGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a participant by ID")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Participant ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);
                var participant = await client.GetParticipantAsync(orgId, id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(participant, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Participant details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {participant.Id}");
                Console.WriteLine($"  User ID:         {participant.UserId}");
                Console.WriteLine($"  Organization:    {participant.OrganizationId}");
                Console.WriteLine($"  Display Name:    {participant.DisplayName}");
                Console.WriteLine($"  Status:          {participant.Status}");
                Console.WriteLine($"  Created:         {participant.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Updated:         {participant.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                if (participant.WalletLinks.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  Linked Wallets ({participant.WalletLinks.Count}):");
                    foreach (var link in participant.WalletLinks)
                    {
                        var verified = link.VerifiedAt.HasValue ? $"verified {link.VerifiedAt:yyyy-MM-dd}" : "unverified";
                        Console.WriteLine($"    - {link.WalletAddress} ({link.Status}, {verified})");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{id}' not found in organization '{orgId}'.");
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
                ConsoleHelper.WriteError($"Failed to get participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Updates a participant.
/// </summary>
public class ParticipantUpdateCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _idOption;
    private readonly Option<string?> _displayNameOption;
    private readonly Option<string?> _statusOption;

    public ParticipantUpdateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("update", "Update a participant")
    {
        _orgIdOption = new Option<string>("--org-id", "-o")
        {
            Description = "Organization ID",
            Required = true
        };

        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Participant ID",
            Required = true
        };

        _displayNameOption = new Option<string?>("--display-name", "-n")
        {
            Description = "New display name"
        };

        _statusOption = new Option<string?>("--status", "-s")
        {
            Description = "New status (Active, Inactive)"
        };

        Options.Add(_orgIdOption);
        Options.Add(_idOption);
        Options.Add(_displayNameOption);
        Options.Add(_statusOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var id = parseResult.GetValue(_idOption)!;
            var displayName = parseResult.GetValue(_displayNameOption);
            var status = parseResult.GetValue(_statusOption);

            if (displayName == null && status == null)
            {
                ConsoleHelper.WriteError("At least one update option is required (--display-name or --status).");
                return ExitCodes.ValidationError;
            }

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);

                var request = new UpdateParticipantRequest
                {
                    DisplayName = displayName,
                    Status = status
                };

                var participant = await client.UpdateParticipantAsync(orgId, id, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(participant, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Participant updated successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {participant.Id}");
                Console.WriteLine($"  Display Name:    {participant.DisplayName}");
                Console.WriteLine($"  Status:          {participant.Status}");
                Console.WriteLine($"  Updated:         {participant.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{id}' not found.");
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
                ConsoleHelper.WriteError($"Failed to update participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Searches for participants across accessible organizations.
/// </summary>
public class ParticipantSearchCommand : Command
{
    private readonly Option<string> _queryOption;
    private readonly Option<string?> _statusOption;

    public ParticipantSearchCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("search", "Search for participants across organizations")
    {
        _queryOption = new Option<string>("--query", "-q")
        {
            Description = "Search query (name, email, etc.)",
            Required = true
        };

        _statusOption = new Option<string?>("--status", "-s")
        {
            Description = "Filter by status (Active, Inactive)"
        };

        Options.Add(_queryOption);
        Options.Add(_statusOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var query = parseResult.GetValue(_queryOption)!;
            var status = parseResult.GetValue(_statusOption);

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);

                var request = new SearchParticipantsRequest
                {
                    Query = query,
                    Status = status
                };

                var participants = await client.SearchParticipantsAsync(request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(participants, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                if (participants == null || participants.Count == 0)
                {
                    ConsoleHelper.WriteInfo($"No participants found matching '{query}'.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {participants.Count} participant(s) matching '{query}':");
                Console.WriteLine();
                Console.WriteLine($"{"ID",-38} {"Display Name",-30} {"Organization",-38} {"Status",-10}");
                Console.WriteLine(new string('-', 120));

                foreach (var p in participants)
                {
                    Console.WriteLine($"{p.Id,-38} {p.DisplayName,-30} {p.OrganizationId,-38} {p.Status,-10}");
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
                ConsoleHelper.WriteError($"Failed to search participants: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Initiates a wallet link for a participant.
/// </summary>
public class ParticipantWalletLinkCommand : Command
{
    private readonly Option<string> _participantIdOption;
    private readonly Option<string> _walletAddressOption;

    public ParticipantWalletLinkCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("wallet-link", "Initiate a wallet link challenge for a participant")
    {
        _participantIdOption = new Option<string>("--participant-id", "-p")
        {
            Description = "Participant ID",
            Required = true
        };

        _walletAddressOption = new Option<string>("--wallet-address", "-w")
        {
            Description = "Wallet address to link",
            Required = true
        };

        Options.Add(_participantIdOption);
        Options.Add(_walletAddressOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var participantId = parseResult.GetValue(_participantIdOption)!;
            var walletAddress = parseResult.GetValue(_walletAddressOption)!;

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);

                var request = new InitiateWalletLinkRequest
                {
                    WalletAddress = walletAddress
                };

                var challenge = await client.InitiateWalletLinkAsync(participantId, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(challenge, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Wallet link challenge initiated!");
                Console.WriteLine();
                Console.WriteLine($"  Challenge ID:  {challenge.ChallengeId}");
                Console.WriteLine($"  Nonce:         {challenge.Nonce}");
                Console.WriteLine($"  Expires At:    {challenge.ExpiresAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
                ConsoleHelper.WriteInfo("Sign the nonce with your wallet and verify using:");
                ConsoleHelper.WriteInfo($"  sorcha participant verify-link --participant-id {participantId} --challenge-id {challenge.ChallengeId} --signature <signature> --public-key <public-key>");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{participantId}' not found.");
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
                ConsoleHelper.WriteError($"Failed to initiate wallet link: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Suspends an active participant.
/// </summary>
public class ParticipantSuspendCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _idOption;

    public ParticipantSuspendCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("suspend", "Suspend an active participant")
    {
        _orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        _idOption = new Option<string>("--id")
        {
            Description = "Participant ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);
                await client.SuspendParticipantAsync(orgId, id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { participantId = id, status = "Suspended" }, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Participant '{id}' suspended successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Participant ID:  {id}");
                Console.WriteLine($"  Status:          Suspended");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{id}' not found in organization '{orgId}'.");
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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError($"Permission denied: insufficient access to manage participant '{id}'.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to suspend participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Reactivates a suspended participant.
/// </summary>
public class ParticipantReactivateCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _idOption;

    public ParticipantReactivateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("reactivate", "Reactivate a suspended participant")
    {
        _orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        _idOption = new Option<string>("--id")
        {
            Description = "Participant ID",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);
                await client.ReactivateParticipantAsync(orgId, id, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { participantId = id, status = "Active" }, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Participant '{id}' reactivated successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Participant ID:  {id}");
                Console.WriteLine($"  Status:          Active");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{id}' not found in organization '{orgId}'.");
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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError($"Permission denied: insufficient access to manage participant '{id}'.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to reactivate participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Publishes a participant to a register.
/// </summary>
public class ParticipantPublishCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _nameOption;
    private readonly Option<string> _orgNameOption;
    private readonly Option<string> _walletOption;
    private readonly Option<string> _signerOption;

    public ParticipantPublishCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("publish", "Publish a participant to a register")
    {
        _orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        _registerIdOption = new Option<string>("--register-id")
        {
            Description = "Register ID",
            Required = true
        };

        _nameOption = new Option<string>("--name")
        {
            Description = "Participant name",
            Required = true
        };

        _orgNameOption = new Option<string>("--org-name")
        {
            Description = "Organization name",
            Required = true
        };

        _walletOption = new Option<string>("--wallet")
        {
            Description = "Wallet address",
            Required = true
        };

        _signerOption = new Option<string>("--signer")
        {
            Description = "Signer wallet address",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_registerIdOption);
        Options.Add(_nameOption);
        Options.Add(_orgNameOption);
        Options.Add(_walletOption);
        Options.Add(_signerOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var name = parseResult.GetValue(_nameOption)!;
            var orgName = parseResult.GetValue(_orgNameOption)!;
            var wallet = parseResult.GetValue(_walletOption)!;
            var signer = parseResult.GetValue(_signerOption)!;

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);

                var request = new PublishParticipantRequest
                {
                    RegisterId = registerId,
                    Name = name,
                    OrganizationName = orgName,
                    WalletAddresses = [wallet],
                    SignerWalletAddress = signer
                };

                var result = await client.PublishParticipantAsync(orgId, request, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Participant published successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Participant ID:  {result.ParticipantId}");
                Console.WriteLine($"  Register ID:     {result.RegisterId}");
                Console.WriteLine($"  Status:          {result.Status}");
                Console.WriteLine($"  Transaction ID:  {result.TransactionId}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Organization '{orgId}' not found.");
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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError($"Permission denied: insufficient access to publish participant in organization '{orgId}'.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to publish participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Revokes a published participant record from a register.
/// </summary>
public class ParticipantUnpublishCommand : Command
{
    private readonly Option<string> _orgIdOption;
    private readonly Option<string> _idOption;
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _signerOption;

    public ParticipantUnpublishCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("unpublish", "Revoke a published participant record")
    {
        _orgIdOption = new Option<string>("--org-id")
        {
            Description = "Organization ID",
            Required = true
        };

        _idOption = new Option<string>("--id")
        {
            Description = "Participant ID",
            Required = true
        };

        _registerIdOption = new Option<string>("--register-id")
        {
            Description = "Register ID",
            Required = true
        };

        _signerOption = new Option<string>("--signer")
        {
            Description = "Signer wallet address",
            Required = true
        };

        Options.Add(_orgIdOption);
        Options.Add(_idOption);
        Options.Add(_registerIdOption);
        Options.Add(_signerOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var orgId = parseResult.GetValue(_orgIdOption)!;
            var id = parseResult.GetValue(_idOption)!;
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var signer = parseResult.GetValue(_signerOption)!;

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

                var client = await clientFactory.CreateParticipantServiceClientAsync(profileName);
                var result = await client.UnpublishParticipantAsync(orgId, id, registerId, signer, $"Bearer {token}");

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Participant '{id}' unpublished from register '{registerId}' successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Participant ID:  {result.ParticipantId}");
                Console.WriteLine($"  Register ID:     {result.RegisterId}");
                Console.WriteLine($"  Status:          {result.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Participant '{id}' not found in organization '{orgId}'.");
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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError($"Permission denied: insufficient access to unpublish participant '{id}'.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to unpublish participant: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
