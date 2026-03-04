// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MongoDB.Bson;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Repositories;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Service for managing the system register initialization and blueprint publication
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Initialize system register on hub node startup
/// - Seed default blueprints (register-creation-v1)
/// - Validate system register integrity
/// - Provide idempotent initialization (skip if already initialized)
/// </remarks>
public class SystemRegisterService
{
    private readonly ISystemRegisterRepository _repository;
    private readonly ILogger<SystemRegisterService> _logger;
    private const string DefaultBlueprintId = "register-creation-v1";
    private const string GovernanceBlueprintId = "register-governance-v1";

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegisterService"/> class
    /// </summary>
    /// <param name="repository">System register repository</param>
    /// <param name="logger">Logger instance</param>
    public SystemRegisterService(
        ISystemRegisterRepository repository,
        ILogger<SystemRegisterService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Check if already initialized
            var isInitialized = await _repository.IsSystemRegisterInitializedAsync(cancellationToken);

            if (isInitialized)
            {
                _logger.LogInformation("System register already initialized - skipping initialization");

                // Validate integrity
                await ValidateSystemRegisterIntegrityAsync(cancellationToken);

                return false;
            }

            _logger.LogInformation("System register not initialized - beginning initialization");

            // Seed default blueprints
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
    private async Task SeedDefaultBlueprintsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding default blueprints into system register");

        // Create register-creation-v1 blueprint
        var registerCreationBlueprint = CreateRegisterCreationBlueprintDocument();

        await _repository.PublishBlueprintAsync(
            blueprintId: DefaultBlueprintId,
            blueprintDocument: registerCreationBlueprint,
            publishedBy: "system",
            metadata: new Dictionary<string, string>
            {
                { "category", "register" },
                { "type", "creation" },
                { "isDefault", "true" }
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation("Seeded default blueprint: {BlueprintId}", DefaultBlueprintId);

        // Create register-governance-v1 blueprint
        var governanceBlueprint = CreateGovernanceBlueprintDocument();

        await _repository.PublishBlueprintAsync(
            blueprintId: GovernanceBlueprintId,
            blueprintDocument: governanceBlueprint,
            publishedBy: "system",
            metadata: new Dictionary<string, string>
            {
                { "category", "governance" },
                { "type", "register-governance" },
                { "isSystem", "true" },
                { "hasCycles", "true" }
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation("Seeded governance blueprint: {BlueprintId}", GovernanceBlueprintId);
    }

    /// <summary>
    /// Creates the register-creation-v1 blueprint document
    /// </summary>
    /// <returns>BSON document representing the register creation blueprint</returns>
    private static BsonDocument CreateRegisterCreationBlueprintDocument()
    {
        // This is a simplified version - in production this would be a complete JSON-LD blueprint
        var blueprint = new BsonDocument
        {
            { "@context", "https://sorcha.dev/blueprints/v1" },
            { "id", "register-creation-v1" },
            { "name", "Register Creation Workflow" },
            { "version", "1.0.0" },
            { "description", "Default workflow for creating a new register in the Sorcha platform" },
            { "actions", new BsonArray
                {
                    new BsonDocument
                    {
                        { "id", "validate-request" },
                        { "name", "Validate Register Creation Request" },
                        { "type", "validation" }
                    },
                    new BsonDocument
                    {
                        { "id", "create-register" },
                        { "name", "Create New Register" },
                        { "type", "register-creation" }
                    },
                    new BsonDocument
                    {
                        { "id", "publish-transaction" },
                        { "name", "Publish Register Creation Transaction" },
                        { "type", "transaction" }
                    }
                }
            }
        };

        return blueprint;
    }

    /// <summary>
    /// Creates the register-governance-v1 blueprint document
    /// </summary>
    /// <returns>BSON document representing the governance blueprint</returns>
    private static BsonDocument CreateGovernanceBlueprintDocument()
    {
        var blueprint = new BsonDocument
        {
            { "@context", "https://sorcha.dev/blueprints/v1" },
            { "id", "register-governance-v1" },
            { "title", "Register Governance" },
            { "version", "1.0.0" },
            { "description", "Manages admin roster changes for a register via multi-sig quorum workflow" },
            { "participants", new BsonArray
                {
                    new BsonDocument { { "id", "proposer" }, { "name", "Proposer" }, { "organisation", "Register Admin" } },
                    new BsonDocument { { "id", "voter" }, { "name", "Voter" }, { "organisation", "Register Admin" } },
                    new BsonDocument { { "id", "target" }, { "name", "Target" }, { "organisation", "New/Departing Admin" } }
                }
            },
            { "actions", new BsonArray
                {
                    new BsonDocument
                    {
                        { "id", 0 },
                        { "title", "Assert Ownership" },
                        { "sender", "proposer" },
                        { "isStartingAction", true },
                        { "routes", new BsonArray { new BsonDocument { { "id", "genesis-to-propose" }, { "nextActionIds", new BsonArray { 1 } }, { "isDefault", true } } } }
                    },
                    new BsonDocument
                    {
                        { "id", 1 },
                        { "title", "Propose Change" },
                        { "sender", "proposer" },
                        { "routes", new BsonArray
                            {
                                new BsonDocument { { "id", "transfer-skip-quorum" }, { "nextActionIds", new BsonArray { 3 } }, { "condition", new BsonDocument { { "==", new BsonArray { new BsonDocument { { "var", "operationType" } }, "Transfer" } } } } },
                                new BsonDocument { { "id", "owner-override" }, { "nextActionIds", new BsonArray { 3 } }, { "condition", new BsonDocument { { "==", new BsonArray { new BsonDocument { { "var", "ownerOverride" } }, true } } } } },
                                new BsonDocument { { "id", "to-quorum" }, { "nextActionIds", new BsonArray { 2 } }, { "isDefault", true } }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "id", 2 },
                        { "title", "Collect Quorum" },
                        { "sender", "voter" },
                        { "routes", new BsonArray
                            {
                                new BsonDocument { { "id", "quorum-met" }, { "nextActionIds", new BsonArray { 3 } }, { "condition", new BsonDocument { { ">=", new BsonArray { new BsonDocument { { "var", "approvalPercentage" } }, 50.01 } } } } },
                                new BsonDocument { { "id", "quorum-blocked" }, { "nextActionIds", new BsonArray { 1 } }, { "condition", new BsonDocument { { "==", new BsonArray { new BsonDocument { { "var", "quorumBlocked" } }, true } } } } },
                                new BsonDocument { { "id", "collect-more" }, { "nextActionIds", new BsonArray { 2 } }, { "isDefault", true } }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "id", 3 },
                        { "title", "Accept Role" },
                        { "sender", "target" },
                        { "routes", new BsonArray
                            {
                                new BsonDocument { { "id", "accepted" }, { "nextActionIds", new BsonArray { 4 } }, { "condition", new BsonDocument { { "==", new BsonArray { new BsonDocument { { "var", "accepted" } }, true } } } } },
                                new BsonDocument { { "id", "declined" }, { "nextActionIds", new BsonArray { 1 } }, { "isDefault", true } }
                            }
                        }
                    },
                    new BsonDocument
                    {
                        { "id", 4 },
                        { "title", "Record Control Transaction" },
                        { "sender", "proposer" },
                        { "routes", new BsonArray { new BsonDocument { { "id", "loop-back" }, { "nextActionIds", new BsonArray { 1 } }, { "isDefault", true } } } }
                    }
                }
            },
            { "metadata", new BsonDocument
                {
                    { "category", "governance" },
                    { "type", "register-governance" },
                    { "isSystem", "true" },
                    { "hasCycles", "true" }
                }
            }
        };

        return blueprint;
    }

    /// <summary>
    /// Validates system register integrity on startup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task ValidateSystemRegisterIntegrityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating system register integrity");

            // Get all blueprints
            var blueprints = await _repository.GetAllBlueprintsAsync(cancellationToken);

            _logger.LogInformation("System register contains {Count} active blueprints", blueprints.Count);

            // Validate each blueprint has correct RegisterId
            var invalidBlueprints = blueprints
                .Where(b => b.RegisterId != Guid.Empty)
                .Select(b => b.BlueprintId)
                .ToList();

            if (invalidBlueprints.Any())
            {
                var invalidIds = string.Join(", ", invalidBlueprints);
                _logger.LogError("System register integrity check failed - invalid register IDs found: {InvalidIds}", invalidIds);
                throw new InvalidOperationException($"System register integrity check failed - blueprints with invalid register IDs: {invalidIds}");
            }

            // Validate version sequence (should be monotonically increasing)
            var orderedBlueprints = blueprints.OrderBy(b => b.Version).ToList();
            for (int i = 1; i < orderedBlueprints.Count; i++)
            {
                if (orderedBlueprints[i].Version <= orderedBlueprints[i - 1].Version)
                {
                    _logger.LogError("System register integrity check failed - version sequence violation at index {Index}", i);
                    throw new InvalidOperationException($"System register integrity check failed - version sequence is not monotonic");
                }
            }

            _logger.LogInformation("System register integrity validated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System register integrity validation failed");
            throw;
        }
    }

    /// <summary>
    /// Publishes a new blueprint to the system register
    /// </summary>
    /// <param name="blueprintId">Unique blueprint identifier</param>
    /// <param name="blueprintDocument">Blueprint JSON document</param>
    /// <param name="publishedBy">Publisher identity</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published blueprint entry</returns>
    public async Task<SystemRegisterEntry> PublishBlueprintAsync(
        string blueprintId,
        BsonDocument blueprintDocument,
        string publishedBy,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Publishing blueprint {BlueprintId} to system register", blueprintId);

            var entry = await _repository.PublishBlueprintAsync(
                blueprintId,
                blueprintDocument,
                publishedBy,
                metadata,
                cancellationToken);

            _logger.LogInformation("Blueprint {BlueprintId} published with version {Version}",
                blueprintId, entry.Version);

            // TODO: Trigger push notification to connected peers

            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish blueprint {BlueprintId}", blueprintId);
            throw;
        }
    }

    /// <summary>
    /// Gets all blueprints from the system register
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active blueprints</returns>
    public async Task<List<SystemRegisterEntry>> GetAllBlueprintsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllBlueprintsAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a specific blueprint by ID
    /// </summary>
    /// <param name="blueprintId">Blueprint identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Blueprint entry or null</returns>
    public async Task<SystemRegisterEntry?> GetBlueprintAsync(string blueprintId, CancellationToken cancellationToken = default)
    {
        return await _repository.GetBlueprintByIdAsync(blueprintId, cancellationToken);
    }

    /// <summary>
    /// Gets the current system register version (latest blueprint version)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current version number</returns>
    public async Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetLatestVersionAsync(cancellationToken);
    }

    /// <summary>
    /// Publishes a new version of an existing blueprint (or a brand-new blueprint)
    /// to the system register via the <c>control.blueprint.publish</c> governance action.
    /// </summary>
    /// <param name="blueprintId">Unique identifier for the new blueprint entry</param>
    /// <param name="blueprintDocument">Blueprint JSON document</param>
    /// <param name="publishedBy">Publisher identity</param>
    /// <param name="previousBlueprintId">ID of the previous version being superseded (null for new blueprints)</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Published blueprint entry</returns>
    /// <exception cref="InvalidOperationException">Thrown if previousBlueprintId references a non-existent blueprint</exception>
    public async Task<SystemRegisterEntry> PublishBlueprintVersionAsync(
        string blueprintId,
        BsonDocument blueprintDocument,
        string publishedBy,
        string? previousBlueprintId = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentNullException.ThrowIfNull(blueprintDocument);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedBy);

        // Validate the previous version exists if specified
        if (!string.IsNullOrEmpty(previousBlueprintId))
        {
            var previousEntry = await _repository.GetBlueprintByIdAsync(previousBlueprintId, cancellationToken);
            if (previousEntry is null)
            {
                throw new InvalidOperationException(
                    $"Previous blueprint version '{previousBlueprintId}' not found in system register");
            }

            if (!previousEntry.IsActive)
            {
                _logger.LogWarning(
                    "Previous blueprint version '{PreviousBlueprintId}' is deprecated — publishing new version anyway",
                    previousBlueprintId);
            }
        }

        // Merge previousBlueprintId into metadata for traceability
        var effectiveMetadata = metadata ?? new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(previousBlueprintId))
        {
            effectiveMetadata["previousVersionId"] = previousBlueprintId;
        }

        _logger.LogInformation(
            "Publishing blueprint version {BlueprintId} to system register (previous: {PreviousId})",
            blueprintId, previousBlueprintId ?? "none");

        var entry = await _repository.PublishBlueprintAsync(
            blueprintId,
            blueprintDocument,
            publishedBy,
            effectiveMetadata,
            cancellationToken);

        _logger.LogInformation(
            "Blueprint version {BlueprintId} published with global version {Version}",
            blueprintId, entry.Version);

        return entry;
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

        var entry = await _repository.GetBlueprintByIdAsync(blueprintId, cancellationToken);
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
        var isInitialized = await _repository.IsSystemRegisterInitializedAsync(cancellationToken);
        var blueprintCount = isInitialized
            ? await _repository.GetBlueprintCountAsync(cancellationToken)
            : 0;
        var currentVersion = isInitialized
            ? await _repository.GetLatestVersionAsync(cancellationToken)
            : 0;

        // Get earliest blueprint's PublishedAt as the "created" timestamp
        DateTime? createdAt = null;
        if (isInitialized)
        {
            var blueprints = await _repository.GetAllBlueprintsAsync(cancellationToken);
            createdAt = blueprints.MinBy(b => b.PublishedAt)?.PublishedAt;
        }

        return new SystemRegisterInfo
        {
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Status = isInitialized ? "initialized" : "not_initialized",
            BlueprintCount = blueprintCount,
            CurrentVersion = currentVersion,
            CreatedAt = createdAt
        };
    }
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
    /// UTC timestamp when the system register was first initialized (earliest blueprint).
    /// Null if not yet initialized.
    /// </summary>
    public DateTime? CreatedAt { get; init; }
}
