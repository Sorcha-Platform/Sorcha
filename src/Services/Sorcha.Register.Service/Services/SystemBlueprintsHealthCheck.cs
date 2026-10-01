// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Feature 197 (#1466): reports whether the image's system blueprints agree with the system register.
/// Never <see cref="HealthStatus.Unhealthy"/> — drift is a signal to an operator, not a reason to
/// take the node out of rotation.
/// </summary>
public sealed class SystemBlueprintsHealthCheck(
    ISystemBlueprintDriftSnapshot snapshot,
    ISystemRegisterBootstrapStatus bootstrapStatus) : IHealthCheck
{
    /// <summary>The registered health check name.</summary>
    public const string Name = "system-blueprints";

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var entries = snapshot.Entries;
        if (entries is null || entries.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("System blueprint drift not yet computed"));

        // The platform wire name (in-sync, image-ahead, ...) — the same vocabulary as /drift and the gauge's
        // state tag, so an operator correlating the three never translates between spellings.
        var data = entries.ToDictionary(e => e.BlueprintId, e => (object)WireName(e.State), StringComparer.Ordinal);

        // A SyncOnly replica legitimately lacks blueprints until sync delivers them; an owner has
        // no such excuse once bootstrap has completed.
        var missingIsDegraded = bootstrapStatus.IsCompleted
            && bootstrapStatus.Mode != Sorcha.ServiceDefaults.BootstrapMode.SyncOnly;

        var offending = entries
            .Where(e => e.State switch
            {
                SystemBlueprintDriftState.InSync => false,
                SystemBlueprintDriftState.Missing => missingIsDegraded,
                _ => true, // ImageBehind, ImageAhead, Unknown
            })
            .Select(e => $"{e.BlueprintId} ({WireName(e.State)})")
            .ToList();

        return Task.FromResult(offending.Count == 0
            ? HealthCheckResult.Healthy("System blueprints are in sync with the system register", data)
            : HealthCheckResult.Degraded(
                $"System blueprints out of sync: {string.Join(", ", offending)}", data: data));
    }

    private static string WireName(SystemBlueprintDriftState state)
        => JsonNamingPolicy.KebabCaseLower.ConvertName(state.ToString());
}
