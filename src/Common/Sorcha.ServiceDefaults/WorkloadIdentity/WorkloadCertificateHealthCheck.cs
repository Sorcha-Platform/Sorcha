// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Sorcha.WorkloadIdentity;

namespace Sorcha.ServiceDefaults.WorkloadIdentity;

/// <summary>
/// F191 US4 — in-band workload-certificate expiry surveillance. An expired workload
/// certificate fails far from its cause (a token refresh five minutes before some downstream
/// 401), so every service reports its configured material through the standard health surface:
/// Healthy outside the warning window, Degraded inside it (naming the certificate and expiry
/// date), Unhealthy when expired or unreadable — and Healthy("not configured") in legacy
/// secret mode so dev deployments never fail on the absence of certificates.
/// Also exposes <c>sorcha_workload_cert_days_to_expiry{subject}</c> on the
/// <c>Sorcha.WorkloadIdentity</c> meter for dashboards/alerting.
/// </summary>
public sealed class WorkloadCertificateHealthCheck : IHealthCheck, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkloadCertificateHealthCheck> _logger;
    private readonly Meter _meter;

    public WorkloadCertificateHealthCheck(
        IConfiguration configuration,
        ILogger<WorkloadCertificateHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _meter = new Meter(WorkloadIdentityConfig.MeterName);
        _meter.CreateObservableGauge(
            "sorcha_workload_cert_days_to_expiry",
            ObserveDaysToExpiry,
            unit: "d",
            description: "Days until each configured workload certificate expires (negative when past expiry).");
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sources = ConfiguredSources();
        if (sources.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Workload certificates not configured (legacy shared-secret mode) — not applicable."));
        }

        var threshold = _configuration.GetValue(
            WorkloadIdentityConfig.ExpiryWarningDays, WorkloadIdentityConfig.DefaultExpiryWarningDays);
        var now = DateTimeOffset.UtcNow;

        var worst = HealthStatus.Healthy;
        var details = new List<string>();

        foreach (var (kind, source, password) in sources)
        {
            try
            {
                using var certificate = WorkloadCertificateLoader.Load(source, password);
                var status = WorkloadCertificateInventory.Describe(kind, certificate, now, threshold);
                switch (status.State)
                {
                    case WorkloadCertificateState.Expired:
                        worst = HealthStatus.Unhealthy;
                        details.Add($"{kind} '{status.Subject}' EXPIRED {status.NotAfter:yyyy-MM-dd}");
                        break;
                    case WorkloadCertificateState.Expiring:
                        if (worst == HealthStatus.Healthy) worst = HealthStatus.Degraded;
                        details.Add($"{kind} '{status.Subject}' expires {status.NotAfter:yyyy-MM-dd} ({status.DaysRemaining} days)");
                        break;
                    default:
                        details.Add($"{kind} '{status.Subject}' ok until {status.NotAfter:yyyy-MM-dd}");
                        break;
                }
            }
            catch (WorkloadCertificateLoadException ex)
            {
                worst = HealthStatus.Unhealthy;
                details.Add($"{kind}: unreadable ({ex.Message})");
            }
        }

        var description = string.Join("; ", details);
        return Task.FromResult(new HealthCheckResult(worst, description));
    }

    private IReadOnlyList<(string Kind, string Source, string? Password)> ConfiguredSources()
    {
        var sources = new List<(string, string, string?)>();

        var clientCertificate = _configuration[WorkloadIdentityConfig.ClientCertificate];
        if (!string.IsNullOrWhiteSpace(clientCertificate))
        {
            sources.Add(("workload client certificate", clientCertificate,
                _configuration[WorkloadIdentityConfig.ClientCertificatePassword]));
        }

        var serverCertificate = _configuration[WorkloadIdentityConfig.MtlsServerCertificate];
        if (!string.IsNullOrWhiteSpace(serverCertificate))
        {
            sources.Add(("workload mTLS server certificate", serverCertificate,
                _configuration[WorkloadIdentityConfig.MtlsServerCertificatePassword]));
        }

        return sources;
    }

    private IEnumerable<Measurement<double>> ObserveDaysToExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (_, source, password) in ConfiguredSources())
        {
            X509Certificate2 certificate;
            try
            {
                certificate = WorkloadCertificateLoader.Load(source, password);
            }
            catch (WorkloadCertificateLoadException ex)
            {
                _logger.LogDebug(ex, "Workload certificate unreadable while observing expiry metric");
                continue;
            }

            using (certificate)
            {
                var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                yield return new Measurement<double>(
                    (notAfter - now).TotalDays,
                    new KeyValuePair<string, object?>("subject", certificate.Subject));
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
