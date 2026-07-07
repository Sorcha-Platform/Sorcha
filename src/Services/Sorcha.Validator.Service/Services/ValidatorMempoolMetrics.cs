// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// OpenTelemetry metric for validator mempool lease expiry on the
/// <c>Sorcha.Validator.Mempool</c> meter source. The size gauge originally
/// shipped with this PR was removed in claude-review round 1 because the
/// Redis-backed implementation didn't expose totals — better no metric than
/// a flatlined zero. A per-register size gauge is tracked as a follow-up.
/// </summary>
/// <remarks>
/// Singleton — both <see cref="InMemoryVerifiedTransactionQueue"/> and
/// <see cref="RedisVerifiedTransactionQueue"/> inject this and call
/// <see cref="RecordLeaseExpired"/> when the auto-release path moves a
/// transaction from claimed back to available. New meter source — register
/// via <c>metrics.AddMeter("Sorcha.Validator.Mempool")</c>.
/// </remarks>
public sealed class ValidatorMempoolMetrics : IDisposable
{
    /// <summary>The meter source name.</summary>
    public const string MeterName = "Sorcha.Validator.Mempool";

    private readonly Meter _meter;
    private readonly Counter<long> _leaseExpired;
    private readonly Counter<long> _unregisteredWithPending;
    private readonly Counter<long> _validationCycleTimeout;
    private readonly Counter<long> _validationSlotReclaimed;

    /// <summary>Creates the meter and registers the counter instrument.</summary>
    public ValidatorMempoolMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _leaseExpired = _meter.CreateCounter<long>(
            name: "sorcha_validator_mempool_lease_expired_total",
            unit: "{lease}",
            description: "Validator mempool leases that auto-released because they expired without ConfirmAsync. High values indicate the validator is dying mid-seal or lease duration is too short.");

        _unregisteredWithPending = _meter.CreateCounter<long>(
            name: "sorcha_validator_monitoring_unregistered_with_pending_total",
            unit: "{register}",
            description: "Registers un-monitored (genesis-failure or roster-change) while unverified transactions were still pending — the orphans are left to the pool retry-limit eviction (Gap B). Non-zero values need admin attention (issue #787 Gap A).");

        _validationCycleTimeout = _meter.CreateCounter<long>(
            name: "sorcha_validator_validation_cycle_timeout_total",
            unit: "{register}",
            description: "Per-register validation cycles aborted for exceeding ValidationTimeout — the guard that stops a stale Redis/gRPC connection after idle from hanging and wedging the whole validation loop (issue #814). Sustained non-zero values mean a backend connection is unhealthy.");

        _validationSlotReclaimed = _meter.CreateCounter<long>(
            name: "sorcha_validator_validation_slot_reclaimed_total",
            unit: "{register}",
            description: "Stuck per-register validation slots reclaimed because a prior cycle never released the in-memory flag (a hung await that outlived its timeout). Belt-and-braces recovery for the issue #814 wedge; any non-zero value means the timeout guard was itself bypassed and needs investigation.");
    }

    /// <summary>
    /// Records that <paramref name="count"/> lease(s) for the given register
    /// auto-released because their lease expiry passed without ConfirmAsync.
    /// Counter is no-op on zero/negative count.
    /// </summary>
    public void RecordLeaseExpired(string registerId, int count = 1)
    {
        if (count <= 0) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        _leaseExpired.Add(count, new KeyValuePair<string, object?>("register_id", registerId));
    }

    /// <summary>
    /// Records that a register was un-monitored while <paramref name="pendingCount"/> unverified
    /// transactions were still in its pool (issue #787 Gap A). Increments the counter once, tagged
    /// with the <paramref name="reason"/> — <c>"genesis-failure"</c> or <c>"roster-change"</c>.
    /// </summary>
    /// <param name="registerId">The register that was un-monitored.</param>
    /// <param name="reason">Why the register was un-monitored: <c>"genesis-failure"</c> | <c>"roster-change"</c>.</param>
    /// <param name="pendingCount">The number of orphaned pending transactions (surfaced as a tag).</param>
    public void RecordUnregisteredWithPending(string registerId, string reason, long pendingCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _unregisteredWithPending.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("register_id", registerId),
            new KeyValuePair<string, object?>("pending_count", pendingCount));
    }

    /// <summary>
    /// Records that a per-register validation cycle was aborted for exceeding ValidationTimeout
    /// (issue #814 — the stale-connection hang guard fired). Increments once, tagged with the register.
    /// </summary>
    public void RecordValidationCycleTimeout(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        _validationCycleTimeout.Add(1, new KeyValuePair<string, object?>("register_id", registerId));
    }

    /// <summary>
    /// Records that a stuck per-register validation slot was reclaimed because a prior cycle never
    /// released it (issue #814 belt-and-braces recovery). Increments once, tagged with the register.
    /// </summary>
    public void RecordValidationSlotReclaimed(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        _validationSlotReclaimed.Add(1, new KeyValuePair<string, object?>("register_id", registerId));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
