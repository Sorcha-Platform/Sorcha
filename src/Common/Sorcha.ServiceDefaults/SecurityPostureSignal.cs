// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Records that a component has fallen back to a weaker security posture (Feature 138, FR-022).
/// When a node cannot establish a trust guarantee it is designed to provide — e.g. it cannot
/// verify status lists so it fails closed, or it cannot establish mTLS for peer transport — it
/// reports the condition here so operators see it on the <c>security-posture</c> health check
/// rather than have security silently weaken. Mirrors the Storage Registration Log pattern
/// (CLAUDE.md §10/§11): a singleton observability surface, not an enforcement gate.
/// </summary>
public interface ISecurityPostureSignal
{
    /// <summary>
    /// Mark <paramref name="component"/> as operating in a degraded security posture. Idempotent:
    /// repeated reports for the same component overwrite the reason. Logs at Warning with the
    /// <c>[SECURITY-POSTURE]</c> banner the first time a component degrades.
    /// </summary>
    /// <param name="component">Stable component key, e.g. <c>status-list-verification</c>, <c>peer-mtls</c>.</param>
    /// <param name="reason">Operator-facing rationale, e.g. "issuer key unresolved; failing closed".</param>
    void ReportDegraded(string component, string reason);

    /// <summary>Clears a previously-reported degradation for <paramref name="component"/> once recovered.</summary>
    void ClearDegraded(string component);

    /// <summary>Returns an immutable snapshot of currently-degraded components and their reasons.</summary>
    IReadOnlyDictionary<string, string> Snapshot();
}

/// <summary>Thread-safe singleton implementation of <see cref="ISecurityPostureSignal"/>.</summary>
public sealed class SecurityPostureSignal : ISecurityPostureSignal
{
    private readonly ConcurrentDictionary<string, string> _degraded = new(StringComparer.Ordinal);
    private readonly ILogger<SecurityPostureSignal> _logger;

    /// <summary>Creates the signal registry.</summary>
    public SecurityPostureSignal(ILogger<SecurityPostureSignal> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public void ReportDegraded(string component, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        var isNew = !_degraded.ContainsKey(component);
        _degraded[component] = reason ?? string.Empty;
        if (isNew)
        {
            _logger.LogWarning(
                "[SECURITY-POSTURE] {Component} degraded: {Reason}", component, reason);
        }
    }

    /// <inheritdoc />
    public void ClearDegraded(string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        if (_degraded.TryRemove(component, out _))
        {
            _logger.LogInformation("[SECURITY-POSTURE] {Component} recovered", component);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_degraded, StringComparer.Ordinal);
}

/// <summary>
/// Health check that reports <see cref="HealthStatus.Degraded"/> while any component has an
/// active security-posture degradation, and <see cref="HealthStatus.Healthy"/> otherwise.
/// Degraded (not Unhealthy) because the node is still functioning correctly and fail-closed —
/// it has simply lost a trust guarantee operators should be alerted to.
/// </summary>
public sealed class SecurityPostureHealthCheck : IHealthCheck
{
    /// <summary>The well-known name used to register this health check.</summary>
    public const string Name = "security-posture";

    private readonly ISecurityPostureSignal _signal;

    /// <summary>Creates the health check bound to the given signal registry.</summary>
    public SecurityPostureHealthCheck(ISecurityPostureSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _signal = signal;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _signal.Snapshot();
        if (snapshot.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No security-posture degradations."));
        }

        var description = "Security posture degraded: " +
            string.Join(", ", snapshot.Select(kv => $"{kv.Key} ({kv.Value})"));
        var data = snapshot.ToDictionary<KeyValuePair<string, string>, string, object>(
            kv => kv.Key, kv => kv.Value);

        return Task.FromResult(HealthCheckResult.Degraded(description, exception: null, data: data));
    }
}

/// <summary>Registration helpers for the security-posture signal and its health check.</summary>
public static class SecurityPostureSignalExtensions
{
    /// <summary>
    /// Registers <see cref="ISecurityPostureSignal"/> as a singleton and adds the
    /// <see cref="SecurityPostureHealthCheck"/> under the name <c>security-posture</c>.
    /// Idempotent — safe to call from multiple services' wiring.
    /// </summary>
    public static IServiceCollection AddSecurityPostureSignal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISecurityPostureSignal, SecurityPostureSignal>();
        services.AddHealthChecks()
            .AddCheck<SecurityPostureHealthCheck>(
                SecurityPostureHealthCheck.Name,
                tags: new[] { "security" });
        return services;
    }
}
