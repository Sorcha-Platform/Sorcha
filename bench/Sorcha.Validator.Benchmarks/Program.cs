// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

var config = ManualConfig
    .CreateMinimumViable()
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.FullCompressed)
    .AddExporter(MarkdownExporter.GitHub)
    .AddLogger(ConsoleLogger.Default)
    .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
