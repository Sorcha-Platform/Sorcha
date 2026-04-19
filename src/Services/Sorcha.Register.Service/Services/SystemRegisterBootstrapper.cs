// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceDefaults;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that bootstraps the system register on startup.
/// Supports three modes: Auto (dev default), SyncOnly (production), GenesisFile (network creation).
/// </summary>
public class SystemRegisterBootstrapper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SystemRegisterBootstrapper> _logger;
    private readonly SystemRegisterOptions _options;
    private const int AutoMaxRetries = 3;
    private static readonly TimeSpan GenesisTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegisterBootstrapper"/> class.
    /// </summary>
    public SystemRegisterBootstrapper(
        IServiceScopeFactory scopeFactory,
        ILogger<SystemRegisterBootstrapper> logger,
        IOptions<SystemRegisterOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // FR-014: Validate bootstrap mode
            if (!Enum.IsDefined(_options.BootstrapMode))
            {
                throw new InvalidOperationException(
                    $"Invalid BootstrapMode '{_options.BootstrapMode}'. " +
                    $"Valid values are: {string.Join(", ", Enum.GetNames<BootstrapMode>())}.");
            }

            // FR-007: Log bootstrap mode and strategy at startup
            _logger.LogInformation(
                "System register bootstrap started — Mode={BootstrapMode}, " +
                "FastRetryInterval={FastRetryIntervalSeconds}s, " +
                "FastRetryDuration={FastRetryDurationSeconds}s, " +
                "BackoffInterval={BackoffIntervalSeconds}s",
                _options.BootstrapMode,
                _options.FastRetryIntervalSeconds,
                _options.FastRetryDurationSeconds,
                _options.BackoffIntervalSeconds);

            var startTime = DateTimeOffset.UtcNow;

            switch (_options.BootstrapMode)
            {
                case BootstrapMode.SyncOnly:
                    await BootstrapSyncOnlyAsync(stoppingToken);
                    break;

                case BootstrapMode.GenesisFile:
                    await BootstrapGenesisFileAsync(stoppingToken);
                    break;

                case BootstrapMode.Auto:
                default:
                    await BootstrapAutoAsync(stoppingToken);
                    break;
            }

            _logger.LogInformation(
                "System register bootstrap completed in {DurationMs}ms (Mode={BootstrapMode})",
                (DateTimeOffset.UtcNow - startTime).TotalMilliseconds,
                _options.BootstrapMode);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("System register bootstrap cancelled due to host shutdown");
        }
        catch (SystemRegisterBootstrapStopException ex)
        {
            _logger.LogCritical(
                "System register bootstrap STOPPED: {Reason}. " +
                "The service cannot proceed without the system register. " +
                "Run 'sorcha system-register create' to initialize a new network or " +
                "deploy with a valid genesis file.",
                ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Invalid BootstrapMode"))
        {
            _logger.LogCritical(ex, "System register bootstrap failed due to invalid configuration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "System register bootstrap failed after all retries. " +
                "The system register may need manual initialization");
        }
    }

    /// <summary>
    /// SyncOnly mode: waits for peer sync indefinitely with two-phase backoff.
    /// Never ingests a genesis file.
    /// </summary>
    private async Task BootstrapSyncOnlyAsync(CancellationToken cancellationToken)
    {
        var fastRetryInterval = TimeSpan.FromSeconds(_options.FastRetryIntervalSeconds);
        var fastRetryDuration = TimeSpan.FromSeconds(_options.FastRetryDurationSeconds);
        var backoffInterval = TimeSpan.FromSeconds(_options.BackoffIntervalSeconds);
        var startTime = DateTimeOffset.UtcNow;
        var attempt = 0;

        // Phase 1: Fast retries
        while (DateTimeOffset.UtcNow - startTime < fastRetryDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            // FR-010: Idempotent check each iteration
            using (var scope = _scopeFactory.CreateScope())
            {
                var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                var register = await registerManager.GetRegisterAsync(
                    SystemRegisterConstants.SystemRegisterId, cancellationToken);

                if (register is not null)
                {
                    _logger.LogInformation(
                        "System register found via peer sync (Height={Height}, Phase=FastRetry, Attempt={Attempt})",
                        register.Height, attempt);
                    await PostBootstrapAsync(registerManager, scope, cancellationToken);
                    return;
                }
            }

            var elapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
            _logger.LogInformation(
                "System register not found — waiting for peer sync " +
                "(Phase=FastRetry, Attempt={Attempt}, ElapsedSeconds={ElapsedSeconds:F0}, " +
                "NextRetrySeconds={NextRetrySeconds})",
                attempt, elapsed, fastRetryInterval.TotalSeconds);

            await Task.Delay(fastRetryInterval, cancellationToken);
        }

        // Phase transition
        _logger.LogInformation(
            "Switching to periodic polling every {BackoffIntervalSeconds}s " +
            "(Phase=BackoffPolling, TotalFastRetryAttempts={Attempt})",
            backoffInterval.TotalSeconds, attempt);

        // Phase 2: Backoff polling (indefinite)
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            using (var scope = _scopeFactory.CreateScope())
            {
                var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                var register = await registerManager.GetRegisterAsync(
                    SystemRegisterConstants.SystemRegisterId, cancellationToken);

                if (register is not null)
                {
                    _logger.LogInformation(
                        "System register found via peer sync (Height={Height}, Phase=BackoffPolling, Attempt={Attempt})",
                        register.Height, attempt);
                    await PostBootstrapAsync(registerManager, scope, cancellationToken);
                    return;
                }
            }

            var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMinutes;
            _logger.LogDebug(
                "System register not found — still polling " +
                "(Phase=BackoffPolling, Attempt={Attempt}, ElapsedMinutes={ElapsedMinutes:F1}, " +
                "NextRetrySeconds={NextRetrySeconds})",
                attempt, elapsed, backoffInterval.TotalSeconds);

            await Task.Delay(backoffInterval, cancellationToken);
        }
    }

    /// <summary>
    /// GenesisFile mode: ingests genesis immediately without peer sync.
    /// </summary>
    private async Task BootstrapGenesisFileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();

        // FR-010: Idempotent check
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
            _logger.LogInformation(
                "Ingesting genesis file directly (BootstrapMode: GenesisFile)");

            var genesisIngestion = scope.ServiceProvider.GetRequiredService<GenesisIngestionService>();
            var genesis = await genesisIngestion.LoadAndVerifyGenesisAsync(cancellationToken);

            if (genesis is null)
            {
                var path = _options.GenesisFile ?? "embedded resource";
                throw new SystemRegisterBootstrapStopException(
                    $"No genesis file found at '{path}'. " +
                    "Ensure GenesisFile is configured or the embedded resource is valid. " +
                    "Run 'sorcha system-register create' to generate a genesis file.");
            }

            _logger.LogInformation(
                "Genesis loaded: Network={NetworkId}, Fingerprint={Fingerprint}",
                genesis.NetworkId, genesis.GenesisPublicKeyFingerprint);

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

        await PostBootstrapAsync(registerManager, scope, cancellationToken);
    }

    /// <summary>
    /// Auto mode: preserves current behaviour — brief peer sync window, then genesis fallback.
    /// </summary>
    private async Task BootstrapAutoAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= AutoMaxRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                var genesisIngestion = scope.ServiceProvider.GetRequiredService<GenesisIngestionService>();

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
                    // Step 2: Peer sync is opportunistic — retries check local store
                    // Step 3: Load and ingest pre-signed genesis
                    var genesis = await genesisIngestion.LoadAndVerifyGenesisAsync(cancellationToken);
                    if (genesis is null)
                    {
                        throw new SystemRegisterBootstrapStopException(
                            "No system register genesis file found. " +
                            "No peers available and no genesis file configured or embedded. " +
                            "Run 'sorcha system-register create' to initialize a new network.");
                    }

                    // FR-012: Warn when Auto mode falls back to embedded genesis
                    _logger.LogWarning(
                        "Ingesting embedded genesis — creating a new local network. " +
                        "Set BootstrapMode to SyncOnly to join an existing network instead. " +
                        "(Network={NetworkId}, Fingerprint={Fingerprint})",
                        genesis.NetworkId, genesis.GenesisPublicKeyFingerprint);

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

                await PostBootstrapAsync(registerManager, scope, cancellationToken);
                return;
            }
            catch (SystemRegisterBootstrapStopException)
            {
                throw; // Deliberate stop — don't retry
            }
            catch (OperationCanceledException)
            {
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "System register bootstrap attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}s",
                    attempt, AutoMaxRetries, delay.TotalSeconds);

                if (attempt == AutoMaxRetries)
                {
                    throw;
                }

                await Task.Delay(delay, cancellationToken);
                delay *= 2;
            }
        }
    }

    /// <summary>
    /// Common post-bootstrap logic: wait for genesis docket, seed blueprints.
    /// </summary>
    private async Task PostBootstrapAsync(
        RegisterManager registerManager,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        await WaitForGenesisDocketAsync(registerManager, cancellationToken);

        var systemRegisterService = scope.ServiceProvider.GetRequiredService<SystemRegisterService>();
        await SeedBlueprintsIfMissingAsync(systemRegisterService, cancellationToken);
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
        var blueprints = new[]
        {
            "register-creation-v1",
            "register-governance-v1",
            "create-organisation-v1",
            // Spec master Phase 2 US11 — audit-trail blueprint for private-register
            // invitation lifecycle. Must be published before the first acceptance so
            // RegisterInvitationService can submit an instance against it.
            "join-private-register-v1",
        };

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
