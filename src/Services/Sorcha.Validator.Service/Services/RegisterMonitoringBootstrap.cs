// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Events;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Feature 108. Seeds <see cref="IRegisterMonitoringRegistry"/> at startup and on every
/// <c>register:relationship-changed</c> Redis event by querying Register.Service for the
/// list of registers whose roster includes this validator's docket-signing public key.
/// A 30-second safety poll reconciles against missed events.
/// </summary>
/// <remarks>
/// Replaces the side-effect enrolment that previously happened inside
/// <c>ValidationEndpoints.ValidateTransaction</c>: monitoring is now authoritative-roster
/// driven rather than submission-driven, so subscribers never attempt to seal registers
/// they aren't on the roster for.
/// </remarks>
public sealed class RegisterMonitoringBootstrap : BackgroundService
{
    private static readonly TimeSpan SafetyPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRegisterMonitoringRegistry _registry;
    private readonly IValidatorKeyProvider _keyProvider;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly IEventSubscriber? _eventSubscriber;
    private readonly ILogger<RegisterMonitoringBootstrap> _logger;

    public RegisterMonitoringBootstrap(
        IServiceScopeFactory scopeFactory,
        IRegisterMonitoringRegistry registry,
        IValidatorKeyProvider keyProvider,
        ValidatorMempoolMetrics metrics,
        ILogger<RegisterMonitoringBootstrap> logger,
        IEventSubscriber? eventSubscriber = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventSubscriber = eventSubscriber;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegisterMonitoringBootstrap starting");

        // Subscribe to relationship-change events once (idempotent if subscriber not available).
        if (_eventSubscriber is not null)
        {
            try
            {
                await _eventSubscriber.SubscribeAsync<RegisterRelationshipChangedEvent>(
                    RegisterEventChannels.RegisterRelationshipChanged,
                    evt => HandleRelationshipChangedAsync(evt, stoppingToken),
                    stoppingToken);

                _logger.LogInformation(
                    "Subscribed to {Channel} — monitoring enrolment will refresh on role changes",
                    RegisterEventChannels.RegisterRelationshipChanged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to subscribe to {Channel} — relying on safety poll loop only",
                    RegisterEventChannels.RegisterRelationshipChanged);
            }
        }

        // Initial seed with brief retry if Register.Service isn't up yet.
        await ReconcileWithRetryAsync(InitialRetryDelay, stoppingToken);

        // Safety poll — every 5 minutes re-run the full enumeration to recover from any
        // missed Redis events.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SafetyPollInterval, stoppingToken);
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RegisterMonitoringBootstrap safety poll errored");
            }
        }

        _logger.LogInformation("RegisterMonitoringBootstrap stopped");
    }

    private async Task ReconcileWithRetryAsync(TimeSpan initialDelay, CancellationToken ct)
    {
        var delay = initialDelay;
        for (var attempt = 1; attempt <= 5 && !ct.IsCancellationRequested; attempt++)
        {
            if (await TryReconcileOnceAsync(ct)) return;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks));
        }
    }

    private async Task ReconcileAsync(CancellationToken ct) => await TryReconcileOnceAsync(ct);

    private async Task<bool> TryReconcileOnceAsync(CancellationToken ct)
    {
        var key = await _keyProvider.GetValidatorPublicKeyAsync(ct);
        if (key is null || key.Length == 0)
        {
            _logger.LogDebug("Monitoring reconcile skipped — validator public key not yet resolvable");
            return false;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            var rosterRegisters = await registerClient.GetMyValidatedRegistersAsync(key, ct);

            // null = the lookup itself failed (HTTP error, network exception). DO NOT prune —
            // a single transient failure used to wedge every monitored register because we
            // treated "lookup failed" the same as "validator is on no rosters" (issue #787).
            // Skip this cycle; the next safety poll will retry.
            if (rosterRegisters is null)
            {
                _logger.LogWarning(
                    "Monitoring reconcile skipped — roster lookup returned no authoritative result. " +
                    "Keeping current monitoring set untouched until the next safety poll.");
                return false;
            }

            var desired = new HashSet<string>(rosterRegisters, StringComparer.Ordinal);
            var current = _registry.GetAll().ToHashSet(StringComparer.Ordinal);

            // Add newly-rostered.
            foreach (var add in desired.Except(current))
            {
                _registry.RegisterForMonitoring(add);
                _logger.LogInformation(
                    "Monitoring enrolled for register {RegisterId} — validator key is on the roster",
                    add);
            }

            // Defence-in-depth: if a roster lookup succeeded but returned a suspiciously small
            // set relative to what we're currently monitoring, refuse to mass-prune in a single
            // cycle. A genuine roster shrinkage from N registers to 0 should be vanishingly
            // rare; far more likely is that the register-service returned a partial result
            // during a deploy / restart / index rebuild. If that's a real change the next
            // safety poll will confirm it and prune.
            var toRemove = current.Except(desired).ToList();
            if (toRemove.Count > 0 && current.Count >= 3 && toRemove.Count == current.Count)
            {
                _logger.LogWarning(
                    "Monitoring reconcile would prune ALL {Count} currently-monitored registers " +
                    "in a single cycle. Refusing as a safety threshold — this is almost certainly " +
                    "a partial roster lookup, not a real roster wipe. Will re-check on next safety poll.",
                    current.Count);
                return false;
            }

            // Remove no-longer-rostered (drain-on-remove semantics live in ValidationEngineService).
            foreach (var remove in toRemove)
            {
                await ReleaseDerosteredRegisterAsync(remove, ct);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Monitoring reconcile failed");
            return false;
        }
    }

    /// <summary>
    /// Issue #787 Gap A: releases a register this validator is no longer on the roster for. Before
    /// un-monitoring, we query the per-register unverified pool count so that releasing a register
    /// with still-pending transactions is observable and alertable rather than silent. This validator
    /// can't seal those transactions (it's off the roster) — they must be handled by the register's
    /// current roster (via replication) or be evicted by the pool retry limit (Gap B, #1092).
    /// </summary>
    /// <remarks>
    /// The pending-count query is guarded: a pool-count failure must NEVER prevent the release, which
    /// always happens. This method does not throw.
    /// </remarks>
    internal async Task ReleaseDerosteredRegisterAsync(string registerId, CancellationToken ct)
    {
        long? pendingCount = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var poller = scope.ServiceProvider.GetRequiredService<ITransactionPoolPoller>();
            pendingCount = await poller.GetUnverifiedCountAsync(registerId, ct);
        }
        catch (Exception countEx)
        {
            _logger.LogWarning(countEx,
                "Failed to read unverified pool count for register {RegisterId} while releasing a de-rostered register — count unknown",
                registerId);
        }

        if (pendingCount is > 0)
        {
            _logger.LogWarning(
                "Monitoring released for register {RegisterId} — validator key no longer on roster — while {PendingCount} unverified transaction(s) are still pending. " +
                "This validator cannot seal them; they must be handled by the register's current roster or evicted by the pool retry limit.",
                registerId, pendingCount);
            _metrics.RecordUnregisteredWithPending(registerId, "roster-change", pendingCount.Value);
        }
        else
        {
            _logger.LogInformation(
                "Monitoring released for register {RegisterId} — validator key no longer on roster{PendingSuffix}",
                registerId, pendingCount is null ? " (pending count unknown)" : string.Empty);
        }

        _registry.UnregisterFromMonitoring(registerId);
    }

    private async Task HandleRelationshipChangedAsync(RegisterRelationshipChangedEvent evt, CancellationToken ct)
    {
        _logger.LogDebug(
            "Relationship change observed for register {RegisterId}: +{Added} / -{Removed}",
            evt.RegisterId, evt.AddedRoles, evt.RemovedRoles);

        // Cheap path: just re-reconcile everything. Accurate and simple.
        await TryReconcileOnceAsync(ct);
    }
}
