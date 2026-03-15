// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that bootstraps the system register on startup using the
/// standard two-phase register creation flow via <see cref="RegisterCreationOrchestrator"/>.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrapper always runs (idempotent). It checks whether the system register exists,
/// creates it via the standard initiate/finalize flow if missing, waits for the genesis docket,
/// and seeds default blueprints.
/// </para>
/// <para>
/// Exceptions are caught and logged to avoid crashing the host. After a maximum
/// of three retries (2s, 4s, 8s exponential backoff), the bootstrapper gives up gracefully.
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "System register bootstrap failed after all retries. The system register may need manual initialization");
        }
    }

    /// <summary>
    /// Attempts bootstrap with exponential backoff retry (2s, 4s, 8s).
    /// Handles partial progress: if the register exists but blueprints are missing,
    /// only the blueprint seeding is retried.
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
                var orchestrator = scope.ServiceProvider.GetRequiredService<IRegisterCreationOrchestrator>();
                var signingService = scope.ServiceProvider.GetRequiredService<ISystemWalletSigningService>();
                var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
                var systemRegisterService = scope.ServiceProvider.GetRequiredService<SystemRegisterService>();
                var signingOptions = scope.ServiceProvider.GetRequiredService<SystemWalletSigningOptions>();

                // Step 1: Check if system register already exists
                var existingRegister = await registerManager.GetRegisterAsync(
                    SystemRegisterConstants.SystemRegisterId, cancellationToken);

                if (existingRegister is null)
                {
                    _logger.LogInformation("System register not found — creating via standard register creation flow");
                    await CreateSystemRegisterAsync(
                        registerManager, orchestrator, signingService, walletClient,
                        signingOptions, cancellationToken);
                }
                else
                {
                    _logger.LogInformation(
                        "System register already exists (Height={Height}, Status={Status})",
                        existingRegister.Height, existingRegister.Status);
                }

                // Step 5: Wait for genesis docket if register height is 0
                await WaitForGenesisDocketAsync(registerManager, cancellationToken);

                // Step 6: Seed default blueprints if missing
                await SeedBlueprintsIfMissingAsync(systemRegisterService, cancellationToken);

                _logger.LogInformation("System register bootstrap completed successfully");
                return;
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
    /// Creates the system register using the standard two-phase register creation flow.
    /// </summary>
    private async Task CreateSystemRegisterAsync(
        RegisterManager registerManager,
        IRegisterCreationOrchestrator orchestrator,
        ISystemWalletSigningService signingService,
        IWalletServiceClient walletClient,
        SystemWalletSigningOptions signingOptions,
        CancellationToken cancellationToken)
    {
        // Get system wallet address for the owner attestation
        var systemWalletAddress = await walletClient.CreateOrRetrieveSystemWalletAsync(
            signingOptions.ValidatorId, cancellationToken);

        _logger.LogInformation(
            "Using system wallet {WalletAddress} for system register bootstrap",
            systemWalletAddress);

        // Step 2: Initiate register creation with deterministic ID
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Description = "Platform-wide system register for blueprint governance and metadata storage",
            TenantId = "system",
            Advertise = true,
            Owners = new List<OwnerInfo>
            {
                new OwnerInfo
                {
                    UserId = "system",
                    WalletId = systemWalletAddress
                }
            }
        };

        var initiateResponse = await orchestrator.InitiateAsync(initiateRequest, cancellationToken);

        _logger.LogInformation(
            "System register initiation complete: RegisterId={RegisterId}, {AttestationCount} attestation(s) to sign",
            initiateResponse.RegisterId, initiateResponse.AttestationsToSign.Count);

        // Step 3: Sign the attestation(s) using the wallet service directly
        // (ISystemWalletSigningService adds an extra SHA256("{txId}:{payloadHash}") layer
        // which is for transaction signing, not attestation signing)
        var signedAttestations = new List<SignedAttestation>();
        foreach (var attestation in initiateResponse.AttestationsToSign)
        {
            // DataToSign is a hex-encoded SHA-256 hash — sign it directly (pre-hashed)
            var hashBytes = Convert.FromHexString(attestation.DataToSign);
            var signResult = await walletClient.SignTransactionAsync(
                systemWalletAddress,
                hashBytes,
                derivationPath: "sorcha:register-attestation",
                isPreHashed: true,
                cancellationToken);

            signedAttestations.Add(new SignedAttestation
            {
                AttestationData = attestation.AttestationData,
                PublicKey = Convert.ToBase64String(signResult.PublicKey),
                Signature = Convert.ToBase64String(signResult.Signature),
                Algorithm = MapAlgorithmString(signResult.Algorithm)
            });

            _logger.LogDebug(
                "Signed attestation for {Subject} ({Role})",
                attestation.AttestationData.Subject,
                attestation.AttestationData.Role);
        }

        // Step 4: Finalize register creation
        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = initiateResponse.Nonce,
            SignedAttestations = signedAttestations
        };

        var finalizeResponse = await orchestrator.FinalizeAsync(finalizeRequest, cancellationToken);

        _logger.LogInformation(
            "System register created successfully: RegisterId={RegisterId}, GenesisTransactionId={GenesisTransactionId}",
            finalizeResponse.RegisterId, finalizeResponse.GenesisTransactionId);
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
        // Check and publish register-creation-v1
        if (!await systemRegisterService.BlueprintExistsAsync("register-creation-v1", cancellationToken))
        {
            _logger.LogInformation("Seeding blueprint: register-creation-v1");
            var creationBlueprint = CreateRegisterCreationBlueprintJson();
            await systemRegisterService.PublishBlueprintAsync(
                "register-creation-v1",
                creationBlueprint,
                "system",
                new Dictionary<string, string> { ["seedReason"] = "bootstrap" },
                cancellationToken);
            _logger.LogInformation("Blueprint register-creation-v1 seeded successfully");
        }
        else
        {
            _logger.LogInformation("Blueprint register-creation-v1 already exists — skipping");
        }

        // Check and publish register-governance-v1
        if (!await systemRegisterService.BlueprintExistsAsync("register-governance-v1", cancellationToken))
        {
            _logger.LogInformation("Seeding blueprint: register-governance-v1");
            var governanceBlueprint = CreateGovernanceBlueprintJson();
            await systemRegisterService.PublishBlueprintAsync(
                "register-governance-v1",
                governanceBlueprint,
                "system",
                new Dictionary<string, string> { ["seedReason"] = "bootstrap" },
                cancellationToken);
            _logger.LogInformation("Blueprint register-governance-v1 seeded successfully");
        }
        else
        {
            _logger.LogInformation("Blueprint register-governance-v1 already exists — skipping");
        }
    }

    /// <summary>
    /// Creates the register-creation-v1 blueprint JSON content.
    /// </summary>
    private static JsonElement CreateRegisterCreationBlueprintJson()
    {
        var json = """
        {
            "@context": "https://sorcha.dev/blueprints/v1",
            "id": "register-creation-v1",
            "name": "Register Creation Workflow",
            "version": "1.0.0",
            "description": "Default workflow for creating a new register in the Sorcha platform",
            "actions": [
                {"id": "validate-request", "name": "Validate Register Creation Request", "type": "validation"},
                {"id": "create-register", "name": "Create New Register", "type": "register-creation"},
                {"id": "publish-transaction", "name": "Publish Register Creation Transaction", "type": "transaction"}
            ]
        }
        """;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Creates the register-governance-v1 blueprint JSON content.
    /// </summary>
    private static JsonElement CreateGovernanceBlueprintJson()
    {
        var json = """
        {
            "@context": "https://sorcha.dev/blueprints/v1",
            "id": "register-governance-v1",
            "name": "Register Governance Workflow",
            "version": "1.0.0",
            "description": "Default governance workflow for managing register policies, membership, and lifecycle",
            "actions": [
                {"id": "propose-change", "name": "Propose Governance Change", "type": "governance-proposal"},
                {"id": "validate-proposal", "name": "Validate Governance Proposal", "type": "validation"},
                {"id": "collect-votes", "name": "Collect Member Votes", "type": "voting"},
                {"id": "apply-change", "name": "Apply Governance Change", "type": "governance-execution"},
                {"id": "publish-update", "name": "Publish Governance Update", "type": "transaction"}
            ]
        }
        """;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// Maps an algorithm string (e.g. "ED25519") to the <see cref="SignatureAlgorithm"/> enum.
    /// </summary>
    private static SignatureAlgorithm MapAlgorithmString(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "ED25519" => SignatureAlgorithm.ED25519,
            "NISTP256" or "P-256" or "P256" => SignatureAlgorithm.NISTP256,
            "RSA4096" or "RSA-4096" => SignatureAlgorithm.RSA4096,
            "ML-DSA-65" or "ML_DSA_65" => SignatureAlgorithm.ML_DSA_65,
            "SLH-DSA-128S" or "SLH_DSA_128S" => SignatureAlgorithm.SLH_DSA_128s,
            _ => throw new ArgumentException($"Unknown signature algorithm: {algorithm}")
        };
    }
}
