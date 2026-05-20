// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 135 / T039 — the trust-list trust source validates an x5c chain against an operator
/// snapshot and records the snapshot id + freshness in the evidence; a missing snapshot fails closed.
/// </summary>
public class TrustListSourceTests
{
    private sealed class FakeAnchors(TrustAnchorSet? set) : ITenantTrustAnchorProvider
    {
        public string? RequestedAnchorId { get; private set; }
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default)
        {
            RequestedAnchorId = anchorId;
            return Task.FromResult(set);
        }
    }

    private static (byte[] leafDer, byte[] rootDer) BuildChain()
    {
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootReq = new CertificateRequest("CN=Trust List Root", rootKey, HashAlgorithmName.SHA256);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootReq.CreateSelfSigned(now, now.AddYears(1));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafReq = new CertificateRequest("CN=Trust List Leaf", leafKey, HashAlgorithmName.SHA256);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var leaf = leafReq.Create(root, now, now.AddMonths(1), [1, 2, 3, 5]);

        return (leaf.Export(X509ContentType.Cert), root.Export(X509ContentType.Cert));
    }

    private static IssuerContext Issuer(IReadOnlyList<byte[]>? x5c) =>
        new() { IssuerId = "did:sorcha:org:trustlisted", SignatureVerified = true, X5cChain = x5c };

    [Fact]
    public async Task TrustList_ValidChainToSnapshotRoot_Vouches_AndRecordsSnapshotEvidence()
    {
        var (leaf, root) = BuildChain();
        var freshness = DateTimeOffset.UtcNow.AddHours(-2);
        var anchors = new FakeAnchors(new TrustAnchorSet
        {
            Roots = [root], AnchorSetId = "eu-lotl-snap-2026-05", Freshness = freshness, CheckRevocation = false
        });
        var resolver = new TrustListSourceResolver(anchors);

        var vouch = await resolver.VouchAsync(Issuer([leaf]),
            new TrustSourceRef { Kind = TrustSourceKind.TrustList, TrustListId = "eu-lotl-snap-2026-05" });

        resolver.Kind.Should().Be(TrustSourceKind.TrustList);
        anchors.RequestedAnchorId.Should().Be("eu-lotl-snap-2026-05"); // requested by TrustListId
        vouch.Vouched.Should().BeTrue();

        var evidence = new TrustEvidence();
        vouch.ApplyEvidence!(evidence);
        evidence.TrustListId.Should().Be("eu-lotl-snap-2026-05");
        evidence.TrustListFreshness.Should().Be(freshness);
    }

    [Fact]
    public async Task TrustList_MissingSnapshot_FailsClosed_SourceUnavailable()
    {
        var (leaf, _) = BuildChain();
        var resolver = new TrustListSourceResolver(new FakeAnchors(null));

        var vouch = await resolver.VouchAsync(Issuer([leaf]),
            new TrustSourceRef { Kind = TrustSourceKind.TrustList, TrustListId = "unknown" });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.SourceUnavailable);
    }

    [Fact]
    public async Task TrustList_NoX5cChain_DeclinesChainInvalid()
    {
        var resolver = new TrustListSourceResolver(new FakeAnchors(new TrustAnchorSet { Roots = [[1, 2]] }));

        var vouch = await resolver.VouchAsync(Issuer(x5c: null),
            new TrustSourceRef { Kind = TrustSourceKind.TrustList, TrustListId = "x" });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.ChainInvalid);
    }
}
