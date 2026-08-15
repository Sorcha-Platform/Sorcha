// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.WorkloadIdentity;
using Sorcha.WorkloadIdentity;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// F191 US4 — the in-band expiry warning: Healthy outside the window, Degraded inside it
/// (naming the certificate and expiry), Unhealthy at/past expiry or on unreadable material,
/// and Healthy-not-applicable when no certificate is configured (legacy secret mode).
/// </summary>
public class WorkloadCertificateHealthCheckTests
{
    private static string Base64Pfx(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service",
            notBefore: notBefore, notAfter: notAfter);
        return Convert.ToBase64String(leaf.Export(X509ContentType.Pfx, "pw"));
    }

    private static WorkloadCertificateHealthCheck CreateSut(Dictionary<string, string?> config) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(),
            NullLogger<WorkloadCertificateHealthCheck>.Instance);

    private static Task<HealthCheckResult> CheckAsync(WorkloadCertificateHealthCheck sut) =>
        sut.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                WorkloadIdentityConfig.HealthCheckName, sut, null, null),
        });

    [Fact]
    public async Task Unconfigured_IsHealthyNotApplicable()
    {
        var sut = CreateSut([]);

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task WellOutsideWarningWindow_IsHealthy()
    {
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(400)),
            [WorkloadIdentityConfig.ClientCertificatePassword] = "pw",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task InsideWarningWindow_IsDegraded_NamingCertificateAndExpiry()
    {
        var notAfter = DateTimeOffset.UtcNow.AddDays(10);
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-1), notAfter),
            [WorkloadIdentityConfig.ClientCertificatePassword] = "pw",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("service-blueprint");
        result.Description.Should().Contain(notAfter.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task PastExpiry_IsUnhealthy()
    {
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-100), DateTimeOffset.UtcNow.AddDays(-1)),
            [WorkloadIdentityConfig.ClientCertificatePassword] = "pw",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task UnreadableMaterial_IsUnhealthy()
    {
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] = "not-a-file-and-not-base64!!",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CustomWarningWindow_IsHonoured()
    {
        // 100 days remaining: Healthy under the default 30-day window, Degraded under 180.
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(100)),
            [WorkloadIdentityConfig.ClientCertificatePassword] = "pw",
            [WorkloadIdentityConfig.ExpiryWarningDays] = "180",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task ServerCertificate_IsAlsoInspected()
    {
        // A Tenant-style deployment: healthy client cert but an expiring listener cert must
        // still surface Degraded — the worst state wins.
        var sut = CreateSut(new Dictionary<string, string?>
        {
            [WorkloadIdentityConfig.ClientCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(400)),
            [WorkloadIdentityConfig.ClientCertificatePassword] = "pw",
            [WorkloadIdentityConfig.MtlsServerCertificate] =
                Base64Pfx(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5)),
            [WorkloadIdentityConfig.MtlsServerCertificatePassword] = "pw",
        });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
    }
}
