// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Diagnostics;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using Sorcha.Blueprint.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Core validation engine that validates transactions against blueprint rules,
/// cryptographic requirements, and chain integrity.
/// </summary>
public class ValidationEngine : IValidationEngine
{
    private readonly ValidationEngineConfiguration _config;
    private readonly IBlueprintCache _blueprintCache;
    private readonly IBlueprintFetcher? _blueprintFetcher;
    private readonly IHashProvider _hashProvider;
    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IChainTransactionCache? _chainTxCache;
    private readonly IRightsEnforcementService _rightsEnforcementService;
    private readonly IWalletSequenceRepository? _walletSequenceRepository;
    private readonly Sorcha.Register.Core.Services.IGovernanceRosterService? _governanceRosterService;
    private readonly ILogger<ValidationEngine> _logger;

    // Statistics
    private long _totalValidated;
    private long _totalSuccessful;
    private long _totalFailed;
    private int _inProgress;
    private readonly ConcurrentDictionary<ValidationErrorCategory, long> _errorsByCategory = new();
    private readonly ConcurrentQueue<double> _durations = new();
    private readonly object _statsLock = new();

    public ValidationEngine(
        IOptions<ValidationEngineConfiguration> config,
        IBlueprintCache blueprintCache,
        IHashProvider hashProvider,
        ICryptoModule cryptoModule,
        IWalletUtilities walletUtilities,
        IRegisterServiceClient registerClient,
        IRightsEnforcementService rightsEnforcementService,
        ILogger<ValidationEngine> logger,
        IBlueprintFetcher? blueprintFetcher = null,
        IWalletSequenceRepository? walletSequenceRepository = null,
        IChainTransactionCache? chainTxCache = null,
        Sorcha.Register.Core.Services.IGovernanceRosterService? governanceRosterService = null)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _blueprintCache = blueprintCache ?? throw new ArgumentNullException(nameof(blueprintCache));
        _blueprintFetcher = blueprintFetcher;
        _walletSequenceRepository = walletSequenceRepository;
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _chainTxCache = chainTxCache;
        _rightsEnforcementService = rightsEnforcementService ?? throw new ArgumentNullException(nameof(rightsEnforcementService));
        _governanceRosterService = governanceRosterService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_blueprintFetcher != null)
        {
            _logger.LogInformation("ValidationEngine initialized with BlueprintFetcher fallback enabled");
        }
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> ValidateTransactionAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var sw = Stopwatch.StartNew();
        Interlocked.Increment(ref _inProgress);
        using var _totalScope = RuleTelemetry.TimeSection("Total");

        try
        {
            var errors = new List<ValidationEngineError>();

            // 1. Validate structure
            var structureResult = ValidateStructure(transaction);
            if (!structureResult.IsValid)
            {
                // Structure errors are fatal - can't continue
                return RecordResult(structureResult, sw.Elapsed);
            }

            // 2. Validate payload hash
            var hashResult = ValidatePayloadHash(transaction);
            if (!hashResult.IsValid)
            {
                errors.AddRange(hashResult.Errors);
                // Hash mismatch is fatal
                return RecordResult(CreateFailureResult(transaction, sw.Elapsed, errors), sw.Elapsed);
            }

            // 3. Validate schema (if enabled)
            if (_config.EnableSchemaValidation)
            {
                var schemaResult = await ValidateSchemaAsync(transaction, ct);
                if (!schemaResult.IsValid)
                {
                    errors.AddRange(schemaResult.Errors);
                }
            }

            // 4. Verify signatures (if enabled)
            if (_config.EnableSignatureVerification)
            {
                var sigResult = await VerifySignaturesAsync(transaction, ct);
                if (!sigResult.IsValid)
                {
                    errors.AddRange(sigResult.Errors);
                }
            }

            // 4b. Validate blueprint conformance (if enabled)
            if (_config.EnableBlueprintConformance)
            {
                var bpResult = await ValidateBlueprintConformanceAsync(transaction, ct);
                if (!bpResult.IsValid)
                {
                    errors.AddRange(bpResult.Errors);
                }
            }

            // 4b-ii. Validate file reference fields (structural checks only)
            if (_config.EnableFileReferenceValidation)
            {
                var fileRefResult = ValidateFileReferences(transaction);
                if (!fileRefResult.IsValid)
                {
                    errors.AddRange(fileRefResult.Errors);
                }
            }

            // 4b-iii. Validate the carried routing decision (Feature 145, VAL_ROUTING_*).
            //         Makes the next-action set a trusted, governed ledger fact: the validator
            //         confirms every next action is a structural successor of the completed action
            //         and that the attestation verifies + satisfies the register's routingAttestation
            //         policy. Transactions that carry no decision are unaffected.
            if (_config.EnableRoutingValidation)
            {
                var routingResult = await ValidateRoutingDecisionAsync(transaction, ct);
                if (!routingResult.IsValid)
                {
                    errors.AddRange(routingResult.Errors);
                }
            }

            // 4c. Validate governance rights for Control transactions (if enabled)
            if (_config.EnableGovernanceValidation)
            {
                var govResult = await _rightsEnforcementService.ValidateGovernanceRightsAsync(transaction, ct);
                if (!govResult.IsValid)
                {
                    errors.AddRange(govResult.Errors);
                }
            }

            // 4e. Validate revocation transaction rules
            if (TransactionTypeClassifier.IsRevocationTransaction(transaction))
            {
                var revResult = await ValidateRevocationAsync(transaction, ct);
                if (!revResult.IsValid)
                {
                    errors.AddRange(revResult.Errors);
                }
            }

            // 4d. Validate crypto policy compliance (if enabled)
            if (_config.EnableCryptoPolicyValidation)
            {
                var cryptoPolicyResult = ValidateCryptoPolicy(transaction);
                if (!cryptoPolicyResult.IsValid)
                {
                    errors.AddRange(cryptoPolicyResult.Errors);
                }
            }

            // 5. Validate chain (if enabled)
            if (_config.EnableChainValidation)
            {
                var chainResult = await ValidateChainAsync(transaction, ct);
                if (!chainResult.IsValid)
                {
                    errors.AddRange(chainResult.Errors);
                }
            }

            // 5b. Validate sequence number for replay protection (SEC-AUDIT 4.2)
            if (_walletSequenceRepository != null && !TransactionTypeClassifier.IsGenesisOrControlTransaction(transaction))
            {
                using var _replayScope = RuleTelemetry.TimeSection("SequenceReplay");
                try
                {
                    if (transaction.SequenceNumber == 0)
                    {
                        errors.Add(CreateError("VAL_REPLAY_001",
                            "Non-genesis transactions must have a sequence number > 0",
                            ValidationErrorCategory.Structure, "SequenceNumber"));
                    }
                    else
                    {
                        // Derive sender wallet from first signature
                        var senderWallet = transaction.Signatures.FirstOrDefault()?.SignedBy;
                        if (string.IsNullOrWhiteSpace(senderWallet))
                        {
                            errors.Add(CreateError("VAL_REPLAY_004",
                                "Cannot validate sequence — no signer wallet found on transaction signatures",
                                ValidationErrorCategory.Structure, "SequenceNumber"));
                        }
                        else
                        {
                            var seqValid = await _walletSequenceRepository.ValidateAndIncrementAsync(
                                transaction.RegisterId, senderWallet, transaction.SequenceNumber, ct);

                            if (!seqValid)
                            {
                                var currentSeq = await _walletSequenceRepository.GetSequenceNumberAsync(
                                    transaction.RegisterId, senderWallet, ct);
                                errors.Add(CreateError("VAL_REPLAY_002",
                                    $"Sequence number {transaction.SequenceNumber} is invalid for wallet '{senderWallet}' on register '{transaction.RegisterId}'. " +
                                    $"Expected {currentSeq + 1}. Possible replay or out-of-order submission.",
                                    ValidationErrorCategory.Chain, "SequenceNumber"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Fail-closed: reject transaction when sequence store is unavailable (SEC-AUDIT 4.2)
                    _logger.LogError(ex, "Sequence validation failed (store unavailable) for transaction {TransactionId} — rejecting (fail-closed)",
                        transaction.TransactionId);
                    errors.Add(CreateError("VAL_REPLAY_003",
                        "Sequence validation unavailable — transaction rejected for safety",
                        ValidationErrorCategory.Chain, "SequenceNumber"));
                }
            }

            // 6. Validate timing
            var timingResult = ValidateTiming(transaction);
            if (!timingResult.IsValid)
            {
                errors.AddRange(timingResult.Errors);
            }

            if (errors.Count > 0)
            {
                return RecordResult(CreateFailureResult(transaction, sw.Elapsed, errors), sw.Elapsed);
            }

            var result = ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);

            return RecordResult(result, sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating transaction {TransactionId}", transaction.TransactionId);

            var result = ValidationEngineResult.Failure(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed,
                new ValidationEngineError
                {
                    Code = "VAL_INTERNAL",
                    Message = $"Internal validation error: {ex.Message}",
                    Category = ValidationErrorCategory.Internal,
                    IsFatal = true
                });

            return RecordResult(result, sw.Elapsed);
        }
        finally
        {
            Interlocked.Decrement(ref _inProgress);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ValidationEngineResult>> ValidateBatchAsync(
        IReadOnlyList<Transaction> transactions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        if (transactions.Count == 0)
            return [];

        _logger.LogDebug("Validating batch of {Count} transactions", transactions.Count);

        if (_config.EnableParallelValidation && transactions.Count > 1)
        {
            var tasks = transactions.Select(tx => ValidateTransactionAsync(tx, ct));
            var results = await Task.WhenAll(tasks);
            return results;
        }

        // Sequential validation
        var resultList = new List<ValidationEngineResult>();
        foreach (var tx in transactions)
        {
            var result = await ValidateTransactionAsync(tx, ct);
            resultList.Add(result);
        }

        return resultList;
    }

    /// <inheritdoc/>
    public ValidationEngineResult ValidateStructure(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("Structure");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(transaction.TransactionId))
        {
            errors.Add(CreateError("VAL_STRUCT_001", "Transaction ID is required",
                ValidationErrorCategory.Structure, "TransactionId", true));
        }

        if (string.IsNullOrWhiteSpace(transaction.RegisterId))
        {
            errors.Add(CreateError("VAL_STRUCT_002", "Register ID is required",
                ValidationErrorCategory.Structure, "RegisterId", true));
        }

        // BlueprintId and ActionId are required for blueprint-based transactions
        // but not for Participant transactions (which have no blueprint context)
        if (!TransactionTypeClassifier.IsParticipantTransaction(transaction))
        {
            if (string.IsNullOrWhiteSpace(transaction.BlueprintId))
            {
                errors.Add(CreateError("VAL_STRUCT_003", "Blueprint ID is required",
                    ValidationErrorCategory.Structure, "BlueprintId", true));
            }

            if (string.IsNullOrWhiteSpace(transaction.ActionId))
            {
                errors.Add(CreateError("VAL_STRUCT_004", "Action ID is required",
                    ValidationErrorCategory.Structure, "ActionId", true));
            }
        }

        if (transaction.Payload.ValueKind == JsonValueKind.Undefined ||
            transaction.Payload.ValueKind == JsonValueKind.Null)
        {
            errors.Add(CreateError("VAL_STRUCT_005", "Payload is required",
                ValidationErrorCategory.Structure, "Payload", true));
        }

        if (string.IsNullOrWhiteSpace(transaction.PayloadHash))
        {
            errors.Add(CreateError("VAL_STRUCT_006", "Payload hash is required",
                ValidationErrorCategory.Structure, "PayloadHash", true));
        }

        if (transaction.Signatures == null || transaction.Signatures.Count == 0)
        {
            errors.Add(CreateError("VAL_STRUCT_007", "At least one signature is required",
                ValidationErrorCategory.Structure, "Signatures", true));
        }
        else
        {
            for (int i = 0; i < transaction.Signatures.Count; i++)
            {
                var sig = transaction.Signatures[i];
                if (sig.PublicKey == null || sig.PublicKey.Length == 0)
                {
                    errors.Add(CreateError("VAL_STRUCT_008",
                        $"Signature {i} is missing public key",
                        ValidationErrorCategory.Structure, $"Signatures[{i}].PublicKey"));
                }

                if (sig.SignatureValue == null || sig.SignatureValue.Length == 0)
                {
                    errors.Add(CreateError("VAL_STRUCT_009",
                        $"Signature {i} is missing signature value",
                        ValidationErrorCategory.Structure, $"Signatures[{i}].SignatureValue"));
                }

                if (string.IsNullOrWhiteSpace(sig.Algorithm))
                {
                    errors.Add(CreateError("VAL_STRUCT_010",
                        $"Signature {i} is missing algorithm",
                        ValidationErrorCategory.Structure, $"Signatures[{i}].Algorithm"));
                }
            }
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> ValidateSchemaAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("Schema");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        // Config check: skip if disabled
        if (!_config.EnableSchemaValidation)
        {
            _logger.LogDebug("Schema validation disabled by configuration");
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        try
        {
            // Genesis/control transactions skip schema validation but MUST have valid signatures (SEC-AUDIT 4.10)
            //
            // Feature 189 (T054): the three governance actions are the exception to the exception.
            // They are Control transactions, so they land here, but the governance blueprint declares
            // a real payload contract for them and they are now checked against it like any other
            // action. Before T054 nothing did: action 1's declared schema described a bare
            // GovernanceOperation while the wire has always carried it nested inside a
            // ControlTransactionPayload envelope, and the two drifted for as long as the contract
            // went unenforced — a schema no conforming payload could satisfy, and no payload that
            // anything checked.
            //
            // This depends on ResolveBlueprintAsync's system-register fallback below. The first
            // attempt at this shipped without it and failed EVERY governance transaction on EVERY
            // register with VAL_SCHEMA_001, live on n1 — register-governance-v1 is seeded to the SSR
            // and is not in the Blueprint Service store. Do not withdraw this exemption again
            // without confirming that fallback still resolves.
            //
            // It withdraws ONLY the schema exemption. The other five riding on the same
            // discriminator stay, and two of them have to — see IsGovernanceActionTransaction.
            if (TransactionTypeClassifier.IsGenesisOrControlTransaction(transaction)
                && !TransactionTypeClassifier.IsGovernanceActionTransaction(transaction))
            {
                _logger.LogDebug("Validating signatures for genesis/control transaction {TransactionId}",
                    transaction.TransactionId);

                if (transaction.Signatures == null || transaction.Signatures.Count == 0)
                {
                    return ValidationEngineResult.Failure(
                        transaction.TransactionId,
                        transaction.RegisterId,
                        sw.Elapsed,
                        [CreateError("VAL_GENESIS_001",
                            "Genesis/control transactions must have at least one signature",
                            ValidationErrorCategory.Cryptographic, "Signatures")]);
                }

                // Verify at least one signature has a valid public key (not empty)
                var hasValidSig = transaction.Signatures.Any(s =>
                    s.PublicKey != null && s.PublicKey.Length > 0 && !string.IsNullOrWhiteSpace(s.Algorithm));

                if (!hasValidSig)
                {
                    return ValidationEngineResult.Failure(
                        transaction.TransactionId,
                        transaction.RegisterId,
                        sw.Elapsed,
                        [CreateError("VAL_GENESIS_002",
                            "Genesis/control transaction signatures must include a valid public key and algorithm",
                            ValidationErrorCategory.Cryptographic, "Signatures")]);
                }

                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            // Participant transactions use a built-in schema instead of blueprint schemas
            if (TransactionTypeClassifier.IsParticipantTransaction(transaction))
            {
                return ValidateParticipantSchema(transaction, sw);
            }

            // Skip schema validation for rejection transactions (payload contains rejection
            // metadata, not the action's data schema)
            if (TransactionTypeClassifier.IsRejectionTransaction(transaction))
            {
                _logger.LogDebug("Skipping schema validation for rejection transaction {TransactionId}",
                    transaction.TransactionId);
                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            // Skip schema validation for presentation lifecycle transactions (PresentationInitiated,
            // PresentationOutcome, PresentationAbandoned). These carry lifecycle metadata — submitter
            // wallet, requirements digest, presentation request id, consumer name — never the gated
            // action's data payload, so the action's `required` schema properties can never be present.
            // Applying the action's data schema to them is a validator bug, not a real violation:
            // a SorchaWallet-gated action whose schema declares `required` fields would otherwise
            // never seal (VAL_SCHEMA_004 on every PresentationInitiated). Chain integrity, signature
            // verification, sender authorisation and route reachability all still apply — this skips
            // ONLY the action-data-payload schema check.
            //
            // C-VAL (catch-up security review 2026-07-29): the discriminator is the SIGNED
            // payload's `type`, never Metadata["Type"]. Metadata is outside the signature, the
            // payload hash and the merkle leaf, so keying this carve-out on it let one unsigned
            // string disable schema validation on any transaction.
            if (TransactionTypeClassifier.IsLifecycleTransaction(transaction))
            {
                _logger.LogDebug(
                    "Skipping schema validation for presentation lifecycle transaction {TransactionId}",
                    transaction.TransactionId);
                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            if (TransactionTypeClassifier.HasUncorroboratedLifecycleMetadata(transaction))
            {
                // Metadata asked for the lifecycle carve-out but the signed payload does not
                // corroborate it. The exemption is (correctly) refused above; log it, because a
                // transaction claiming an exemption it is not entitled to is what an attempted
                // schema-validation bypass looks like. Validation continues normally.
                _logger.LogWarning(
                    "Transaction {TransactionId} carries lifecycle metadata Type={MetadataType} that the "
                    + "signed payload does not corroborate; schema validation is NOT being skipped",
                    transaction.TransactionId, transaction.Metadata["Type"]);
            }

            // Get the blueprint
            var blueprint = await ResolveBlueprintAsync(transaction.BlueprintId!, ct);
            if (blueprint == null)
            {
                errors.Add(CreateError("VAL_SCHEMA_001",
                    $"Blueprint '{transaction.BlueprintId}' not found",
                    ValidationErrorCategory.Blueprint, "BlueprintId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Find the action
            if (!int.TryParse(transaction.ActionId, out var actionIdInt))
            {
                errors.Add(CreateError("VAL_SCHEMA_002",
                    $"Invalid action ID format: '{transaction.ActionId}'",
                    ValidationErrorCategory.Blueprint, "ActionId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
            if (action == null)
            {
                errors.Add(CreateError("VAL_SCHEMA_003",
                    $"Action {transaction.ActionId} not found in blueprint '{transaction.BlueprintId}'",
                    ValidationErrorCategory.Blueprint, "ActionId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Skip schema validation if no schemas defined (FR-006)
            if (action.DataSchemas == null || !action.DataSchemas.Any())
            {
                _logger.LogDebug(
                    "No schemas defined for action {ActionId} in blueprint {BlueprintId}, skipping schema validation",
                    transaction.ActionId, transaction.BlueprintId);
                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            // Skip schema validation for encrypted transactions — the Blueprint Service
            // validates payloads against schemas before encryption, so the validator cannot
            // (and does not need to) repeat schema checks on encrypted ciphertext.
            if (transaction.Payload.ValueKind == JsonValueKind.Object &&
                transaction.Payload.TryGetProperty("contentEncoding", out var encodingProp) &&
                encodingProp.ValueKind == JsonValueKind.String &&
                encodingProp.GetString() == "encrypted")
            {
                _logger.LogDebug(
                    "Transaction {TransactionId} has encrypted payload, skipping schema validation (pre-validated by Blueprint Service)",
                    transaction.TransactionId);
                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            // Extract user payload data from transaction envelope.
            // Transaction payload structure: { type, blueprintId, actionId, ..., payloads: { walletAddr: { userData } } }
            // Schema validation applies to the user data, not the full envelope.
            var payloadToValidate = transaction.Payload;
            if (transaction.Payload.ValueKind == JsonValueKind.Object &&
                transaction.Payload.TryGetProperty("payloads", out var payloadsElement) &&
                payloadsElement.ValueKind == JsonValueKind.Object)
            {
                // Merge ALL disclosed payload views into a single union before schema
                // validation. Each entry in `payloads` is one recipient's disclosure-
                // filtered view of the action data. With field-level disclosures (e.g. a
                // required "evaluationNotes" field disclosed only to the sender via "/*",
                // while other recipients see only "/advancePercentage" + "/feeRate"), no
                // single recipient view is guaranteed to contain every required field. The
                // union of all views is the complete payload the submitter provided, which
                // is what the schema's `required` constraint applies to. Validating only the
                // first view (the previous behaviour) spuriously failed VAL_SCHEMA_004 when
                // the first-enumerated recipient had a narrower disclosure than the schema's
                // required set. Overlapping fields carry identical values across views, so
                // first-writer-wins is safe.
                var merged = new JsonObject();
                var viewCount = 0;
                foreach (var recipient in payloadsElement.EnumerateObject())
                {
                    if (recipient.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    viewCount++;
                    foreach (var field in recipient.Value.EnumerateObject())
                    {
                        if (!merged.ContainsKey(field.Name))
                        {
                            merged[field.Name] = JsonNode.Parse(field.Value.GetRawText());
                        }
                    }
                }

                payloadToValidate = JsonSerializer.SerializeToElement(merged);
                _logger.LogDebug(
                    "Merged {ViewCount} disclosed payload view(s) into a union for schema validation",
                    viewCount);
            }

            // Evaluate payload against all schemas (payload must pass ALL schemas)
            var evalOptions = new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            };

            foreach (var schemaDoc in action.DataSchemas)
            {
                JsonSchema jsonSchema;
                try
                {
                    // Strip x-* custom keywords (e.g. x-pages, x-sections, x-introduction,
                    // x-width, x-rule, x-persona, x-file) before parsing. These are
                    // UI-renderer extensions consumed by Sorcha.Blueprint.Models.SchemaLayoutParser
                    // and friends, not JSON Schema vocabulary. Json.Schema (the library) is
                    // strict about unknown keywords and throws "Unknown keywords (x-pages)
                    // are disallowed for this dialect" when they appear at the schema root.
                    var schemaText = StripCustomExtensionKeywords(schemaDoc.RootElement);
                    jsonSchema = GetOrParseActionSchema(schemaText);
                }
                catch (Exception ex)
                {
                    errors.Add(CreateError("VAL_SCHEMA_005",
                        $"Malformed JSON schema in blueprint '{transaction.BlueprintId}' action {transaction.ActionId}: {ex.Message}",
                        ValidationErrorCategory.Blueprint, "DataSchemas", true));
                    continue;
                }

                EvaluationResults result;
                using (RuleTelemetry.TimeRule("VAL_SCHEMA_004"))
                {
                    result = jsonSchema.Evaluate(payloadToValidate, evalOptions);
                }

                if (!result.IsValid)
                {
                    // Collect all violations from the evaluation
                    if (result.Details != null)
                    {
                        foreach (var detail in result.Details.Where(d => !d.IsValid && d.Errors != null))
                        {
                            foreach (var error in detail.Errors!)
                            {
                                var instanceLocation = detail.InstanceLocation.ToString();
                                errors.Add(CreateError("VAL_SCHEMA_004",
                                    $"Schema violation at '{instanceLocation}': {error.Value}",
                                    ValidationErrorCategory.Schema, instanceLocation));
                            }
                        }
                    }

                    // If no details were extracted, add a generic error
                    if (errors.Count == 0)
                    {
                        errors.Add(CreateError("VAL_SCHEMA_004",
                            "Payload does not conform to the required schema",
                            ValidationErrorCategory.Schema, "Payload"));
                    }
                }
            }

            _logger.LogDebug(
                "Schema validation for transaction {TransactionId} against blueprint {BlueprintId} action {ActionId}: {ViolationCount} violations",
                transaction.TransactionId, transaction.BlueprintId, transaction.ActionId, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating schema for transaction {TransactionId}", transaction.TransactionId);
            errors.Add(CreateError("VAL_SCHEMA_ERR",
                $"Schema validation error: {ex.Message}",
                ValidationErrorCategory.Schema, isFatal: true));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> VerifySignaturesAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("Signatures");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        try
        {
            // The data that was signed is the transaction ID + payload hash
            var signedData = $"{transaction.TransactionId}:{transaction.PayloadHash}";
            var signedHash = _hashProvider.ComputeHash(
                Encoding.UTF8.GetBytes(signedData),
                HashType.SHA256);

            foreach (var signature in transaction.Signatures)
            {
                using var _sigScope = RuleTelemetry.TimeRule("VAL_SIG_VERIFY");
                try
                {
                    // Parse the algorithm
                    if (!AlgorithmMapper.TryParseAlgorithm(signature.Algorithm, out var network))
                    {
                        errors.Add(CreateError("VAL_SIG_001",
                            $"Unsupported signature algorithm: {signature.Algorithm}",
                            ValidationErrorCategory.Cryptographic,
                            $"Signatures.Algorithm"));
                        continue;
                    }

                    // Verify the signature
                    var verifyResult = await _cryptoModule.VerifyAsync(
                        signature.SignatureValue,
                        signedHash,
                        (byte)network,
                        signature.PublicKey,
                        ct);

                    if (verifyResult != CryptoStatus.Success)
                    {
                        var publicKeyHex = Convert.ToHexString(signature.PublicKey);
                        errors.Add(CreateError("VAL_SIG_002",
                            $"Invalid signature from public key {publicKeyHex[..Math.Min(20, publicKeyHex.Length)]}...",
                            ValidationErrorCategory.Cryptographic,
                            "Signatures"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to verify signature for transaction {TransactionId}",
                        transaction.TransactionId);
                    errors.Add(CreateError("VAL_SIG_003",
                        $"Signature verification failed: {ex.Message}",
                        ValidationErrorCategory.Cryptographic,
                        "Signatures"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signatures for transaction {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_SIG_ERR",
                $"Signature verification error: {ex.Message}",
                ValidationErrorCategory.Cryptographic, isFatal: true));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <summary>
    /// Validates that transaction signature algorithms comply with crypto policy.
    /// Checks that all algorithms are recognized and supported.
    /// Per-register policy enforcement checks accepted/required algorithms.
    /// </summary>
    private ValidationEngineResult ValidateCryptoPolicy(Transaction transaction)
    {
        using var _section = RuleTelemetry.TimeSection("CryptoPolicy");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        // Skip policy validation for system/control transactions
        if (transaction.Metadata.TryGetValue("Type", out var txType) &&
            txType is "Genesis" or "Control")
        {
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        // Recognized signature algorithms (classical + PQC)
        var recognizedAlgorithms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ED25519", "NISTP256", "NIST-P256", "P256", "ECDSA-P256",
            "RSA4096", "RSA-4096",
            "ML-DSA-65", "MLDSA65",
            "SLH-DSA-128S", "SLHDSA128S"
        };

        foreach (var signature in transaction.Signatures)
        {
            if (!recognizedAlgorithms.Contains(signature.Algorithm))
            {
                errors.Add(CreateError("VAL_POLICY_001",
                    $"Signature algorithm '{signature.Algorithm}' is not recognized by the crypto policy",
                    ValidationErrorCategory.Cryptographic,
                    "Signatures.Algorithm"));
            }
        }

        if (transaction.Signatures.Count == 0)
        {
            errors.Add(CreateError("VAL_POLICY_002",
                "Transaction has no signatures — crypto policy requires at least one",
                ValidationErrorCategory.Cryptographic,
                "Signatures",
                isFatal: true));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <summary>
    /// Validates the routing decision carried on an action transaction (Feature 145).
    /// <para>
    /// <b>VAL_ROUTING_001</b> — every <c>nextActions[i]</c> must be a structural successor of the
    /// completed action in the published route graph (the same graph VAL_BP_003 enforces backward).
    /// An empty next-action set (a terminating branch) is always structurally valid.
    /// </para>
    /// <para>
    /// <b>VAL_ROUTING_002</b> — the carried attestation must verify and satisfy the register's
    /// <c>routingAttestation</c> governance policy. v1 supports only
    /// <see cref="Sorcha.Register.Models.AttestationKind.SenderSigned"/>: the sender wallet signature
    /// over the canonical, attestation-free decision is verified against the transaction signer.
    /// A register that governs itself up to a stronger strength — or a decision carrying a reserved
    /// attestation kind — is refused until v2/v3 land.
    /// </para>
    /// Transactions that carry no decision are unaffected (the producer writes one for every action;
    /// genesis/control/participant/rejection and intra-action lifecycle txns are skipped).
    /// </summary>
    public async Task<ValidationEngineResult> ValidateRoutingDecisionAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("RoutingDecision");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        ValidationEngineResult Ok() => ValidationEngineResult.Success(
            transaction.TransactionId, transaction.RegisterId, sw.Elapsed);

        // Routing decisions ride on forward-routing action transactions only.
        if (TransactionTypeClassifier.IsGenesisOrControlTransaction(transaction)
            || TransactionTypeClassifier.IsParticipantTransaction(transaction)
            || TransactionTypeClassifier.IsRejectionTransaction(transaction)
            || TransactionTypeClassifier.IsIntraActionLifecycleTerminal(transaction))
        {
            return Ok();
        }

        // No carried decision → nothing to validate (legacy / pre-Feature-145 transactions).
        if (transaction.Metadata is null
            || !transaction.Metadata.TryGetValue("routingDecision", out var decisionJson)
            || string.IsNullOrWhiteSpace(decisionJson))
        {
            return Ok();
        }

        Sorcha.Register.Models.RoutingDecision? decision;
        try
        {
            decision = JsonSerializer.Deserialize<Sorcha.Register.Models.RoutingDecision>(
                decisionJson, Sorcha.Register.Models.RegisterSerializationOptions.Canonical);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Routing decision on transaction {TransactionId} is malformed JSON",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_ROUTING_002",
                "Routing decision is malformed and cannot be validated",
                ValidationErrorCategory.Blueprint, "routingDecision", isFatal: true));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        if (decision is null)
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                "Routing decision deserialized to null",
                ValidationErrorCategory.Blueprint, "routingDecision", isFatal: true));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        // ---- VAL_ROUTING_001: structural successor check against the published route graph ----
        var blueprint = await ResolveBlueprintAsync(transaction.BlueprintId!, ct);
        if (blueprint is null)
        {
            // Schema/conformance validation already flags a missing blueprint; surface a routing
            // error too so the decision is never trusted without its route graph.
            errors.Add(CreateError("VAL_ROUTING_001",
                $"Cannot verify routing decision: blueprint '{transaction.BlueprintId}' not found",
                ValidationErrorCategory.Blueprint, "routingDecision"));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        if (int.TryParse(transaction.ActionId, out var txActionId)
            && decision.CompletedActionId != txActionId)
        {
            errors.Add(CreateError("VAL_ROUTING_001",
                $"Routing decision completedActionId {decision.CompletedActionId} does not match the transaction's action {txActionId}",
                ValidationErrorCategory.Blueprint, "routingDecision.completedActionId"));
        }

        var completedAction = blueprint.Actions.FirstOrDefault(a => a.Id == decision.CompletedActionId);
        if (completedAction is null)
        {
            errors.Add(CreateError("VAL_ROUTING_001",
                $"Routing decision references completed action {decision.CompletedActionId} which is not in blueprint '{transaction.BlueprintId}'",
                ValidationErrorCategory.Blueprint, "routingDecision.completedActionId"));
        }
        else
        {
            var structuralSuccessors = (completedAction.Routes ?? [])
                .SelectMany(r => r.NextActionIds)
                .ToHashSet();
            if (completedAction.RejectionConfig is not null)
            {
                structuralSuccessors.Add(completedAction.RejectionConfig.TargetActionId);
            }

            foreach (var next in decision.NextActions)
            {
                if (!structuralSuccessors.Contains(next.ActionId))
                {
                    errors.Add(CreateError("VAL_ROUTING_001",
                        $"Routing decision next action {next.ActionId} is not a structural successor of action {decision.CompletedActionId} in the published route graph",
                        ValidationErrorCategory.Blueprint, "routingDecision.nextActions"));
                }
            }
        }

        // ---- VAL_ROUTING_002: governance strength + attestation verification ----
        var requiredStrength = await ResolveRequiredRoutingAttestationAsync(transaction.RegisterId, ct);
        if (requiredStrength != Sorcha.Register.Models.AttestationKind.SenderSigned)
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                $"Register '{transaction.RegisterId}' requires routing attestation strength '{requiredStrength}', which is not supported in this version",
                ValidationErrorCategory.Blueprint, "routingAttestation"));
            // No point verifying a v1 signature against a policy we cannot satisfy.
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        var attestation = decision.Attestation;
        if (attestation is null)
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                "Routing decision carries no attestation",
                ValidationErrorCategory.Cryptographic, "routingDecision.attestation"));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        if (attestation.Kind != Sorcha.Register.Models.AttestationKind.SenderSigned)
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                $"Routing attestation kind '{attestation.Kind}' is not supported in this version (only SenderSigned)",
                ValidationErrorCategory.Cryptographic, "routingDecision.attestation.kind"));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        if (string.IsNullOrEmpty(attestation.Signature))
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                "Routing attestation has no signature",
                ValidationErrorCategory.Cryptographic, "routingDecision.attestation.signature"));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        var signer = transaction.Signatures.FirstOrDefault();
        if (signer is null || !AlgorithmMapper.TryParseAlgorithm(signer.Algorithm, out var network))
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                "Cannot verify routing attestation: transaction has no usable signer",
                ValidationErrorCategory.Cryptographic, "routingDecision.attestation"));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        try
        {
            // The producer signs the canonical, attestation-free decision with isPreHashed:false,
            // i.e. over SHA-256 of the signable bytes — mirror that exactly for verification.
            var signableHash = _hashProvider.ComputeHash(decision.ComputeSignableBytes(), HashType.SHA256);
            var signatureBytes = Convert.FromBase64String(attestation.Signature);
            var verifyResult = await _cryptoModule.VerifyAsync(
                signatureBytes, signableHash, (byte)network, signer.PublicKey, ct);

            if (verifyResult != CryptoStatus.Success)
            {
                errors.Add(CreateError("VAL_ROUTING_002",
                    "Routing attestation signature is invalid",
                    ValidationErrorCategory.Cryptographic, "routingDecision.attestation.signature"));
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            errors.Add(CreateError("VAL_ROUTING_002",
                $"Routing attestation could not be verified: {ex.Message}",
                ValidationErrorCategory.Cryptographic, "routingDecision.attestation.signature"));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return Ok();
    }

    /// <summary>
    /// Resolves the register's required routing-attestation strength (Feature 145 governance).
    /// Reads <c>routingAttestation</c> from the register's current control record; defaults to
    /// <see cref="Sorcha.Register.Models.AttestationKind.SenderSigned"/> when no roster service is
    /// wired or no policy is set.
    /// </summary>
    private async Task<Sorcha.Register.Models.AttestationKind> ResolveRequiredRoutingAttestationAsync(
        string registerId, CancellationToken ct)
    {
        if (_governanceRosterService is null)
        {
            return Sorcha.Register.Models.AttestationKind.SenderSigned;
        }

        try
        {
            var roster = await _governanceRosterService.GetCurrentRosterAsync(registerId, ct);
            return roster?.ControlRecord?.RoutingAttestation
                ?? Sorcha.Register.Models.AttestationKind.SenderSigned;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not resolve routingAttestation policy for register {RegisterId}; defaulting to SenderSigned",
                registerId);
            return Sorcha.Register.Models.AttestationKind.SenderSigned;
        }
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> ValidateChainAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("Chain");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        // Config check: skip if disabled
        if (!_config.EnableChainValidation)
        {
            _logger.LogDebug("Chain validation disabled by configuration");
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        try
        {
            // 1. Transaction-level chain validation
            var previousTxId = transaction.PreviousTransactionId;
            if (!string.IsNullOrWhiteSpace(previousTxId))
            {
                using var _predecessorScope = RuleTelemetry.TimeRule("VAL_CHAIN_PREDECESSOR_LOOKUP");
                // Cached predecessor lookup — sealed register transactions are
                // immutable, so the L1+L2 cache (Redis + local) shaves the
                // MongoDB roundtrip out of the hot path for repeat lookups
                // within a docket batch. Falls through to direct fetch when
                // the cache is not wired up (legacy DI / tests).
                var previousTx = _chainTxCache is not null
                    ? await _chainTxCache.GetOrFetchAsync(
                        transaction.RegisterId, previousTxId,
                        (reg, tx, token) => _registerClient.GetTransactionAsync(reg, tx, token), ct)
                    : await _registerClient.GetTransactionAsync(
                        transaction.RegisterId, previousTxId, ct);

                if (previousTx == null)
                {
                    errors.Add(CreateError("VAL_CHAIN_001",
                        $"Previous transaction '{previousTxId}' not found in register '{transaction.RegisterId}'",
                        ValidationErrorCategory.Chain, "PreviousTransactionId"));
                }
                else if (!string.Equals(previousTx.RegisterId, transaction.RegisterId, StringComparison.Ordinal))
                {
                    errors.Add(CreateError("VAL_CHAIN_002",
                        $"Previous transaction '{previousTxId}' belongs to register '{previousTx.RegisterId}', expected '{transaction.RegisterId}'",
                        ValidationErrorCategory.Chain, "PreviousTransactionId"));
                }

                // 3. Fork detection — check if other transactions already reference the same predecessor.
                // Control transactions (genesis, blueprint-publish) are expected to have multiple
                // children — each workflow instance forks from its blueprint publish TX by design.
                if (previousTx != null)
                {
                    // Multi-child predecessors: genesis (Control) and blueprint-publish (post-#876
                    // BlueprintPublish, pre-#876 Control). Every workflow instance forks from its
                    // blueprint publish TX by design — without this bypass each new instance
                    // triggers a spurious VAL_CHAIN_FORK. When MetaData is null (legacy / genesis
                    // round-trip shape failures), treat as the multi-child case to avoid the same
                    // false fork — non-control transactions always have MetaData populated.
                    var isControlTx = previousTx.MetaData == null
                        || previousTx.MetaData.TransactionType == Sorcha.Register.Models.Enums.TransactionType.Control
                        || previousTx.MetaData.TransactionType == Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish;
                    if (!isControlTx)
                    {
                        using var _forkScope = RuleTelemetry.TimeRule(ValidationErrorCodes.ChainFork);
                        // Fetch up to 50 existing successors so we can distinguish an
                        // idempotent resubmission (same TxId already present) from a
                        // genuine fork (different TxId with the same parent). Without
                        // this dedup, a retry after a transient confirmation failure
                        // gets a spurious VAL_CHAIN_FORK even though the canonical
                        // transaction bytes are identical.
                        var existingSuccessors = await _registerClient.GetTransactionsByPrevTxIdAsync(
                            transaction.RegisterId, previousTxId, 1, 50, ct);

                        if (existingSuccessors.Total > 0)
                        {
                            var isIdempotentResubmission = existingSuccessors.Transactions?.Any(t =>
                                string.Equals(t.TxId, transaction.TransactionId, StringComparison.OrdinalIgnoreCase)) == true;

                            if (!isIdempotentResubmission)
                            {
                                errors.Add(CreateError(ValidationErrorCodes.ChainFork,
                                    $"Fork detected: {existingSuccessors.Total} existing transaction(s) already reference previous transaction '{previousTxId}' in register '{transaction.RegisterId}'",
                                    ValidationErrorCategory.Chain, "PreviousTransactionId"));
                            }
                            else
                            {
                                _logger.LogInformation(
                                    "Idempotent resubmission of transaction {TxId} against previous {PrevTxId} in register {RegisterId} — already present, skipping fork error",
                                    transaction.TransactionId, previousTxId, transaction.RegisterId);
                            }
                        }
                    }
                }
            }

            // 2. Docket-level chain validation
            using var _docketScope = RuleTelemetry.TimeRule("VAL_CHAIN_DOCKET");
            var height = await _registerClient.GetRegisterHeightAsync(transaction.RegisterId, ct);

            if (height > 0)
            {
                var latestDocket = await _registerClient.ReadDocketAsync(
                    transaction.RegisterId, height, ct);

                if (latestDocket != null && height > 1)
                {
                    var predecessorDocket = await _registerClient.ReadDocketAsync(
                        transaction.RegisterId, height - 1, ct);

                    if (predecessorDocket == null)
                    {
                        errors.Add(CreateError("VAL_CHAIN_003",
                            $"Docket gap detected: docket {height - 1} not found in register '{transaction.RegisterId}'",
                            ValidationErrorCategory.Chain, "DocketNumber"));
                    }
                    else
                    {
                        // Verify hash linkage
                        if (!string.Equals(latestDocket.PreviousHash, predecessorDocket.DocketHash, StringComparison.Ordinal))
                        {
                            errors.Add(CreateError("VAL_CHAIN_004",
                                $"Docket hash chain broken: docket {height} PreviousHash does not match docket {height - 1} DocketHash",
                                ValidationErrorCategory.Chain, "DocketHash"));
                        }

                        // Verify sequential numbering
                        if (latestDocket.DocketNumber != predecessorDocket.DocketNumber + 1)
                        {
                            errors.Add(CreateError("VAL_CHAIN_003",
                                $"Docket numbering gap: expected {predecessorDocket.DocketNumber + 1}, found {latestDocket.DocketNumber}",
                                ValidationErrorCategory.Chain, "DocketNumber"));
                        }
                    }
                }
            }

            _logger.LogDebug(
                "Chain validation for transaction {TransactionId} in register {RegisterId}: {ErrorCount} errors",
                transaction.TransactionId, transaction.RegisterId, errors.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Register Service unavailable during chain validation for transaction {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_CHAIN_TRANSIENT",
                $"Register Service unavailable: {ex.Message}",
                ValidationErrorCategory.Chain, isFatal: false));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Register Service timed out during chain validation for transaction {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_CHAIN_TRANSIENT",
                $"Register Service timed out: {ex.Message}",
                ValidationErrorCategory.Chain, isFatal: false));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <inheritdoc/>
    public ValidationEngineStats GetStats()
    {
        double avgDuration = 0;
        lock (_statsLock)
        {
            if (_durations.Count > 0)
            {
                avgDuration = _durations.Average();
            }
        }

        return new ValidationEngineStats
        {
            TotalValidated = Interlocked.Read(ref _totalValidated),
            TotalSuccessful = Interlocked.Read(ref _totalSuccessful),
            TotalFailed = Interlocked.Read(ref _totalFailed),
            AverageValidationDuration = TimeSpan.FromMilliseconds(avgDuration),
            ErrorsByCategory = new Dictionary<ValidationErrorCategory, long>(_errorsByCategory),
            InProgress = _inProgress
        };
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> ValidateBlueprintConformanceAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("BlueprintConformance");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        // Skip for genesis/control transactions
        if (TransactionTypeClassifier.IsGenesisOrControlTransaction(transaction))
        {
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        // Skip for participant transactions (no blueprint context)
        if (TransactionTypeClassifier.IsParticipantTransaction(transaction))
        {
            _logger.LogDebug("Skipping blueprint conformance for participant transaction {TransactionId}",
                transaction.TransactionId);
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        // Skip for rejection transactions (payload contains rejection metadata, not action data)
        if (TransactionTypeClassifier.IsRejectionTransaction(transaction))
        {
            _logger.LogDebug("Skipping blueprint conformance for rejection transaction {TransactionId}",
                transaction.TransactionId);
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        try
        {
            // Blueprint + action lookup (reuse logic from ValidateSchemaAsync)
            BlueprintModel? blueprint;
            using (RuleTelemetry.TimeRule("VAL_BP_RESOLVE"))
            {
                blueprint = await ResolveBlueprintAsync(transaction.BlueprintId!, ct);
            }
            if (blueprint == null)
            {
                errors.Add(CreateError("VAL_SCHEMA_001",
                    $"Blueprint '{transaction.BlueprintId}' not found",
                    ValidationErrorCategory.Blueprint, "BlueprintId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            if (!int.TryParse(transaction.ActionId, out var actionIdInt))
            {
                errors.Add(CreateError("VAL_SCHEMA_002",
                    $"Invalid action ID format: '{transaction.ActionId}'",
                    ValidationErrorCategory.Blueprint, "ActionId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
            if (action == null)
            {
                errors.Add(CreateError("VAL_SCHEMA_003",
                    $"Action {transaction.ActionId} not found in blueprint '{transaction.BlueprintId}'",
                    ValidationErrorCategory.Blueprint, "ActionId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // 1. Starting action validation
            if (string.IsNullOrWhiteSpace(transaction.PreviousTransactionId))
            {
                if (!action.IsStartingAction)
                {
                    errors.Add(CreateError("VAL_BP_001",
                        $"Action {actionIdInt} is not a starting action but has no previous transaction",
                        ValidationErrorCategory.Blueprint, "ActionId"));
                }
            }

            // 2. Sender authorization — derive wallet from signature and compare to participant
            //    Starting actions accept any wallet (binding happens in ActionExecutionService).
            //    Non-starting actions validate against blueprint wallet or instance bindings.
            if (action.IsStartingAction)
            {
                _logger.LogDebug(
                    "Starting action {ActionId}: accepting any wallet (binding at execution time)",
                    actionIdInt);
            }
            else if (transaction.Signatures.Count > 0)
            {
                using var _bp002Scope = RuleTelemetry.TimeRule("VAL_BP_002");
                var firstSig = transaction.Signatures[0];

                if (AlgorithmMapper.TryParseAlgorithm(firstSig.Algorithm, out var sigNetwork))
                {
                    var derivedWallet = _walletUtilities.PublicKeyToWallet(firstSig.PublicKey, (byte)sigNetwork);
                    var participant = blueprint.Participants.FirstOrDefault(p =>
                        string.Equals(p.Id, action.Sender, StringComparison.OrdinalIgnoreCase));

                    if (participant != null && !string.IsNullOrWhiteSpace(participant.WalletAddress))
                    {
                        // Tier 1: Match against blueprint-hardcoded wallet address
                        if (!string.Equals(derivedWallet, participant.WalletAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add(CreateError("VAL_BP_002",
                                $"Signer wallet {derivedWallet} does not match authorized sender {participant.WalletAddress} for action {actionIdInt}",
                                ValidationErrorCategory.Permission, "Signatures"));
                        }
                    }
                    else if (participant != null && string.IsNullOrWhiteSpace(participant.WalletAddress))
                    {
                        // Tier 2: Participant has no hardcoded wallet — resolve from register
                        var resolvedRecord = await _registerClient.ResolveParticipantAsync(
                            transaction.RegisterId,
                            participant.Id,
                            participant.Organisation,
                            ct);

                        if (resolvedRecord != null && resolvedRecord.Addresses.Count > 0)
                        {
                            var walletMatch = resolvedRecord.Addresses.Any(a =>
                                string.Equals(a.WalletAddress, derivedWallet, StringComparison.OrdinalIgnoreCase));

                            if (!walletMatch)
                            {
                                errors.Add(CreateError("VAL_BP_002",
                                    $"Signer wallet {derivedWallet} not in published addresses for participant '{participant.Id}' on action {actionIdInt}",
                                    ValidationErrorCategory.Permission, "Signatures"));
                            }
                            else
                            {
                                _logger.LogDebug(
                                    "Participant {ParticipantId} resolved from register — wallet {Wallet} matches published address",
                                    participant.Id, derivedWallet);
                            }
                        }
                        else
                        {
                            // Tier 3: chain-derived late-binding. Before failing hard, walk
                            // the in-instance transaction chain. If an earlier tx in this
                            // instance was signed for the same participant role, its signing
                            // wallet IS the late-binding — authoritative because it's
                            // on-ledger and signed. This keeps "open participant" blueprints
                            // (public citizen, applicant, procurement-mgr on its own flow)
                            // working without an auto-publish side-effect on late-bind.
                            var chainWallet = await ResolveChainBoundWalletAsync(
                                transaction, action.Sender, blueprint, ct);

                            if (chainWallet != null)
                            {
                                if (string.Equals(chainWallet, derivedWallet, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger.LogDebug(
                                        "Participant {ParticipantId} chain-bound to wallet {Wallet} from prior in-instance tx — matches current signer",
                                        action.Sender, derivedWallet);
                                }
                                else
                                {
                                    // Immutable-binding violation (FR-004): a prior in-instance tx
                                    // bound this participant role to a different wallet.
                                    errors.Add(CreateError("VAL_BP_002",
                                        $"Signer wallet {derivedWallet} does not match chain-derived binding {chainWallet} for participant '{action.Sender}' (late-bound on an earlier action in this instance)",
                                        ValidationErrorCategory.Permission, "Signatures"));
                                }
                            }
                            else
                            {
                                // SEC-AUDIT 4.8: no Tier 1/2 record AND no Tier 3 chain
                                // binding — nothing on-ledger authorises this submitter.
                                // NB: if ResolveChainBoundWalletAsync logged a prior
                                // warning (transient register-service failure), the error
                                // message hint below flags the degraded-query case so an
                                // operator can correlate the two logs.
                                _logger.LogWarning(
                                    "Participant {ParticipantId} has no wallet, no published record, and no prior in-instance binding on register {RegisterId} — rejecting transaction for action {ActionId}",
                                    action.Sender, transaction.RegisterId, actionIdInt);
                                errors.Add(CreateError("VAL_BP_002",
                                    $"Cannot verify sender authorization: participant '{action.Sender}' has no wallet address, no published record on register '{transaction.RegisterId}', and no prior in-instance binding. (Chain lookup degraded? See preceding validator warnings.)",
                                    ValidationErrorCategory.Permission, "Signatures"));
                            }
                        }
                    }
                }
            }

            // 3. Action sequencing — if PreviousTransactionId is set, validate route reachability.
            //    Feature 119: presentation-outcome and presentation-abandoned are intra-action
            //    lifecycle events that chain off the same action's presentation-initiated.
            //    Their predecessor carries the same ActionId, which would otherwise trip
            //    VAL_BP_003 reflexively (action N is not reachable from action N). Skip the
            //    route check for these tx types — chain integrity is still enforced by
            //    VAL_CHAIN_001 / VAL_CHAIN_FORK; only the workflow-routing check is bypassed.
            var isIntraActionLifecycleTx = TransactionTypeClassifier.IsIntraActionLifecycleTerminal(transaction);

            if (!isIntraActionLifecycleTx && !string.IsNullOrWhiteSpace(transaction.PreviousTransactionId))
            {
                using var _bp003Scope = RuleTelemetry.TimeRule("VAL_BP_003");
                // Same predecessor lookup as the chain section — reuse the cache so
                // repeated route-reachability checks within a docket don't double-fetch.
                var previousTx = _chainTxCache is not null
                    ? await _chainTxCache.GetOrFetchAsync(
                        transaction.RegisterId, transaction.PreviousTransactionId,
                        (reg, tx, token) => _registerClient.GetTransactionAsync(reg, tx, token), ct)
                    : await _registerClient.GetTransactionAsync(
                        transaction.RegisterId, transaction.PreviousTransactionId, ct);

                if (previousTx?.MetaData?.ActionId != null)
                {
                    var previousActionId = (int)previousTx.MetaData.ActionId.Value;
                    var previousAction = blueprint.Actions.FirstOrDefault(a => a.Id == previousActionId);

                    if (previousAction != null)
                    {
                        var routes = previousAction.Routes?.ToList();
                        if (routes != null && routes.Count > 0)
                        {
                            var reachableActionIds = routes
                                .SelectMany(r => r.NextActionIds)
                                .ToHashSet();

                            // Also check rejection routing
                            if (previousAction.RejectionConfig != null)
                            {
                                reachableActionIds.Add(previousAction.RejectionConfig.TargetActionId);
                            }

                            if (!reachableActionIds.Contains(actionIdInt))
                            {
                                errors.Add(CreateError("VAL_BP_003",
                                    $"Action {actionIdInt} is not reachable from action {previousActionId} via blueprint routes",
                                    ValidationErrorCategory.Blueprint, "ActionId"));
                            }
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Previous action {PreviousActionId} has no routes defined, skipping sequence check for action {ActionId}",
                                previousActionId, actionIdInt);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "Previous transaction {PrevTxId} missing ActionId in metadata, skipping sequence check",
                        transaction.PreviousTransactionId);
                }
            }

            _logger.LogDebug(
                "Blueprint conformance validation for transaction {TransactionId}: {ErrorCount} errors",
                transaction.TransactionId, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating blueprint conformance for transaction {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_BP_ERR",
                $"Blueprint conformance validation error: {ex.Message}",
                ValidationErrorCategory.Blueprint, isFatal: false));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    private ValidationEngineResult ValidateParticipantSchema(
        Transaction transaction,
        Stopwatch sw)
    {
        using var _section = RuleTelemetry.TimeSection("ParticipantSchema");
        var errors = new List<ValidationEngineError>();

        try
        {
            var schema = GetParticipantSchema();
            var result = schema.Evaluate(transaction.Payload, new Json.Schema.EvaluationOptions
            {
                OutputFormat = Json.Schema.OutputFormat.List
            });

            if (!result.IsValid)
            {
                var details = result.Details?
                    .Where(d => !d.IsValid && d.Errors != null)
                    .SelectMany(d => d.Errors!)
                    .Select(e => $"{e.Key}: {e.Value}")
                    .ToList() ?? [];

                var message = details.Count > 0
                    ? $"Participant record schema validation failed: {string.Join("; ", details.Take(5))}"
                    : "Participant record schema validation failed";

                errors.Add(CreateError("VAL_PARTICIPANT_001", message,
                    ValidationErrorCategory.Schema, "Payload", true));

                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            _logger.LogDebug("Participant record schema validation passed for {TransactionId}",
                transaction.TransactionId);

            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating participant record schema for {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_PARTICIPANT_ERR",
                $"Participant record schema validation error: {ex.Message}",
                ValidationErrorCategory.Schema, "Payload", true));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }
    }


    // Transaction-type predicates moved to TransactionTypeClassifier (post-Feature 119
    // rule-base cleanup). All call sites in this engine route through the classifier.

    /// <summary>
    /// Validates a revocation transaction: checks target exists, not already revoked,
    /// target is not itself a revocation, and revoker is authorised.
    /// </summary>
    private async Task<ValidationEngineResult> ValidateRevocationAsync(
        Transaction transaction,
        CancellationToken ct)
    {
        using var _section = RuleTelemetry.TimeSection("Revocation");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        try
        {
            // Parse revocation payload
            Register.Models.RevocationPayload? revocationPayload;
            try
            {
                revocationPayload = JsonSerializer.Deserialize<Register.Models.RevocationPayload>(
                    transaction.Payload.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                errors.Add(CreateError(ValidationErrorCodes.RevocationInvalid,
                    $"Invalid revocation payload: {ex.Message}",
                    ValidationErrorCategory.Structure, "Payload"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            if (revocationPayload == null)
            {
                errors.Add(CreateError(ValidationErrorCodes.RevocationInvalid,
                    "Revocation payload is null",
                    ValidationErrorCategory.Structure, "Payload"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Validate payload structure using RevocationValidator
            var validator = new Sorcha.Validator.Core.RevocationValidator();
            var payloadResult = validator.ValidatePayload(revocationPayload);
            if (!payloadResult.IsValid)
            {
                foreach (var err in payloadResult.Errors)
                {
                    errors.Add(CreateError(payloadResult.ErrorCode ?? ValidationErrorCodes.RevocationInvalid,
                        err, ValidationErrorCategory.Structure, "Payload"));
                }
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Check target transaction exists
            using var _rev002Scope = RuleTelemetry.TimeRule("VAL_REV_002");
            var targetTx = await _registerClient.GetTransactionAsync(
                transaction.RegisterId, revocationPayload.OriginalTxId, ct);
            if (targetTx == null)
            {
                errors.Add(CreateError("VAL_REV_002",
                    $"Target transaction {revocationPayload.OriginalTxId} not found",
                    ValidationErrorCategory.Structure, "OriginalTxId"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Check target is not itself a revocation transaction
            if (targetTx.MetaData?.TransactionType == Register.Models.Enums.TransactionType.Revocation)
            {
                errors.Add(CreateError("VAL_REV_004",
                    "Cannot revoke a revocation transaction",
                    ValidationErrorCategory.Structure, "OriginalTxId"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Check not already revoked (cheap DB query — run before expensive authority check)
            using var _rev003Scope = RuleTelemetry.TimeRule("VAL_REV_003");
            var existingRevocations = await _registerClient.GetTransactionsByPrevTxIdAsync(
                transaction.RegisterId, revocationPayload.OriginalTxId, 1, 10, ct);
            var existingRevocation = existingRevocations?.Transactions?.FirstOrDefault(t =>
                t.MetaData?.TransactionType == Register.Models.Enums.TransactionType.Revocation);
            if (existingRevocation != null)
            {
                errors.Add(CreateError("VAL_REV_003",
                    $"Transaction {revocationPayload.OriginalTxId} is already revoked by {existingRevocation.TxId}",
                    ValidationErrorCategory.Structure, "OriginalTxId"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Check authority: revoker must be original signer or governance roster Owner/Admin
            var revokerWallet = transaction.Signatures?.FirstOrDefault()?.SignedBy;
            var targetSender = targetTx.SenderWallet;

            if (!string.IsNullOrEmpty(revokerWallet) && !string.IsNullOrEmpty(targetSender) &&
                !string.Equals(revokerWallet, targetSender, StringComparison.OrdinalIgnoreCase))
            {
                using var _rev005Scope = RuleTelemetry.TimeRule("VAL_REV_005");
                // Not the original signer — check governance roster for Owner/Admin role
                var govResult = await _rightsEnforcementService.ValidateGovernanceRightsAsync(transaction, ct);
                if (!govResult.IsValid)
                {
                    errors.Add(CreateError("VAL_REV_005",
                        "Revoker is neither the original transaction signer nor a governance roster Owner/Admin",
                        ValidationErrorCategory.Structure, "Signature"));
                    return CreateFailureResult(transaction, sw.Elapsed, errors);
                }
            }

            _logger.LogDebug(
                "Revocation validation passed for transaction {TxId} revoking {TargetTxId}",
                transaction.TransactionId, revocationPayload.OriginalTxId);

            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Error validating revocation transaction {TxId}", transaction.TransactionId);
            errors.Add(CreateError("VAL_REV_ERR",
                $"Revocation validation error: {ex.Message}",
                ValidationErrorCategory.Structure, "Payload", true));
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }
    }

    private static Json.Schema.JsonSchema? _participantSchema;
    private static readonly object _schemaLock = new();

    /// <summary>
    /// Content-addressed cache of parsed blueprint action data schemas.
    /// <para>
    /// JsonSchema.Net's <see cref="Json.Schema.JsonSchema.FromText(string)"/> eagerly
    /// registers any sub-schema with an <c>$id</c> in the process-global
    /// <c>SchemaRegistry.Global</c>. Feature 103 wave 6's <c>SchemaRefResolver</c> flattens
    /// the core identity primitives (<c>PersonName.v1</c>, <c>DateOfBirth.v1</c>,
    /// <c>EmailAddress.v1</c>, <c>PostalAddress.v1</c>) into action schemas, and those
    /// primitives carry stable <c>$id</c> URIs. Parsing the same flattened schema twice
    /// from two different transactions therefore trips the library's
    /// "Overwriting registered schemas is not permitted" error.
    /// </para>
    /// <para>
    /// This cache guarantees <see cref="Json.Schema.JsonSchema.FromText(string)"/> is called
    /// exactly once per unique schema text within a validator process lifetime. The key is
    /// a SHA-256 of the stripped schema text, so republishes with changed content
    /// naturally miss the cache and get a fresh parse. The value is wrapped in
    /// <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
    /// to guarantee exactly-once parsing even under concurrent transaction validation.
    /// </para>
    /// <para>
    /// The cache is intentionally unbounded. In practice the number of distinct action
    /// schemas in any validator process is bounded by the set of live blueprint versions,
    /// which is small and finite. If this ever becomes a memory concern, add a size cap
    /// with LRU eviction keyed by last-access time.
    /// </para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Json.Schema.JsonSchema>> _actionSchemaCache = new();

    internal static Json.Schema.JsonSchema GetOrParseActionSchema(string schemaText)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(schemaText));
        var key = Convert.ToHexString(hashBytes);
        var lazy = _actionSchemaCache.GetOrAdd(key, _ => new Lazy<Json.Schema.JsonSchema>(
            () => Json.Schema.JsonSchema.FromText(schemaText),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private static Json.Schema.JsonSchema GetParticipantSchema()
    {
        if (_participantSchema != null) return _participantSchema;

        lock (_schemaLock)
        {
            if (_participantSchema != null) return _participantSchema;

            var assembly = typeof(ValidationEngine).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "Sorcha.Validator.Service.Schemas.participant-record-v1.json")
                ?? throw new InvalidOperationException("Participant record schema not found as embedded resource");

            using var reader = new StreamReader(stream);
            var schemaJson = reader.ReadToEnd();
            _participantSchema = Json.Schema.JsonSchema.FromText(schemaJson);
            return _participantSchema;
        }
    }

    /// <summary>
    /// Canonical JSON serializer options for deterministic payload hashing.
    /// MUST match the options used by Blueprint Service (TransactionBuilderServiceExtensions)
    /// and Validator Core (TransactionValidator).
    /// Contract: compact, no property renaming, UnsafeRelaxedJsonEscaping (no \u002B for +).
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private ValidationEngineResult ValidatePayloadHash(Transaction transaction)
    {
        using var _section = RuleTelemetry.TimeSection("PayloadHash");
        var sw = Stopwatch.StartNew();

        try
        {
            // Re-canonicalize the payload through deterministic serializer options.
            // This ensures hash verification is independent of how the JSON arrived
            // (HTTP encoding, Redis round-trip, etc.) — only the logical data matters.
            var payloadJson = JsonSerializer.Serialize(transaction.Payload, CanonicalJsonOptions);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            var computedHash = _hashProvider.ComputeHash(payloadBytes, HashType.SHA256);
            var computedHashHex = Convert.ToHexString(computedHash).ToLowerInvariant();

            if (!string.Equals(computedHashHex, transaction.PayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                return CreateFailureResult(transaction, sw.Elapsed,
                [
                    CreateError("VAL_HASH_001",
                        $"Payload hash mismatch. Expected: {transaction.PayloadHash}, Computed: {computedHashHex}",
                        ValidationErrorCategory.Cryptographic, "PayloadHash", true)
                ]);
            }
        }
        catch (Exception ex)
        {
            return CreateFailureResult(transaction, sw.Elapsed,
            [
                CreateError("VAL_HASH_ERR",
                    $"Error computing payload hash: {ex.Message}",
                    ValidationErrorCategory.Cryptographic, "PayloadHash", true)
            ]);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    private ValidationEngineResult ValidateTiming(Transaction transaction)
    {
        using var _section = RuleTelemetry.TimeSection("Timing");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();
        var now = DateTimeOffset.UtcNow;

        // Check for future timestamps
        if (transaction.CreatedAt > now.Add(_config.MaxClockSkew))
        {
            errors.Add(CreateError("VAL_TIME_001",
                "Transaction timestamp is in the future",
                ValidationErrorCategory.Timing, "CreatedAt"));
        }

        // Check for expired transactions.
        //
        // The genesis transaction gets its own (short) freshness window rather than the
        // live-transaction window. SECURITY: a pre-signed genesis is an offline ceremony
        // artifact; if a stale genesis stayed valid forever it would be a replay vector — an
        // attacker could push an old/superseded genesis to seed or hijack a bootstrapping node.
        // Bounding it (GenesisMaxAge, default 1h) forces a regenerated system register to be
        // minted, embedded, deployed, and bootstrapped within the hour. This guards the
        // ingest-and-seal path (Auto bootstrap); a node that pulls an already-sealed genesis
        // docket from a peer verifies the sealed docket (validator signature + chain), not the
        // genesis tx's age, so late-joining SyncOnly replicas are unaffected.
        var isGenesis = TransactionTypeClassifier.IsGenesisTransaction(transaction);
        var maxAge = isGenesis ? _config.GenesisMaxAge : _config.MaxTransactionAge;
        if (transaction.CreatedAt < now.Subtract(maxAge))
        {
            errors.Add(CreateError("VAL_TIME_002",
                isGenesis
                    ? $"Genesis transaction is too old (max age: {maxAge}); mint, deploy, and bootstrap within this window"
                    : $"Transaction is too old (max age: {maxAge})",
                ValidationErrorCategory.Timing, "CreatedAt"));
        }

        // Check explicit expiration
        if (transaction.ExpiresAt.HasValue && transaction.ExpiresAt.Value <= now)
        {
            errors.Add(CreateError("VAL_TIME_003",
                "Transaction has expired",
                ValidationErrorCategory.Timing, "ExpiresAt"));
        }

        if (errors.Count > 0)
        {
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }


    /// <summary>
    /// Validates the structural integrity of any <c>file-reference</c> fields present in
    /// the transaction payload.
    /// </summary>
    /// <remarks>
    /// This method performs <em>structural</em> validation only: required fields, hash
    /// format (<c>sha256:&lt;hex&gt;</c>), chunk-count and size constraints from the
    /// platform limits and any <c>x-file</c> schema extension.  It does NOT fetch or
    /// verify individual chunk transactions — that full per-chunk integrity check is
    /// deferred to docket-sealing time where all referenced chunk transactions are
    /// available locally without additional network round-trips.
    ///
    /// Encrypted transactions are skipped because the payload ciphertext is opaque and
    /// the Blueprint Service already validated it before encryption.
    /// </remarks>
    private ValidationEngineResult ValidateFileReferences(Transaction transaction)
    {
        using var _section = RuleTelemetry.TimeSection("FileReferences");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();

        try
        {
            var payload = transaction.Payload;

            // Skip opaque encrypted payloads — Blueprint Service validates before encryption.
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("contentEncoding", out var enc) &&
                enc.ValueKind == JsonValueKind.String &&
                enc.GetString() == "encrypted")
            {
                return ValidationEngineResult.Success(
                    transaction.TransactionId, transaction.RegisterId, sw.Elapsed);
            }

            // Work on the user data envelope (same extraction logic as ValidateSchemaAsync).
            var payloadToScan = payload;
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("payloads", out var payloadsEl) &&
                payloadsEl.ValueKind == JsonValueKind.Object)
            {
                using var en = payloadsEl.EnumerateObject();
                if (en.MoveNext())
                    payloadToScan = en.Current.Value;
            }

            if (payloadToScan.ValueKind != JsonValueKind.Object)
                return ValidationEngineResult.Success(
                    transaction.TransactionId, transaction.RegisterId, sw.Elapsed);

            // Walk every property and validate those that look like file references
            // (contain a "chunkTransactionIds" array) or explicit null/object markers.
            // Supports both scalar file-reference objects and array-typed file fields.
            foreach (var property in payloadToScan.EnumerateObject())
            {
                var value = property.Value;

                if (value.ValueKind == JsonValueKind.Null ||
                    value.ValueKind == JsonValueKind.Undefined)
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    // Heuristic: a file reference object always has "chunkTransactionIds".
                    if (!value.TryGetProperty("chunkTransactionIds", out _))
                        continue;

                    var fieldPath = $"/{property.Name}";
                    ValidateFileReferenceObject(value, fieldPath, errors);
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    // Array file fields: validate each item that looks like a file reference.
                    int index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("chunkTransactionIds", out _))
                        {
                            var itemPath = $"/{property.Name}/{index}";
                            ValidateFileReferenceObject(item, itemPath, errors);
                        }
                        index++;
                    }
                }
            }

            _logger.LogDebug(
                "File reference validation for transaction {TransactionId}: {ErrorCount} error(s)",
                transaction.TransactionId, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file reference validation for transaction {TransactionId}",
                transaction.TransactionId);
            errors.Add(CreateError("VAL_FILE_ERR",
                $"File reference validation error: {ex.Message}",
                ValidationErrorCategory.Schema));
        }

        return errors.Count > 0
            ? CreateFailureResult(transaction, sw.Elapsed, errors)
            : ValidationEngineResult.Success(transaction.TransactionId, transaction.RegisterId, sw.Elapsed);
    }

    /// <summary>
    /// Validates the structural integrity of a single file-reference JSON object, accumulating
    /// any errors into <paramref name="errors"/>. Called for both scalar and array-item file fields.
    /// </summary>
    /// <param name="value">The <see cref="JsonElement"/> expected to be a file-reference object.</param>
    /// <param name="fieldPath">JSON Pointer path used in error messages (e.g. <c>/attachment</c> or <c>/attachments/0</c>).</param>
    /// <param name="errors">Accumulator for validation errors.</param>
    private void ValidateFileReferenceObject(
        JsonElement value,
        string fieldPath,
        List<ValidationEngineError> errors)
    {
        // --- Required fields ---
        foreach (var required in FileReferenceRequiredFields)
        {
            if (!value.TryGetProperty(required, out _))
            {
                errors.Add(CreateError("VAL_FILE_001",
                    $"File reference at '{fieldPath}' is missing required field \"{required}\".",
                    ValidationErrorCategory.Schema, fieldPath));
            }
        }

        // --- Hash format: "sha256:" + 64 lowercase hex chars ---
        if (value.TryGetProperty("hash", out var hashEl) &&
            hashEl.ValueKind == JsonValueKind.String)
        {
            var hash = hashEl.GetString() ?? string.Empty;
            if (!IsValidFileReferenceHash(hash))
            {
                errors.Add(CreateError("VAL_FILE_002",
                    $"File reference at '{fieldPath}' has an invalid hash format. " +
                    "Expected \"sha256:\" followed by 64 lowercase hexadecimal characters.",
                    ValidationErrorCategory.Schema, $"{fieldPath}/hash"));
            }
        }

        // --- chunkTransactionIds bounds (1–10 platform limit) ---
        long fileSize = 0;
        if (value.TryGetProperty("chunkTransactionIds", out var chunksEl) &&
            chunksEl.ValueKind == JsonValueKind.Array)
        {
            var chunkCount = chunksEl.GetArrayLength();
            if (chunkCount < 1 || chunkCount > Sorcha.Blueprint.Models.FileSchemaExtension.PlatformMaxChunks)
            {
                errors.Add(CreateError("VAL_FILE_003",
                    $"File reference at '{fieldPath}' has {chunkCount} chunk transaction ID(s); " +
                    $"must be between 1 and {Sorcha.Blueprint.Models.FileSchemaExtension.PlatformMaxChunks}.",
                    ValidationErrorCategory.Schema, $"{fieldPath}/chunkTransactionIds"));
            }
        }

        // --- Size > 0 and within platform limit ---
        if (value.TryGetProperty("size", out var sizeEl) &&
            sizeEl.TryGetInt64(out fileSize))
        {
            if (fileSize <= 0)
            {
                errors.Add(CreateError("VAL_FILE_004",
                    $"File reference at '{fieldPath}' has an invalid size ({fileSize}); must be > 0.",
                    ValidationErrorCategory.Schema, $"{fieldPath}/size"));
            }
            else if (fileSize > Sorcha.Blueprint.Models.FileSchemaExtension.PlatformMaxSizeBytes)
            {
                errors.Add(CreateError("VAL_FILE_005",
                    $"File reference at '{fieldPath}' declares size {fileSize} bytes which exceeds " +
                    $"the platform maximum of {Sorcha.Blueprint.Models.FileSchemaExtension.PlatformMaxSizeBytes / (1024 * 1024)} MB.",
                    ValidationErrorCategory.Schema, $"{fieldPath}/size"));
            }
        }
    }

    /// <summary>Required top-level fields on a file-reference JSON object.</summary>
    private static readonly string[] FileReferenceRequiredFields =
    [
        "fileName",
        "contentType",
        "size",
        "hash",
        "salt",
        "chunkTransactionIds"
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> matches the
    /// <c>sha256:</c> prefix followed by exactly 64 lowercase hexadecimal characters.
    /// </summary>
    private static bool IsValidFileReferenceHash(string value)
    {
        const string Prefix = "sha256:";
        const int HexLength = 64;

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var hex = value.AsSpan(Prefix.Length);
        if (hex.Length != HexLength)
            return false;

        foreach (var ch in hex)
        {
            if (!Uri.IsHexDigit(ch))
                return false;
        }

        return true;
    }

    private static ValidationEngineError CreateError(
        string code,
        string message,
        ValidationErrorCategory category,
        string? field = null,
        bool isFatal = false)
    {
        // Gated emission counter — no-op when benchmark telemetry is disabled.
        // Captures every rule fire across the whole engine via the single
        // CreateError choke point. See bench/baseline-2026-05/README.md.
        RuleTelemetry.RuleEmitted(code);

        return new ValidationEngineError
        {
            Code = code,
            Message = message,
            Category = category,
            Field = field,
            IsFatal = isFatal
        };
    }

    /// <summary>
    /// Returns the schema JSON with any property whose name starts with "x-"
    /// recursively removed. Json.Schema rejects unknown keywords, but Sorcha
    /// blueprints embed UI-renderer hints (x-pages, x-sections, x-introduction,
    /// x-width, x-rule, x-persona, x-file) that must survive round-trips
    /// without interfering with schema validation.
    /// <para>
    /// Also strips <c>$id</c> everywhere. An action schema reaching the validator is a
    /// SELF-CONTAINED document: Blueprint Service's <c>SchemaRefResolver.Flatten</c> has already
    /// inlined every <c>https://schemas.sorcha.dev/core/*</c> reference, copying the primitive's own
    /// <c>$id</c> along with its body, and catalogue-derived schemas (DPP, FHIR, ISO 20022,
    /// schema.org) carry a root <c>$id</c> of their source URL. Identity keywords only matter for
    /// resolving references BETWEEN documents, which cannot happen here — but Json.Schema registers
    /// every identified schema it parses in a process-wide registry, so retaining them has two
    /// consequences and no benefit:
    /// </para>
    /// <list type="bullet">
    /// <item>The second blueprint to inline a shared primitive fails to parse outright —
    /// "Overwriting registered schemas is not permitted" — reported as VAL_SCHEMA_005 "Malformed
    /// JSON schema", which points the operator at a blueprint that is not malformed. Every
    /// submission to it is rejected and never seals. Found on n1 by the AssuredIdentity walkthrough
    /// (#1427), whose action 1 inlines four core primitives.</item>
    /// <item>Worse than the failure: had it not thrown, one blueprint's registered schema could
    /// silently resolve a DIFFERENT blueprint's dangling <c>$ref</c>, making validation depend on
    /// which blueprint the process happened to parse first.</item>
    /// </list>
    /// </summary>
    internal static string StripCustomExtensionKeywords(JsonElement root)
    {
        var node = JsonNode.Parse(root.GetRawText());
        StripXPrefixedKeysRecursive(node);
        EnsureDialectDeclared(node);
        return node?.ToJsonString() ?? "{}";
    }

    /// <summary>
    /// The dialect a schema is evaluated under. 2020-12 is what every Sorcha core primitive declares
    /// and what the engine has always evaluated against.
    /// </summary>
    private const string DefaultSchemaDialect = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>
    /// Declares the JSON Schema dialect at the document root when the document does not declare one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without a <c>$schema</c>, JsonSchema.Net parses under a strict default in which an unknown
    /// keyword is FATAL, not an annotation. Sorcha action schemas never declare a dialect themselves,
    /// so until #1445 the only declaration in the document was the one that rode in on an inlined
    /// core primitive. #1445 correctly stopped copying a component's <c>$id</c> (it collides in the
    /// process-wide registry) but took <c>$schema</c> with it — and <c>$schema</c> is not merely an
    /// identity keyword, it selects the vocabulary.
    /// </para>
    /// <para>
    /// The consequence was invisible until a blueprint inlined a primitive carrying a NON-CORE
    /// keyword. <c>DateOfBirth.v1</c> carries <c>"formatMaximum": "today"</c> (a Sorcha token), so
    /// every blueprint asking for a date of birth started failing
    /// <c>VAL_SCHEMA_005 "Unknown keywords (formatMaximum) are disallowed for this dialect"</c> —
    /// every submission refused, never sealed, behind an HTTP 202. Found on n1 2026-08-17.
    /// </para>
    /// <para>
    /// Normalising here rather than in <c>SchemaRefResolver</c> is deliberate: a blueprint already
    /// sealed on a ledger cannot be re-flattened, and the dialect a document is read under is an
    /// evaluation concern rather than part of the signed content. An explicit declaration is never
    /// overwritten — a schema that says which draft it is written in keeps it.
    /// </para>
    /// </remarks>
    private static void EnsureDialectDeclared(JsonNode? node)
    {
        if (node is JsonObject obj && !obj.ContainsKey("$schema"))
        {
            obj["$schema"] = DefaultSchemaDialect;
        }
    }

    private static void StripXPrefixedKeysRecursive(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var toRemove = obj
                    .Where(kvp => kvp.Key.StartsWith("x-", StringComparison.Ordinal)
                               || string.Equals(kvp.Key, "$id", StringComparison.Ordinal))
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in toRemove)
                {
                    obj.Remove(key);
                }

                // Relax the "type" constraint on file-reference fields. A field declared
                // { "type": "string", "format": "file-reference" } carries an OBJECT at
                // runtime (the FileReference: fileName/hash/chunkTransactionIds/... plus, for
                // F107 portrait capture, a tokenImageBase64 sibling) — never a plain string.
                // The JSON-Schema "type" check therefore spuriously fails VAL_SCHEMA_004
                // ("Value is object but should be string") whenever the payload is visible to
                // the validator (DevMode / publicly-disclosed registers; encrypted registers
                // skip schema validation entirely, which is why this stayed latent). Structural
                // integrity of file references is enforced separately by ValidateFileReferences,
                // so drop the "type" keyword here and let any value shape through.
                if (obj.TryGetPropertyValue("format", out var formatNode) &&
                    formatNode is JsonValue formatValue &&
                    formatValue.TryGetValue<string>(out var formatStr) &&
                    string.Equals(formatStr, "file-reference", StringComparison.Ordinal))
                {
                    obj.Remove("type");
                }

                foreach (var kvp in obj)
                {
                    StripXPrefixedKeysRecursive(kvp.Value);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    StripXPrefixedKeysRecursive(item);
                }
                break;
        }
    }

    private static ValidationEngineResult CreateFailureResult(
        Transaction transaction,
        TimeSpan duration,
        List<ValidationEngineError> errors)
    {
        return ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            duration,
            errors.ToArray());
    }

    private ValidationEngineResult RecordResult(ValidationEngineResult result, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalValidated);

        if (result.IsValid)
        {
            Interlocked.Increment(ref _totalSuccessful);
        }
        else
        {
            Interlocked.Increment(ref _totalFailed);

            // Track error categories
            foreach (var error in result.Errors)
            {
                _errorsByCategory.AddOrUpdate(error.Category, 1, (_, count) => count + 1);
            }
        }

        // Track duration (keep last 1000)
        lock (_statsLock)
        {
            _durations.Enqueue(duration.TotalMilliseconds);
            while (_durations.Count > 1000)
            {
                _durations.TryDequeue(out _);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a blueprint by ID, checking cache first then falling back to BlueprintFetcher.
    /// On successful fetch, the blueprint is cached for subsequent lookups.
    /// </summary>
    private async Task<BlueprintModel?> ResolveBlueprintAsync(string blueprintId, CancellationToken ct)
    {
        // Try cache first (L1 → L2)
        var blueprint = await _blueprintCache.GetBlueprintAsync(blueprintId, ct);
        if (blueprint != null)
            return blueprint;

        // Cache miss — try fetching from Blueprint Service
        if (_blueprintFetcher != null)
        {
            _logger.LogInformation(
                "Blueprint {BlueprintId} not in cache, fetching from Blueprint Service",
                blueprintId);

            try
            {
                blueprint = await _blueprintFetcher.FetchBlueprintAsync(blueprintId, ct);
                if (blueprint != null)
                {
                    // Populate cache for future lookups
                    await _blueprintCache.SetBlueprintAsync(blueprint, ct: ct);
                    _logger.LogInformation(
                        "Blueprint {BlueprintId} fetched and cached from Blueprint Service",
                        blueprintId);
                    return blueprint;
                }

                _logger.LogWarning("Blueprint {BlueprintId} not found in Blueprint Service", blueprintId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch blueprint {BlueprintId} from Blueprint Service", blueprintId);
            }
        }

        // Feature 189 (T054): last resort — the SYSTEM REGISTER.
        //
        // Blueprints the platform itself executes (register-governance-v1 and its siblings) are
        // seeded onto the SSR by SystemRegisterBootstrapper and are deliberately absent from the
        // Blueprint Service store, which rejects them with `no_provenance`. Resolution that stops at
        // the fetcher therefore cannot see them AT ALL — the first attempt at T054 failed every
        // governance transaction on every register with VAL_SCHEMA_001, live on n1, for exactly this
        // reason.
        //
        // Reading the SSR ledger works on every node holding a replica, including a subscriber that
        // seeded nothing itself, so this is not owner-only. It is tried last so it costs nothing on
        // the ordinary path.
        blueprint = await ResolveFromSystemRegisterAsync(blueprintId, ct);
        if (blueprint != null)
        {
            await _blueprintCache.SetBlueprintAsync(blueprint, ct: ct);
            _logger.LogInformation(
                "Blueprint {BlueprintId} resolved from the system register and cached", blueprintId);
            return blueprint;
        }

        return null;
    }

    /// <summary>
    /// Resolves a system-seeded blueprint from the system register's ledger.
    /// </summary>
    /// <remarks>
    /// Failure is always <c>null</c>, never a throw: an unreachable Register Service must leave the
    /// caller reporting "blueprint not found" rather than turning a transient outage into a
    /// different validation error. The caller treats null as unresolved, which fails closed.
    /// </remarks>
    private async Task<BlueprintModel?> ResolveFromSystemRegisterAsync(string blueprintId, CancellationToken ct)
    {
        try
        {
            var json = await _registerClient.GetSystemRegisterBlueprintJsonAsync(blueprintId, ct);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var blueprint = JsonSerializer.Deserialize<BlueprintModel>(json, SystemBlueprintJsonOptions);
            if (blueprint == null)
            {
                _logger.LogWarning(
                    "System register returned blueprint {BlueprintId} but it did not deserialize",
                    blueprintId);
            }

            return blueprint;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve blueprint {BlueprintId} from the system register", blueprintId);
            return null;
        }
    }

    /// <summary>
    /// Mirrors <c>BlueprintFetcher</c>'s options so a blueprint means the same thing whichever
    /// source it arrived from.
    /// </summary>
    private static readonly JsonSerializerOptions SystemBlueprintJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Tier 3 sender-authorisation fallback — derives a late-binding from the earliest prior in-instance transaction signed for the same participant role.</summary>
    /// <remarks>
    /// Trust invariant: <c>TransactionModel.SenderWallet</c> on a sealed transaction is
    /// populated by the validator at docket-sealing time (see
    /// <c>DocketSerializer.GetSenderWallet</c>) from the Wallet Service's verified
    /// <c>Signature.SignedBy</c>. Any transaction that reaches the chain-walk below has
    /// already passed signature verification and (for non-starting actions) Tier 1/2 sender
    /// checks, so <c>SenderWallet</c> is the cryptographically-bound wallet, not
    /// submitter-supplied metadata.
    /// </remarks>
    private async Task<string?> ResolveChainBoundWalletAsync(
        Transaction currentTx,
        string participantId,
        BlueprintModel blueprint,
        CancellationToken ct)
    {
        if (!currentTx.Metadata.TryGetValue("instanceId", out var instanceId)
            || string.IsNullOrWhiteSpace(instanceId))
        {
            return null;
        }

        List<Sorcha.Register.Models.TransactionModel> priorTxs;
        try
        {
            priorTxs = await _registerClient.GetTransactionsByInstanceIdAsync(
                currentTx.RegisterId, instanceId, ct);
        }
        // Deliberate caller cancellation (shutdown, request timeout) must propagate —
        // falling through to VAL_BP_002 on a cancelled request would mislead callers
        // into thinking the chain authoritatively lacked a binding.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                      or TaskCanceledException
                                      or TimeoutException
                                      or IOException)
        {
            _logger.LogWarning(ex,
                "Chain-binding lookup failed for instance {InstanceId} on register {RegisterId} — treating as no binding",
                instanceId, currentTx.RegisterId);
            return null;
        }

        if (priorTxs.Count == 0)
        {
            return null;
        }

        // Oldest first — the earliest matching tx is the binding authority. TxId
        // secondary sort is a deterministic tie-breaker for equal TimeStamps.
        // TODO: Tier 3 currently fetches all in-instance txs. For long-running instances
        // (100+ actions) a purpose-built "first tx by participant role" register query
        // would let the DB short-circuit. Fine at MVD scale; revisit when workflows grow.
        var match = priorTxs
            .Where(t => !string.Equals(t.TxId, currentTx.TransactionId, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.MetaData?.ActionId is not null)
            .OrderBy(t => t.TimeStamp)
            .ThenBy(t => t.TxId, StringComparer.Ordinal)
            .FirstOrDefault(t =>
            {
                // `a.Id >= 0` guard makes the cast intent obvious to readers: a uint
                // ActionId >= int.MaxValue must never match a negative blueprint Id.
                var action = blueprint.Actions.FirstOrDefault(a =>
                    a.Id >= 0 && (uint)a.Id == t.MetaData!.ActionId!.Value);
                return action != null
                    && string.Equals(action.Sender, participantId, StringComparison.OrdinalIgnoreCase);
            });

        return !string.IsNullOrWhiteSpace(match?.SenderWallet) ? match.SenderWallet : null;
    }
}
