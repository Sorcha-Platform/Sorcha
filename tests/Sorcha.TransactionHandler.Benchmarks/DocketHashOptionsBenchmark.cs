// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Sorcha.TransactionHandler.Benchmarks;

/// <summary>
/// Perf-audit T10 evidence: the DocketHasher serialize step formerly allocated a fresh
/// <see cref="JsonSerializerOptions"/> per call (once per transaction AND once per docket during sealing).
/// A fresh options instance rebuilds an un-shared converter/metadata cache, so the serialize pays that
/// cost every time. <c>PerCallOptions</c> reproduces the old pattern; <c>SharedOptions</c> is the shipped
/// change. Both produce byte-identical JSON (verified separately by the crypto determinism tests).
/// </summary>
[MemoryDiagnoser]
public class DocketHashOptionsBenchmark
{
    // Representative docket-sealing hash input (matches DocketHasher.ComputeTransactionHash's shape).
    private readonly object _hashInput = new
    {
        TransactionId = "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        PayloadHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        Timestamp = 1_720_080_000_000L,
    };

    private static readonly JsonSerializerOptions SharedHashOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    [Benchmark(Baseline = true, Description = "new JsonSerializerOptions per call (old)")]
    public byte[] PerCallOptions()
    {
        var json = JsonSerializer.Serialize(_hashInput, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        });
        return Encoding.UTF8.GetBytes(json);
    }

    [Benchmark(Description = "shared static JsonSerializerOptions (new)")]
    public byte[] SharedOptions()
    {
        var json = JsonSerializer.Serialize(_hashInput, SharedHashOptions);
        return Encoding.UTF8.GetBytes(json);
    }
}
