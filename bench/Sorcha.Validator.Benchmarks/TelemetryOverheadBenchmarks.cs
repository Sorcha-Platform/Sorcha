// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BenchmarkDotNet.Attributes;
using Sorcha.Validator.Service.Diagnostics;

namespace Sorcha.Validator.Benchmarks;

/// <summary>
/// Measures the cost of the gated telemetry primitives both enabled and
/// disabled. The disabled measurement substantiates the "permanent
/// instrumentation, zero overhead when off" claim — if the disabled path is
/// not effectively free we know to revisit the design.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class TelemetryOverheadBenchmarks
{
    [Params(false, true)]
    public bool TelemetryEnabled;

    [GlobalSetup]
    public void Setup()
    {
        RuleTelemetry.SetEnabled(TelemetryEnabled, captureLabel: "bench");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        RuleTelemetry.SetEnabled(false);
    }

    [Benchmark(Baseline = true)]
    public int Empty_Loop()
    {
        var x = 0;
        for (var i = 0; i < 16; i++) x += i;
        return x;
    }

    [Benchmark]
    public void RuleEmitted_x16()
    {
        for (var i = 0; i < 16; i++)
        {
            RuleTelemetry.RuleEmitted("VAL_BENCH_001");
        }
    }

    [Benchmark]
    public void TimeRule_Empty_x16()
    {
        for (var i = 0; i < 16; i++)
        {
            using var _ = RuleTelemetry.TimeRule("VAL_BENCH_002");
        }
    }

    [Benchmark]
    public void TimeSection_Empty_x16()
    {
        for (var i = 0; i < 16; i++)
        {
            using var _ = RuleTelemetry.TimeSection("Bench");
        }
    }
}
