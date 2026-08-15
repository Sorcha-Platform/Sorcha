// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.WorkloadIdentity;

namespace Sorcha.WorkloadIdentity.Tests;

public class WorkloadTrustBundleTests
{
    private static (X509Certificate2 Ca, X509Certificate2 Leaf) IssuePair(string installation, string clientId, string dns)
    {
        var ca = WorkloadCertificateAuthority.CreateCertificateAuthority(installation);
        var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService(installation, clientId), dns);
        return (ca, leaf);
    }

    [Fact]
    public void Validate_LeafSignedByBundledRoot_Succeeds()
    {
        var (ca, leaf) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { ca }));

        bundle.Validate(leaf, out var reason).Should().BeTrue(reason);
        reason.Should().BeNull();
    }

    [Fact]
    public void Validate_LeafFromForeignCa_IsRefused()
    {
        var (_, foreignLeaf) = IssuePair("other-install", "service-blueprint", "blueprint-service");
        var (trustedCa, _) = IssuePair("sorcha", "service-wallet", "wallet-service");
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { trustedCa }));

        bundle.Validate(foreignLeaf, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_SelfSignedCertificateNotInBundle_IsRefused()
    {
        // A self-signed cert is its own root — CustomRootTrust must still refuse it
        // unless that exact root is in the bundle.
        var rogue = WorkloadCertificateAuthority.CreateCertificateAuthority("rogue");
        var (trustedCa, _) = IssuePair("sorcha", "service-wallet", "wallet-service");
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { trustedCa }));

        bundle.Validate(rogue, out _).Should().BeFalse();
    }

    [Fact]
    public void Validate_ExpiredLeaf_IsRefused()
    {
        var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var expired = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service",
            notBefore: DateTimeOffset.UtcNow.AddDays(-10), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { ca }));

        bundle.Validate(expired, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_NotYetValidLeaf_IsRefused()
    {
        var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var future = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service",
            notBefore: DateTimeOffset.UtcNow.AddDays(1), notAfter: DateTimeOffset.UtcNow.AddDays(365));
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { ca }));

        bundle.Validate(future, out _).Should().BeFalse();
    }

    [Fact]
    public void Validate_MultiRootBundle_AcceptsLeavesOfBothRoots()
    {
        // rotate-ca overlap: bundle carries [new, old]; both generations must validate.
        var (oldCa, oldLeaf) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        var (newCa, newLeaf) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        var bundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { newCa, oldCa }));

        bundle.Validate(oldLeaf, out var oldReason).Should().BeTrue(oldReason);
        bundle.Validate(newLeaf, out var newReason).Should().BeTrue(newReason);
    }

    [Fact]
    public void Validate_AfterRotationComplete_OldRootLeafIsRefused()
    {
        var (oldCa, oldLeaf) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        var (newCa, newLeaf) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        _ = oldCa;
        var singleRootBundle = WorkloadTrustBundle.FromPem(WorkloadTrustBundle.ExportBundlePem(new[] { newCa }));

        singleRootBundle.Validate(oldLeaf, out _).Should().BeFalse();
        singleRootBundle.Validate(newLeaf, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public void FromPem_RoundTripsThroughExport()
    {
        var (caA, _) = IssuePair("sorcha", "service-blueprint", "blueprint-service");
        var (caB, _) = IssuePair("sorcha", "service-wallet", "wallet-service");

        var pem = WorkloadTrustBundle.ExportBundlePem(new[] { caA, caB });
        var bundle = WorkloadTrustBundle.FromPem(pem);

        bundle.Roots.Should().HaveCount(2);
        bundle.Roots.Select(r => r.Thumbprint).Should().BeEquivalentTo(new[] { caA.Thumbprint, caB.Thumbprint });
        // Exported roots must be public-only: the bundle is mounted into every service.
        bundle.Roots.Should().OnlyContain(r => !r.HasPrivateKey);
    }

    [Fact]
    public void FromPem_EmptyOrGarbage_Throws()
    {
        var actEmpty = () => WorkloadTrustBundle.FromPem("");
        var actGarbage = () => WorkloadTrustBundle.FromPem("not a pem at all");

        actEmpty.Should().Throw<WorkloadCertificateLoadException>();
        actGarbage.Should().Throw<WorkloadCertificateLoadException>();
    }

    [Fact]
    public void LoadFromFile_MissingFile_ThrowsNamingThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-bundle-{Guid.NewGuid():N}.pem");

        var act = () => WorkloadTrustBundle.LoadFromFile(missing);

        act.Should().Throw<WorkloadCertificateLoadException>().WithMessage($"*{missing}*");
    }
}
