// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.Register.Models;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Transaction management commands.
/// </summary>
public class TransactionCommand : Command
{
    public TransactionCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        // The submit example must name the options `submit` actually declares. It previously
        // advertised `--data '{"key":"value"}'`, an option this command has never had, so an
        // operator's first attempt failed with "Unrecognized command or argument '--data'".
        // Submitting a transaction needs the signed envelope — type, payload AND signature —
        // because the CLI does not sign for you.
        : base("tx", "Manage transactions in registers\n\nExamples:\n  sorcha tx list --register-id <id>\n  sorcha tx get --register-id <id> --tx-id <tx-id>\n  sorcha tx submit --register-id <id> --wallet <addr> --type <type> --payload '{\"key\":\"value\"}' --signature <sig>\n  sorcha tx proof --register-id <id> --tx-id <tx-id> --out proof.json\n  sorcha tx revoke --register-id <id> --tx-id <tx-id> --reason Erroneous")

    {
        Subcommands.Add(new TxListCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxSubmitCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxStatusCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxProofCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxVerifyProofCommand(clientFactory, authService, configService));
        Subcommands.Add(new TxRevokeCommand(clientFactory, authService, configService));
    }

    /// <summary>
    /// JSON options for reading/writing Merkle proof artifacts so they round-trip with
    /// the platform's camelCase + string-enum wire format.
    /// </summary>
    internal static readonly JsonSerializerOptions ProofJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Lists transactions in a register.
/// </summary>
public class TxListCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<int?> _pageOption;
    private readonly Option<int?> _pageSizeOption;

    public TxListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List transactions in a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _pageOption = new Option<int?>("--page", "-p")
        {
            Description = "Page number (default: 1)"
        };

        _pageSizeOption = new Option<int?>("--page-size", "-s")
        {
            Description = "Number of transactions per page (default: 50)"
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
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to list transactions.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var transactions = await client.ListTransactionsAsync(registerId, page, pageSize, $"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteCollection(parseResult, transactions);
                    return ExitCodes.Success;
                }

                // Display results
                if (transactions == null || transactions.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No transactions found.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {transactions.Count} transaction(s) in register '{registerId}':");
                Console.WriteLine();

                // Display as table with TransactionModel fields
                Console.WriteLine($"{"TX ID",-66} {"Sender",-40} {"Block",8} {"Payloads",8} {"Timestamp"}");
                Console.WriteLine(new string('-', 145));

                foreach (var tx in transactions)
                {
                    var timestamp = tx.TimeStamp.ToString("yyyy-MM-dd HH:mm:ss");
                    var block = tx.DocketNumber?.ToString() ?? "-";
                    Console.WriteLine($"{tx.TxId,-66} {tx.SenderWallet,-40} {block,8} {tx.PayloadCount,8} {timestamp}");
                }

                // Show pagination info
                if (page.HasValue || pageSize.HasValue)
                {
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo($"Showing page {page ?? 1}, {pageSize ?? 50} per page");
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
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to view transactions in this register.");
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
                ConsoleHelper.WriteError($"Failed to list transactions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets a transaction by ID.
/// </summary>
public class TxGetCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _txIdOption;

    public TxGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get a transaction by ID")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _txIdOption = new Option<string>("--tx-id", "-t")
        {
            Description = "Transaction ID",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_txIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var txId = parseResult.GetValue(_txIdOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to get a transaction.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var tx = await client.GetTransactionAsync(registerId, txId, $"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, tx);
                    return ExitCodes.Success;
                }

                // Display results with TransactionModel fields
                ConsoleHelper.WriteSuccess("Transaction details:");
                Console.WriteLine();
                Console.WriteLine($"  Transaction ID:  {tx.TxId}");
                Console.WriteLine($"  Register ID:     {tx.RegisterId}");
                Console.WriteLine($"  Docket Number:   {tx.DocketNumber?.ToString() ?? "Pending"}");
                Console.WriteLine($"  Version:         {tx.Version}");
                Console.WriteLine($"  Sender Wallet:   {tx.SenderWallet}");
                Console.WriteLine($"  Timestamp:       {tx.TimeStamp:yyyy-MM-dd HH:mm:ss}");

                if (!string.IsNullOrEmpty(tx.PrevTxId) && tx.PrevTxId != new string('0', 64))
                {
                    Console.WriteLine($"  Previous TX:     {tx.PrevTxId}");
                }

                if (tx.RecipientsWallets.Any())
                {
                    Console.WriteLine($"  Recipients:      {string.Join(", ", tx.RecipientsWallets)}");
                }

                if (tx.MetaData != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Metadata:");
                    if (!string.IsNullOrEmpty(tx.MetaData.BlueprintId))
                        Console.WriteLine($"    Blueprint ID:    {tx.MetaData.BlueprintId}");
                    if (tx.MetaData.ActionId.HasValue)
                        Console.WriteLine($"    Action ID:       {tx.MetaData.ActionId}");
                    if (!string.IsNullOrEmpty(tx.MetaData.InstanceId))
                        Console.WriteLine($"    Instance ID:     {tx.MetaData.InstanceId}");
                }

                Console.WriteLine();
                Console.WriteLine($"  Payload Count:   {tx.PayloadCount}");
                if (tx.Payloads.Length > 0)
                {
                    Console.WriteLine("  Payloads:");
                    for (int i = 0; i < tx.Payloads.Length; i++)
                    {
                        var payload = tx.Payloads[i];
                        Console.WriteLine($"    [{i}] Hash: {payload.Hash[..16]}..., Size: {payload.PayloadSize} bytes");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("  Signature:");
                Console.WriteLine($"  {tx.Signature}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Transaction '{txId}' not found in register '{registerId}'.");
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
                ConsoleHelper.WriteError("You do not have permission to view this transaction.");
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
                ConsoleHelper.WriteError($"Failed to get transaction: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Submits a new transaction.
/// </summary>
public class TxSubmitCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _txTypeOption;
    private readonly Option<string> _walletOption;
    private readonly Option<string> _payloadOption;
    private readonly Option<string> _signatureOption;
    private readonly Option<string?> _previousTxOption;

    public TxSubmitCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("submit", "Submit a new transaction to a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _txTypeOption = new Option<string>("--type", "-t")
        {
            Description = "Transaction type",
            Required = true
        };

        _walletOption = new Option<string>("--wallet", "-w")
        {
            Description = "Sender wallet address",
            Required = true
        };

        _payloadOption = new Option<string>("--payload", "-p")
        {
            Description = "Transaction payload (JSON)",
            Required = true
        };

        _signatureOption = new Option<string>("--signature", "-s")
        {
            Description = "Transaction signature",
            Required = true
        };

        _previousTxOption = new Option<string?>("--previous-tx", "-x")
        {
            Description = "Previous transaction ID (for chaining)"
        };

        Options.Add(_registerIdOption);
        Options.Add(_txTypeOption);
        Options.Add(_walletOption);
        Options.Add(_payloadOption);
        Options.Add(_signatureOption);
        Options.Add(_previousTxOption);

        this.SetAction((ParseResult parseResult, CancellationToken ct) =>
        {
            // Raw transaction submission is intentionally NOT supported from the CLI.
            //
            // The register's POST /transactions endpoint consumes a complete, signed
            // TransactionModel — Payloads[], MetaData, PayloadCount, chained previousTxId, and a
            // signature computed over the canonical transaction bytes. That object is produced by
            // executing a blueprint action (which builds and signs it correctly), not by pasting a
            // payload string and a signature on a command line. The flags this command used to take
            // (--payload / --signature) could never assemble a valid transaction, so the previous
            // implementation reported success while the server rejected — or silently mis-stored —
            // whatever it was sent.
            //
            // Removing the fake path rather than leaving it to mislead. The read side of the ledger
            // (list / get / status / query) remains fully supported.
            ConsoleHelper.WriteError("Raw transaction submission is not supported from the CLI.");
            Console.WriteLine();
            ConsoleHelper.WriteInfo(
                "A register transaction is a complete signed object (payloads, metadata, chain "
                + "linkage, signature) built by executing a blueprint action — not something that "
                + "can be assembled from a payload string on the command line.");
            Console.WriteLine();
            ConsoleHelper.WriteInfo(
                "To create transactions, execute the relevant blueprint action against a workflow "
                + "instance. To inspect existing transactions, use 'sorcha tx list', 'sorcha tx get', "
                + "'sorcha tx status', or 'sorcha query'.");

            return Task.FromResult(ExitCodes.GeneralError);
        });
    }
}

/// <summary>
/// Gets the status of a transaction.
/// </summary>
public class TxStatusCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _txIdOption;

    public TxStatusCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("status", "Get the status of a transaction")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _txIdOption = new Option<string>("--tx-id", "-t")
        {
            Description = "Transaction ID",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_txIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var txId = parseResult.GetValue(_txIdOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to check transaction status.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                // Create Register Service client
                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);

                // Call API
                var response = await client.GetTransactionStatusAsync(registerId, txId, $"Bearer {token}");

                // Check output format
                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, response);
                    return ExitCodes.Success;
                }

                // Display results
                ConsoleHelper.WriteSuccess("Transaction lifecycle status:");
                Console.WriteLine();
                Console.WriteLine($"  Transaction ID:  {response.TransactionId}");
                Console.WriteLine($"  Status:          {response.Status}");

                if (!string.IsNullOrEmpty(response.RevocationTxId))
                {
                    Console.WriteLine($"  Revocation TX:   {response.RevocationTxId}");
                }
                if (!string.IsNullOrEmpty(response.SupersededByTxId))
                {
                    Console.WriteLine($"  Superseded by:   {response.SupersededByTxId}");
                }
                if (response.RevokedAt.HasValue)
                {
                    Console.WriteLine($"  Revoked at:      {response.RevokedAt.Value:yyyy-MM-dd HH:mm:ss} UTC");
                }
                if (response.Reason.HasValue)
                {
                    Console.WriteLine($"  Reason:          {response.Reason.Value}");
                }

                // Provide status explanation
                Console.WriteLine();
                switch (response.Status)
                {
                    case TransactionLifecycleStatus.Active:
                        ConsoleHelper.WriteSuccess("The transaction is valid and current.");
                        break;
                    case TransactionLifecycleStatus.Revoked:
                        ConsoleHelper.WriteWarning("The transaction has been explicitly revoked.");
                        break;
                    case TransactionLifecycleStatus.Superseded:
                        ConsoleHelper.WriteWarning("The transaction has been replaced by a newer transaction.");
                        break;
                    default:
                        ConsoleHelper.WriteWarning($"Unknown status: {response.Status}");
                        break;
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Transaction '{txId}' not found in register '{registerId}'.");
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
                ConsoleHelper.WriteError("You do not have permission to view transaction status.");
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
                ConsoleHelper.WriteError($"Failed to get transaction status: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Generates a Merkle inclusion proof for a sealed transaction.
/// </summary>
public class TxProofCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _txIdOption;
    private readonly Option<string?> _outOption;

    public TxProofCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("proof", "Generate a Merkle inclusion proof for a sealed transaction")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _txIdOption = new Option<string>("--tx-id", "-t")
        {
            Description = "Transaction ID",
            Required = true
        };

        _outOption = new Option<string?>("--out", "-o")
        {
            Description = "Write the proof JSON to this file (for offline verification)"
        };

        Options.Add(_registerIdOption);
        Options.Add(_txIdOption);
        Options.Add(_outOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var txId = parseResult.GetValue(_txIdOption)!;
            var outFile = parseResult.GetValue(_outOption);

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to generate an inclusion proof.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var proof = await client.GetInclusionProofAsync(registerId, txId, $"Bearer {token}");

                if (!string.IsNullOrEmpty(outFile))
                {
                    var json = JsonSerializer.Serialize(proof, TransactionCommand.ProofJsonOptions);
                    await File.WriteAllTextAsync(outFile, json, ct);
                    ConsoleHelper.WriteSuccess($"Inclusion proof written to '{outFile}'.");
                    ConsoleHelper.WriteInfo($"Verify it with: sorcha tx verify-proof --register-id {registerId} --file {outFile}");
                    return ExitCodes.Success;
                }

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, proof);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Merkle inclusion proof:");
                Console.WriteLine();
                Console.WriteLine($"  Transaction Hash: {proof.TransactionHash}");
                Console.WriteLine($"  Docket Number:    {proof.DocketNumber}");
                Console.WriteLine($"  Merkle Root:      {proof.MerkleRoot}");
                Console.WriteLine($"  Leaf Index:       {proof.LeafIndex} of {proof.TreeSize}");
                Console.WriteLine($"  Proof Path:       {proof.ProofPath.Count} step(s)");
                foreach (var step in proof.ProofPath)
                {
                    Console.WriteLine($"    [{step.Position}] {step.Hash}");
                }
                Console.WriteLine();
                ConsoleHelper.WriteInfo("Use --out <file> to save the proof for offline verification.");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Transaction '{txId}' not found in register '{registerId}', or it is not yet sealed.");
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
                ConsoleHelper.WriteError("You do not have permission to read this transaction.");
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
                ConsoleHelper.WriteError($"Failed to generate inclusion proof: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Verifies a Merkle inclusion proof (offline-capable).
/// </summary>
public class TxVerifyProofCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _fileOption;

    public TxVerifyProofCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("verify-proof", "Verify a previously generated Merkle inclusion proof")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _fileOption = new Option<string>("--file", "-f")
        {
            Description = "Path to the proof JSON produced by 'sorcha tx proof --out'",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_fileOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var file = parseResult.GetValue(_fileOption)!;

            MerkleInclusionProof? proof;
            try
            {
                if (!File.Exists(file))
                {
                    ConsoleHelper.WriteError($"Proof file '{file}' not found.");
                    return ExitCodes.ValidationError;
                }

                var json = await File.ReadAllTextAsync(file, ct);
                proof = JsonSerializer.Deserialize<MerkleInclusionProof>(json, TransactionCommand.ProofJsonOptions);
                if (proof == null)
                {
                    ConsoleHelper.WriteError($"Proof file '{file}' did not contain a valid inclusion proof.");
                    return ExitCodes.ValidationError;
                }
            }
            catch (JsonException ex)
            {
                ConsoleHelper.WriteError($"Proof file '{file}' is not valid JSON: {ex.Message}");
                return ExitCodes.ValidationError;
            }

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Verification is an anonymous endpoint, but send a token if we have one.
                var token = await authService.GetAccessTokenAsync(profileName);

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var request = new VerifyMerkleInclusionProofRequest
                {
                    TransactionHash = proof.TransactionHash,
                    MerkleRoot = proof.MerkleRoot,
                    ProofPath = proof.ProofPath,
                    DocketNumber = proof.DocketNumber
                };

                var result = await client.VerifyInclusionProofAsync(
                    registerId, request, token is null ? string.Empty : $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return result.IsValid ? ExitCodes.Success : ExitCodes.GeneralError;
                }

                Console.WriteLine();
                if (result.IsValid)
                {
                    // #1372 — "included in the docket" is a claim about the LEDGER, and folding a
                    // proof path does not establish it: a root recomputed over altered data is
                    // internally self-consistent, so the proof verifies against it perfectly. Only
                    // say it when the register confirms the root its validator sealed.
                    if (string.Equals(result.LedgerAnchored, "verified", StringComparison.OrdinalIgnoreCase))
                    {
                        ConsoleHelper.WriteSuccess("Proof is VALID — the transaction is included in the docket, "
                            + "and the docket matches the root its validator sealed.");
                    }
                    else
                    {
                        ConsoleHelper.WriteWarning("Proof is well-formed — the proof path folds to the stated root.");
                        Console.WriteLine("  The register did NOT confirm that root against the one its validator sealed"
                            + (string.IsNullOrWhiteSpace(result.LedgerAnchorReason)
                                ? "."
                                : $": {result.LedgerAnchorReason}"));
                    }
                    Console.WriteLine($"  Computed root: {result.ComputedRoot}");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteError("Proof is INVALID — the computed root does not match.");
                Console.WriteLine($"  Expected root: {proof.MerkleRoot}");
                Console.WriteLine($"  Computed root: {result.ComputedRoot}");
                return ExitCodes.GeneralError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError("The proof was rejected as malformed.");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
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
                ConsoleHelper.WriteError($"Failed to verify proof: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Revokes a transaction with a recorded reason.
/// </summary>
public class TxRevokeCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _txIdOption;
    private readonly Option<string> _reasonOption;
    private readonly Option<string?> _supersededByOption;
    private readonly Option<string?> _signerOption;

    public TxRevokeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("revoke", "Revoke a transaction with a recorded reason")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID",
            Required = true
        };

        _txIdOption = new Option<string>("--tx-id", "-t")
        {
            Description = "ID of the transaction to revoke",
            Required = true
        };

        _reasonOption = new Option<string>("--reason")
        {
            Description = "Revocation reason: Superseded, Erroneous, Compromised, Expired, Withdrawn, Regulatory",
            Required = true
        };

        _supersededByOption = new Option<string?>("--superseded-by")
        {
            Description = "ID of the replacement transaction (required when --reason is Superseded)"
        };

        _signerOption = new Option<string?>("--signer")
        {
            Description = "Signer wallet address for authority traceability"
        };

        Options.Add(_registerIdOption);
        Options.Add(_txIdOption);
        Options.Add(_reasonOption);
        Options.Add(_supersededByOption);
        Options.Add(_signerOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var txId = parseResult.GetValue(_txIdOption)!;
            var reason = parseResult.GetValue(_reasonOption)!;
            var supersededBy = parseResult.GetValue(_supersededByOption);
            var signer = parseResult.GetValue(_signerOption);

            // Validate the reason against the known revocation reasons for a friendly error.
            if (!Enum.TryParse<RevocationReason>(reason, ignoreCase: true, out var parsedReason))
            {
                ConsoleHelper.WriteError($"Invalid revocation reason '{reason}'.");
                ConsoleHelper.WriteInfo($"Valid reasons: {string.Join(", ", Enum.GetNames<RevocationReason>())}");
                return ExitCodes.ValidationError;
            }

            if (parsedReason == RevocationReason.Superseded && string.IsNullOrWhiteSpace(supersededBy))
            {
                ConsoleHelper.WriteError("--superseded-by is required when --reason is 'Superseded'.");
                return ExitCodes.ValidationError;
            }

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                var token = await authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("You must be authenticated to revoke a transaction.");
                    ConsoleHelper.WriteInfo("Run 'sorcha auth login' to authenticate.");
                    return ExitCodes.AuthenticationError;
                }

                var client = await clientFactory.CreateRegisterServiceClientAsync(profileName);
                var request = new RevokeTransactionRequest
                {
                    OriginalTxId = txId,
                    Reason = parsedReason.ToString(),
                    SupersededByTxId = supersededBy,
                    SignerWalletAddress = signer
                };

                var result = await client.RevokeTransactionAsync(registerId, request, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess("Revocation submitted.");
                Console.WriteLine();
                Console.WriteLine($"  Revocation TX:   {result.RevocationTxId}");
                Console.WriteLine($"  Original TX:     {result.OriginalTxId}");
                Console.WriteLine($"  Status:          {result.Status}");
                Console.WriteLine();
                ConsoleHelper.WriteInfo($"Confirm with: sorcha tx status --register-id {registerId} --tx-id {txId}");
                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                ConsoleHelper.WriteError("The revocation was rejected as invalid.");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Transaction '{txId}' not found in register '{registerId}'.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError("The transaction is already revoked.");
                if (ex.Content != null)
                {
                    ConsoleHelper.WriteError($"Details: {ex.Content}");
                }
                return ExitCodes.GeneralError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Your access token may have expired.");
                ConsoleHelper.WriteInfo("Run 'sorcha auth login' to re-authenticate.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                ConsoleHelper.WriteError("You do not have permission to revoke transactions in this register.");
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
                ConsoleHelper.WriteError($"Failed to revoke transaction: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
