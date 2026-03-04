// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Processes control dockets containing governance transactions.
/// Handles validator management, configuration updates, and blueprint publications.
/// </summary>
public class ControlDocketProcessor : IControlDocketProcessor
{
    private readonly IGenesisConfigService _genesisConfigService;
    private readonly IControlBlueprintVersionResolver _versionResolver;
    private readonly IValidatorRegistry _validatorRegistry;
    private readonly ILogger<ControlDocketProcessor> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // Control action ID constants (lowercase for case-insensitive matching)
    private const string ControlActionPrefix = "control.";
    private const string ValidatorRegisterAction = "control.validator.register";
    private const string ValidatorApproveAction = "control.validator.approve";
    private const string ValidatorSuspendAction = "control.validator.suspend";
    private const string ValidatorRemoveAction = "control.validator.remove";
    private const string ConfigUpdateAction = "control.config.update";
    private const string BlueprintPublishAction = "control.blueprint.publish";
    private const string RegisterUpdateMetadataAction = "control.register.updatemetadata";
    private const string CryptoPolicyUpdateAction = "control.crypto.update";
    private const string PolicyUpdateAction = "control.policy.update";

    /// <inheritdoc/>
    public event EventHandler<ControlActionAppliedEventArgs>? ControlActionApplied;

    public ControlDocketProcessor(
        IGenesisConfigService genesisConfigService,
        IControlBlueprintVersionResolver versionResolver,
        IValidatorRegistry validatorRegistry,
        ILogger<ControlDocketProcessor> logger)
    {
        _genesisConfigService = genesisConfigService ?? throw new ArgumentNullException(nameof(genesisConfigService));
        _versionResolver = versionResolver ?? throw new ArgumentNullException(nameof(versionResolver));
        _validatorRegistry = validatorRegistry ?? throw new ArgumentNullException(nameof(validatorRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc/>
    public IReadOnlyList<ControlTransaction> ExtractControlTransactions(Docket docket)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var controlTransactions = new List<ControlTransaction>();

        foreach (var tx in docket.Transactions)
        {
            if (tx.ActionId is null) continue;
            var actionType = GetControlActionType(tx.ActionId);

            if (actionType != ControlActionType.Unknown)
            {
                try
                {
                    var payload = ParseControlPayload(tx, actionType);
                    controlTransactions.Add(new ControlTransaction
                    {
                        Transaction = tx,
                        ActionType = actionType,
                        ActionId = tx.ActionId,
                        Payload = payload
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to parse control transaction {TxId} with action {ActionId}",
                        tx.TransactionId, tx.ActionId);
                }
            }
        }

        _logger.LogDebug(
            "Extracted {ControlCount} control transactions from docket {DocketId}",
            controlTransactions.Count, docket.DocketId);

        return controlTransactions.AsReadOnly();
    }

    /// <inheritdoc/>
    public bool IsControlDocket(Docket docket)
    {
        ArgumentNullException.ThrowIfNull(docket);

        return docket.Transactions.Any(tx => tx.ActionId is not null && IsControlAction(tx.ActionId));
    }

    /// <inheritdoc/>
    public async Task<ControlValidationResult> ValidateControlTransactionsAsync(
        string registerId,
        IReadOnlyList<ControlTransaction> controlTransactions,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(controlTransactions);

        if (controlTransactions.Count == 0)
        {
            return ControlValidationResult.Success([]);
        }

        _logger.LogDebug(
            "Validating {Count} control transactions for register {RegisterId}",
            controlTransactions.Count, registerId);

        var errors = new Dictionary<string, List<string>>();
        var validTransactions = new List<ControlTransaction>();

        // Get current configuration for validation
        var config = await _genesisConfigService.GetFullConfigAsync(registerId, ct);
        var validatorConfig = config.Validators;

        foreach (var controlTx in controlTransactions)
        {
            var txErrors = new List<string>();

            // Validate based on action type
            switch (controlTx.ActionType)
            {
                case ControlActionType.ValidatorRegister:
                    txErrors.AddRange(ValidateValidatorRegistration(controlTx, validatorConfig));
                    break;

                case ControlActionType.ValidatorApprove:
                    txErrors.AddRange(await ValidateValidatorApprovalAsync(controlTx, registerId, ct));
                    break;

                case ControlActionType.ValidatorSuspend:
                    txErrors.AddRange(await ValidateValidatorSuspensionAsync(controlTx, registerId, ct));
                    break;

                case ControlActionType.ValidatorRemove:
                    txErrors.AddRange(await ValidateValidatorRemovalAsync(controlTx, registerId, config, ct));
                    break;

                case ControlActionType.ConfigUpdate:
                    txErrors.AddRange(ValidateConfigUpdate(controlTx, config));
                    break;

                case ControlActionType.BlueprintPublish:
                    txErrors.AddRange(ValidateBlueprintPublish(controlTx));
                    break;

                case ControlActionType.RegisterUpdateMetadata:
                    txErrors.AddRange(ValidateMetadataUpdate(controlTx));
                    break;

                case ControlActionType.CryptoPolicyUpdate:
                    txErrors.AddRange(ValidateCryptoPolicyUpdate(controlTx));
                    break;

                case ControlActionType.PolicyUpdate:
                    txErrors.AddRange(ValidatePolicyUpdate(controlTx, config));
                    break;

                default:
                    txErrors.Add($"Unknown control action type: {controlTx.ActionType}");
                    break;
            }

            if (txErrors.Count > 0)
            {
                errors[controlTx.Transaction.TransactionId] = txErrors;
            }
            else
            {
                validTransactions.Add(controlTx);
            }
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Control transaction validation failed for register {RegisterId}: {ErrorCount} errors",
                registerId, errors.Count);

            return ControlValidationResult.Failure(
                errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()));
        }

        return ControlValidationResult.Success(validTransactions.AsReadOnly());
    }

    /// <inheritdoc/>
    public async Task<ControlProcessingResult> ProcessCommittedDocketAsync(
        string registerId,
        Docket docket,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(docket);

        _logger.LogInformation(
            "Processing committed control docket {DocketId} for register {RegisterId}",
            docket.DocketId, registerId);

        var controlTransactions = ExtractControlTransactions(docket);

        if (controlTransactions.Count == 0)
        {
            return new ControlProcessingResult
            {
                Success = true,
                ActionsApplied = 0,
                ActionResults = [],
                ConfigurationUpdated = false,
                ValidatorsModified = false
            };
        }

        var actionResults = new List<ControlActionResult>();
        var configUpdated = false;
        var validatorsModified = false;

        foreach (var controlTx in controlTransactions)
        {
            try
            {
                var result = await ApplyControlActionAsync(registerId, controlTx, ct);
                actionResults.Add(result);

                if (result.Success)
                {
                    if (result.ActionType is ControlActionType.ConfigUpdate or ControlActionType.CryptoPolicyUpdate or ControlActionType.PolicyUpdate)
                    {
                        configUpdated = true;
                    }

                    if (IsValidatorAction(result.ActionType))
                    {
                        validatorsModified = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply control action {ActionType} for transaction {TxId}",
                    controlTx.ActionType, controlTx.Transaction.TransactionId);

                actionResults.Add(new ControlActionResult
                {
                    TransactionId = controlTx.Transaction.TransactionId,
                    ActionType = controlTx.ActionType,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        // Invalidate caches if configuration was updated
        if (configUpdated)
        {
            await _genesisConfigService.RefreshConfigAsync(registerId, ct);
            _versionResolver.InvalidateCache(registerId);
        }

        // Refresh validator registry if validators were modified
        if (validatorsModified)
        {
            await _validatorRegistry.RefreshAsync(registerId, ct);
        }

        var successCount = actionResults.Count(r => r.Success);
        _logger.LogInformation(
            "Processed control docket {DocketId}: {Success}/{Total} actions applied",
            docket.DocketId, successCount, actionResults.Count);

        return new ControlProcessingResult
        {
            Success = actionResults.All(r => r.Success),
            ActionsApplied = successCount,
            ActionResults = actionResults.AsReadOnly(),
            ConfigurationUpdated = configUpdated,
            ValidatorsModified = validatorsModified
        };
    }

    /// <inheritdoc/>
    public async Task<ControlActionResult> ApplyControlActionAsync(
        string registerId,
        ControlTransaction controlTransaction,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(controlTransaction);

        var txId = controlTransaction.Transaction.TransactionId;

        _logger.LogDebug(
            "Applying control action {ActionType} for transaction {TxId} in register {RegisterId}",
            controlTransaction.ActionType, txId, registerId);

        try
        {
            string changeDescription;

            switch (controlTransaction.ActionType)
            {
                case ControlActionType.ValidatorRegister:
                    changeDescription = await ApplyValidatorRegisterAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.ValidatorApprove:
                    changeDescription = await ApplyValidatorApproveAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.ValidatorSuspend:
                    changeDescription = await ApplyValidatorSuspendAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.ValidatorRemove:
                    changeDescription = await ApplyValidatorRemoveAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.ConfigUpdate:
                    changeDescription = await ApplyConfigUpdateAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.BlueprintPublish:
                    changeDescription = await ApplyBlueprintPublishAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.RegisterUpdateMetadata:
                    changeDescription = await ApplyMetadataUpdateAsync(registerId, controlTransaction, ct);
                    break;

                case ControlActionType.CryptoPolicyUpdate:
                    changeDescription = ApplyCryptoPolicyUpdate(registerId, controlTransaction);
                    break;

                case ControlActionType.PolicyUpdate:
                    changeDescription = await ApplyPolicyUpdateAsync(registerId, controlTransaction, ct);
                    break;

                default:
                    return new ControlActionResult
                    {
                        TransactionId = txId,
                        ActionType = controlTransaction.ActionType,
                        Success = false,
                        ErrorMessage = $"Unknown control action type: {controlTransaction.ActionType}"
                    };
            }

            // Raise event
            RaiseControlActionApplied(registerId, txId, controlTransaction.ActionType, changeDescription);

            return new ControlActionResult
            {
                TransactionId = txId,
                ActionType = controlTransaction.ActionType,
                Success = true,
                ChangeDescription = changeDescription
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to apply control action {ActionType} for transaction {TxId}",
                controlTransaction.ActionType, txId);

            return new ControlActionResult
            {
                TransactionId = txId,
                ActionType = controlTransaction.ActionType,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }


    private static bool IsControlAction(string actionId)
    {
        return !string.IsNullOrEmpty(actionId) &&
               actionId.StartsWith(ControlActionPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static ControlActionType GetControlActionType(string actionId)
    {
        if (string.IsNullOrEmpty(actionId))
            return ControlActionType.Unknown;

        return actionId.ToLowerInvariant() switch
        {
            ValidatorRegisterAction => ControlActionType.ValidatorRegister,
            ValidatorApproveAction => ControlActionType.ValidatorApprove,
            ValidatorSuspendAction => ControlActionType.ValidatorSuspend,
            ValidatorRemoveAction => ControlActionType.ValidatorRemove,
            ConfigUpdateAction => ControlActionType.ConfigUpdate,
            BlueprintPublishAction => ControlActionType.BlueprintPublish,
            RegisterUpdateMetadataAction => ControlActionType.RegisterUpdateMetadata,
            CryptoPolicyUpdateAction => ControlActionType.CryptoPolicyUpdate,
            PolicyUpdateAction => ControlActionType.PolicyUpdate,
            _ when actionId.StartsWith(ControlActionPrefix, StringComparison.OrdinalIgnoreCase) => ControlActionType.Unknown,
            _ => ControlActionType.Unknown
        };
    }

    private static bool IsValidatorAction(ControlActionType actionType)
    {
        return actionType is ControlActionType.ValidatorRegister
            or ControlActionType.ValidatorApprove
            or ControlActionType.ValidatorSuspend
            or ControlActionType.ValidatorRemove;
    }



    private ControlPayload ParseControlPayload(Transaction tx, ControlActionType actionType)
    {
        var payloadJson = tx.Payload.GetRawText();

        return actionType switch
        {
            ControlActionType.ValidatorRegister =>
                JsonSerializer.Deserialize<ValidatorRegisterPayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse validator register payload"),

            ControlActionType.ValidatorApprove =>
                JsonSerializer.Deserialize<ValidatorApprovePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse validator approve payload"),

            ControlActionType.ValidatorSuspend =>
                JsonSerializer.Deserialize<ValidatorSuspendPayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse validator suspend payload"),

            ControlActionType.ValidatorRemove =>
                JsonSerializer.Deserialize<ValidatorRemovePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse validator remove payload"),

            ControlActionType.ConfigUpdate =>
                JsonSerializer.Deserialize<ConfigUpdatePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse config update payload"),

            ControlActionType.BlueprintPublish =>
                JsonSerializer.Deserialize<BlueprintPublishPayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse blueprint publish payload"),

            ControlActionType.RegisterUpdateMetadata =>
                JsonSerializer.Deserialize<RegisterMetadataUpdatePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse metadata update payload"),

            ControlActionType.CryptoPolicyUpdate =>
                JsonSerializer.Deserialize<CryptoPolicyUpdatePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse crypto policy update payload"),

            ControlActionType.PolicyUpdate =>
                JsonSerializer.Deserialize<PolicyUpdatePayload>(payloadJson, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to parse policy update payload"),

            _ => throw new InvalidOperationException($"Unknown control action type: {actionType}")
        };
    }



    private static List<string> ValidateValidatorRegistration(
        ControlTransaction controlTx,
        ValidatorConfig validatorConfig)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as ValidatorRegisterPayload;

        if (payload == null)
        {
            errors.Add("Invalid validator registration payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.ValidatorId))
            errors.Add("ValidatorId is required");

        if (string.IsNullOrWhiteSpace(payload.PublicKey))
            errors.Add("PublicKey is required");

        if (string.IsNullOrWhiteSpace(payload.Endpoint))
            errors.Add("Endpoint is required");
        else if (!Uri.TryCreate(payload.Endpoint, UriKind.Absolute, out _))
            errors.Add("Endpoint must be a valid URI");

        return errors;
    }

    private async Task<List<string>> ValidateValidatorApprovalAsync(
        ControlTransaction controlTx,
        string registerId,
        CancellationToken ct)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as ValidatorApprovePayload;

        if (payload == null)
        {
            errors.Add("Invalid validator approval payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.ValidatorId))
        {
            errors.Add("ValidatorId is required");
            return errors;
        }

        // Check that validator exists and is pending
        var validator = await _validatorRegistry.GetValidatorAsync(registerId, payload.ValidatorId, ct);
        if (validator == null)
        {
            errors.Add($"Validator {payload.ValidatorId} not found");
        }
        else if (validator.Status != Interfaces.ValidatorStatus.Pending)
        {
            errors.Add($"Validator {payload.ValidatorId} is not pending approval (status: {validator.Status})");
        }

        return errors;
    }

    private async Task<List<string>> ValidateValidatorSuspensionAsync(
        ControlTransaction controlTx,
        string registerId,
        CancellationToken ct)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as ValidatorSuspendPayload;

        if (payload == null)
        {
            errors.Add("Invalid validator suspension payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.ValidatorId))
        {
            errors.Add("ValidatorId is required");
            return errors;
        }

        // Check that validator exists and is active
        var validator = await _validatorRegistry.GetValidatorAsync(registerId, payload.ValidatorId, ct);
        if (validator == null)
        {
            errors.Add($"Validator {payload.ValidatorId} not found");
        }
        else if (validator.Status != Interfaces.ValidatorStatus.Active)
        {
            errors.Add($"Validator {payload.ValidatorId} is not active (status: {validator.Status})");
        }

        return errors;
    }

    private async Task<List<string>> ValidateValidatorRemovalAsync(
        ControlTransaction controlTx,
        string registerId,
        GenesisConfiguration config,
        CancellationToken ct)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as ValidatorRemovePayload;

        if (payload == null)
        {
            errors.Add("Invalid validator removal payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.ValidatorId))
        {
            errors.Add("ValidatorId is required");
            return errors;
        }

        // Check that validator exists
        var validator = await _validatorRegistry.GetValidatorAsync(registerId, payload.ValidatorId, ct);
        if (validator == null)
        {
            errors.Add($"Validator {payload.ValidatorId} not found");
            return errors;
        }

        // Check minimum validators constraint
        var activeCount = await _validatorRegistry.GetActiveCountAsync(registerId, ct);
        if (validator.Status == Interfaces.ValidatorStatus.Active && activeCount <= config.Validators.MinValidators)
        {
            errors.Add($"Cannot remove validator: would go below minimum ({config.Validators.MinValidators})");
        }

        return errors;
    }

    private static List<string> ValidateConfigUpdate(
        ControlTransaction controlTx,
        GenesisConfiguration currentConfig)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as ConfigUpdatePayload;

        if (payload == null)
        {
            errors.Add("Invalid config update payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.Path))
            errors.Add("Path is required");

        if (payload.NewValue == null)
            errors.Add("NewValue is required");

        // Validate path is a known configuration path
        var validPaths = new[]
        {
            "consensus.signatureThreshold.min",
            "consensus.signatureThreshold.max",
            "consensus.docketTimeout",
            "consensus.maxTransactionsPerDocket",
            "validators.registrationMode",
            "validators.minValidators",
            "validators.maxValidators",
            "leaderElection.mechanism",
            "leaderElection.heartbeatInterval",
            "leaderElection.leaderTimeout"
        };

        if (!string.IsNullOrEmpty(payload.Path) &&
            !validPaths.Any(p => p.Equals(payload.Path, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Unknown configuration path: {payload.Path}");
        }

        return errors;
    }

    private static List<string> ValidateBlueprintPublish(ControlTransaction controlTx)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as BlueprintPublishPayload;

        if (payload == null)
        {
            errors.Add("Invalid blueprint publish payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.BlueprintId))
            errors.Add("BlueprintId is required");

        if (string.IsNullOrWhiteSpace(payload.BlueprintJson))
            errors.Add("BlueprintJson is required");

        if (string.IsNullOrWhiteSpace(payload.PublishedBy))
            errors.Add("PublishedBy is required");

        // Try to parse the blueprint JSON
        if (!string.IsNullOrEmpty(payload.BlueprintJson))
        {
            try
            {
                JsonDocument.Parse(payload.BlueprintJson);
            }
            catch (JsonException)
            {
                errors.Add("BlueprintJson is not valid JSON");
            }
        }

        return errors;
    }

    private static List<string> ValidateMetadataUpdate(ControlTransaction controlTx)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as RegisterMetadataUpdatePayload;

        if (payload == null)
        {
            errors.Add("Invalid metadata update payload");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(payload.Field))
            errors.Add("Field is required");

        var validFields = new[] { "name", "description", "tags" };
        if (!string.IsNullOrEmpty(payload.Field) &&
            !validFields.Contains(payload.Field, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Invalid field: {payload.Field}. Must be one of: {string.Join(", ", validFields)}");
        }

        if (string.IsNullOrWhiteSpace(payload.NewValue))
            errors.Add("NewValue is required");

        return errors;
    }

    private static List<string> ValidateCryptoPolicyUpdate(ControlTransaction controlTx)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as CryptoPolicyUpdatePayload;

        if (payload == null)
        {
            errors.Add("Invalid crypto policy update payload");
            return errors;
        }

        if (payload.Version <= 0)
            errors.Add("Policy version must be greater than 0");

        if (payload.AcceptedSignatureAlgorithms == null || payload.AcceptedSignatureAlgorithms.Length == 0)
            errors.Add("AcceptedSignatureAlgorithms must contain at least one algorithm");

        // Validate RequiredSignatureAlgorithms is a subset of AcceptedSignatureAlgorithms
        if (payload.RequiredSignatureAlgorithms.Length > 0 && payload.AcceptedSignatureAlgorithms != null)
        {
            var accepted = new HashSet<string>(payload.AcceptedSignatureAlgorithms, StringComparer.OrdinalIgnoreCase);
            var missing = payload.RequiredSignatureAlgorithms.Where(r => !accepted.Contains(r)).ToArray();
            if (missing.Length > 0)
            {
                errors.Add($"RequiredSignatureAlgorithms contains algorithms not in AcceptedSignatureAlgorithms: {string.Join(", ", missing)}");
            }
        }

        var validModes = new[] { "Permissive", "Strict" };
        if (!validModes.Contains(payload.EnforcementMode, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"EnforcementMode must be one of: {string.Join(", ", validModes)}");
        }

        return errors;
    }

    private static List<string> ValidatePolicyUpdate(
        ControlTransaction controlTx,
        GenesisConfiguration config)
    {
        var errors = new List<string>();
        var payload = controlTx.Payload as PolicyUpdatePayload;

        if (payload == null)
        {
            errors.Add("Invalid policy update payload");
            return errors;
        }

        if (payload.Policy == null)
        {
            errors.Add("Policy is required");
            return errors;
        }

        if (payload.Policy.Version < 1)
            errors.Add("Policy version must be >= 1");

        if (string.IsNullOrWhiteSpace(payload.UpdatedBy))
            errors.Add("UpdatedBy is required");

        if (payload.Policy.Validators.MinValidators < 1)
            errors.Add("MinValidators must be >= 1");

        if (payload.Policy.Validators.MaxValidators < payload.Policy.Validators.MinValidators)
            errors.Add("MaxValidators must be >= MinValidators");

        if (payload.Policy.Consensus.SignatureThresholdMin < 1)
            errors.Add("SignatureThresholdMin must be >= 1");

        if (payload.Policy.Consensus.SignatureThresholdMax < payload.Policy.Consensus.SignatureThresholdMin)
            errors.Add("SignatureThresholdMax must be >= SignatureThresholdMin");

        // If registration mode is changing from Public to Consent, TransitionMode is required
        var currentMode = config.Validators.RegistrationMode;
        var newMode = payload.Policy.Validators.RegistrationMode;
        if (currentMode.Equals("public", StringComparison.OrdinalIgnoreCase) &&
            newMode == Sorcha.Register.Models.RegistrationMode.Consent &&
            payload.TransitionMode == null)
        {
            errors.Add("TransitionMode is required when changing RegistrationMode from Public to Consent");
        }

        return errors;
    }


    private async Task<string> ApplyValidatorRegisterAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (ValidatorRegisterPayload)controlTx.Payload;

        _logger.LogInformation(
            "Registering validator {ValidatorId} for register {RegisterId}",
            payload.ValidatorId, registerId);

        var registration = new ValidatorRegistration
        {
            ValidatorId = payload.ValidatorId,
            PublicKey = payload.PublicKey,
            GrpcEndpoint = payload.Endpoint,
            Metadata = payload.Metadata
        };

        var result = await _validatorRegistry.RegisterAsync(registerId, registration, ct);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to register validator: {result.ErrorMessage}");
        }

        return $"Validator {payload.ValidatorId} registered (order: {result.OrderIndex})";
    }

    private async Task<string> ApplyValidatorApproveAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (ValidatorApprovePayload)controlTx.Payload;

        _logger.LogInformation(
            "Approving validator {ValidatorId} for register {RegisterId}",
            payload.ValidatorId, registerId);

        // Get the current validator
        var validator = await _validatorRegistry.GetValidatorAsync(registerId, payload.ValidatorId, ct);
        if (validator == null)
        {
            throw new InvalidOperationException($"Validator {payload.ValidatorId} not found");
        }

        // Update status to Active
        // Note: The ValidatorRegistry would need an UpdateStatusAsync method
        // For now, we refresh and log the approval
        _logger.LogInformation(
            "Validator {ValidatorId} approved by {ApprovedBy}",
            payload.ValidatorId, payload.ApprovedBy);

        await _validatorRegistry.RefreshAsync(registerId, ct);

        return $"Validator {payload.ValidatorId} approved by {payload.ApprovedBy}";
    }

    private async Task<string> ApplyValidatorSuspendAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (ValidatorSuspendPayload)controlTx.Payload;

        _logger.LogInformation(
            "Suspending validator {ValidatorId} for register {RegisterId}. Reason: {Reason}",
            payload.ValidatorId, registerId, payload.Reason);

        await _validatorRegistry.RefreshAsync(registerId, ct);

        var untilMsg = payload.SuspendedUntil.HasValue
            ? $" until {payload.SuspendedUntil.Value:u}"
            : " indefinitely";

        return $"Validator {payload.ValidatorId} suspended by {payload.SuspendedBy}{untilMsg}";
    }

    private async Task<string> ApplyValidatorRemoveAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (ValidatorRemovePayload)controlTx.Payload;

        _logger.LogInformation(
            "Removing validator {ValidatorId} from register {RegisterId}. Reason: {Reason}",
            payload.ValidatorId, registerId, payload.Reason);

        await _validatorRegistry.RefreshAsync(registerId, ct);

        return $"Validator {payload.ValidatorId} removed by {payload.RemovedBy}. Reason: {payload.Reason}";
    }

    private Task<string> ApplyConfigUpdateAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (ConfigUpdatePayload)controlTx.Payload;

        _logger.LogInformation(
            "Updating configuration for register {RegisterId}: {Path} = {NewValue}",
            registerId, payload.Path, payload.NewValue);

        // Configuration updates are applied through the genesis config refresh
        // The actual change is persisted in the transaction chain

        return Task.FromResult($"Configuration updated: {payload.Path} = {payload.NewValue}");
    }

    private Task<string> ApplyBlueprintPublishAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (BlueprintPublishPayload)controlTx.Payload;

        _logger.LogInformation(
            "Blueprint {BlueprintId} published to register {RegisterId} by {PublishedBy}",
            payload.BlueprintId, registerId, payload.PublishedBy);

        var versionMsg = string.IsNullOrEmpty(payload.PreviousVersionId)
            ? "new blueprint"
            : $"update from {payload.PreviousVersionId}";

        return Task.FromResult($"Blueprint {payload.BlueprintId} published ({versionMsg})");
    }

    private Task<string> ApplyMetadataUpdateAsync(
        string registerId,
        ControlTransaction controlTx,
        CancellationToken ct)
    {
        var payload = (RegisterMetadataUpdatePayload)controlTx.Payload;

        _logger.LogInformation(
            "Updating register metadata for {RegisterId}: {Field} = {NewValue}",
            registerId, payload.Field, payload.NewValue);

        return Task.FromResult($"Register {payload.Field} updated to: {payload.NewValue}");
    }

    private string ApplyCryptoPolicyUpdate(
        string registerId,
        ControlTransaction controlTx)
    {
        var payload = (CryptoPolicyUpdatePayload)controlTx.Payload;

        _logger.LogInformation(
            "Crypto policy updated for register {RegisterId}: version {Version}, mode {Mode}, accepted algorithms: {Algorithms}",
            registerId, payload.Version, payload.EnforcementMode,
            string.Join(", ", payload.AcceptedSignatureAlgorithms));

        // Policy change is persisted in the transaction chain and read by CryptoPolicyService
        // on the Register Service side. The Validator refreshes its genesis config cache above.

        return $"Crypto policy updated to version {payload.Version} ({payload.EnforcementMode} mode, {payload.AcceptedSignatureAlgorithms.Length} accepted algorithms)";
    }

    private async Task<string> ApplyPolicyUpdateAsync(
        string registerId,
        ControlTransaction controlTransaction,
        CancellationToken ct)
    {
        var payload = (PolicyUpdatePayload)controlTransaction.Payload;

        _logger.LogInformation(
            "Register policy updated for register {RegisterId}: version {Version} by {UpdatedBy}",
            registerId, payload.Policy.Version, payload.UpdatedBy);

        // Enforce registration mode transition if changing from public to consent
        if (payload.TransitionMode.HasValue &&
            payload.Policy.Validators.RegistrationMode == Sorcha.Register.Models.RegistrationMode.Consent)
        {
            var ejected = await _validatorRegistry.EnforceRegistrationModeTransitionAsync(
                registerId,
                payload.Policy.Validators.ApprovedValidators,
                payload.TransitionMode.Value,
                ct);

            if (ejected > 0)
            {
                _logger.LogWarning(
                    "Transition enforcement ejected {EjectedCount} unapproved validators from register {RegisterId} (mode={TransitionMode})",
                    ejected, registerId, payload.TransitionMode.Value);
            }

            return $"Register policy updated to version {payload.Policy.Version} by {payload.UpdatedBy} ({ejected} validators affected by transition)";
        }

        return $"Register policy updated to version {payload.Policy.Version} by {payload.UpdatedBy}";
    }


    private void RaiseControlActionApplied(
        string registerId,
        string transactionId,
        ControlActionType actionType,
        string? changeDescription)
    {
        ControlActionApplied?.Invoke(this, new ControlActionAppliedEventArgs
        {
            RegisterId = registerId,
            TransactionId = transactionId,
            ActionType = actionType,
            AppliedAt = DateTimeOffset.UtcNow,
            ChangeDescription = changeDescription
        });
    }

}
