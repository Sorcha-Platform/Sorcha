// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Sorcha.Validator.Benchmarks;

/// <summary>
/// Default benchmark job. Release config is set on the project; we run a
/// short job so the suite finishes quickly during smoke runs but is still
/// statistically meaningful for the baseline (≈ 30 s per benchmark method).
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(8)
            .WithLaunchCount(1)
            .WithUnrollFactor(16));
    }
}
