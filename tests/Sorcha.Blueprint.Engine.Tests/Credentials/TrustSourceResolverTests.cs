// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

public class TrustSourceResolverTests
{
    private sealed class FakeDirectory : IIssuerDirectory
    {
        public IssuerDirectoryEntry Entry { get; init; } = new();
        public Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken ct) => Task.FromResult(Entry);
    }

    private sealed class FakeAnchors(TrustAnchorSet? set) : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct) => Task.FromResult(set);
    }

    private static IssuerContext Issuer(string id = "did:sorcha:org:test", string? signingKeyId = null, IReadOnlyList<byte[]>? x5c = null)
        => new() { IssuerId = id, SigningKeyId = signingKeyId, X5cChain = x5c, SignatureVerified = true };

    // ---- Register source -----------------------------------------------------

    [Fact]
    public async Task Register_Resolved_NoSigningKey_Vouches()
    {
        var resolver = new RegisterTrustSourceResolver(new FakeDirectory { Entry = new() { Resolved = true, RegisterHeight = 42 } });
        var vouch = await resolver.VouchAsync(Issuer(), new TrustSourceRef { Kind = TrustSourceKind.Register, ConfersAssurance = AssuranceLevel.Low });

        vouch.Vouched.Should().BeTrue();
        var ev = new TrustEvidence();
        vouch.ApplyEvidence!(ev);
        ev.RegisterHeight.Should().Be(42);
    }

    [Fact]
    public async Task Register_Unresolved_Declines()
    {
        var resolver = new RegisterTrustSourceResolver(new FakeDirectory { Entry = new() { Resolved = false } });
        var vouch = await resolver.VouchAsync(Issuer(), new TrustSourceRef { Kind = TrustSourceKind.Register });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.UntrustedIssuer);
    }

    [Fact]
    public async Task Register_SigningKeyNotInAssertionMethod_Declines()
    {
        var resolver = new RegisterTrustSourceResolver(new FakeDirectory
        {
            Entry = new() { Resolved = true, AssertionMethodKeyIds = ["did:sorcha:org:test#key-2"] }
        });
        var vouch = await resolver.VouchAsync(Issuer(signingKeyId: "did:sorcha:org:test#key-1"),
            new TrustSourceRef { Kind = TrustSourceKind.Register });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.UntrustedIssuer);
    }

    [Fact]
    public async Task Register_SigningKeyInAssertionMethod_Vouches()
    {
        var resolver = new RegisterTrustSourceResolver(new FakeDirectory
        {
            Entry = new() { Resolved = true, AssertionMethodKeyIds = ["did:sorcha:org:test#key-1"] }
        });
        var vouch = await resolver.VouchAsync(Issuer(signingKeyId: "did:sorcha:org:test#key-1"),
            new TrustSourceRef { Kind = TrustSourceKind.Register, ConfersAssurance = AssuranceLevel.Substantial });

        vouch.Vouched.Should().BeTrue();
        vouch.Assurance.Should().Be(AssuranceLevel.Substantial);
    }

    // ---- DID-allowlist source ------------------------------------------------

    [Fact]
    public async Task Allowlist_DirectMatch_Vouches()
    {
        var resolver = new DidAllowlistTrustSourceResolver(new FakeDirectory());
        var vouch = await resolver.VouchAsync(Issuer("did:sorcha:org:gov"),
            new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = ["did:sorcha:org:gov"] });

        vouch.Vouched.Should().BeTrue();
    }

    [Fact]
    public async Task Allowlist_EquivalenceMatch_Vouches()
    {
        var resolver = new DidAllowlistTrustSourceResolver(new FakeDirectory
        {
            Entry = new() { Resolved = true, AlsoKnownAs = ["did:web:gov.example"] }
        });
        var vouch = await resolver.VouchAsync(Issuer("did:sorcha:org:gov"),
            new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = ["did:web:gov.example"] });

        vouch.Vouched.Should().BeTrue();
    }

    [Fact]
    public async Task Allowlist_NoMatch_Declines()
    {
        var resolver = new DidAllowlistTrustSourceResolver(new FakeDirectory { Entry = new() { Resolved = true } });
        var vouch = await resolver.VouchAsync(Issuer("did:sorcha:org:other"),
            new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = ["did:sorcha:org:gov"] });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.UntrustedIssuer);
    }

    [Fact]
    public async Task Allowlist_Empty_Declines()
    {
        var resolver = new DidAllowlistTrustSourceResolver(new FakeDirectory());
        var vouch = await resolver.VouchAsync(Issuer(),
            new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = [] });

        vouch.Vouched.Should().BeFalse();
    }

    // ---- X.509-tenant source -------------------------------------------------

    private static (byte[] leafDer, byte[] rootDer, byte[] otherRootDer) BuildChain()
    {
        var now = DateTimeOffset.UtcNow.AddDays(-1);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootReq = new CertificateRequest("CN=Sorcha Test Root", rootKey, HashAlgorithmName.SHA256);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootReq.CreateSelfSigned(now, now.AddYears(1));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leafReq = new CertificateRequest("CN=Sorcha Test Leaf", leafKey, HashAlgorithmName.SHA256);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        using var leaf = leafReq.Create(root, now, now.AddMonths(1), new byte[] { 1, 2, 3, 4 });

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherReq = new CertificateRequest("CN=Other Root", otherKey, HashAlgorithmName.SHA256);
        otherReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var otherRoot = otherReq.CreateSelfSigned(now, now.AddYears(1));

        return (leaf.Export(X509ContentType.Cert), root.Export(X509ContentType.Cert), otherRoot.Export(X509ContentType.Cert));
    }

    [Fact]
    public async Task X509_ValidChainToTrustedRoot_Vouches()
    {
        var (leaf, root, _) = BuildChain();
        var resolver = new X509TenantTrustSourceResolver(new FakeAnchors(
            new TrustAnchorSet { Roots = [root], CrlVersion = "v1", CheckRevocation = false }));

        var vouch = await resolver.VouchAsync(Issuer(x5c: [leaf]),
            new TrustSourceRef { Kind = TrustSourceKind.X509Tenant });

        vouch.Vouched.Should().BeTrue();
        var ev = new TrustEvidence();
        vouch.ApplyEvidence!(ev);
        ev.CrlVersion.Should().Be("v1");
    }

    [Fact]
    public async Task X509_LeafNotUnderTrustedRoot_DeclinesChainInvalid()
    {
        var (leaf, _, otherRoot) = BuildChain();
        var resolver = new X509TenantTrustSourceResolver(new FakeAnchors(
            new TrustAnchorSet { Roots = [otherRoot], CheckRevocation = false }));

        var vouch = await resolver.VouchAsync(Issuer(x5c: [leaf]),
            new TrustSourceRef { Kind = TrustSourceKind.X509Tenant });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.ChainInvalid);
    }

    [Fact]
    public async Task X509_NoChain_DeclinesChainInvalid()
    {
        var resolver = new X509TenantTrustSourceResolver(new FakeAnchors(new TrustAnchorSet { Roots = [[1, 2]] }));
        var vouch = await resolver.VouchAsync(Issuer(x5c: null), new TrustSourceRef { Kind = TrustSourceKind.X509Tenant });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.ChainInvalid);
    }

    [Fact]
    public async Task X509_NoAnchors_DeclinesSourceUnavailable()
    {
        var (leaf, _, _) = BuildChain();
        var resolver = new X509TenantTrustSourceResolver(new FakeAnchors(null));
        var vouch = await resolver.VouchAsync(Issuer(x5c: [leaf]), new TrustSourceRef { Kind = TrustSourceKind.X509Tenant });

        vouch.Vouched.Should().BeFalse();
        vouch.Reason.Should().Be(TrustFailureReason.SourceUnavailable);
    }
}
