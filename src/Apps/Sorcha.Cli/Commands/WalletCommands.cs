// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Wallet management commands.
/// </summary>
public class WalletCommand : Command
{
    public WalletCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("wallet", "Manage cryptographic wallets")
    {
        Subcommands.Add(new WalletListCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletCreateCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletRecoverCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletDeleteCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletSignCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletAccessCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all wallets for the current user.
/// </summary>
public class WalletListCommand : Command
{
    public WalletListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all wallets")
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
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Call API
                var wallets = await client.ListWalletsAsync($"Bearer {token}");

                // Display results
                if (wallets == null || wallets.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No wallets found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {wallets.Count} wallet(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Address",-45} {"Name",-25} {"Algorithm",-12} {"Status",-10}");
                Console.WriteLine(new string('-', 95));
                foreach (var wallet in wallets)
                {
                    Console.WriteLine($"{wallet.Address,-45} {wallet.Name,-25} {wallet.Algorithm,-12} {wallet.Status,-10}");
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
                ConsoleHelper.WriteError($"Failed to list wallets: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a wallet by address.
/// </summary>
public class WalletGetCommand : Command
{
    private readonly Option<string> _addressOption;

    public WalletGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a wallet by address")
    {
        _addressOption = new Option<string>("--address", "-a")
        {
            Description = "Wallet address",
            Required = true
        };

        Options.Add(_addressOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Call API
                var wallet = await client.GetWalletAsync(address, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess("Wallet details:");
                Console.WriteLine();
                Console.WriteLine($"  Address:     {wallet.Address}");
                Console.WriteLine($"  Name:        {wallet.Name}");
                Console.WriteLine($"  Algorithm:   {wallet.Algorithm}");
                Console.WriteLine($"  Public Key:  {wallet.PublicKey}");
                Console.WriteLine($"  Status:      {wallet.Status}");
                Console.WriteLine($"  Owner:       {wallet.Owner}");
                Console.WriteLine($"  Tenant:      {wallet.Tenant}");
                Console.WriteLine($"  Created:     {wallet.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Updated:     {wallet.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

                if (wallet.Metadata != null && wallet.Metadata.Count > 0)
                {
                    Console.WriteLine("  Metadata:");
                    foreach (var (key, value) in wallet.Metadata)
                    {
                        Console.WriteLine($"    {key}: {value}");
                    }
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Wallet '{address}' not found.");
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
                ConsoleHelper.WriteError($"Failed to get wallet: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Creates a new wallet.
/// </summary>
public class WalletCreateCommand : Command
{
    private readonly Option<string> _nameOption;
    private readonly Option<string> _algorithmOption;
    private readonly Option<int> _wordCountOption;
    private readonly Option<string?> _passphraseOption;

    public WalletCreateCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("create", "Create a new wallet")
    {
        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Wallet name",
            Required = true
        };

        _algorithmOption = new Option<string>("--algorithm", "-a")
        {
            Description = "Cryptographic algorithm (ED25519, NISTP256, RSA4096)",
            DefaultValueFactory = _ => "ED25519"
        };

        _wordCountOption = new Option<int>("--word-count", "-w")
        {
            Description = "Number of words in mnemonic (12, 15, 18, 21, or 24)",
            DefaultValueFactory = _ => 12
        };

        _passphraseOption = new Option<string?>("--passphrase", "-p")
        {
            Description = "Optional passphrase for additional security"
        };

        Options.Add(_nameOption);
        Options.Add(_algorithmOption);
        Options.Add(_wordCountOption);
        Options.Add(_passphraseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;
            var algorithm = parseResult.GetValue(_algorithmOption)!;
            var wordCount = parseResult.GetValue(_wordCountOption);
            var passphrase = parseResult.GetValue(_passphraseOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Build request
                var request = new CreateWalletRequest
                {
                    Name = name,
                    Algorithm = algorithm,
                    WordCount = wordCount,
                    Passphrase = passphrase
                };

                // Call API
                var response = await client.CreateWalletAsync(request, $"Bearer {token}");

                // Display results with security warning
                ConsoleHelper.WriteSuccess($"Wallet created successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Address:     {response.Wallet?.Address}");
                Console.WriteLine($"  Name:        {response.Wallet?.Name}");
                Console.WriteLine($"  Algorithm:   {response.Wallet?.Algorithm}");
                Console.WriteLine($"  Public Key:  {response.Wallet?.PublicKey}");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("CRITICAL: Save your mnemonic phrase securely!");
                Console.WriteLine($"  Mnemonic:    {string.Join(" ", response.MnemonicWords)}");
                Console.WriteLine();
                ConsoleHelper.WriteWarning("The mnemonic phrase will NEVER be displayed again.");
                ConsoleHelper.WriteWarning("Write it down on paper and store it in a secure location.");
                ConsoleHelper.WriteWarning("Anyone with this phrase can access your wallet!");

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
                ConsoleHelper.WriteError($"Failed to create wallet: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Recovers a wallet from mnemonic phrase.
/// </summary>
public class WalletRecoverCommand : Command
{
    private readonly Option<string> _nameOption;
    private readonly Option<string> _algorithmOption;
    private readonly Option<string> _mnemonicOption;
    private readonly Option<string?> _passphraseOption;

    public WalletRecoverCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("recover", "Recover a wallet from mnemonic phrase")
    {
        _nameOption = new Option<string>("--name", "-n")
        {
            Description = "Wallet name",
            Required = true
        };

        _algorithmOption = new Option<string>("--algorithm", "-a")
        {
            Description = "Cryptographic algorithm (ED25519, NISTP256, RSA4096)",
            DefaultValueFactory = _ => "ED25519"
        };

        _mnemonicOption = new Option<string>("--mnemonic", "-m")
        {
            Description = "Mnemonic phrase (space-separated words)",
            Required = true
        };

        _passphraseOption = new Option<string?>("--passphrase", "-p")
        {
            Description = "Optional passphrase if one was used during creation"
        };

        Options.Add(_nameOption);
        Options.Add(_algorithmOption);
        Options.Add(_mnemonicOption);
        Options.Add(_passphraseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var name = parseResult.GetValue(_nameOption)!;
            var algorithm = parseResult.GetValue(_algorithmOption)!;
            var mnemonic = parseResult.GetValue(_mnemonicOption)!;
            var passphrase = parseResult.GetValue(_passphraseOption);

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Build request
                var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var request = new RecoverWalletRequest
                {
                    MnemonicWords = words,
                    Name = name,
                    Algorithm = algorithm,
                    Passphrase = passphrase
                };

                // Call API
                var wallet = await client.RecoverWalletAsync(request, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Wallet recovered successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Address:     {wallet.Address}");
                Console.WriteLine($"  Name:        {wallet.Name}");
                Console.WriteLine($"  Algorithm:   {wallet.Algorithm}");
                Console.WriteLine($"  Public Key:  {wallet.PublicKey}");
                Console.WriteLine($"  Status:      {wallet.Status}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid mnemonic phrase or parameters: {ex.Content}");
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
                ConsoleHelper.WriteError($"Failed to recover wallet: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Deletes a wallet.
/// </summary>
public class WalletDeleteCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<bool> _confirmOption;

    public WalletDeleteCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("delete", "Delete a wallet")
    {
        _addressOption = new Option<string>("--address", "-a")
        {
            Description = "Wallet address",
            Required = true
        };

        _confirmOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt"
        };

        Options.Add(_addressOption);
        Options.Add(_confirmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var confirm = parseResult.GetValue(_confirmOption);

            try
            {
                // Confirm deletion
                if (!confirm)
                {
                    ConsoleHelper.WriteWarning("WARNING: This will soft-delete the wallet.");
                    if (!ConsoleHelper.Confirm($"Are you sure you want to delete wallet '{address}'?", defaultYes: false))
                    {
                        ConsoleHelper.WriteInfo("Deletion cancelled.");
                        return ExitCodes.Success;
                    }
                }

                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Call API
                await client.DeleteWalletAsync(address, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Wallet '{address}' deleted successfully.");
                ConsoleHelper.WriteInfo("Note: This is a soft delete. Contact support to recover if needed.");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Wallet '{address}' not found.");
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
                ConsoleHelper.WriteError($"Failed to delete wallet: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Signs data with a wallet's private key.
/// </summary>
public class WalletSignCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<string> _dataOption;

    public WalletSignCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("sign", "Sign data with a wallet's private key")
    {
        _addressOption = new Option<string>("--address", "-a")
        {
            Description = "Wallet address",
            Required = true
        };

        _dataOption = new Option<string>("--data", "-d")
        {
            Description = "Data to sign (base64 encoded)",
            Required = true
        };

        Options.Add(_addressOption);
        Options.Add(_dataOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var data = parseResult.GetValue(_dataOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Wallet Service client
                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // Build request
                var request = new SignTransactionRequest
                {
                    TransactionData = data
                };

                // Call API
                var response = await client.SignTransactionAsync(address, request, $"Bearer {token}");

                // Display results
                ConsoleHelper.WriteSuccess($"Data signed successfully!");
                Console.WriteLine();
                Console.WriteLine($"  Signature:   {response.Signature}");
                Console.WriteLine($"  Signed By:   {response.SignedBy}");
                Console.WriteLine($"  Signed At:   {response.SignedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Wallet '{address}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError($"Invalid data or parameters: {ex.Content}");
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
                ConsoleHelper.WriteError($"Failed to sign data: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Wallet access delegation commands (grant, list, revoke, check).
/// </summary>
public class WalletAccessCommand : Command
{
    public WalletAccessCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("access", "Manage wallet access delegation")
    {
        Subcommands.Add(new WalletAccessGrantCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletAccessListCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletAccessRevokeCommand(clientFactory, authService, configService));
        Subcommands.Add(new WalletAccessCheckCommand(clientFactory, authService, configService));
    }
}

public class WalletAccessGrantCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<string> _subjectOption;
    private readonly Option<string> _rightOption;
    private readonly Option<string?> _reasonOption;

    public WalletAccessGrantCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("grant", "Grant access to a wallet")
    {
        _addressOption = new Option<string>("--address", "-a") { Description = "Wallet address", Required = true };
        _subjectOption = new Option<string>("--subject", "-s") { Description = "Subject (user ID) to grant access to", Required = true };
        _rightOption = new Option<string>("--right", "-r") { Description = "Access right: Owner, ReadWrite, ReadOnly", Required = true };
        _reasonOption = new Option<string?>("--reason") { Description = "Reason for granting access" };

        Options.Add(_addressOption);
        Options.Add(_subjectOption);
        Options.Add(_rightOption);
        Options.Add(_reasonOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var subject = parseResult.GetValue(_subjectOption)!;
            var right = parseResult.GetValue(_rightOption)!;
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

                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);
                var request = new Models.GrantAccessRequest
                {
                    Subject = subject,
                    AccessRight = right,
                    Reason = reason
                };

                var grant = await client.GrantAccessAsync(address, request, $"Bearer {token}");
                ConsoleHelper.WriteSuccess($"Access granted: {grant.Subject} → {grant.AccessRight} on {address}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Permission denied: you are not the owner of this wallet.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to grant access: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

public class WalletAccessListCommand : Command
{
    private readonly Option<string> _addressOption;

    public WalletAccessListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List access grants for a wallet")
    {
        _addressOption = new Option<string>("--address", "-a") { Description = "Wallet address", Required = true };
        Options.Add(_addressOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;

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

                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);
                var grants = await client.ListAccessAsync(address, $"Bearer {token}");

                if (grants.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No access grants found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {grants.Count} access grant(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Subject",-25} {"Right",-12} {"Granted By",-20} {"Active",-8} {"Expires",-20}");
                Console.WriteLine(new string('-', 90));
                foreach (var g in grants)
                {
                    Console.WriteLine($"{g.Subject,-25} {g.AccessRight,-12} {g.GrantedBy,-20} {g.IsActive,-8} {g.ExpiresAt?.ToString("g") ?? "Never",-20}");
                }
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Permission denied: you are not the owner of this wallet.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list access grants: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

public class WalletAccessRevokeCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<string> _subjectOption;

    public WalletAccessRevokeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("revoke", "Revoke access from a wallet")
    {
        _addressOption = new Option<string>("--address", "-a") { Description = "Wallet address", Required = true };
        _subjectOption = new Option<string>("--subject", "-s") { Description = "Subject to revoke", Required = true };
        Options.Add(_addressOption);
        Options.Add(_subjectOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var subject = parseResult.GetValue(_subjectOption)!;

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

                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);
                await client.RevokeAccessAsync(address, subject, $"Bearer {token}");
                ConsoleHelper.WriteSuccess($"Access revoked for {subject} on {address}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Permission denied: you are not the owner of this wallet.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to revoke access: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

public class WalletAccessCheckCommand : Command
{
    private readonly Option<string> _addressOption;
    private readonly Option<string> _subjectOption;
    private readonly Option<string> _rightOption;

    public WalletAccessCheckCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("check", "Check if a subject has access to a wallet")
    {
        _addressOption = new Option<string>("--address", "-a") { Description = "Wallet address", Required = true };
        _subjectOption = new Option<string>("--subject", "-s") { Description = "Subject to check", Required = true };
        _rightOption = new Option<string>("--right", "-r") { Description = "Required access right", Required = true };
        Options.Add(_addressOption);
        Options.Add(_subjectOption);
        Options.Add(_rightOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var address = parseResult.GetValue(_addressOption)!;
            var subject = parseResult.GetValue(_subjectOption)!;
            var right = parseResult.GetValue(_rightOption)!;

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

                var client = await clientFactory.CreateWalletServiceClientAsync(profileName);
                var result = await client.CheckAccessAsync(address, subject, right, $"Bearer {token}");

                if (result.HasAccess)
                    ConsoleHelper.WriteSuccess($"{subject} HAS {right} access to {address}");
                else
                    ConsoleHelper.WriteWarning($"{subject} does NOT have {right} access to {address}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("Permission denied: you are not the owner of this wallet.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to check access: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
