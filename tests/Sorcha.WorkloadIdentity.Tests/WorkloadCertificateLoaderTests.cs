// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.WorkloadIdentity;

namespace Sorcha.WorkloadIdentity.Tests;

public class WorkloadCertificateLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public WorkloadCertificateLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"workload-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static byte[] MakePfx(string password)
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service");
        return leaf.Export(X509ContentType.Pfx, password);
    }

    [Fact]
    public void Load_FromPfxFilePath_ReturnsCertWithPrivateKey()
    {
        var path = Path.Combine(_tempDir, "client.pfx");
        File.WriteAllBytes(path, MakePfx("pw"));

        using var cert = WorkloadCertificateLoader.Load(path, "pw");

        cert.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Load_FromBase64Blob_ReturnsCertWithPrivateKey()
    {
        var blob = Convert.ToBase64String(MakePfx("pw"));

        using var cert = WorkloadCertificateLoader.Load(blob, "pw");

        cert.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Load_MissingFileThatIsNotBase64_ThrowsNamingTheSource()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.pfx");

        var act = () => WorkloadCertificateLoader.Load(missing, "pw");

        // FR-009: fail-fast with a typed exception naming the configured source —
        // callers must NEVER catch-and-fall-back to the shared secret.
        act.Should().Throw<WorkloadCertificateLoadException>().WithMessage($"*{missing}*");
    }

    [Fact]
    public void Load_WrongPassword_Throws()
    {
        var path = Path.Combine(_tempDir, "client.pfx");
        File.WriteAllBytes(path, MakePfx("correct"));

        var act = () => WorkloadCertificateLoader.Load(path, "wrong");

        act.Should().Throw<WorkloadCertificateLoadException>().WithMessage($"*{path}*");
    }

    [Fact]
    public void Load_GarbageBytesOnDisk_Throws()
    {
        var path = Path.Combine(_tempDir, "garbage.pfx");
        File.WriteAllText(path, "this is not pkcs12");

        var act = () => WorkloadCertificateLoader.Load(path, "pw");

        act.Should().Throw<WorkloadCertificateLoadException>();
    }
}

public class WorkloadCertificateInventoryTests
{
    private static X509Certificate2 CertExpiring(DateTimeOffset notAfter)
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        return WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service",
            notBefore: notAfter.AddYears(-1), notAfter: notAfter);
    }

    [Fact]
    public void Describe_WellOutsideThreshold_IsOk()
    {
        var now = DateTimeOffset.UtcNow;
        using var cert = CertExpiring(now.AddDays(200));

        var status = WorkloadCertificateInventory.Describe("service-leaf", cert, now, thresholdDays: 30);

        status.State.Should().Be(WorkloadCertificateState.Ok);
        status.DaysRemaining.Should().BeInRange(198, 201);
        status.Identity.Should().Be("spiffe://sorcha/service/service-blueprint");
    }

    [Fact]
    public void Describe_InsideThreshold_IsExpiring()
    {
        var now = DateTimeOffset.UtcNow;
        using var cert = CertExpiring(now.AddDays(10));

        var status = WorkloadCertificateInventory.Describe("service-leaf", cert, now, thresholdDays: 30);

        status.State.Should().Be(WorkloadCertificateState.Expiring);
    }

    [Fact]
    public void Describe_ExactlyAtThresholdBoundary_IsExpiring()
    {
        var now = DateTimeOffset.UtcNow;
        using var cert = CertExpiring(now.AddDays(30));

        var status = WorkloadCertificateInventory.Describe("service-leaf", cert, now, thresholdDays: 30);

        status.State.Should().Be(WorkloadCertificateState.Expiring);
    }

    [Fact]
    public void Describe_PastExpiry_IsExpired()
    {
        var now = DateTimeOffset.UtcNow;
        using var cert = CertExpiring(now.AddDays(-1));

        var status = WorkloadCertificateInventory.Describe("service-leaf", cert, now, thresholdDays: 30);

        status.State.Should().Be(WorkloadCertificateState.Expired);
        status.DaysRemaining.Should().BeLessThanOrEqualTo(0);
    }
}
