// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Diagnostics;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Wires the validator's gated benchmark instrumentation. Idempotent and
/// no-cost when <c>Validator:Benchmark:Enabled</c> is false.
/// </summary>
public static class BenchmarkExtensions
{
    public static IServiceCollection AddValidatorBenchmarking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(BenchmarkSettings.SectionName);
        services.Configure<BenchmarkSettings>(section);

        var settings = section.Get<BenchmarkSettings>() ?? new BenchmarkSettings();
        RuleTelemetry.SetEnabled(settings.Enabled, settings.CaptureLabel);

        services.AddHostedService<TelemetryFlusherService>();
        return services;
    }
}
