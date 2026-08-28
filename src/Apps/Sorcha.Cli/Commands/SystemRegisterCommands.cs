// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Services;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.Wallet.Contracts.Constants;

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

        // 1. Generate BIP39 mnemonic and derive keys
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var mnemonicWords = mnemonic.ToString();

        // Direct BIP32 derivation off the master ExtKey — this matches the
        // runtime sign path post-#471 (wallet-service decrypts the master
        // seed blob and derives purpose keys with a single HMAC). Pre-#471
        // the runtime walked a double-HMAC chain (wrap leaf as fresh seed,
        // derive again); the CLI had to replicate that quirk verbatim to
        // keep the genesis roster matching the runtime. With the runtime
        // fixed, the CLI uses the standard BIP32 chain too.
        var masterExtKey = mnemonic.DeriveExtKey();

        // Derive sorcha:docket-signing (m/44'/0'/0'/0/102) directly off the
        // master ExtKey. The resulting public key goes into the genesis
        // validator roster and the validator-service signs dockets with
        // the same key derived through the same chain at runtime.
        var derivedPrivateKeyBytes = masterExtKey.Derive(new KeyPath(SorchaDerivationPaths.DocketSigningPath)).PrivateKey.ToBytes();

        // Generate ED25519 keypair from derived 32-byte seed
        var keyResult = await crypto.GenerateKeySetAsync(network, seed: derivedPrivateKeyBytes, cancellationToken: ct);
        if (!keyResult.IsSuccess)
        {
            ConsoleHelper.WriteError($"Key generation failed: {keyResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }

        var keySet = keyResult.Value;
        var publicKeyBytes = keySet.PublicKey.Key!;
        var privateKeyBytes = keySet.PrivateKey.Key!;

        // Also derive the register-control key (m/44'/0'/0'/0/101) for signing the genesis transaction.
        // Same direct-master chain as docket-signing — derive off the master ExtKey directly.
        var controlPrivateKeyBytes = masterExtKey.Derive(new KeyPath(SorchaDerivationPaths.RegisterControlPath)).PrivateKey.ToBytes();
        var controlKeyResult = await crypto.GenerateKeySetAsync(network, seed: controlPrivateKeyBytes, cancellationToken: ct);
        if (!controlKeyResult.IsSuccess)
        {
            ConsoleHelper.WriteError($"Control key derivation failed: {controlKeyResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }
        var controlKeySet = controlKeyResult.Value;
        var controlPublicKeyBytes = controlKeySet.PublicKey.Key!;
        var controlPrivateKey = controlKeySet.PrivateKey.Key!;

        // Feature 196 (#1591): also derive sorcha:blueprint-publish. The Validator grants the
        // blueprint-publication exemption — which waives six rules including VAL_BP_002 sender
        // authorisation — only to a signer holding an ACTIVE roster entry under this context.
        // Without it in the genesis control record, the system register accepts no blueprint
        // publications at all and SSR seeding fails on the first blueprint.
        //
        // Same direct-master chain as the other two, so the node recovers the identical key from
        // this mnemonic at runtime and matches its own roster entry.
        var publishPrivateKeyBytes = masterExtKey.Derive(new KeyPath(SorchaDerivationPaths.BlueprintPublishPath)).PrivateKey.ToBytes();
        var publishKeyResult = await crypto.GenerateKeySetAsync(network, seed: publishPrivateKeyBytes, cancellationToken: ct);
        if (!publishKeyResult.IsSuccess)
        {
            ConsoleHelper.WriteError($"Blueprint-publish key derivation failed: {publishKeyResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }
        var publishPublicKeyBytes = publishKeyResult.Value.PublicKey.Key!;

        // 2. Build the control record and wrap it in a ControlTransactionPayload envelope.
        //    PR #868 made the orchestrator's genesis path emit the wrapper shape
        //    ({ version, roster, operation }) so GovernanceRosterService and the non-genesis
        //    governance-commit path can read it back via a single deserialise. The CLI mints
        //    the SYSTEM register genesis JSON that is then embedded in the register-service
        //    image, so it must produce the same shape — otherwise the system register's
        //    /governance/roster surfaces members:[] (an issue observed live on n1 the day
        //    after PR #868 deployed). All known readers accept either shape via
        //    RegisterControlPayloadReader; new genesis bytes therefore go through the same
        //    path the orchestrator uses.
        var controlRecord = BuildControlRecord(publicKeyBytes, publishPublicKeyBytes, algorithm);
        var controlPayload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = controlRecord,
            Operation = null, // genesis carries no producing operation
        };
        var controlRecordJson = JsonSerializer.Serialize(controlPayload, CanonicalJsonOptions);
        var controlRecordBytes = Encoding.UTF8.GetBytes(controlRecordJson);

        // 3. Compute payload hash
        var payloadHash = Convert.ToHexString(SHA256.HashData(controlRecordBytes)).ToLowerInvariant();
        var payloadBase64 = Convert.ToBase64String(controlRecordBytes);

        // 4. Compute deterministic TxId
        var txId = GenesisSignatureVerifier.ComputeGenesisTxId();

        // 5. Sign: SHA256(UTF8("{TxId}:{PayloadHash}"))
        var dataToSign = $"{txId}:{payloadHash}";
        var signedDataHash = SHA256.HashData(Encoding.UTF8.GetBytes(dataToSign));

        // Sign genesis with the register-control derived key (not docket-signing)
        var signResult = await crypto.SignAsync(signedDataHash, (byte)network, controlPrivateKey, ct);
        if (!signResult.IsSuccess || signResult.Value is null)
        {
            ConsoleHelper.WriteError($"Signing failed: {signResult.ErrorMessage}");
            return ExitCodes.GeneralError;
        }

        var signatureBytes = signResult.Value;
        var now = DateTimeOffset.UtcNow;
        var fingerprint = GenesisFileLoader.ComputeFingerprint(controlPublicKeyBytes);

        // 6. Derive wallet address from docket-signing public key (used in validator roster)
        var walletUtils = new WalletUtilities();
        var walletAddress = walletUtils.PublicKeyToWallet(publicKeyBytes, (byte)network)
            ?? DeriveWalletAddress(publicKeyBytes, network);

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
                    PublicKey = Convert.ToBase64String(controlPublicKeyBytes),
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
                        DerivationContext = SorchaDerivationPaths.DocketSigning,
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = now
                    },
                    // Feature 196: mirrors the signed control record above. The authoritative copy
                    // is the one inside the payload; this one must not disagree with it.
                    new ValidatorRosterEntry
                    {
                        ValidatorId = walletAddress,
                        PublicKey = Convert.ToBase64String(publishPublicKeyBytes),
                        Algorithm = ParseAlgorithm(algorithm),
                        DerivationContext = SorchaDerivationPaths.BlueprintPublish,
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = now
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = fingerprint
        };

        // 8. Build validator key file (contains mnemonic for wallet recovery)
        var validatorKeyFile = new GenesisValidatorKeyFile
        {
            Version = 1,
            NetworkId = networkId,
            WalletAddress = walletAddress,
            // PrivateKey omitted — recover from mnemonic instead
            PublicKey = Convert.ToBase64String(publicKeyBytes),
            Algorithm = algorithm,
            CreatedAt = now,
            Fingerprint = fingerprint,
            Mnemonic = mnemonicWords
        };

        // 9. Write output files
        var genesisPath = outputPath ?? FindDefaultGenesisPath();
        var genesisJson = JsonSerializer.Serialize(genesis, PrettyJsonOptions);

        var genesisDir = Path.GetDirectoryName(genesisPath);
        if (!string.IsNullOrEmpty(genesisDir))
            Directory.CreateDirectory(genesisDir);

        await File.WriteAllTextAsync(genesisPath, genesisJson, ct);

        var keyFilePath = ResolveValidatorKeyPath(outputPath, Directory.GetCurrentDirectory());
        var keyFileDir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(keyFileDir))
            Directory.CreateDirectory(keyFileDir);

        var keyFileJson = JsonSerializer.Serialize(validatorKeyFile, PrettyJsonOptions);
        await File.WriteAllTextAsync(keyFilePath, keyFileJson, ct);

        // 10. Zeroize key material in memory
        keySet.Zeroize();
        controlKeySet.Zeroize();

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
        ConsoleHelper.WriteWarning("Store genesis-validator-key.json securely or destroy it after importing");
        ConsoleHelper.WriteWarning("into the first validator. The mnemonic controls ALL keys derived from");
        ConsoleHelper.WriteWarning("this wallet, not just the genesis signing key.");

        return ExitCodes.Success;
    }

    /// <summary>
    /// The genesis control record. Its <c>Validators</c> is the roster the Validator actually reads:
    /// <c>GovernanceRosterService</c> reconstructs from the control-transaction PAYLOAD, not from the
    /// genesis document's top-level <c>validatorRoster</c> field.
    /// </summary>
    /// <param name="publicKey">The docket-signing key (m/44'/0'/0'/0/102).</param>
    /// <param name="publishPublicKey">The blueprint-publish key (Feature 196).</param>
    /// <param name="algorithm">Signature algorithm name.</param>
    private static RegisterControlRecord BuildControlRecord(
        byte[] publicKey, byte[] publishPublicKey, string algorithm)
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
                        DerivationContext = SorchaDerivationPaths.DocketSigning,
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = now
                    },
                    // Feature 196 (#1591): who may PUBLISH definitions to the system register.
                    // A separate key from docket signing by design — a compromise of one is then
                    // not a compromise of the other.
                    new ValidatorRosterEntry
                    {
                        // Same node id as the docket-signing entry above: one node, two purpose
                        // keys, distinguished by DerivationContext. Authority is matched by public
                        // key, never by this id.
                        ValidatorId = DeriveWalletAddress(publicKey, Enum.Parse<WalletNetworks>(algorithm, ignoreCase: true)),
                        PublicKey = Convert.ToBase64String(publishPublicKey),
                        Algorithm = ParseAlgorithm(algorithm),
                        DerivationContext = SorchaDerivationPaths.BlueprintPublish,
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
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported signature algorithm")
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

    /// <summary>
    /// Where the genesis ceremony's validator key file goes.
    ///
    /// <para>This is private key material: its mnemonic controls EVERY key derived from the genesis
    /// wallet, not just the genesis signing key. With no explicit <c>--output</c> it used to land in
    /// the process's current directory — which, for the documented ceremony workflow, is the repo
    /// root. A <c>genesis-validator-key.json.bak-pre471</c> duly sat there untracked for weeks, one
    /// <c>git add -A</c> from being published (<c>.gitignore</c> matched the exact filename, which a
    /// <c>.bak-*</c> suffix defeats; it now globs the family).</para>
    ///
    /// <para>The default is now the repo's gitignored <c>/temp</c>, located by walking up for the
    /// same marker <see cref="FindDefaultGenesisPath"/> uses — so it does not matter where inside the
    /// checkout the CLI was invoked from. Outside a checkout (the CLI ships as a global tool and can
    /// run anywhere) it falls back to the working directory rather than inventing a location.</para>
    ///
    /// <para>An explicit <c>--output</c> still keeps the key adjacent to the genesis file. Scripted
    /// ceremonies pass one and depend on that; only the default moved.</para>
    ///
    /// <para>Internal rather than private so it can be tested directly — the alternative is asserting
    /// on a path the test would have to reproduce by copying this logic, which proves nothing.</para>
    /// </summary>
    internal static string ResolveValidatorKeyPath(string? outputPath, string workingDirectory)
    {
        const string KeyFileName = "genesis-validator-key.json";

        if (outputPath is not null)
        {
            return Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? workingDirectory,
                KeyFileName);
        }

        var repoRoot = FindRepoRoot(workingDirectory);
        return repoRoot is not null
            ? Path.Combine(repoRoot, "temp", KeyFileName)
            : Path.Combine(workingDirectory, KeyFileName);
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for the Sorcha checkout marker,
    /// returning the repo root or null when not inside one. Shared by the genesis-file and
    /// validator-key path defaults so the two cannot disagree about where the repo is.
    /// </summary>
    private static string? FindRepoRoot(string startDirectory)
    {
        var dir = startDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "Common", "Sorcha.Register.Models")))
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        return null;
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
/// Imports a genesis validator key into the local Wallet Service by recovering
/// a wallet from the BIP39 mnemonic in the key file.
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
        var validatorIdOption = new Option<string>("--validator-id")
        {
            Description = "Validator ID — must match the running Validator Service's Validator__ValidatorId config. Defaults to 'local-validator' (the docker-compose default).",
            Required = false,
            DefaultValueFactory = _ => "local-validator"
        };

        Options.Add(keyOption);
        Options.Add(validatorIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var keyFilePath = parseResult.GetValue(keyOption)!;
            var validatorId = parseResult.GetValue(validatorIdOption)!;

            try
            {
                // 1. Load and validate key file
                if (!File.Exists(keyFilePath))
                {
                    ConsoleHelper.WriteError($"Key file not found: {keyFilePath}");
                    return ExitCodes.ValidationError;
                }

                var json = await File.ReadAllTextAsync(keyFilePath, ct);
                var keyFile = JsonSerializer.Deserialize<GenesisValidatorKeyFile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (keyFile is null)
                {
                    ConsoleHelper.WriteError("Failed to parse genesis validator key file.");
                    return ExitCodes.ValidationError;
                }

                if (string.IsNullOrWhiteSpace(keyFile.Mnemonic))
                {
                    ConsoleHelper.WriteError("Key file does not contain a mnemonic. Regenerate with the latest ceremony.");
                    return ExitCodes.ValidationError;
                }

                // 2. Get auth token and wallet client
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);

                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    return ExitCodes.AuthenticationError;
                }

                var walletClient = await clientFactory.CreateWalletServiceClientAsync(profileName);

                // 3. Recover the SYSTEM wallet from mnemonic (tenant=system,
                //    owner=validator:{validatorId}, name=system-wallet-{validatorId})
                //    so Validator Service's CreateOrRetrieveSystemWalletAsync finds it.
                var recoverRequest = new Models.RecoverSystemWalletApiRequest
                {
                    ValidatorId = validatorId,
                    Mnemonic = keyFile.Mnemonic,
                    Algorithm = keyFile.Algorithm ?? "ED25519"
                };

                ConsoleHelper.WriteInfo(
                    $"Recovering system wallet for validator '{validatorId}' on network '{keyFile.NetworkId}'...");

                var response = await walletClient.RecoverSystemWalletAsync(
                    recoverRequest,
                    $"Bearer {token}");

                // 4. Display result
                Console.WriteLine();
                ConsoleHelper.WriteSuccess("Validator key imported successfully.");
                Console.WriteLine();
                Console.WriteLine($"  Wallet Address: {response.Address}");
                Console.WriteLine($"  Validator ID:   {validatorId}");
                Console.WriteLine($"  Network ID:     {keyFile.NetworkId}");
                Console.WriteLine($"  Algorithm:      {keyFile.Algorithm}");
                Console.WriteLine($"  Fingerprint:    {keyFile.Fingerprint}");
                Console.WriteLine();
                ConsoleHelper.WriteInfo(
                    "The Validator Service can now seal system register dockets — restart it (or wait for " +
                    "the next CreateOrRetrieveSystemWalletAsync cache refresh) to pick up the new wallet.");

                return ExitCodes.Success;
            }
            catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Run 'sorcha auth login' first.");
                return ExitCodes.AuthenticationError;
            }
            catch (Refit.ApiException ex)
            {
                ConsoleHelper.WriteError($"Wallet Service error: {ex.StatusCode} - {ex.Content}");
                return ExitCodes.ServiceError;
            }
            catch (HttpRequestException)
            {
                ConsoleHelper.WriteError("Cannot reach Wallet Service. Ensure services are running.");
                return ExitCodes.NetworkError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Import failed: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
