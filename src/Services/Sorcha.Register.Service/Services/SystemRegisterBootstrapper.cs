// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Service.Configuration;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that bootstraps the system register on startup when
/// the <c>SORCHA_SEED_SYSTEM_REGISTER</c> environment variable is set to <c>true</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrapper runs once during application startup. It checks the seed flag,
/// verifies whether the system register is already initialized (idempotent), and
/// performs initialization with retry logic using exponential backoff.
/// </para>
/// <para>
/// Exceptions are caught and logged to avoid crashing the host. After a maximum
/// of three retries (1s, 2s, 4s backoff), the bootstrapper gives up gracefully.
/// </para>
/// </remarks>
public class SystemRegisterBootstrapper : BackgroundService
{
    private readonly IOptions<SystemRegisterConfiguration> _config;
    private readonly SystemRegisterService _systemRegisterService;
    private readonly ILogger<SystemRegisterBootstrapper> _logger;
    private const int MaxRetries = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemRegisterBootstrapper"/> class.
    /// </summary>
    /// <param name="config">System register configuration options</param>
    /// <param name="systemRegisterService">Service for system register management</param>
    /// <param name="logger">Logger instance</param>
    public SystemRegisterBootstrapper(
        IOptions<SystemRegisterConfiguration> config,
        SystemRegisterService systemRegisterService,
        ILogger<SystemRegisterBootstrapper> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _systemRegisterService = systemRegisterService ?? throw new ArgumentNullException(nameof(systemRegisterService));
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
            if (!_config.Value.SeedSystemRegister)
            {
                _logger.LogInformation("System register seeding is disabled (SORCHA_SEED_SYSTEM_REGISTER != true). Skipping bootstrap");
                return;
            }

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
    /// Attempts bootstrap with exponential backoff retry (1s, 2s, 4s).
    /// </summary>
    private async Task BootstrapWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Check if already initialized (idempotent)
                var isInitialized = await _systemRegisterService.GetCurrentVersionAsync(cancellationToken) > 0;

                if (isInitialized)
                {
                    _logger.LogInformation("System register already initialized — skipping bootstrap");
                    return;
                }

                // Perform initialization
                var result = await _systemRegisterService.InitializeSystemRegisterAsync(cancellationToken);

                if (result)
                {
                    _logger.LogInformation("System register bootstrap completed successfully");
                }
                else
                {
                    _logger.LogInformation("System register was initialized by another instance — skipping");
                }

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
                delay *= 2; // Exponential backoff: 1s → 2s → 4s
            }
        }
    }
}
