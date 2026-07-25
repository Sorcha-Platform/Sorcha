// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Register management commands.
/// </summary>
public class RegisterCommand : Command
{
    public RegisterCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("register", "Manage registers (distributed ledgers)\n\nExamples:\n  sorcha register list\n  sorcha register get --id <register-id>\n  sorcha register create --name \"My Register\" --owner-wallet <wallet-addr>\n  sorcha register stats")
    {
        Subcommands.Add(new RegisterListCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterUpdateCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterStatsCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterPolicyCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterSystemCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterExportCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterExportTransactionsCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterRelationshipCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterSyncStateCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterSyncHealthCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Shows the local node's derived relationship (role set) for a register.
/// </summary>
public class RegisterRelationshipCommand : Command
{
    private readonly Option<string> _idOption;

    public RegisterRelationshipCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("relationship", "Show this node's derived role set for a register (owner/validator/subscriber)")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_idOption)!;
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query a register relationship.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var rel = await client.GetLocalRelationshipAsync(registerId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, rel);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Local register relationship:");
                Console.WriteLine();
                Console.WriteLine($"  Register ID:     {rel.RegisterId}");
                Console.WriteLine($"  Roles:           {rel.Roles}");
                Console.WriteLine($"  Owner:           {rel.IsOwner}");
                Console.WriteLine($"  Admin:           {rel.IsAdmin}");
                Console.WriteLine($"  Validator:       {rel.IsValidator}");
                Console.WriteLine($"  Auditor:         {rel.IsAuditor}");
                Console.WriteLine($"  Designer:        {rel.IsDesigner}");
                Console.WriteLine($"  Subscriber:      {rel.IsSubscriber}");
                Console.WriteLine($"  Control version: {rel.ControlRecordVersion}");
                Console.WriteLine($"  Derived at:      {rel.DerivedAt:yyyy-MM-dd HH:mm:ss} UTC");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get register relationship: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Shows a register's sync state and the evidence that produced it.
/// </summary>
public class RegisterSyncStateCommand : Command
{
    private readonly Option<string> _idOption;

    public RegisterSyncStateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("sync-state", "Show a register's sync state (indeterminate/syncing/caught-up/error)")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };
        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_idOption)!;
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to query register sync state.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var state = await client.GetSyncStateAsync(registerId, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, state);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Register sync state:");
                Console.WriteLine();
                Console.WriteLine($"  Register ID:        {state.RegisterId}");
                Console.WriteLine($"  State:              {state.State}");
                Console.WriteLine($"  Local height:       {state.LocalHeight}");
                Console.WriteLine($"  Network high-water: {state.NetworkHeightHighWaterMark?.ToString() ?? "-"}");
                Console.WriteLine($"  Peer observers:     {state.DistinctPeerObservers}");
                Console.WriteLine($"  Single-peer mode:   {state.SinglePeerMode}");
                Console.WriteLine($"  Last advert:        {state.LastAdvertAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}");
                if (!string.IsNullOrEmpty(state.LastErrorMessage))
                {
                    Console.WriteLine($"  Last error:         {state.LastErrorMessage}");
                }
                if (state.ValidatorSnapshot is not null)
                {
                    Console.WriteLine($"  Validator sealed:   height {state.ValidatorSnapshot.LastSealedHeight}, mempool {state.ValidatorSnapshot.MempoolDepth}");
                }
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get register sync state: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Shows recovery sync health across all registers on the node.
/// </summary>
public class RegisterSyncHealthCommand : Command
{
    public RegisterSyncHealthCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("sync-health", "Show recovery sync health across all registers on this node")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var health = await client.GetSyncHealthAsync(token is null ? string.Empty : $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, health);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Sync health: {health.Status} (checked {health.CheckedAt:yyyy-MM-dd HH:mm:ss} UTC)");
                Console.WriteLine();
                if (health.Registers.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No registers on this node.");
                    return ExitCodes.Success;
                }

                Console.WriteLine($"{"Register",-40} {"Status",-12} {"Local",8} {"Target",8} {"Progress",9} {"Stale"}");
                Console.WriteLine(new string('-', 90));
                foreach (var r in health.Registers)
                {
                    Console.WriteLine($"{r.RegisterId,-40} {r.Status,-12} {r.CurrentDocket,8} {r.TargetDocket,8} {r.ProgressPercent + "%",9} {(r.IsStale ? "yes" : "no")}");
                }
                return ExitCodes.Success;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get sync health: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists all registers.
/// </summary>
public class RegisterListCommand : Command
{
    public RegisterListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all registers")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to list registers.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var registers = await client.ListRegistersAsync($"Bearer {token}");

                // Display results
                if (registers == null || registers.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No registers found.");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, registers);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {registers.Count} register(s):");
                Console.WriteLine();

                Console.WriteLine($"{"ID",-34} {"Name",-25} {"Height",8} {"Status",-10} {"Purpose",-10} {"Advertise",-9} {"Created"}");
                Console.WriteLine(new string('-', 120));

                foreach (var register in registers)
                {
                    var advertise = register.Advertise ? "Yes" : "No";
                    Console.WriteLine($"{register.Id,-34} {register.Name,-25} {register.Height,8} {register.Status,-10} {register.Purpose,-10} {advertise,-9} {register.CreatedAt:yyyy-MM-dd}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to list registers.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list registers: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a register by ID.
/// </summary>
public class RegisterGetCommand : Command
{
    private readonly Option<string> _idOption;

    public RegisterGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a register by ID")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };

        Options.Add(_idOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to get a register.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var register = await client.GetRegisterAsync(id, $"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, register);
                    return ExitCodes.Success;
                }

                // Display results with all new fields
                ConsoleHelper.WriteSuccess("Register details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {register.Id}");
                Console.WriteLine($"  Name:            {register.Name}");
                Console.WriteLine($"  Status:          {register.Status}");
                Console.WriteLine($"  Purpose:         {register.Purpose}");
                Console.WriteLine($"  Height:          {register.Height}");
                Console.WriteLine($"  Advertise:       {(register.Advertise ? "Yes" : "No")}");
                Console.WriteLine($"  IsFullReplica:   {(register.IsFullReplica ? "Yes" : "No")}");
                Console.WriteLine($"  Created:         {register.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Updated:         {register.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                if (!string.IsNullOrEmpty(register.Votes))
                {
                    Console.WriteLine($"  Votes:           {register.Votes}");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view this register.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get register: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new register using the two-phase cryptographic attestation flow.
/// </summary>
public class RegisterCreateCommand : Command
{
    private readonly Option<string> _nameOption;
    private readonly Option<string> _ownerWalletOption;
    private readonly Option<string?> _descriptionOption;
    private readonly Option<string> _purposeOption;

    public RegisterCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a new register")
    {
        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Register name",
            Required = true
        };

        _ownerWalletOption = new Option<string>("--owner-wallet", "-w")
        {
            Description = "Owner wallet address for signing attestation",
            Required = true
        };

        _descriptionOption = new Option<string?>("--description", "-d")
        {
            Description = "Register description"
        };

        _purposeOption = new Option<string>("--purpose")
        {
            Description = "Register purpose (General or System)",
            DefaultValueFactory = _ => "General"
        };

        Options.Add(_nameOption);
        Options.Add(_ownerWalletOption);
        Options.Add(_descriptionOption);
        Options.Add(_purposeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;
            var ownerWallet = parseResult.GetValue(_ownerWalletOption)!;
            var description = parseResult.GetValue(_descriptionOption);
            var purpose = parseResult.GetValue(_purposeOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to create a register.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Extract user ID from token claims
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                    ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "userId")?.Value
                    ?? throw new InvalidOperationException("Could not extract user ID from token");

                // Create clients
                var registerClient = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var walletClient = await clientFactory.CreateWalletServiceClientAsync(profileName);

                ConsoleHelper.WriteInfo("Phase 1: Initiating register creation...");

                // Build initiation request
                var initiateRequest = new InitiateRegisterCreationRequest
                {
                    Name = name,
                    Description = description,
                    Purpose = Enum.TryParse<RegisterPurpose>(purpose, true, out var p) ? p : RegisterPurpose.General,
                    Owners = new List<OwnerInfo>
                    {
                        new OwnerInfo
                        {
                            UserId = userId,
                            WalletId = ownerWallet
                        }
                    }
                };

                // Phase 1: Initiate
                var initiateResponse = await registerClient.InitiateRegisterCreationAsync(initiateRequest, $"Bearer {token}");

                // Check expiration
                if (initiateResponse.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    ConsoleHelper.WriteError("Registration expired before signing could begin. Please try again.");
                    return ExitCodes.GeneralError;
                }

                ConsoleHelper.WriteInfo($"  Register ID: {initiateResponse.RegisterId}");
                ConsoleHelper.WriteInfo($"  Expires at: {initiateResponse.ExpiresAt:HH:mm:ss}");
                ConsoleHelper.WriteInfo($"  Attestations to sign: {initiateResponse.AttestationsToSign.Count}");

                // Phase 2: Sign attestations
                ConsoleHelper.WriteInfo("Phase 2: Signing attestations...");

                var signedAttestations = new List<SignedAttestation>();

                foreach (var attestation in initiateResponse.AttestationsToSign)
                {
                    ConsoleHelper.WriteInfo($"  Signing attestation for {attestation.Role}...");

                    // Convert hex hash to base64 for signing
                    var hashBytes = Convert.FromHexString(attestation.DataToSign);
                    var base64Hash = Convert.ToBase64String(hashBytes);

                    // Sign using wallet service with IsPreHashed=true
                    var signRequest = new SignTransactionRequest
                    {
                        TransactionData = base64Hash,
                        IsPreHashed = true
                    };

                    SignTransactionResponse signResponse;
                    try
                    {
                        signResponse = await walletClient.SignTransactionAsync(attestation.WalletId, signRequest, $"Bearer {token}");
                    }
                    catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable || ex.StatusCode == HttpStatusCode.GatewayTimeout)
                    {
                        ConsoleHelper.WriteError("Wallet service is unreachable. Please ensure the wallet service is running.");
                        return ExitCodes.GeneralError;
                    }
                    catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        ConsoleHelper.WriteError($"Wallet '{attestation.WalletId}' not found.");
                        return ExitCodes.NotFound;
                    }

                    // The wallet sign endpoint does not return a signature algorithm, so ED25519 (the
                    // platform default for register attestations) is used. This is unchanged behaviour:
                    // the former SignTransactionResponse.Algorithm field was never populated by the
                    // server, so the previous Enum.TryParse always fell through to this default.
                    var algorithm = SignatureAlgorithm.ED25519;

                    signedAttestations.Add(new SignedAttestation
                    {
                        AttestationData = attestation.AttestationData,
                        PublicKey = signResponse.PublicKey,
                        Signature = signResponse.Signature,
                        Algorithm = algorithm
                    });
                }

                // Check expiration again before finalize
                if (initiateResponse.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    ConsoleHelper.WriteError("Registration expired during signing. Please try again.");
                    ConsoleHelper.WriteInfo("Tip: Ensure your wallet service responds quickly.");
                    return ExitCodes.GeneralError;
                }

                // Phase 3: Finalize
                ConsoleHelper.WriteInfo("Phase 3: Finalizing register creation...");

                var finalizeRequest = new FinalizeRegisterCreationRequest
                {
                    RegisterId = initiateResponse.RegisterId,
                    Nonce = initiateResponse.Nonce,
                    SignedAttestations = signedAttestations
                };

                FinalizeRegisterCreationResponse finalizeResponse;
                try
                {
                    finalizeResponse = await registerClient.FinalizeRegisterCreationAsync(finalizeRequest, $"Bearer {token}");
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Gone)
                {
                    ConsoleHelper.WriteError("Registration expired. The 5-minute window has passed. Please try again.");
                    return ExitCodes.GeneralError;
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
                {
                    ConsoleHelper.WriteError("Invalid signature or attestation data.");
                    if (ex.Content != null)
                    {
                        ConsoleHelper.WriteError($"Details: {ex.Content}");
                    }
                    return ExitCodes.ValidationError;
                }

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, finalizeResponse);
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Register created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Register ID:       {finalizeResponse.RegisterId}");
                Console.WriteLine($"  Genesis TX ID:     {finalizeResponse.GenesisTransactionId}");
                Console.WriteLine($"  Genesis Docket ID: {finalizeResponse.GenesisDocketId}");
                Console.WriteLine($"  Created:           {finalizeResponse.CreatedAt:yyyy-MM-dd HH:mm:ss}");

                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Use 'sorcha register get --id {finalizeResponse.RegisterId}' to view details.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError("Invalid request. Please check your input.");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to create registers.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"A register with name '{name}' already exists.");
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to create register: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a register.
/// </summary>
public class RegisterDeleteCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<bool> _confirmOption;

    public RegisterDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a register")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
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
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to delete a register.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Confirm deletion
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("WARNING: This will permanently delete the register and all its transactions.");
                    Console.Write($"Are you sure you want to delete register '{id}'? [y/N]: ");
                    var response = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (response != "y" && response != "yes")
                    {
                        ConsoleHelper.WriteInfo("Deletion cancelled.");
                        return ExitCodes.Success;
                    }
                }

                // Call API
                await client.DeleteRegisterAsync(id, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Register '{id}' deleted successfully.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to delete this register.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to delete register: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Updates a register's metadata.
/// </summary>
public class RegisterUpdateCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string?> _nameOption;
    private readonly Option<string?> _statusOption;
    private readonly Option<bool?> _advertiseOption;

    public RegisterUpdateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("update", "Update register metadata")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };

        _nameOption = new Option<string?>("--name", "-n")
        {
            Description = "New register name"
        };

        _statusOption = new Option<string?>("--status", "-s")
        {
            Description = "New status (Online, Offline, Checking, Recovery)"
        };

        _advertiseOption = new Option<bool?>("--advertise", "-a")
        {
            Description = "Whether to advertise on peer network (true/false)"
        };

        Options.Add(_idOption);
        Options.Add(_nameOption);
        Options.Add(_statusOption);
        Options.Add(_advertiseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var name = parseResult.GetValue(_nameOption);
            var status = parseResult.GetValue(_statusOption);
            var advertise = parseResult.GetValue(_advertiseOption);

            // Validate at least one update field
            if (name == null && status == null && advertise == null)
            {
                ConsoleHelper.WriteError("At least one update option is required (--name, --status, or --advertise).");
                return ExitCodes.ValidationError;
            }

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to update a register.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Build request
                var request = new UpdateRegisterRequest
                {
                    Name = name,
                    Status = status,
                    Advertise = advertise
                };

                // Call API
                var register = await client.UpdateRegisterAsync(id, request, $"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, register);
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Register updated successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {register.Id}");
                Console.WriteLine($"  Name:            {register.Name}");
                Console.WriteLine($"  Status:          {register.Status}");
                Console.WriteLine($"  Advertise:       {(register.Advertise ? "Yes" : "No")}");
                Console.WriteLine($"  Updated:         {register.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{id}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to update this register.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to update register: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets register statistics.
/// </summary>
public class RegisterStatsCommand : Command
{
    public RegisterStatsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("stats", "Get register statistics")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to get register statistics.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var stats = await client.GetRegisterStatsAsync($"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, stats);
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Register statistics:");
                Console.WriteLine();
                Console.WriteLine($"  Total registers: {stats.Count}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view register statistics.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API Error: {ex.Message}");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get register statistics: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Register policy management commands.
/// </summary>
public class RegisterPolicyCommand : Command
{
    public RegisterPolicyCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("policy", "Manage register policies")
    {
        Subcommands.Add(new RegisterPolicyGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterPolicyHistoryCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterPolicyUpdateCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets the current register policy.
/// </summary>
public class RegisterPolicyGetCommand : Command
{
    private readonly Option<string> _registerIdOption;

    public RegisterPolicyGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get current register policy")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        Options.Add(_registerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;

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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var response = await client.GetPolicyAsync(registerId, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess("Register policy:");
                Console.WriteLine();
                Console.WriteLine($"  Register ID:        {RegisterJsonHelper.GetString(root, "registerId")}");
                Console.WriteLine($"  Min Validators:     {RegisterJsonHelper.GetString(root, "minValidators")}");
                Console.WriteLine($"  Max Validators:     {RegisterJsonHelper.GetString(root, "maxValidators")}");
                Console.WriteLine($"  Signature Threshold:{RegisterJsonHelper.GetString(root, "signatureThreshold")}");
                Console.WriteLine($"  Registration Mode:  {RegisterJsonHelper.GetString(root, "registrationMode")}");
                Console.WriteLine($"  Transition Mode:    {RegisterJsonHelper.GetString(root, "transitionMode")}");
                Console.WriteLine($"  Version:            {RegisterJsonHelper.GetString(root, "version")}");
                Console.WriteLine($"  Updated At:         {RegisterJsonHelper.GetString(root, "updatedAt")}");
                Console.WriteLine($"  Updated By:         {RegisterJsonHelper.GetString(root, "updatedBy")}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found or has no policy.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get register policy: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets the register policy version history.
/// </summary>
public class RegisterPolicyHistoryCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public RegisterPolicyHistoryCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("history", "Get register policy version history")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _pageOption = new Option<int?>("--page")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size")
        {
            Description = "Page size (default: 20)"
        };

        Options.Add(_registerIdOption);
        Options.Add(_pageOption);
        Options.Add(_pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var page = parseResult.GetValue(_pageOption);
            var pageSize = parseResult.GetValue(_pageSizeOption);

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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var response = await client.GetPolicyHistoryAsync(registerId, page, pageSize, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess($"Policy history for register '{registerId}':");
                Console.WriteLine();

                if (root.TryGetProperty("versions", out var versions) && versions.GetArrayLength() > 0)
                {
                    Console.WriteLine($"{"Version",-10} {"Updated By",-30} {"Updated At"}");
                    Console.WriteLine(new string('-', 70));

                    foreach (var version in versions.EnumerateArray())
                    {
                        Console.WriteLine($"{RegisterJsonHelper.GetString(version, "version"),-10} {RegisterJsonHelper.GetString(version, "updatedBy"),-30} {RegisterJsonHelper.GetString(version, "updatedAt")}");
                    }

                    if (root.TryGetProperty("totalCount", out var totalCount))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  Total: {totalCount}");
                    }
                }
                else
                {
                    ConsoleHelper.WriteInfo("No policy history found.");
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
                ConsoleHelper.WriteError($"Failed to get policy history: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Proposes a policy update for a register.
/// </summary>
public class RegisterPolicyUpdateCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<int?> _minValidatorsOption;
    private readonly Option<int?> _maxValidatorsOption;
    private readonly Option<int?> _signatureThresholdMinOption;
    private readonly Option<int?> _signatureThresholdMaxOption;
    private readonly Option<string?> _registrationModeOption;
    private readonly Option<string?> _updatedByOption;
    private readonly Option<bool> _confirmOption;

    public RegisterPolicyUpdateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("update", "Propose a register policy update")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _minValidatorsOption = new Option<int?>("--min-validators")
        {
            Description = "Minimum number of validators"
        };

        _maxValidatorsOption = new Option<int?>("--max-validators")
        {
            Description = "Maximum number of validators"
        };

        // PolicyConsensusConfig bounds the threshold with a min and a max rather than carrying a
        // single value, so the old --signature-threshold had no unambiguous target.
        _signatureThresholdMinOption = new Option<int?>("--signature-threshold-min")
        {
            Description = "Minimum signature threshold for consensus"
        };

        _signatureThresholdMaxOption = new Option<int?>("--signature-threshold-max")
        {
            Description = "Maximum signature threshold for consensus"
        };

        _registrationModeOption = new Option<string?>("--registration-mode")
        {
            Description = "Registration mode (Public or Consent)"
        };

        _updatedByOption = new Option<string?>("--updated-by")
        {
            Description = "DID of the proposer (sent to the server as updatedBy)"
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_minValidatorsOption);
        Options.Add(_maxValidatorsOption);
        Options.Add(_signatureThresholdMinOption);
        Options.Add(_signatureThresholdMaxOption);
        Options.Add(_registrationModeOption);
        Options.Add(_updatedByOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var minValidators = parseResult.GetValue(_minValidatorsOption);
            var maxValidators = parseResult.GetValue(_maxValidatorsOption);
            var signatureThresholdMin = parseResult.GetValue(_signatureThresholdMinOption);
            var signatureThresholdMax = parseResult.GetValue(_signatureThresholdMaxOption);
            var registrationMode = parseResult.GetValue(_registrationModeOption);
            var updatedBy = parseResult.GetValue(_updatedByOption);
            var confirm = parseResult.GetValue(_confirmOption);

            if (minValidators == null && maxValidators == null && signatureThresholdMin == null
                && signatureThresholdMax == null && registrationMode == null)
            {
                ConsoleHelper.WriteError("At least one policy field must be specified (--min-validators, --max-validators, --signature-threshold-min, --signature-threshold-max, --registration-mode).");
                return ExitCodes.ValidationError;
            }

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("You are about to propose a policy update:");
                    if (minValidators.HasValue) Console.WriteLine($"  Min Validators:     {minValidators}");
                    if (maxValidators.HasValue) Console.WriteLine($"  Max Validators:     {maxValidators}");
                    if (signatureThresholdMin.HasValue) Console.WriteLine($"  Sig Threshold Min:  {signatureThresholdMin}");
                    if (signatureThresholdMax.HasValue) Console.WriteLine($"  Sig Threshold Max:  {signatureThresholdMax}");
                    if (registrationMode != null) Console.WriteLine($"  Registration Mode:  {registrationMode}");

                    if (!ConsoleHelper.Confirm("Propose policy update?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Policy update cancelled.");
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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // The server replaces the policy wholesale - it binds a complete RegisterPolicy, not
                // a set of deltas. So read the current policy, apply the requested changes to it,
                // and propose the result. Sending only the changed fields (as this command used to)
                // would leave every other setting at its type default.
                var currentResponse = await client.GetPolicyAsync(registerId, $"Bearer {token}");
                if (!currentResponse.IsSuccessStatusCode)
                {
                    var currentError = await currentResponse.Content.ReadAsStringAsync(ct);
                    ConsoleHelper.WriteError(
                        $"Could not read the current policy to base this proposal on ({currentResponse.StatusCode}): {currentError}");
                    return ExitCodes.GeneralError;
                }

                var current = JsonSerializer.Deserialize<RegisterPolicyResponse>(
                    await currentResponse.Content.ReadAsStringAsync(ct), SorchaJsonOptions.Default);

                if (current?.Policy is null)
                {
                    ConsoleHelper.WriteError("The current policy could not be parsed; refusing to propose a replacement.");
                    return ExitCodes.GeneralError;
                }

                var proposed = current.Policy;
                if (minValidators.HasValue) proposed.Validators.MinValidators = minValidators.Value;
                if (maxValidators.HasValue) proposed.Validators.MaxValidators = maxValidators.Value;
                if (signatureThresholdMin.HasValue) proposed.Consensus.SignatureThresholdMin = signatureThresholdMin.Value;
                if (signatureThresholdMax.HasValue) proposed.Consensus.SignatureThresholdMax = signatureThresholdMax.Value;
                if (!string.IsNullOrWhiteSpace(registrationMode))
                {
                    if (!Enum.TryParse<RegistrationMode>(registrationMode, ignoreCase: true, out var mode))
                    {
                        ConsoleHelper.WriteError($"Unknown registration mode '{registrationMode}'. Expected Public or Consent.");
                        return ExitCodes.ValidationError;
                    }

                    proposed.Validators.RegistrationMode = mode;
                }

                // The server expects the proposal to carry an incremented version.
                proposed.Version = current.Policy.Version + 1;

                var request = new PolicyUpdateRequest
                {
                    Policy = proposed,
                    UpdatedBy = updatedBy ?? string.Empty
                };

                var response = await client.ProposePolicyUpdateAsync(registerId, request, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess("Policy update proposed!");
                Console.WriteLine();
                Console.WriteLine($"  Proposal ID:       {RegisterJsonHelper.GetString(root, "proposalId")}");
                Console.WriteLine($"  Proposed Version:  {RegisterJsonHelper.GetString(root, "proposedVersion")}");
                Console.WriteLine($"  Status:            {RegisterJsonHelper.GetString(root, "status")}");
                Console.WriteLine($"  Required Votes:    {RegisterJsonHelper.GetString(root, "requiredVotes")}");
                Console.WriteLine($"  Current Votes:     {RegisterJsonHelper.GetString(root, "currentVotes")}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to update this register's policy.");
                return ExitCodes.AuthorizationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to propose policy update: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// System register management commands.
/// </summary>
public class RegisterSystemCommand : Command
{
    public RegisterSystemCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("system", "Manage the system register")
    {
        Subcommands.Add(new RegisterSystemStatusCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterSystemBlueprintsCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Gets the system register status.
/// </summary>
public class RegisterSystemStatusCommand : Command
{
    public RegisterSystemStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get system register status")
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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var response = await client.GetSystemRegisterStatusAsync($"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess("System register status:");
                Console.WriteLine();

                foreach (var prop in root.EnumerateObject())
                {
                    Console.WriteLine($"  {prop.Name,-25} {prop.Value}");
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
                ConsoleHelper.WriteError($"Failed to get system register status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets blueprints published to the system register.
/// </summary>
public class RegisterSystemBlueprintsCommand : Command
{
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public RegisterSystemBlueprintsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("blueprints", "List blueprints on the system register")
    {
        _pageOption = new Option<int?>("--page")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size")
        {
            Description = "Page size (default: 20)"
        };

        Options.Add(_pageOption);
        Options.Add(_pageSizeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var page = parseResult.GetValue(_pageOption);
            var pageSize = parseResult.GetValue(_pageSizeOption);

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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var response = await client.GetSystemRegisterBlueprintsAsync(page, pageSize, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    Console.WriteLine(content);
                    return ExitCodes.Success;
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                ConsoleHelper.WriteSuccess("System register blueprints:");
                Console.WriteLine();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0)
                    {
                        ConsoleHelper.WriteInfo("No blueprints found.");
                        return ExitCodes.Success;
                    }

                    Console.WriteLine($"{"ID",-38} {"Title",-30} {"Status",-12} {"Published"}");
                    Console.WriteLine(new string('-', 90));

                    foreach (var item in root.EnumerateArray())
                    {
                        Console.WriteLine($"{RegisterJsonHelper.GetString(item, "id"),-38} {RegisterJsonHelper.GetString(item, "title"),-30} {RegisterJsonHelper.GetString(item, "status"),-12} {RegisterJsonHelper.GetString(item, "publishedAt")}");
                    }
                }
                else if (root.TryGetProperty("items", out var items))
                {
                    if (items.GetArrayLength() == 0)
                    {
                        ConsoleHelper.WriteInfo("No blueprints found.");
                        return ExitCodes.Success;
                    }

                    Console.WriteLine($"{"ID",-38} {"Title",-30} {"Status",-12} {"Published"}");
                    Console.WriteLine(new string('-', 90));

                    foreach (var item in items.EnumerateArray())
                    {
                        Console.WriteLine($"{RegisterJsonHelper.GetString(item, "id"),-38} {RegisterJsonHelper.GetString(item, "title"),-30} {RegisterJsonHelper.GetString(item, "status"),-12} {RegisterJsonHelper.GetString(item, "publishedAt")}");
                    }

                    if (root.TryGetProperty("totalCount", out var totalCount))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"  Total: {totalCount}");
                    }
                }
                else
                {
                    // Fallback: display raw properties
                    foreach (var prop in root.EnumerateObject())
                    {
                        Console.WriteLine($"  {prop.Name,-25} {prop.Value}");
                    }
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
                ConsoleHelper.WriteError($"Failed to get system register blueprints: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Helper to safely extract a string from a JsonElement (file-local).
/// </summary>
file static class RegisterJsonHelper
{
    public static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ToString();
        }
        return "-";
    }
}

/// <summary>
/// Exports register metadata and policy as JSON to a file.
/// </summary>
public class RegisterExportCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _outputOption;

    public RegisterExportCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("export", "Export register metadata and policy as JSON")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };

        _outputOption = new Option<string>("--output")
        {
            Description = "Output file path (JSON)",
            Required = true
        };

        Options.Add(_idOption);
        Options.Add(_outputOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var outputPath = parseResult.GetValue(_outputOption)!;

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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                ConsoleHelper.WriteInfo($"Exporting register '{id}'...");

                // Fetch register metadata
                var register = await client.GetRegisterAsync(id, $"Bearer {token}");

                // Fetch policy (returns HttpResponseMessage)
                JsonElement? policy = null;
                try
                {
                    var policyResponse = await client.GetPolicyAsync(id, $"Bearer {token}");
                    if (policyResponse.IsSuccessStatusCode)
                    {
                        var policyContent = await policyResponse.Content.ReadAsStringAsync(ct);
                        policy = JsonSerializer.Deserialize<JsonElement>(policyContent);
                    }
                }
                catch
                {
                    // Policy may not exist; continue without it
                }

                // Build export object
                var export = new
                {
                    ExportedAt = DateTimeOffset.UtcNow,
                    Register = register,
                    Policy = policy
                };

                var json = JsonSerializer.Serialize(export, SorchaJsonOptions.Default);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(outputPath, json, ct);

                ConsoleHelper.WriteSuccess($"Register exported to: {Path.GetFullPath(outputPath)}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{id}' not found.");
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
                ConsoleHelper.WriteError($"Failed to export register: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Exports all transactions from a register as CSV or JSON.
/// </summary>
public class RegisterExportTransactionsCommand : Command
{
    private readonly Option<string> _idOption;
    private readonly Option<string> _outputOption;
    private readonly Option<string> _formatOption;

    public RegisterExportTransactionsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("export-transactions", "Export register transactions as CSV or JSON")
    {
        _idOption = new Option<string>("--id", "-i")
        {
            Description = "Register ID",
            Required = true
        };

        _outputOption = new Option<string>("--output")
        {
            Description = "Output file path",
            Required = true
        };

        _formatOption = new Option<string>("--format", "-f")
        {
            Description = "Export format (json or csv)",
            DefaultValueFactory = _ => "json"
        };

        Options.Add(_idOption);
        Options.Add(_outputOption);
        Options.Add(_formatOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var id = parseResult.GetValue(_idOption)!;
            var outputPath = parseResult.GetValue(_outputOption)!;
            var format = parseResult.GetValue(_formatOption)!.ToLowerInvariant();

            if (format is not "json" and not "csv")
            {
                ConsoleHelper.WriteError("Format must be 'json' or 'csv'.");
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

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                ConsoleHelper.WriteInfo($"Fetching transactions for register '{id}'...");

                // Fetch all transactions with pagination
                var allTransactions = new List<TransactionModel>();
                var page = 1;
                const int pageSize = 100;

                while (true)
                {
                    var batch = await client.ListTransactionsAsync(id, page, pageSize, $"Bearer {token}");
                    if (batch == null || batch.Count == 0)
                        break;

                    allTransactions.AddRange(batch);
                    Console.WriteLine($"  Fetched page {page} ({allTransactions.Count} transactions so far)...");

                    if (batch.Count < pageSize)
                        break;

                    page++;
                }

                if (allTransactions.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No transactions found to export.");
                    return ExitCodes.Success;
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (format == "json")
                {
                    var json = JsonSerializer.Serialize(allTransactions, SorchaJsonOptions.Default);
                    await File.WriteAllTextAsync(outputPath, json, ct);
                }
                else // csv
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Id,TxId,RegisterId,SenderWallet,DocketNumber,Version,TimeStamp,PayloadCount,PrevTxId");

                    foreach (var tx in allTransactions)
                    {
                        var txIdField = Escape(tx.Id);
                        var txHash = Escape(tx.TxId);
                        var regId = Escape(tx.RegisterId);
                        var sender = Escape(tx.SenderWallet);
                        var docket = tx.DocketNumber?.ToString() ?? "";
                        var version = tx.Version.ToString();
                        var timestamp = tx.TimeStamp.ToString("o");
                        var payloadCount = tx.PayloadCount.ToString();
                        var prevTxId = Escape(tx.PrevTxId);

                        sb.AppendLine($"{txIdField},{txHash},{regId},{sender},{docket},{version},{timestamp},{payloadCount},{prevTxId}");
                    }

                    await File.WriteAllTextAsync(outputPath, sb.ToString(), ct);
                }

                ConsoleHelper.WriteSuccess($"Exported {allTransactions.Count} transaction(s) to: {Path.GetFullPath(outputPath)}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{id}' not found.");
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
                ConsoleHelper.WriteError($"Failed to export transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    /// <summary>
    /// Escapes a value for CSV output.
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
