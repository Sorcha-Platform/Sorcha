// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Service.Services.Interfaces;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Background service that runs once on startup to ensure each register's bloom
/// filter has at least one address indexed. If the params hash for a register is
/// missing or has <c>address_count == 0</c>, the service rebuilds it via
/// <see cref="IBloomFilterRebuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: bloom registration during normal wallet creation is fire-and-forget
/// and can fail (peer down, transient network) without failing the wallet create.
/// Redis volume wipes also lose bloom state. Without a startup reconciliation, a
/// freshly booted Register Service would silently drop every inbound credential
/// notification because <c>InboundTransactionRouter.MayContainAsync</c> returns
/// false for everyone.
/// </para>
/// <para>
/// Behaviour:
/// <list type="bullet">
///   <item>Runs once at startup, after a short delay so dependent services finish
///         coming up (Redis, Wallet Service gRPC).</item>
///   <item>Iterates every register known to <see cref="RegisterManager"/>.</item>
///   <item>Skips registers whose bloom already reports a non-zero
///         <c>address_count</c> in <see cref="ILocalAddressIndex.GetStatsAsync"/>.</item>
///   <item>For empty/missing blooms, delegates to <see cref="IBloomFilterRebuilder"/>.</item>
///   <item>Failures are logged and skipped — the service does not throw on
///         individual register failures and never blocks ASP.NET startup.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class BloomFilterStartupRebuildService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BloomFilterStartupRebuildService> _logger;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Initialises a new instance of <see cref="BloomFilterStartupRebuildService"/>.
    /// </summary>
    public BloomFilterStartupRebuildService(
        IServiceProvider services,
        ILogger<BloomFilterStartupRebuildService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested) return;

        await using var scope = _services.CreateAsyncScope();
        var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
        var addressIndex = scope.ServiceProvider.GetRequiredService<ILocalAddressIndex>();
        var rebuilder = scope.ServiceProvider.GetRequiredService<IBloomFilterRebuilder>();

        IReadOnlyList<Sorcha.Register.Models.Register> registers;
        try
        {
            registers = (await registerManager.GetAllRegistersAsync(stoppingToken)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bloom startup-rebuild: failed to enumerate registers; skipping.");
            return;
        }

        if (registers.Count == 0)
        {
            _logger.LogInformation("Bloom startup-rebuild: no registers found, nothing to reconcile.");
            return;
        }

        var rebuilt = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var register in registers)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var registerId = register.Id;
            if (string.IsNullOrEmpty(registerId))
            {
                continue;
            }

            try
            {
                var stats = await addressIndex.GetStatsAsync(registerId, stoppingToken);
                if (stats is { AddressCount: > 0 })
                {
                    skipped++;
                    _logger.LogDebug(
                        "Bloom startup-rebuild: register {RegisterId} already has {Count} addresses; skipping.",
                        registerId, stats.AddressCount);
                    continue;
                }

                _logger.LogInformation(
                    "Bloom startup-rebuild: rebuilding register {RegisterId} (current={Count})",
                    registerId, stats?.AddressCount ?? 0);

                var newStats = await rebuilder.RebuildAsync(registerId, stoppingToken);

                rebuilt++;
                _logger.LogInformation(
                    "Bloom startup-rebuild: register {RegisterId} rebuilt with {Count} addresses.",
                    registerId, newStats.AddressCount);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Bloom startup-rebuild: failed for register {RegisterId}; subsequent transactions on this register may miss inbound routing until next admin rebuild.",
                    registerId);
            }
        }

        _logger.LogInformation(
            "Bloom startup-rebuild complete: {Rebuilt} rebuilt, {Skipped} already populated, {Failed} failed (of {Total}).",
            rebuilt, skipped, failed, registers.Count);
    }
}
