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
/// Verification commands for transaction receipts and bundles.
/// </summary>
public class VerifyCommand : Command
{
    public VerifyCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("verify", "Verify transaction receipts and bundles\n\nExamples:\n  sorcha verify receipt --register-id <id> --tx-id <id>\n  sorcha verify bundle --register-id <id> --tx-id <id>")
    {
        Subcommands.Add(new VerifyReceiptCommand(clientFactory, authService, configService));
        Subcommands.Add(new VerifyBundleCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Verifies a transaction receipt.
/// </summary>
public class VerifyReceiptCommand : Command
{
    public VerifyReceiptCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("receipt", "Get and verify a transaction receipt")
    {
        var registerIdOption = new Option<string>("--register-id") { Description = "Register ID", Required = true };
        var txIdOption = new Option<string>("--tx-id") { Description = "Transaction ID", Required = true };

        Options.Add(registerIdOption);
        Options.Add(txIdOption);

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

                var registerId = parseResult.GetValue(registerIdOption)!;
                var txId = parseResult.GetValue(txIdOption)!;

                var client = await clientFactory.CreateVerificationServiceClientAsync(profileName);

                // Get the receipt
                var receipt = await client.GetReceiptAsync(registerId, txId, $"Bearer {token}");

                // Verify the receipt
                var verification = await client.VerifyReceiptAsync(registerId, receipt, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    var result = new { Receipt = receipt, Verification = verification };
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                // Display receipt details
                ConsoleHelper.WriteInfo("Transaction Receipt:");
                Console.WriteLine($"  Transaction ID:      {receipt.TransactionId}");
                Console.WriteLine($"  Register ID:         {receipt.RegisterId}");
                Console.WriteLine($"  Docket Number:       {receipt.DocketNumber}");
                Console.WriteLine($"  Merkle Root:         {receipt.MerkleRoot}");
                Console.WriteLine($"  Validator Address:   {receipt.ValidatorAddress}");
                Console.WriteLine($"  Issued At:           {receipt.IssuedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Proof Steps:         {receipt.MerkleProof.Count}");
                Console.WriteLine();

                // Display verification result
                if (verification.IsValid)
                {
                    ConsoleHelper.WriteSuccess("Verification: VALID");
                }
                else
                {
                    ConsoleHelper.WriteError("Verification: INVALID");
                    foreach (var error in verification.Errors)
                    {
                        ConsoleHelper.WriteError($"  - {error}");
                    }
                }

                if (verification.Checks is { } checks)
                {
                    Console.WriteLine($"  Signature valid:        {checks.SignatureValid}");
                    Console.WriteLine($"  Inclusion proof valid:  {checks.InclusionProofValid}");
                    Console.WriteLine($"  Merkle root consistent: {checks.MerkleRootConsistent}");
                }

                return verification.IsValid ? ExitCodes.Success : ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError("Transaction receipt not found. The transaction may not have been sealed in a docket yet.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Register Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Receipt verification failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets and verifies a verification bundle.
/// </summary>
public class VerifyBundleCommand : Command
{
    public VerifyBundleCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("bundle", "Get and verify a transaction verification bundle")
    {
        var registerIdOption = new Option<string>("--register-id") { Description = "Register ID", Required = true };
        var txIdOption = new Option<string>("--tx-id") { Description = "Transaction ID", Required = true };

        Options.Add(registerIdOption);
        Options.Add(txIdOption);

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

                var registerId = parseResult.GetValue(registerIdOption)!;
                var txId = parseResult.GetValue(txIdOption)!;

                var client = await clientFactory.CreateVerificationServiceClientAsync(profileName);

                // Get the bundle
                var bundle = await client.GetVerificationBundleAsync(registerId, txId, $"Bearer {token}");

                // Verify the bundle
                var verification = await client.VerifyBundleAsync(registerId, bundle, $"Bearer {token}");

                var outputFormat = OutputHelper.GetOutputFormat(parseResult);
                if (OutputHelper.IsStructuredFormat(outputFormat))
                {
                    var result = new { Bundle = bundle, Verification = verification };
                    OutputHelper.WriteSingle(parseResult, result);
                    return ExitCodes.Success;
                }

                // Display bundle details
                ConsoleHelper.WriteInfo("Verification Bundle:");
                Console.WriteLine($"  Transaction ID:   {bundle.TransactionId}");
                Console.WriteLine($"  Register ID:      {bundle.RegisterId}");
                Console.WriteLine($"  Status:           {bundle.TransactionStatus}");
                Console.WriteLine($"  Has Receipt:      {(bundle.Receipt != null ? "Yes" : "No")}");
                Console.WriteLine($"  Has VC:           {(!string.IsNullOrEmpty(bundle.VerifiableCredential) ? "Yes" : "No")}");
                Console.WriteLine($"  Generated At:     {bundle.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

                if (bundle.Receipt != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Receipt Details:");
                    Console.WriteLine($"    Docket Number:    {bundle.Receipt.DocketNumber}");
                    Console.WriteLine($"    Merkle Root:      {bundle.Receipt.MerkleRoot}");
                    Console.WriteLine($"    Validator:        {bundle.Receipt.ValidatorAddress}");
                }

                Console.WriteLine();

                // Display verification result
                if (verification.IsValid)
                {
                    ConsoleHelper.WriteSuccess("Verification: VALID");
                }
                else
                {
                    ConsoleHelper.WriteError("Verification: INVALID");
                    foreach (var error in verification.Errors)
                    {
                        ConsoleHelper.WriteError($"  - {error}");
                    }
                }

                if (verification.Checks is { } checks)
                {
                    Console.WriteLine($"  Signature valid:        {checks.SignatureValid}");
                    Console.WriteLine($"  Inclusion proof valid:  {checks.InclusionProofValid}");
                    Console.WriteLine($"  Merkle root consistent: {checks.MerkleRootConsistent}");
                }

                return verification.IsValid ? ExitCodes.Success : ExitCodes.ValidationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login'.");
                return ExitCodes.AuthenticationError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError("Verification bundle not found. The transaction may not have been sealed yet.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex)
            {
                ConsoleHelper.WriteError($"API error ({ex.StatusCode}): {ex.Content}");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Cannot reach Register Service: {ex.Message}");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Bundle verification failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
