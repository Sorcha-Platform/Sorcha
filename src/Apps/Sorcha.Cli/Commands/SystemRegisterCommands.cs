// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Parent command group for system register genesis operations.
/// </summary>
public class SystemRegisterGenesisCommand : Command
{
    public SystemRegisterGenesisCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("system-register", "System register genesis ceremony and verification")
    {
        Subcommands.Add(new SystemRegisterCreateCommand());
        Subcommands.Add(new SystemRegisterVerifyCommand());
        Subcommands.Add(new SystemRegisterImportValidatorKeyCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Generates a pre-signed system register genesis block offline.
/// No running services required.
/// </summary>
public class SystemRegisterCreateCommand : Command
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public SystemRegisterCreateCommand()
        : base("create", "Generate a pre-signed system register genesis block")
    {
        var networkIdOption = new Option<string>("--network-id", "-n")
        {
            Description = "Human-readable network identifier (e.g., sorcha-prod, sorcha-dev)",
            DefaultValueFactory = _ => "sorcha-local"
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = "Genesis file output path (default: embedded resource location)"
        };

        var algorithmOption = new Option<string>("--algorithm", "-a")
        {
            Description = "Signing algorithm (default: ED25519)",
            DefaultValueFactory = _ => "ED25519"
        };

        Options.Add(networkIdOption);
        Options.Add(outputOption);
        Options.Add(algorithmOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var networkId = parseResult.GetValue(networkIdOption) ?? "sorcha-local";
            var outputPath = parseResult.GetValue(outputOption);
            var algorithm = parseResult.GetValue(algorithmOption) ?? "ED25519";

            try
            {
                return await ExecuteCreateAsync(networkId, outputPath, algorithm, ct);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Genesis ceremony failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    internal static async Task<int> ExecuteCreateAsync(
        string networkId, string? outputPath, string algorithm, CancellationToken ct)
    {
        // Validate algorithm
        if (!Enum.TryParse<WalletNetworks>(algorithm, ignoreCase: true, out var network))
        {
            ConsoleHelper.WriteError($"Unsupported algorithm: {algorithm}. Supported: ED25519, NISTP256, RSA4096");
            return ExitCodes.ValidationError;
        }

        var crypto = new CryptoModule();

        // 1. Generate genesis keypair
        var keyResult = await crypto.GenerateKeySetAsync(network, cancellationToken: ct);
        if (!keyResult.IsSuccess)
        {
            ConsoleHelper.WriteError($"Key generation failed: {keyResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }

        var keySet = keyResult.Value;
        var publicKeyBytes = keySet.PublicKey.Key!;
        var privateKeyBytes = keySet.PrivateKey.Key!;

        // 2. Build the control record
        var controlRecord = BuildControlRecord(publicKeyBytes, algorithm);
        var controlRecordJson = JsonSerializer.Serialize(controlRecord, CanonicalJsonOptions);
        var controlRecordBytes = Encoding.UTF8.GetBytes(controlRecordJson);

        // 3. Compute payload hash
        var payloadHash = Convert.ToHexString(SHA256.HashData(controlRecordBytes)).ToLowerInvariant();
        var payloadBase64 = Convert.ToBase64String(controlRecordBytes);

        // 4. Compute deterministic TxId
        var txId = GenesisSignatureVerifier.ComputeGenesisTxId();

        // 5. Sign: SHA256(UTF8("{TxId}:{PayloadHash}"))
        var dataToSign = $"{txId}:{payloadHash}";
        var signedDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(dataToSign));

        var signResult = await crypto.SignAsync(signedDataHash, (byte)network, privateKeyBytes, ct);
        if (!signResult.IsSuccess || signResult.Value is null)
        {
            ConsoleHelper.WriteError($"Signing failed: {signResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }

        var signatureBytes = signResult.Value;
        var now = DateTimeOffset.UtcNow;
        var fingerprint = GenesisFileLoader.ComputeFingerprint(publicKeyBytes);

        // 6. Derive wallet address from public key
        var walletAddress = DeriveWalletAddress(publicKeyBytes, network);

        // 7. Build genesis file
        var genesis = new SystemRegisterGenesis
        {
            Version = SystemRegisterGenesis.CurrentVersion,
            NetworkId = networkId,
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = txId,
                Payload = payloadBase64,
                PayloadHash = payloadHash,
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKeyBytes),
                    SignatureValue = Convert.ToBase64String(signatureBytes),
                    Algorithm = algorithm,
                    SignedAt = now
                }
            },
            ValidatorRoster = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = walletAddress,
                        PublicKey = Convert.ToBase64String(publicKeyBytes),
                        Algorithm = ParseAlgorithm(algorithm),
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = now
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = fingerprint
        };

        // 8. Build validator key file
        var validatorKeyFile = new GenesisValidatorKeyFile
        {
            Version = 1,
            NetworkId = networkId,
            WalletAddress = walletAddress,
            PrivateKey = Convert.ToBase64String(privateKeyBytes),
            PublicKey = Convert.ToBase64String(publicKeyBytes),
            Algorithm = algorithm,
            CreatedAt = now,
            Fingerprint = fingerprint
        };

        // 9. Write output files
        var genesisPath = outputPath ?? FindDefaultGenesisPath();
        var genesisJson = JsonSerializer.Serialize(genesis, PrettyJsonOptions);

        var genesisDir = Path.GetDirectoryName(genesisPath);
        if (!string.IsNullOrEmpty(genesisDir))
            Directory.CreateDirectory(genesisDir);

        await File.WriteAllTextAsync(genesisPath, genesisJson, ct);

        var keyFileDir = Path.GetDirectoryName(genesisPath) ?? Directory.GetCurrentDirectory();
        var keyFilePath = Path.Combine(keyFileDir, "genesis-validator-key.json");
        var keyFileJson = JsonSerializer.Serialize(validatorKeyFile, PrettyJsonOptions);
        await File.WriteAllTextAsync(keyFilePath, keyFileJson, ct);

        // 10. Zeroize key material in memory
        keySet.Zeroize();

        // 11. Output
        Console.WriteLine();
        ConsoleHelper.WriteSuccess("Genesis ceremony completed.");
        Console.WriteLine();
        Console.WriteLine($"  Network ID:     {networkId}");
        Console.WriteLine($"  Register ID:    {SystemRegisterConstants.SystemRegisterId}");
        Console.WriteLine($"  Algorithm:      {algorithm}");
        Console.WriteLine($"  Fingerprint:    {fingerprint}");
        Console.WriteLine($"  Genesis File:   {genesisPath}");
        Console.WriteLine($"  Validator Key:  {keyFilePath}");
        Console.WriteLine();
        ConsoleHelper.WriteWarning("Store genesis-validator-key.json securely or destroy it after");
        ConsoleHelper.WriteWarning("importing into the first validator. It is not needed for normal operation.");

        return ExitCodes.Success;
    }

    private static RegisterControlRecord BuildControlRecord(byte[] publicKey, string algorithm)
    {
        var now = DateTimeOffset.UtcNow;
        return new RegisterControlRecord
        {
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Description = "Sorcha platform system register — root of trust for blueprints and governance.",
            CreatedAt = now,
            Attestations =
            [
                new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = $"did:sorcha:genesis:{GenesisFileLoader.ComputeFingerprint(publicKey)}",
                    PublicKey = Convert.ToBase64String(publicKey),
                    Signature = "", // Self-referential — the transaction signature covers the whole record
                    Algorithm = ParseAlgorithm(algorithm),
                    GrantedAt = now
                }
            ],
            Validators = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = DeriveWalletAddress(publicKey, Enum.Parse<WalletNetworks>(algorithm, ignoreCase: true)),
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = ParseAlgorithm(algorithm),
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = now
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            CryptoPolicy = CryptoPolicy.CreateDefault(),
            RegisterPolicy = RegisterPolicy.CreateDefault()
        };
    }

    private static SignatureAlgorithm ParseAlgorithm(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "ED25519" => SignatureAlgorithm.ED25519,
            "NISTP256" => SignatureAlgorithm.NISTP256,
            "RSA4096" => SignatureAlgorithm.RSA4096,
            "ML_DSA_65" or "ML-DSA-65" => SignatureAlgorithm.ML_DSA_65,
            "SLH_DSA_128S" or "SLH-DSA-128S" => SignatureAlgorithm.SLH_DSA_128s,
            _ => SignatureAlgorithm.ED25519
        };

    internal static string DeriveWalletAddress(byte[] publicKey, WalletNetworks network)
    {
        // Simple address derivation: network prefix + truncated hash
        var hash = SHA256.HashData(publicKey);
        var prefix = network switch
        {
            WalletNetworks.ED25519 => "s1",
            WalletNetworks.NISTP256 => "s2",
            WalletNetworks.RSA4096 => "s3",
            _ => "s0"
        };
        return $"{prefix}{Convert.ToHexString(hash)[..40].ToLowerInvariant()}";
    }

    private static string FindDefaultGenesisPath()
    {
        // Walk up from CWD looking for the src directory
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "src", "Common", "Sorcha.Register.Models", "Resources", "system-register-genesis.json");
            if (Directory.Exists(Path.Combine(dir, "src", "Common", "Sorcha.Register.Models")))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Fallback to current directory
        return Path.Combine(Directory.GetCurrentDirectory(), "system-register-genesis.json");
    }
}

/// <summary>
/// Verifies a genesis file's signatures and displays its contents.
/// </summary>
public class SystemRegisterVerifyCommand : Command
{
    public SystemRegisterVerifyCommand()
        : base("verify", "Verify a genesis file's signatures and display contents")
    {
        var fileArgument = new Argument<string>("genesis-file")
        {
            Description = "Path to the genesis JSON file to verify"
        };

        Arguments.Add(fileArgument);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var filePath = parseResult.GetValue(fileArgument)!;

            try
            {
                return await ExecuteVerifyAsync(filePath, ct);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Verification failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }

    internal static async Task<int> ExecuteVerifyAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            ConsoleHelper.WriteError($"Genesis file not found: {filePath}");
            return ExitCodes.ValidationError;
        }

        SystemRegisterGenesis genesis;
        try
        {
            genesis = GenesisFileLoader.Load(filePath)!;
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"Failed to parse genesis file: {ex.Message}");
            return ExitCodes.ValidationError;
        }

        if (genesis is null)
        {
            ConsoleHelper.WriteError("Genesis file is empty or a placeholder.");
            return ExitCodes.ValidationError;
        }

        // 1. Structural validation
        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);
        if (errors.Count > 0)
        {
            ConsoleHelper.WriteError("Structural validation failed:");
            foreach (var error in errors)
                ConsoleHelper.WriteError($"  - {error}");
            return ExitCodes.ValidationError;
        }

        // 2. Cryptographic signature verification
        var verificationData = GenesisSignatureVerifier.ExtractVerificationData(genesis);
        var crypto = new CryptoModule();

        if (!Enum.TryParse<WalletNetworks>(verificationData.Algorithm, ignoreCase: true, out var network))
        {
            ConsoleHelper.WriteError($"Unsupported algorithm: {verificationData.Algorithm}");
            return ExitCodes.ValidationError;
        }

        var verifyResult = await crypto.VerifyAsync(
            verificationData.Signature,
            verificationData.SignedDataHash,
            (byte)network,
            verificationData.PublicKey,
            ct);

        if (verifyResult != Sorcha.Cryptography.Enums.CryptoStatus.Success)
        {
            Console.WriteLine();
            ConsoleHelper.WriteError("Genesis file verification FAILED.");
            Console.WriteLine();
            Console.WriteLine($"  Network ID:     {genesis.NetworkId}");
            Console.WriteLine($"  Register ID:    {SystemRegisterConstants.SystemRegisterId}");
            Console.WriteLine();
            ConsoleHelper.WriteError($"  FAILURE: Control record signature is invalid. (Status: {verifyResult})");
            Console.WriteLine($"  Signer:         {genesis.GenesisPublicKeyFingerprint}");
            Console.WriteLine($"  Payload hash:   {genesis.GenesisTransaction.PayloadHash}");
            return 1;
        }

        // 3. Display success
        Console.WriteLine();
        ConsoleHelper.WriteSuccess("Genesis file verified.");
        Console.WriteLine();
        Console.WriteLine($"  Network ID:     {genesis.NetworkId}");
        Console.WriteLine($"  Register ID:    {SystemRegisterConstants.SystemRegisterId}");
        Console.WriteLine($"  Version:        {genesis.Version}");
        Console.WriteLine($"  Algorithm:      {genesis.GenesisTransaction.Signature.Algorithm}");
        Console.WriteLine($"  Fingerprint:    {genesis.GenesisPublicKeyFingerprint}");
        Console.WriteLine($"  Signed At:      {genesis.GenesisTransaction.Signature.SignedAt:O}");
        Console.WriteLine();
        Console.WriteLine("  Validator Roster:");

        var idx = 1;
        foreach (var v in genesis.ValidatorRoster.Validators)
        {
            var status = v.Status.ToString().PadRight(7);
            Console.WriteLine($"    #{idx}  {v.ValidatorId[..Math.Min(12, v.ValidatorId.Length)]}...  {v.Algorithm}  {status}  {v.DerivationContext}");
            idx++;
        }

        Console.WriteLine();
        ConsoleHelper.WriteSuccess("  Signatures:     ALL VALID");

        return ExitCodes.Success;
    }
}

/// <summary>
/// Imports a genesis validator key into the local Wallet Service.
/// Placeholder — full implementation in US5 (Phase 7).
/// </summary>
public class SystemRegisterImportValidatorKeyCommand : Command
{
    public SystemRegisterImportValidatorKeyCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("import-validator-key", "Import genesis validator key into Wallet Service")
    {
        var keyOption = new Option<string>("--key", "-k")
        {
            Description = "Path to the genesis-validator-key.json file",
            Required = true
        };

        Options.Add(keyOption);

        this.SetAction((ParseResult parseResult, CancellationToken ct) =>
        {
            ConsoleHelper.WriteWarning("import-validator-key is not yet implemented. Coming in a future update.");
            return Task.FromResult(ExitCodes.GeneralError);
        });
    }
}
