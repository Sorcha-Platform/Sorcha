// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Diagnostics;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// Internal benchmark / telemetry endpoints. Mounted under <c>/api/internal/benchmark</c>.
/// Authorization is the standard service-principal policy; in benchmark runs
/// the harness uses the bootstrap admin token. No external exposure.
/// </summary>
public static class BenchmarkEndpoints
{
    public static IEndpointRouteBuilder MapBenchmarkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/internal/benchmark")
            .WithTags("Benchmark")
            .ExcludeFromDescription();

        group.MapGet("/status", () => Results.Ok(new
        {
            enabled = RuleTelemetry.IsEnabled,
        }));

        group.MapGet("/snapshot", () =>
        {
            if (!RuleTelemetry.IsEnabled)
            {
                return Results.Ok(new { enabled = false, message = "telemetry disabled" });
            }
            return Results.Content(RuleTelemetry.SnapshotJson(), "application/json");
        });

        group.MapPost("/flush", () =>
        {
            if (!RuleTelemetry.IsEnabled)
            {
                return Results.Ok(new { enabled = false, message = "telemetry disabled" });
            }
            return Results.Content(RuleTelemetry.FlushJson(), "application/json");
        });

        group.MapPost("/reset", () =>
        {
            RuleTelemetry.Reset();
            return Results.Ok(new { reset = true });
        });

        return app;
    }
}
