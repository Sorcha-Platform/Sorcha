// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Sorcha.Validator.Service.Diagnostics;

/// <summary>
/// Lock-free per-rule and per-section telemetry collector. Records counts,
/// emission counts, log-spaced timing histograms, sum, min, max. Designed for
/// permanent gated instrumentation — when <see cref="IsEnabled"/> is false,
/// every public method short-circuits before any allocation or atomic op.
/// </summary>
/// <remarks>
/// <para>Memory footprint when enabled: ~64 buckets × 8 bytes × ~70 codes
/// ≈ 36 KB. Adding a new rule code is a single ConcurrentDictionary insert on
/// first use; subsequent calls are dictionary lookups + Interlocked ops.</para>
///
/// <para>Histogram buckets are powers of two in nanoseconds, from 1 ns
/// (bucket 0) to ~36 hours (bucket 47). Bucket index = ceil(log2(ns)). p50,
/// p95, p99 are recovered from the histogram at flush time using the bucket
/// upper-bound; this is accurate to within a factor of √2 by construction,
/// which is more than enough granularity for baseline-vs-future comparisons.</para>
/// </remarks>
public static class RuleTelemetry
{
    private const int HistogramBuckets = 48;

    private static readonly long s_tickFrequency = Stopwatch.Frequency;
    private static volatile bool s_enabled;
    private static volatile string? s_captureLabel;
    private static long s_startedAtUnixMs;

    private static readonly ConcurrentDictionary<string, RuleStats> s_rules = new();
    private static readonly ConcurrentDictionary<string, RuleStats> s_sections = new();
    private static readonly ConcurrentDictionary<string, long> s_emissions = new();

    /// <summary>True when telemetry is collecting. Volatile read.</summary>
    public static bool IsEnabled => s_enabled;

    /// <summary>
    /// Enable or disable telemetry. Called once at host startup from
    /// <c>BenchmarkExtensions.AddValidatorBenchmarking</c>. Disabling resets
    /// all state.
    /// </summary>
    public static void SetEnabled(bool enabled, string? captureLabel = null)
    {
        s_enabled = enabled;
        s_captureLabel = captureLabel;
        if (enabled)
        {
            s_startedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        else
        {
            s_rules.Clear();
            s_sections.Clear();
            s_emissions.Clear();
        }
    }

    /// <summary>Reset all collected state without flipping the enabled flag.</summary>
    public static void Reset()
    {
        s_rules.Clear();
        s_sections.Clear();
        s_emissions.Clear();
        s_startedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Begin a per-rule timing scope. Returns <c>default</c> when telemetry is
    /// off — the JIT will elide the using block entirely.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuleScope TimeRule(string code)
        => s_enabled ? new RuleScope(code, Stopwatch.GetTimestamp(), isSection: false) : default;

    /// <summary>
    /// Begin a per-section timing scope (e.g. <c>"Structure"</c>, <c>"Schema"</c>).
    /// Sections nest inside <c>ValidateTransactionAsync</c>; one section per call.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuleScope TimeSection(string name)
        => s_enabled ? new RuleScope(name, Stopwatch.GetTimestamp(), isSection: true) : default;

    /// <summary>
    /// Record that a rule code was emitted as a validation error. Cheap counter,
    /// independent of any timing scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RuleEmitted(string code)
    {
        if (!s_enabled) return;
        s_emissions.AddOrUpdate(code, 1, static (_, v) => v + 1);
    }

    internal static void Record(string code, long elapsedTicks, bool isSection)
    {
        if (!s_enabled) return;

        var nanos = TicksToNanos(elapsedTicks);
        var bucket = NanosToBucket(nanos);
        var dict = isSection ? s_sections : s_rules;
        var stats = dict.GetOrAdd(code, static _ => new RuleStats());
        stats.Record(nanos, bucket);
    }

    /// <summary>Snapshot the current state to JSON. Does not reset.</summary>
    public static string SnapshotJson()
    {
        var snapshot = BuildSnapshot();
        return JsonSerializer.Serialize(snapshot, SnapshotJsonContext.Default.TelemetrySnapshot);
    }

    /// <summary>
    /// Snapshot to JSON and reset all state. Used between walkthrough runs by
    /// the capture harness so each run produces a clean per-walkthrough file.
    /// </summary>
    public static string FlushJson()
    {
        var json = SnapshotJson();
        Reset();
        return json;
    }

    internal static TelemetrySnapshot BuildSnapshot()
    {
        var endedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rules = new Dictionary<string, RuleSnapshot>(s_rules.Count);
        foreach (var kv in s_rules)
        {
            rules[kv.Key] = kv.Value.Snapshot(s_emissions.GetValueOrDefault(kv.Key));
        }
        // Include emission-only rules (counted but never timed because they
        // share a block with another timed rule).
        foreach (var kv in s_emissions)
        {
            if (!rules.ContainsKey(kv.Key))
            {
                rules[kv.Key] = new RuleSnapshot
                {
                    Evaluations = 0,
                    Emissions = kv.Value,
                    TotalNanos = 0,
                    MinNanos = 0,
                    MaxNanos = 0,
                    P50Nanos = 0,
                    P95Nanos = 0,
                    P99Nanos = 0,
                };
            }
        }

        var sections = new Dictionary<string, RuleSnapshot>(s_sections.Count);
        foreach (var kv in s_sections)
        {
            sections[kv.Key] = kv.Value.Snapshot(0);
        }

        return new TelemetrySnapshot
        {
            CaptureLabel = s_captureLabel,
            StartedAtUnixMs = s_startedAtUnixMs,
            EndedAtUnixMs = endedAt,
            TickFrequency = s_tickFrequency,
            Sections = sections,
            Rules = rules,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long TicksToNanos(long ticks)
    {
        // ticks * 1_000_000_000 / Stopwatch.Frequency, computed without overflow
        // for typical sub-second deltas (Frequency is usually 10_000_000 on Win
        // and 1_000_000_000 on Linux).
        return s_tickFrequency switch
        {
            1_000_000_000 => ticks,
            10_000_000 => ticks * 100,
            _ => (long)((double)ticks * 1_000_000_000.0 / s_tickFrequency),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NanosToBucket(long nanos)
    {
        if (nanos <= 1) return 0;
        var leading = System.Numerics.BitOperations.LeadingZeroCount((ulong)nanos);
        var bucket = 63 - leading;
        if (bucket >= HistogramBuckets) return HistogramBuckets - 1;
        return bucket;
    }

    internal static long BucketUpperBoundNanos(int bucket) => 1L << (bucket + 1);

    private sealed class RuleStats
    {
        private long _count;
        private long _totalNanos;
        private long _minNanos = long.MaxValue;
        private long _maxNanos;
        private readonly long[] _histogram = new long[HistogramBuckets];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Record(long nanos, int bucket)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _totalNanos, nanos);
            Interlocked.Increment(ref _histogram[bucket]);
            // Min/max — cheap CAS loop; uncontended on the common path.
            long observed;
            do { observed = _minNanos; if (nanos >= observed) break; }
            while (Interlocked.CompareExchange(ref _minNanos, nanos, observed) != observed);
            do { observed = _maxNanos; if (nanos <= observed) break; }
            while (Interlocked.CompareExchange(ref _maxNanos, nanos, observed) != observed);
        }

        public RuleSnapshot Snapshot(long emissions)
        {
            var count = Interlocked.Read(ref _count);
            var total = Interlocked.Read(ref _totalNanos);
            var min = Interlocked.Read(ref _minNanos);
            var max = Interlocked.Read(ref _maxNanos);
            if (min == long.MaxValue) min = 0;

            // Snapshot histogram (no sync — racy reads are tolerable, we lose
            // at most a handful of samples in the percentile calc).
            var histo = new long[HistogramBuckets];
            for (var i = 0; i < HistogramBuckets; i++)
            {
                histo[i] = Interlocked.Read(ref _histogram[i]);
            }

            return new RuleSnapshot
            {
                Evaluations = count,
                Emissions = emissions,
                TotalNanos = total,
                MinNanos = min,
                MaxNanos = max,
                P50Nanos = Percentile(histo, count, 0.50, max),
                P95Nanos = Percentile(histo, count, 0.95, max),
                P99Nanos = Percentile(histo, count, 0.99, max),
            };
        }

        private static long Percentile(long[] histo, long total, double q, long max)
        {
            if (total == 0) return 0;
            var target = (long)Math.Ceiling(total * q);
            long running = 0;
            for (var i = 0; i < histo.Length; i++)
            {
                running += histo[i];
                if (running >= target)
                {
                    var upper = BucketUpperBoundNanos(i);
                    return Math.Min(upper, max);
                }
            }
            return max;
        }
    }
}
