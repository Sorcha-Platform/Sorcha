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
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Wallet.Contracts.Constants;

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
    private const string GovernanceBlueprintId = GovernanceBlueprint.BlueprintId;
    private const string BlueprintPublishTransactionType = "BlueprintPublish";

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

        // Version counts publications OF THE SAME BLUEPRINT, not position in the combined list.
        // A shared running counter made a blueprint's version depend on how many times some OTHER
        // blueprint had been published, which is why register-governance-v1 was reported as v2 and
        // then v5 on n1 without anyone ever republishing it (#1515).
        var publicationCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions.OrderBy(t => t.TimeStamp))
        {
            var blueprintId = GetBlueprintIdFromTransaction(tx);
            if (string.IsNullOrEmpty(blueprintId))
            {
                continue;
            }

            publicationCounts.TryGetValue(blueprintId, out var version);
            publicationCounts[blueprintId] = ++version;

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

        // Version = how many times THIS blueprint has been published, up to and including the match.
        var version = transactions.Count(t =>
            GetBlueprintIdFromTransaction(t) == blueprintId
            && t.TimeStamp <= matchingTx.TimeStamp);

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
            derivationPath: SorchaDerivationPaths.BlueprintPublish,
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
            ["SystemWalletAddress"] = signResult.WalletAddress,
            // Feature 138 US4 — seal the canonical content hash the BlueprintRecoveryService recomputes
            // and compares on recovery. The normal (user) publish path writes this; without it here the
            // seeded system blueprints fail provenance ("no_provenance") on every restart and never
            // re-enter the (in-memory) published store. Use the shared helper so producer and consumer
            // hash the identical canonical form by construction.
            ["contentHash"] = BlueprintContentHash.Compute(canonicalJson)
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

        // This blueprint's OWN publication count, not the register-wide total. Returning the total
        // made the response disagree with what GetBlueprintAsync would report for the very blueprint
        // just published.
        //
        // Whether the transaction submitted a moment ago is already query-visible depends on when the
        // validator seals it, which is not this method's business to know. So count what is there and
        // add this publication only if it is not among them — correct under either timing, rather
        // than correct under the one that happens to hold today.
        var publications = await GetBlueprintTransactionsAsync(cancellationToken);
        var ofThisBlueprint = publications
            .Where(t => GetBlueprintIdFromTransaction(t) == blueprintId)
            .ToList();
        var alreadyVisible = ofThisBlueprint
            .Any(t => string.Equals(t.TxId, txId, StringComparison.OrdinalIgnoreCase));
        var version = ofThisBlueprint.Count + (alreadyVisible ? 0 : 1);

        return new SystemRegisterEntry
        {
            BlueprintId = blueprintId,
            PublishedBy = publishedBy,
            PublishedAt = timestamp.UtcDateTime,
            Version = version,
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
    /// Queries the transactions on the system register that PUBLISH a blueprint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>BlueprintId</c> filter alone is not enough, and issue #1515 is what that costs. Every
    /// governance transaction — propose, approve, enact — carries
    /// <c>MetaData.BlueprintId = "register-governance-v1"</c>, because it genuinely IS an action
    /// submission against that workflow. So governing the system register writes a steady stream of
    /// transactions that look, to a BlueprintId-only filter, exactly like re-publications of the
    /// governance blueprint, and they sort newest-first ahead of the real one.
    /// </para>
    /// <para>
    /// The failure that follows is silent and total. <c>GetBlueprintAsync</c> returns the newest
    /// match — an enactment control transaction — whose payload is a governance operation. It is
    /// valid JSON, so it deserializes into a <c>BlueprintModel</c> without error; it simply has no
    /// actions. The Validator then refuses the next governance transaction with
    /// <c>VAL_SCHEMA_003: Action 1 not found in blueprint 'register-governance-v1'</c>, having
    /// resolved a blueprint that was never a blueprint. Because the Validator caches the resolved
    /// blueprint, the damage is latent until the first cache miss, at which point governance stops
    /// working on EVERY register at once — the resolution is a single global lookup on the SSR.
    /// Observed live on n1 immediately after transferring SSR ownership (F189 US4 / T063).
    /// </para>
    /// <para>
    /// So the filter asks what a transaction IS, not merely which blueprint it names. Note that
    /// <c>MetaData.TransactionType</c> cannot answer it: the SSR's own publications persist as
    /// <c>Control</c> (value 0) alongside every governance transaction, so the post-#876
    /// <c>BlueprintPublish</c> query matches nothing on a real node and the Control arm carries the
    /// whole load. <c>TrackingData["transactionType"]</c> is the marker that actually distinguishes
    /// them, written by <see cref="PublishBlueprintAsync"/> and by the bootstrapper.
    /// </para>
    /// </remarks>
    private async Task<List<TransactionModel>> GetBlueprintTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var register = await _registerManager.GetRegisterAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

        if (register is null)
        {
            return new List<TransactionModel>();
        }

        // Pushed down: two index-backed type queries (post-#876 BlueprintPublish + pre-#876 Control),
        // then the publication filter in memory over that small subset.
        var byPublish = await _transactionManager.GetTransactionsByTypeAsync(
            SystemRegisterConstants.SystemRegisterId,
            Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish,
            Sorcha.Register.Models.Enums.TransactionSort.TimeStampDescending,
            cancellationToken: cancellationToken);
        var byControl = await _transactionManager.GetTransactionsByTypeAsync(
            SystemRegisterConstants.SystemRegisterId,
            Sorcha.Register.Models.Enums.TransactionType.Control,
            Sorcha.Register.Models.Enums.TransactionSort.TimeStampDescending,
            cancellationToken: cancellationToken);

        return byPublish.Concat(byControl)
            .Where(IsBlueprintPublication)
            .OrderByDescending(t => t.TimeStamp)
            .ToList();
    }

    /// <summary>
    /// True when a system-register transaction publishes a blueprint, as opposed to merely naming
    /// one (which every governance action does — see <see cref="GetBlueprintTransactionsAsync"/>).
    /// </summary>
    /// <remarks>
    /// Marker value written alongside the publication rather than inferred, so a future control
    /// transaction that happens to carry a BlueprintId cannot re-open #1515 by accident. The
    /// ActionId arm is the pre-marker fallback: a blueprint publication is not an action submission
    /// and carries no action id, while everything governance writes carries one (1 propose,
    /// 2 approve, 4 enact).
    /// </remarks>
    internal static bool IsBlueprintPublication(TransactionModel tx)
    {
        var meta = tx.MetaData;

        if (meta is null
            || string.IsNullOrEmpty(meta.BlueprintId)
            || meta.BlueprintId == "genesis")
        {
            return false;
        }

        if (meta.TransactionType == Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish)
        {
            return true;
        }

        if (meta.TrackingData is not null
            && meta.TrackingData.TryGetValue("transactionType", out var marker)
            && !string.IsNullOrWhiteSpace(marker))
        {
            return string.Equals(marker, nameof(Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish),
                StringComparison.OrdinalIgnoreCase);
        }

        return meta.ActionId is null;
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

        var latestTx = await _transactionManager.GetLatestTransactionAsync(
            SystemRegisterConstants.SystemRegisterId, cancellationToken);

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
    /// How many times this blueprint has been published to the system register, counting this
    /// publication. The first publication is 1.
    /// </summary>
    /// <remarks>
    /// It is NOT a position in the register's transaction list and NOT a docket number. It used to
    /// be the former, which meant publishing an unrelated blueprint — or, after #1515, any
    /// governance activity at all — silently advanced this number for a blueprint nobody had
    /// touched.
    /// </remarks>
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
