// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Storage;
using Sorcha.Register.Service.Services.Interfaces;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Health endpoints for recovery sync status monitoring.
/// Reports current recovery state per register with progress and staleness detection.
/// </summary>
public static class RecoveryHealthEndpoints
{
    /// <summary>
    /// Maps recovery health endpoints under /health/sync.
    /// </summary>
    public static void MapRecoveryHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/sync", async (
            IRegisterRecoveryService recoveryService,
            IReadOnlyRegisterRepository repository) =>
        {
            var registers = await repository.GetRegistersAsync();
            var results = new List<RegisterSyncStatus>();

            foreach (var register in registers)
            {
                var state = await recoveryService.GetRecoveryStateAsync(register.Id);

                if (state == null)
                {
                    results.Add(new RegisterSyncStatus
                    {
                        RegisterId = register.Id,
                        Status = "synced",
                        CurrentDocket = register.Height > 0 ? (long)(register.Height - 1) : 0,
                        TargetDocket = register.Height > 0 ? (long)(register.Height - 1) : 0,
                        ProgressPercent = 100,
                        DocketsProcessed = 0,
                        IsStale = false
                    });
                    continue;
                }

                var gap = state.NetworkHeadDocket - state.LocalLatestDocket;
                var totalGap = state.NetworkHeadDocket - (state.NetworkHeadDocket - gap);
                var progressPercent = totalGap > 0
                    ? (int)(state.DocketsProcessed * 100 / totalGap)
                    : 100;

                var lastProgressAge = DateTimeOffset.UtcNow - state.LastProgressAt;
                var isStale = state.Status == RecoveryStatus.Recovering &&
                              lastProgressAge.TotalSeconds > 10;

                results.Add(new RegisterSyncStatus
                {
                    RegisterId = state.RegisterId,
                    Status = state.Status.ToString().ToLowerInvariant(),
                    CurrentDocket = state.LocalLatestDocket,
                    TargetDocket = state.NetworkHeadDocket,
                    ProgressPercent = progressPercent,
                    DocketsProcessed = state.DocketsProcessed,
                    LastError = state.LastError,
                    IsStale = isStale
                });
            }

            var overallStatus = results.All(r => r.Status == "synced") ? "synced"
                : results.Any(r => r.Status == "stalled") ? "stalled"
                : "recovering";

            return Results.Ok(new SyncHealthResponse
            {
                Status = overallStatus,
                Registers = results,
                CheckedAt = DateTimeOffset.UtcNow
            });
        })
        .WithName("GetRecoverySyncStatus")
        .WithSummary("Get recovery sync status for all registers")
        .WithDescription(
            "Returns the current recovery/sync status for all local registers. " +
            "Status is 'synced' when up-to-date, 'recovering' when catching up, " +
            "or 'stalled' when recovery has stopped due to errors. " +
            "Includes progress percentage, docket counts, and staleness detection (<10s threshold).")
        .WithTags("Health");
    }

    private record SyncHealthResponse
    {
        public required string Status { get; init; }
        public required List<RegisterSyncStatus> Registers { get; init; }
        public DateTimeOffset CheckedAt { get; init; }
    }

    private record RegisterSyncStatus
    {
        public required string RegisterId { get; init; }
        public required string Status { get; init; }
        public long CurrentDocket { get; init; }
        public long TargetDocket { get; init; }
        public int ProgressPercent { get; init; }
        public long DocketsProcessed { get; init; }
        public string? LastError { get; init; }
        public bool IsStale { get; init; }
    }
}
