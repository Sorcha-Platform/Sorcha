// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.ServiceClients.SystemWallet;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that bootstraps the system register on startup using a
/// pre-signed genesis block. Instances never create a genesis at runtime.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap follows a 4-step flow:
/// 1. Check if system register exists locally (idempotent)
/// 2. Try peer sync (future — currently skipped, handled by Peer Service)
/// 3. Load and ingest pre-signed genesis from configured file or embedded resource
/// 4. Stop if unable to proceed (no genesis file, not rostered, etc.)
/// </para>
/// <para>
/// After genesis confirmation, default blueprints are seeded.
/// Exceptions are caught and logged. After a maximum of three retries
/// (2s, 4s, 8s exponential backoff), the bootstrapper stops gracefully.
/// </para>
/// </remarks>
public class SystemRegisterBootstrapper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemRegisterBootstrapper> _logger;
    private const int MaxRetries = 3;
    private static readonly TimeSpan GenesisTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegisterBootstrapper"/> class.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped dependencies</param>
    /// <param name="logger">Logger instance</param>
    public SystemRegisterBootstrapper(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemRegisterBootstrapper> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the system register bootstrap logic.
    /// </summary>
    /// <param name="stoppingToken">Token that signals when the host is stopping</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow;
            _logger.LogInformation("System register bootstrap started");

            await BootstrapWithRetryAsync(stoppingToken);
            _logger.LogInformation(
                "System register bootstrap completed in {DurationMs}ms",
                (DateTimeOffset.UtcNow - startTime).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("System register bootstrap cancelled due to host shutdown");
        }
        catch (SystemRegisterBootstrapStopException ex)
        {
            // Deliberate stop — not an error, just a hard stop requiring operator action
            _logger.LogCritical(
                "System register bootstrap STOPPED: {Reason}. " +
                "The service cannot proceed without the system register. " +
                "Run 'sorcha system-register create' to initialize a new network or " +
                "deploy with a valid genesis file.",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "System register bootstrap failed after all retries. " +
                "The system register may need manual initialization");
        }
    }

    /// <summary>
    /// Attempts bootstrap with exponential backoff retry (2s, 4s, 8s).
    /// </summary>
    private async Task BootstrapWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                var genesisIngestion = scope.ServiceProvider.GetRequiredService<GenesisIngestionService>();
                var systemRegisterService = scope.ServiceProvider.GetRequiredService<SystemRegisterService>();

                // Step 1: Check if system register already exists locally
                var existingRegister = await registerManager.GetRegisterAsync(
                    SystemRegisterConstants.SystemRegisterId, cancellationToken);

                if (existingRegister is not null)
                {
                    _logger.LogInformation(
                        "System register already exists (Height={Height}, Status={Status})",
                        existingRegister.Height, existingRegister.Status);
                }
                else
                {
                    // Step 2: Peer sync is opportunistic — if the Peer Service has already
                    // replicated the system register from a peer by the time we retry Step 1
                    // (up to 3 retries with exponential backoff: 2s, 4s, 8s), we'll find it
                    // locally and skip directly to blueprint seeding. Otherwise we fall through
                    // to Step 3 (genesis ingestion). The SystemRegisterSyncVerifier in the Peer
                    // Service ensures any synced genesis matches the trusted public key.

                    // Step 3: Load and ingest pre-signed genesis
                    var genesis = await genesisIngestion.LoadAndVerifyGenesisAsync(cancellationToken);
                    if (genesis is null)
                    {
                        // Step 4: Stop — no genesis file, no peers
                        throw new SystemRegisterBootstrapStopException(
                            "No system register genesis file found. " +
                            "No peers available and no genesis file configured or embedded. " +
                            "Run 'sorcha system-register create' to initialize a new network.");
                    }

                    _logger.LogInformation(
                        "System register bootstrap: Network ID = {NetworkId}, Fingerprint = {Fingerprint}",
                        genesis.NetworkId, genesis.GenesisPublicKeyFingerprint);

                    // Check if local validator can seal (is in roster)
                    // For now, attempt ingestion and let the validator handle it
                    var ingested = await genesisIngestion.IngestGenesisAsync(genesis, cancellationToken);
                    if (!ingested)
                    {
                        throw new SystemRegisterBootstrapStopException(
                            "Genesis transaction was rejected by the Validator Service. " +
                            "The local validator may not be in the genesis validator roster. " +
                            "Import the genesis validator key with " +
                            "'sorcha system-register import-validator-key'.");
                    }
                }

                // Wait for genesis docket if register height is 0
                await WaitForGenesisDocketAsync(registerManager, cancellationToken);

                // Seed default blueprints if missing
                await SeedBlueprintsIfMissingAsync(systemRegisterService, cancellationToken);

                _logger.LogInformation("System register bootstrap completed successfully");
                return;
            }
            catch (SystemRegisterBootstrapStopException)
            {
                throw; // Deliberate stop — don't retry
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation — handled by caller
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "System register bootstrap attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}s",
                    attempt, MaxRetries, delay.TotalSeconds);

                if (attempt == MaxRetries)
                {
                    throw; // Final attempt — let outer handler log and swallow
                }

                await Task.Delay(delay, cancellationToken);
                delay *= 2; // Exponential backoff: 2s → 4s → 8s
            }
        }
    }

    /// <summary>
    /// Waits for the genesis docket to be written (Height > 0) with a configurable timeout.
    /// </summary>
    private async Task WaitForGenesisDocketAsync(
        RegisterManager registerManager,
        CancellationToken cancellationToken)
    {
        var registerId = SystemRegisterConstants.SystemRegisterId;
        var deadline = DateTimeOffset.UtcNow.Add(GenesisTimeout);

        _logger.LogInformation("Waiting for genesis docket on system register (timeout: {Timeout}s)", GenesisTimeout.TotalSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var register = await registerManager.GetRegisterAsync(registerId, cancellationToken);
            if (register is not null && register.Height > 0)
            {
                _logger.LogInformation(
                    "Genesis docket confirmed for system register (Height={Height})",
                    register.Height);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        _logger.LogWarning(
            "Timed out waiting for genesis docket on system register after {Timeout}s. " +
            "The Validator Service may not have processed the genesis transaction yet. " +
            "Blueprint seeding will proceed but may fail if the register is not ready.",
            GenesisTimeout.TotalSeconds);
    }

    /// <summary>
    /// Checks for missing seed blueprints and publishes them if needed.
    /// </summary>
    private async Task SeedBlueprintsIfMissingAsync(
        SystemRegisterService systemRegisterService,
        CancellationToken cancellationToken)
    {
        var blueprints = new[] { "register-creation-v1", "register-governance-v1", "create-organisation-v1" };

        foreach (var blueprintId in blueprints)
        {
            if (!await systemRegisterService.BlueprintExistsAsync(blueprintId, cancellationToken))
            {
                _logger.LogInformation("Seeding blueprint: {BlueprintId}", blueprintId);
                var blueprint = LoadBlueprintFromCatalog(blueprintId);
                await systemRegisterService.PublishBlueprintAsync(
                    blueprintId,
                    blueprint,
                    "system",
                    new Dictionary<string, string> { ["seedReason"] = "bootstrap" },
                    cancellationToken);
                _logger.LogInformation("Blueprint {BlueprintId} seeded successfully", blueprintId);
            }
            else
            {
                _logger.LogInformation("Blueprint {BlueprintId} already exists — skipping", blueprintId);
            }
        }
    }

    /// <summary>
    /// Loads a seed blueprint from the template catalog (blueprints/templates/{id}.json).
    /// Extracts the "template" property which contains the actual blueprint content.
    /// </summary>
    private static JsonElement LoadBlueprintFromCatalog(string blueprintId)
    {
        var paths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "blueprints", "templates", $"{blueprintId}.json"),
            Path.Combine("/blueprints", "templates", $"{blueprintId}.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "blueprints", "templates", $"{blueprintId}.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("template", out var template))
                return template.Clone();

            return doc.RootElement.Clone();
        }

        throw new FileNotFoundException(
            $"Blueprint template '{blueprintId}.json' not found in catalog. " +
            $"Searched: {string.Join(", ", paths)}");
    }
}

/// <summary>
/// Thrown when the bootstrapper must stop and wait for operator action.
/// Not an error — a deliberate halt requiring manual intervention.
/// </summary>
internal sealed class SystemRegisterBootstrapStopException : Exception
{
    public SystemRegisterBootstrapStopException(string message) : base(message) { }
}
