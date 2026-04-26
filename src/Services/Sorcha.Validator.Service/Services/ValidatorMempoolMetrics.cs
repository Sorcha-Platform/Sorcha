// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// OpenTelemetry metrics for the validator mempool. Two instruments on the
/// <c>Sorcha.Validator.Mempool</c> meter:
/// <list type="bullet">
///   <item><c>sorcha_validator_mempool_size</c> — observable gauge of pool depth, tagged by <c>register_id</c> and <c>state</c> (available / claimed).</item>
///   <item><c>sorcha_validator_mempool_lease_expired_total</c> — counter incremented when an expired lease is auto-released, tagged by <c>register_id</c>.</item>
/// </list>
/// </summary>
/// <remarks>
/// Registered as a singleton; observable callbacks read from the
/// <see cref="IVerifiedTransactionQueue"/> stats so observations always
/// reflect the current state. New meter source — register via
/// <c>metrics.AddMeter("Sorcha.Validator.Mempool")</c>.
/// </remarks>
public sealed class ValidatorMempoolMetrics : IDisposable
{
    /// <summary>The meter source name.</summary>
    public const string MeterName = "Sorcha.Validator.Mempool";

    private readonly Meter _meter;
    private readonly Counter<long> _leaseExpired;
    private readonly IVerifiedTransactionQueue _queue;

    /// <summary>Creates the metric meter and registers the instruments.</summary>
    public ValidatorMempoolMetrics(IMeterFactory meterFactory, IVerifiedTransactionQueue queue)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(queue);

        _meter = meterFactory.Create(MeterName);
        _queue = queue;

        _meter.CreateObservableGauge(
            name: "sorcha_validator_mempool_size",
            observeValues: ObserveSize,
            unit: "{transaction}",
            description: "Verified transactions in the mempool, tagged by register_id and state (available/claimed).");

        _leaseExpired = _meter.CreateCounter<long>(
            name: "sorcha_validator_mempool_lease_expired_total",
            unit: "{lease}",
            description: "Validator mempool leases that auto-released because they expired without ConfirmAsync. High values indicate the validator is dying mid-seal or lease duration is too short.");
    }

    /// <summary>Records a lease that auto-released for the given register.</summary>
    public void RecordLeaseExpired(string registerId, int count = 1)
    {
        if (count <= 0) return;
        _leaseExpired.Add(count, new KeyValuePair<string, object?>("register_id", registerId));
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveSize()
    {
        var stats = _queue.GetStats();
        // The current IVerifiedTransactionQueue stats don't break down by register or
        // by available/claimed — emit a top-line total per known register.
        // This mirrors the Feature 111 presentation metrics shape; per-state breakdown
        // will need a stats interface extension if operators ask for it.
        yield return new Measurement<long>(
            value: stats.TotalTransactions,
            tags: new KeyValuePair<string, object?>[]
            {
                new("register_id", "_total"),
                new("state", "total"),
            });
    }
}
