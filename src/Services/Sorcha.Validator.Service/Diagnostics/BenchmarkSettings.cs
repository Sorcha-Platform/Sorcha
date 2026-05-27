// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Diagnostics;

/// <summary>
/// Gates the validator's per-rule and per-section telemetry collection.
/// Off by default; turned on for baseline / regression-watch capture runs only.
/// Bound from the <c>Validator:Benchmark</c> configuration section.
/// </summary>
public sealed class BenchmarkSettings
{
    public const string SectionName = "Validator:Benchmark";

    /// <summary>
    /// Master switch. When false, every <see cref="RuleTelemetry"/> call returns
    /// a default struct and the JIT can elide it. No allocation, no atomic ops.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Optional periodic JSON flush. When 0, callers must POST to the flush
    /// endpoint to retrieve the snapshot. Default: 0 (manual flush only).
    /// </summary>
    public int FlushIntervalSeconds { get; init; }

    /// <summary>
    /// Directory the periodic flusher writes JSON snapshots into. Each flush
    /// produces <c>validator-telemetry-{utcTimestamp}.json</c>. Required when
    /// <see cref="FlushIntervalSeconds"/> &gt; 0.
    /// </summary>
    public string? FlushPath { get; init; }

    /// <summary>
    /// Free-form label to embed in every flushed snapshot, e.g. the walkthrough
    /// name and run index. Set via the capture harness, read by the summariser.
    /// </summary>
    public string? CaptureLabel { get; init; }
}
