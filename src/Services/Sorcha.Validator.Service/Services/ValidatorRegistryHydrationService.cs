// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Runs once on startup, ahead of <see cref="DocketBuildTriggerService"/>, to
/// hydrate the Redis-backed <see cref="IValidatorRegistry"/> cache from the
/// MongoDB durable store. This closes the latent cold-Redis bug where a
/// <c>docker compose down -v</c> wipes Redis but leaves Mongo populated, the
/// heartbeat's <c>GetValidatorAsync</c> sees Redis as empty, and
/// <see cref="IValidatorRegistry.RegisterAsync"/> re-registers (growing the
/// order list with duplicates) every ~40s.
///
/// Hydration is best-effort: any exception is logged and swallowed because the
/// runtime fallback to Mongo in <c>GetValidatorAsync</c> / <c>BuildValidatorListAsync</c>
/// already covers the case where this service fails.
/// </summary>
internal sealed class ValidatorRegistryHydrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ValidatorRegistryHydrationService> _logger;

    public ValidatorRegistryHydrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<ValidatorRegistryHydrationService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validator registry hydration starting (Mongo -> Redis)");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IValidatorRegistry>();
            await registry.HydrateFromMongoAsync(cancellationToken);
            _logger.LogInformation("Validator registry hydration complete");
        }
        catch (Exception ex)
        {
            // Best-effort — never block startup. Runtime Mongo fallbacks cover this.
            _logger.LogError(ex,
                "Validator registry hydration failed; relying on runtime Mongo fallback for cold-Redis reads");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
