// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sorcha.ServiceDefaults.Storage;

/// <summary>
/// Health check that reports <see cref="HealthStatus.Degraded"/> when any
/// audited storage interface is registered with an in-memory implementation,
/// and <see cref="HealthStatus.Healthy"/> otherwise.
/// </summary>
/// <remarks>
/// In Production / Staging the
/// <see cref="StorageEnforcementHostedService"/> refuses to start the host
/// when audited interfaces are in-memory, so this check is primarily a
/// signal in Development environments and during the
/// <c>Storage:AllowInMemoryInProduction</c> bypass scenario. Intentionally
/// uses <see cref="HealthStatus.Degraded"/> rather than
/// <see cref="HealthStatus.Unhealthy"/> because a service with an in-memory
/// audited interface is functional but not production-ready.
/// </remarks>
public sealed class StorageProvidersHealthCheck : IHealthCheck
{
    /// <summary>The well-known name used to register this health check.</summary>
    public const string Name = "storage-providers";

    private readonly IStorageRegistrationLog _log;

    /// <summary>Creates a new health check bound to the given registration log.</summary>
    public StorageProvidersHealthCheck(IStorageRegistrationLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _log.Snapshot();
        var inMemoryAudited = snapshot
            .Where(r => r.IsInMemory && r.IsAudited)
            .ToArray();

        if (inMemoryAudited.Length == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                description: $"All {snapshot.Count} registered storage interfaces are persistent."));
        }

        var description = "Audited storage interfaces on in-memory backends: " +
            string.Join(", ", inMemoryAudited.Select(r => $"{r.InterfaceName} → {r.ImplementationName}"));

        var data = inMemoryAudited.ToDictionary<StorageRegistrationRecord, string, object>(
            r => r.InterfaceName,
            r => r.ImplementationName);

        return Task.FromResult(HealthCheckResult.Degraded(description, exception: null, data: data));
    }
}
