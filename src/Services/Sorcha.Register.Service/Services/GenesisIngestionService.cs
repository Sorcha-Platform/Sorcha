// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.Genesis;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceDefaults;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Loads, verifies, and ingests a pre-signed system register genesis block.
/// The genesis is never created at runtime — it is always an externally produced artifact.
/// </summary>
public class GenesisIngestionService
{
    private readonly IOptions<SystemRegisterOptions> _options;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ICryptoModule _cryptoModule;
    private readonly ILogger<GenesisIngestionService> _logger;

    public GenesisIngestionService(
        IOptions<SystemRegisterOptions> options,
        IValidatorServiceClient validatorClient,
        ICryptoModule cryptoModule,
        ILogger<GenesisIngestionService> logger)
    {
        _options = options;
        _validatorClient = validatorClient;
        _cryptoModule = cryptoModule;
        _logger = logger;
    }

    /// <summary>
    /// Loads the genesis file, verifies its signature, and returns the loaded genesis.
    /// Returns null if no genesis file is configured or embedded.
    /// </summary>
    public async Task<SystemRegisterGenesis?> LoadAndVerifyGenesisAsync(CancellationToken cancellationToken)
    {
        var genesis = await GenesisFileLoader.LoadAsync(_options.Value.GenesisFile, cancellationToken);
        if (genesis is null)
        {
            _logger.LogWarning("No system register genesis file found (no config path, no embedded resource)");
            return null;
        }

        _logger.LogInformation(
            "Loaded system register genesis: NetworkId={NetworkId}, Fingerprint={Fingerprint}",
            genesis.NetworkId, genesis.GenesisPublicKeyFingerprint);

        // Structural validation
        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                _logger.LogError("Genesis structural validation error: {Error}", error);
            throw new InvalidOperationException(
                $"System register genesis file failed structural validation with {errors.Count} error(s).");
        }

        // Cryptographic signature verification
        var verificationData = GenesisSignatureVerifier.ExtractVerificationData(genesis);

        if (!Enum.TryParse<WalletNetworks>(verificationData.Algorithm, ignoreCase: true, out var network))
            throw new InvalidOperationException(
                $"Unsupported signature algorithm in genesis: {verificationData.Algorithm}");

        var verifyResult = await _cryptoModule.VerifyAsync(
            verificationData.Signature,
            verificationData.SignedDataHash,
            (byte)network,
            verificationData.PublicKey,
            cancellationToken);

        if (verifyResult != CryptoStatus.Success)
            throw new InvalidOperationException(
                $"System register genesis signature verification failed: {verifyResult}. " +
                $"The genesis file may be corrupted or tampered with.");

        _logger.LogInformation("System register genesis signature verified successfully");
        return genesis;
    }

    /// <summary>
    /// Submits the pre-signed genesis transaction to the Validator Service.
    /// </summary>
    public async Task<bool> IngestGenesisAsync(
        SystemRegisterGenesis genesis,
        CancellationToken cancellationToken)
    {
        var tx = genesis.GenesisTransaction;

        // Decode payload for the Validator Service (expects JsonElement)
        var payloadBytes = Convert.FromBase64String(tx.Payload);
        var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var payloadElement = payloadDoc.RootElement.Clone();

        var submission = new TransactionSubmission
        {
            TransactionId = tx.TxId,
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            BlueprintId = GenesisConstants.BlueprintId,
            ActionId = GenesisConstants.ActionId,
            Payload = payloadElement,
            PayloadHash = tx.PayloadHash,
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = tx.Signature.PublicKey,
                    SignatureValue = tx.Signature.SignatureValue,
                    Algorithm = tx.Signature.Algorithm,
                    SignedBy = "genesis-ceremony"
                }
            ],
            CreatedAt = tx.Signature.SignedAt,
            SequenceNumber = 0,
            Metadata = new Dictionary<string, string>
            {
                ["Type"] = "Genesis",
                ["NetworkId"] = genesis.NetworkId,
                ["Fingerprint"] = genesis.GenesisPublicKeyFingerprint
            }
        };

        _logger.LogInformation(
            "Submitting pre-signed genesis transaction to Validator Service: TxId={TxId}",
            tx.TxId);

        try
        {
            var result = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Genesis transaction accepted by Validator Service: TxId={TxId}",
                    result.TransactionId);
                return true;
            }

            _logger.LogError(
                "Genesis transaction rejected by Validator Service: {ErrorCode} - {ErrorMessage}",
                result.ErrorCode, result.ErrorMessage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit genesis transaction to Validator Service");
            return false;
        }
    }

    /// <summary>
    /// Checks if the genesis fingerprint matches a trusted fingerprint.
    /// Used during peer sync to verify system register origin.
    /// </summary>
    public bool VerifyGenesisFingerprint(byte[] publicKey, string trustedFingerprint)
    {
        return GenesisSignatureVerifier.MatchesFingerprint(publicKey, trustedFingerprint);
    }

    /// <summary>
    /// Pre-creates the system register row with the genesis control record stashed in
    /// <see cref="Register.InitialControlRecord"/> before genesis ingest.
    /// </summary>
    /// <remarks>
    /// Breaks the validator-enrolment deadlock: <c>RegisterLocalRelationshipService</c>
    /// derives the local roster relationship from <c>InitialControlRecord</c> when no
    /// genesis docket is yet sealed, which lets <c>RegisterMonitoringBootstrap</c> in
    /// validator-service enrol for the system register on its 30s reconcile cycle —
    /// without this, the validator never sees the register and docket 0 is never
    /// sealed, so the row never appears.
    ///
    /// Idempotent: returns silently if the register already exists locally.
    /// </remarks>
    public async Task EnsureSystemRegisterRowAsync(
        SystemRegisterGenesis genesis,
        RegisterManager registerManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(genesis);
        ArgumentNullException.ThrowIfNull(registerManager);

        var existing = await registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogDebug(
                "System register row already exists (Height={Height}); skipping pre-create",
                existing.Height);
            return;
        }

        RegisterControlRecord? controlRecord;
        try
        {
            var payloadBytes = Convert.FromBase64String(genesis.GenesisTransaction.Payload);
            controlRecord = JsonSerializer.Deserialize<RegisterControlRecord>(payloadBytes);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            _logger.LogWarning(ex,
                "Failed to decode genesis payload into RegisterControlRecord — " +
                "skipping pre-create. Validator enrolment will rely on docket-0 seal.");
            return;
        }

        if (controlRecord is null)
        {
            _logger.LogWarning(
                "Genesis payload decoded to null RegisterControlRecord — skipping pre-create");
            return;
        }

        await registerManager.CreateRegisterAsync(
            name: SystemRegisterConstants.SystemRegisterName,
            advertise: true,
            isFullReplica: true,
            registerId: SystemRegisterConstants.SystemRegisterId,
            description: "Sorcha platform system register — root of trust for blueprints and governance.",
            purpose: RegisterPurpose.System,
            initialControlRecord: controlRecord,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Pre-created system register row with stashed control record " +
            "(roster size={RosterSize}) — validator can now enrol pre-seal",
            controlRecord.Validators?.Validators.Count ?? 0);
    }
}
