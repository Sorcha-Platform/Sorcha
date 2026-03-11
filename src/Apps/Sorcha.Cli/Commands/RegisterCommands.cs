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
        : base("register", "Manage registers (distributed ledgers)")
    {
        Subcommands.Add(new RegisterListCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterUpdateCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterStatsCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterPolicyCommand(clientFactory, authService, configService));
        Subcommands.Add(new RegisterSystemCommand(clientFactory, authService, configService));
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

                // Check output format
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(registers, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                if (registers == null || registers.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No registers found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {registers.Count} register(s):");
                Console.WriteLine();

                // Display as table with new fields
                Console.WriteLine($"{"ID",-34} {"Name",-25} {"Height",8} {"Status",-10} {"TenantId",-34} {"Advertise",-9} {"Created"}");
                Console.WriteLine(new string('-', 145));

                foreach (var register in registers)
                {
                    var advertise = register.Advertise ? "Yes" : "No";
                    Console.WriteLine($"{register.Id,-34} {register.Name,-25} {register.Height,8} {register.Status,-10} {register.TenantId,-34} {advertise,-9} {register.CreatedAt:yyyy-MM-dd}");
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
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(register, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results with all new fields
                ConsoleHelper.WriteSuccess("Register details:");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {register.Id}");
                Console.WriteLine($"  Name:            {register.Name}");
                Console.WriteLine($"  TenantId:        {register.TenantId}");
                Console.WriteLine($"  Status:          {register.Status}");
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
    private readonly Option<string> _tenantIdOption;
    private readonly Option<string> _ownerWalletOption;
    private readonly Option<string?> _descriptionOption;

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

        _tenantIdOption = new Option<string>("--tenant-id", "-t")
        {
            Description = "Tenant ID",
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

        Options.Add(_nameOption);
        Options.Add(_tenantIdOption);
        Options.Add(_ownerWalletOption);
        Options.Add(_descriptionOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;
            var tenantId = parseResult.GetValue(_tenantIdOption)!;
            var ownerWallet = parseResult.GetValue(_ownerWalletOption)!;
            var description = parseResult.GetValue(_descriptionOption);

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
                    TenantId = tenantId,
                    Description = description,
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

                    // Parse algorithm
                    if (!Enum.TryParse<SignatureAlgorithm>(signResponse.Algorithm, true, out var algorithm))
                    {
                        algorithm = SignatureAlgorithm.ED25519; // Default
                    }

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
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(finalizeResponse, new JsonSerializerOptions { WriteIndented = true }));
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
                ConsoleHelper.WriteError($"A register with name '{name}' already exists in tenant '{tenantId}'.");
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
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(register, new JsonSerializerOptions { WriteIndented = true }));
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Register updated successfully!");
                Console.WriteLine();
                Console.WriteLine($"  ID:              {register.Id}");
                Console.WriteLine($"  Name:            {register.Name}");
                Console.WriteLine($"  TenantId:        {register.TenantId}");
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
                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true }));
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
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
    private readonly Option<int?> _signatureThresholdOption;
    private readonly Option<string?> _registrationModeOption;
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

        _signatureThresholdOption = new Option<int?>("--signature-threshold")
        {
            Description = "Signature threshold for consensus"
        };

        _registrationModeOption = new Option<string?>("--registration-mode")
        {
            Description = "Registration mode (open or consent)"
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_registerIdOption);
        Options.Add(_minValidatorsOption);
        Options.Add(_maxValidatorsOption);
        Options.Add(_signatureThresholdOption);
        Options.Add(_registrationModeOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var minValidators = parseResult.GetValue(_minValidatorsOption);
            var maxValidators = parseResult.GetValue(_maxValidatorsOption);
            var signatureThreshold = parseResult.GetValue(_signatureThresholdOption);
            var registrationMode = parseResult.GetValue(_registrationModeOption);
            var confirm = parseResult.GetValue(_confirmOption);

            if (minValidators == null && maxValidators == null && signatureThreshold == null && registrationMode == null)
            {
                ConsoleHelper.WriteError("At least one policy field must be specified (--min-validators, --max-validators, --signature-threshold, --registration-mode).");
                return ExitCodes.ValidationError;
            }

            try
            {
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("You are about to propose a policy update:");
                    if (minValidators.HasValue) Console.WriteLine($"  Min Validators:     {minValidators}");
                    if (maxValidators.HasValue) Console.WriteLine($"  Max Validators:     {maxValidators}");
                    if (signatureThreshold.HasValue) Console.WriteLine($"  Signature Threshold:{signatureThreshold}");
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

                var request = new PolicyUpdateRequest
                {
                    MinValidators = minValidators,
                    MaxValidators = maxValidators,
                    SignatureThreshold = signatureThreshold,
                    RegistrationMode = registrationMode
                };

                var response = await client.ProposePolicyUpdateAsync(registerId, request, $"Bearer {token}");
                var content = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    ConsoleHelper.WriteError($"API error ({response.StatusCode}): {content}");
                    return ExitCodes.GeneralError;
                }

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
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

                var outputFormat = parseResult.GetValue(BaseCommand.OutputOption!) ?? "table";
                if (outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
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
