// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Service for managing the system register initialization and blueprint publication.
/// </summary>
/// <remarks>
/// <para>
/// The system register is now backed by the real ledger infrastructure (Feature 057).
/// Blueprint entries are stored as control-chain transactions on the well-known system register,
/// replacing the previous standalone MongoDB collection.
/// </para>
/// <para>
/// Responsibilities:
/// - Initialize system register on hub node startup
/// - Seed default blueprints (register-creation-v1, register-governance-v1)
/// - Validate system register integrity
/// - Provide idempotent initialization (skip if already initialized)
/// - Query blueprints from the system register ledger
/// </para>
/// </remarks>
public class SystemRegisterService
{
    private readonly ILogger<SystemRegisterService> _logger;
    private readonly RegisterManager _registerManager;
    private readonly TransactionManager _transactionManager;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ISystemWalletSigningService _signingService;
    private readonly IHashProvider _hashProvider;

    private const string DefaultBlueprintId = "register-creation-v1";
    private const string GovernanceBlueprintId = "register-governance-v1";
    private const string BlueprintPublishTransactionType = "BlueprintPublish";
    private const string BlueprintPublishDerivationPath = "sorcha:blueprint-publish";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegisterService"/> class
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="registerManager">Register manager for register queries</param>
    /// <param name="transactionManager">Transaction manager for querying transactions</param>
    /// <param name="validatorClient">Validator service client for submitting transactions</param>
    /// <param name="signingService">System wallet signing service</param>
    /// <param name="hashProvider">Hash provider for computing SHA-256 hashes</param>
    public SystemRegisterService(
        ILogger<SystemRegisterService> logger,
        RegisterManager registerManager,
        TransactionManager transactionManager,
        IValidatorServiceClient validatorClient,
        ISystemWalletSigningService signingService,
        IHashProvider hashProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _registerManager = registerManager ?? throw new ArgumentNullException(nameof(registerManager));
        _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <summary>
    /// Initializes the system register (idempotent - safe to call multiple times)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if initialization performed, false if already initialized</returns>
    public async Task<bool> InitializeSystemRegisterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking system register initialization status");

            var currentVersion = await GetCurrentVersionAsync(cancellationToken);
            if (currentVersion > 0)
            {
                _logger.LogInformation("System register already initialized (version {Version}) - skipping initialization", currentVersion);
                return false;
            }

            _logger.LogInformation("System register not initialized - beginning initialization");

            // Seed default blueprints via ledger transactions
            await SeedDefaultBlueprintsAsync(cancellationToken);

            _logger.LogInformation("System register initialization complete");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize system register");
            throw;
        }
    }

    /// <summary>
    /// Seeds default blueprints into the system register
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private Task SeedDefaultBlueprintsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding default blueprints into system register");

        // TODO: Feature 057 Phase 2 — publish default blueprints as control-chain transactions
        // on the system register ledger, replacing the old MongoDB-based seeding.
        _logger.LogWarning(
            "System register seeding via ledger transactions not yet implemented (Feature 057 Phase 2). " +
            "Blueprints {DefaultId} and {GovernanceId} will be seeded when ledger publishing is ready",
            DefaultBlueprintId, GovernanceBlueprintId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all blueprints from the system register by querying transactions
    /// with BlueprintPublish metadata
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active blueprints</returns>
    public async Task<List<SystemRegisterEntry>> GetAllBlueprintsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetAllBlueprintsAsync: querying system register ledger");

        var transactions = await GetBlueprintTransactionsAsync(cancellationToken);
        var entries = new List<SystemRegisterEntry>();
        long version = 0;

        foreach (var tx in transactions.OrderBy(t => t.TimeStamp))
        {
            version++;
            var entry = MapTransactionToEntry(tx, version);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        _logger.LogDebug("GetAllBlueprintsAsync: found {Count} blueprint(s)", entries.Count);
        return entries;
    }

    /// <summary>
    /// Gets a specific blueprint by ID from the system register
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint entry or null</returns>
    public async Task<SystemRegisterEntry?> GetBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        _logger.LogDebug("GetBlueprintAsync({BlueprintId}): querying system register ledger", blueprintId);

        var transactions = await GetBlueprintTransactionsAsync(cancellationToken);

        // Find the transaction matching this blueprint ID
        var matchingTx = transactions
            .Where(t => GetBlueprintIdFromTransaction(t) == blueprintId)
            .OrderByDescending(t => t.TimeStamp)
            .FirstOrDefault();

        if (matchingTx is null)
        {
            _logger.LogDebug("GetBlueprintAsync({BlueprintId}): not found", blueprintId);
            return null;
        }

        // Calculate version by counting all blueprint transactions up to this one
        var allOrdered = transactions.OrderBy(t => t.TimeStamp).ToList();
        var version = allOrdered.IndexOf(matchingTx) + 1;

        return MapTransactionToEntry(matchingTx, version);
    }

    /// <summary>
    /// Gets the current system register version (count of blueprint transactions)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current version number</returns>
    public async Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
    {
        var register = await _registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        if (register is null)
        {
            return 0L;
        }

        var transactions = await GetBlueprintTransactionsAsync(cancellationToken);
        return transactions.Count;
    }

    /// <summary>
    /// Publishes a new blueprint to the system register as a control-chain transaction
    /// </summary>
    /// <param name="blueprintId">Unique blueprint identifier</param>
    /// <param name="blueprintJson">Blueprint JSON element</param>
    /// <param name="publishedBy">Publisher identity</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published blueprint entry</returns>
    public async Task<SystemRegisterEntry> PublishBlueprintAsync(
        string blueprintId,
        JsonElement blueprintJson,
        string publishedBy,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedBy);

        _logger.LogInformation("Publishing blueprint {BlueprintId} to system register", blueprintId);

        // Serialize to canonical JSON for deterministic hashing
        var canonicalJson = JsonSerializer.Serialize(blueprintJson, CanonicalJsonOptions);
        var blueprintBytes = Encoding.UTF8.GetBytes(canonicalJson);

        // Compute payload hash
        var payloadHash = _hashProvider.ComputeHash(blueprintBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
        var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

        // Generate deterministic transaction ID: SHA-256 of "blueprint-{blueprintId}-{timestamp}"
        var timestamp = DateTimeOffset.UtcNow;
        var txIdSource = Encoding.UTF8.GetBytes($"blueprint-{blueprintId}-{timestamp.ToUnixTimeMilliseconds()}");
        var txIdHash = _hashProvider.ComputeHash(txIdSource, Sorcha.Cryptography.Enums.HashType.SHA256);
        var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

        // Find previous transaction for chain linking
        string? previousTxId = await GetLatestTransactionIdAsync(cancellationToken);

        // Sign with system wallet
        var signResult = await _signingService.SignAsync(
            registerId: SystemRegisterConstants.SystemRegisterId,
            txId: txId,
            payloadHash: payloadHashHex,
            derivationPath: BlueprintPublishDerivationPath,
            transactionType: "Control",
            cancellationToken);

        var systemSignature = new SignatureInfo
        {
            PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
            SignatureValue = Base64Url.EncodeToString(signResult.Signature),
            Algorithm = signResult.Algorithm
        };

        // Build submission metadata
        var submissionMetadata = new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = BlueprintPublishTransactionType,
            ["BlueprintId"] = blueprintId,
            ["publishedBy"] = publishedBy,
            ["SystemWalletAddress"] = signResult.WalletAddress
        };

        // Merge additional metadata if provided
        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                submissionMetadata.TryAdd(kvp.Key, kvp.Value);
            }
        }

        // Submit via validator
        var submission = new TransactionSubmission
        {
            TransactionId = txId,
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            BlueprintId = blueprintId,
            ActionId = "blueprint-publish",
            Payload = blueprintJson,
            PayloadHash = payloadHashHex,
            PreviousTransactionId = previousTxId,
            Signatures = new List<SignatureInfo> { systemSignature },
            CreatedAt = timestamp,
            Metadata = submissionMetadata
        };

        var submissionResult = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!submissionResult.Success)
        {
            _logger.LogError(
                "Failed to publish blueprint {BlueprintId} to system register: {Error}",
                blueprintId, submissionResult.ErrorMessage);
            throw new InvalidOperationException(
                $"Blueprint publish failed for {blueprintId}: {submissionResult.ErrorMessage}");
        }

        _logger.LogInformation(
            "Blueprint {BlueprintId} published to system register (txId: {TxId})",
            blueprintId, txId);

        var currentVersion = await GetCurrentVersionAsync(cancellationToken);

        return new SystemRegisterEntry
        {
            BlueprintId = blueprintId,
            PublishedBy = publishedBy,
            PublishedAt = timestamp.UtcDateTime,
            Version = currentVersion,
            IsActive = true,
            PublicationTransactionId = txId,
            Checksum = payloadHashHex,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Checks whether a blueprint exists in the system register
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the blueprint exists and is active</returns>
    public async Task<bool> BlueprintExistsAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        var entry = await GetBlueprintAsync(blueprintId, cancellationToken);
        return entry is not null && entry.IsActive;
    }

    /// <summary>
    /// Gets summary information about the system register including its identity,
    /// current status, blueprint count, and initialization timestamp.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System register info record</returns>
    public async Task<SystemRegisterInfo> GetSystemRegisterInfoAsync(CancellationToken cancellationToken = default)
    {
        var register = await _registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        var isInitialized = register is not null;
        var blueprints = isInitialized
            ? await GetAllBlueprintsAsync(cancellationToken)
            : new List<SystemRegisterEntry>();

        var currentVersion = (long)blueprints.Count;

        DateTime? createdAt = null;
        if (isInitialized && blueprints.Count > 0)
        {
            createdAt = blueprints.MinBy(b => b.PublishedAt)?.PublishedAt;
        }
        else if (register is not null)
        {
            createdAt = register.CreatedAt;
        }

        return new SystemRegisterInfo
        {
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Status = isInitialized ? "initialized" : "not_initialized",
            BlueprintCount = blueprints.Count,
            CurrentVersion = currentVersion,
            Height = register?.Height ?? 0,
            CreatedAt = createdAt
        };
    }

    /// <summary>
    /// Queries all BlueprintPublish transactions on the system register
    /// </summary>
    private async Task<List<TransactionModel>> GetBlueprintTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var register = await _registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        if (register is null)
        {
            return new List<TransactionModel>();
        }

        var allTransactions = await _transactionManager.GetTransactionsAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        // Materialize before filtering — IQueryable expression trees cannot use
        // null-propagating operators or out variables
        var materialized = allTransactions.ToList();

        // Filter for blueprint transactions: Control type with a non-genesis BlueprintId.
        // TrackingData is not reliably persisted through the validator pipeline,
        // so we use BlueprintId from MetaData which IS stored.
        return materialized
            .Where(t => t.MetaData is not null
                && t.MetaData.TransactionType == Sorcha.Register.Models.Enums.TransactionType.Control
                && !string.IsNullOrEmpty(t.MetaData.BlueprintId)
                && t.MetaData.BlueprintId != "genesis")
            .ToList();
    }

    /// <summary>
    /// Extracts the blueprint ID from a transaction's metadata
    /// </summary>
    private static string? GetBlueprintIdFromTransaction(TransactionModel tx)
    {
        // BlueprintId is stored in MetaData.BlueprintId (set via TransactionSubmission.BlueprintId)
        return tx.MetaData?.BlueprintId;
    }

    /// <summary>
    /// Gets the latest transaction ID on the system register for chain linking
    /// </summary>
    private async Task<string?> GetLatestTransactionIdAsync(CancellationToken cancellationToken = default)
    {
        var register = await _registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        if (register is null)
        {
            return null;
        }

        var allTransactions = await _transactionManager.GetTransactionsAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        var latestTx = allTransactions
            .OrderByDescending(t => t.TimeStamp)
            .FirstOrDefault();

        return latestTx?.TxId;
    }

    /// <summary>
    /// Maps a transaction to a SystemRegisterEntry
    /// </summary>
    private static SystemRegisterEntry? MapTransactionToEntry(TransactionModel tx, long version)
    {
        var blueprintId = GetBlueprintIdFromTransaction(tx);
        if (string.IsNullOrEmpty(blueprintId))
        {
            return null;
        }

        JsonDocument? document = null;

        // Extract blueprint JSON from the first payload
        if (tx.Payloads.Length > 0 && !string.IsNullOrEmpty(tx.Payloads[0].Data))
        {
            try
            {
                var encoding = tx.Payloads[0].ContentEncoding ?? "base64";
                byte[] dataBytes;

                if (encoding.Contains("base64url", StringComparison.OrdinalIgnoreCase))
                {
                    dataBytes = Base64Url.DecodeFromChars(tx.Payloads[0].Data);
                }
                else
                {
                    dataBytes = Convert.FromBase64String(tx.Payloads[0].Data);
                }

                var json = Encoding.UTF8.GetString(dataBytes);
                document = JsonDocument.Parse(json);
            }
            catch (Exception)
            {
                // If payload decoding fails, leave document null
            }
        }

        var publishedBy = tx.MetaData?.TrackingData?.GetValueOrDefault("publishedBy") ?? "system";

        return new SystemRegisterEntry
        {
            BlueprintId = blueprintId,
            RegisterId = Guid.TryParse(tx.RegisterId, out var regGuid) ? regGuid : Guid.Empty,
            Document = document,
            PublishedAt = tx.TimeStamp,
            PublishedBy = publishedBy,
            Version = version,
            IsActive = true,
            PublicationTransactionId = tx.TxId,
            Checksum = tx.Payloads.Length > 0 ? tx.Payloads[0].Hash : null,
            Metadata = tx.MetaData?.TrackingData
        };
    }
}

/// <summary>
/// Represents a blueprint entry in the system register.
/// This is a POCO model that replaces the previous MongoDB-annotated version.
/// Data is now sourced from the system register ledger's control chain.
/// </summary>
public class SystemRegisterEntry
{
    /// <summary>
    /// Unique blueprint identifier
    /// </summary>
    public string BlueprintId { get; set; } = string.Empty;

    /// <summary>
    /// System register identifier (well-known constant: 00000000-0000-0000-0000-000000000000)
    /// </summary>
    public Guid RegisterId { get; set; } = Guid.Empty;

    /// <summary>
    /// Blueprint document as JSON
    /// </summary>
    public JsonDocument? Document { get; set; }

    /// <summary>
    /// Timestamp when blueprint was published (UTC)
    /// </summary>
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identity of publisher (user ID or "system")
    /// </summary>
    public string PublishedBy { get; set; } = string.Empty;

    /// <summary>
    /// Incrementing version number for sync
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// Whether blueprint is active/available
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Link to register transaction that published this blueprint (optional)
    /// </summary>
    public string? PublicationTransactionId { get; set; }

    /// <summary>
    /// SHA-256 checksum of Document for integrity verification (optional)
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Optional metadata key-value pairs
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Summary information about the system register.
/// </summary>
public record SystemRegisterInfo
{
    /// <summary>
    /// Deterministic system register identifier.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Current status: "initialized" or "not_initialized".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Number of active blueprints in the system register.
    /// </summary>
    public int BlueprintCount { get; init; }

    /// <summary>
    /// Latest blueprint version number.
    /// </summary>
    public long CurrentVersion { get; init; }

    /// <summary>
    /// Register chain height from the ledger.
    /// </summary>
    public long Height { get; init; }

    /// <summary>
    /// UTC timestamp when the system register was first initialized (earliest blueprint).
    /// Null if not yet initialized.
    /// </summary>
    public DateTime? CreatedAt { get; init; }
}
