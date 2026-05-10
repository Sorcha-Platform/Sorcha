// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.ServiceClients.Did;
using Sorcha.Wallet.Service.Credentials;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Feature 120 US5 / FR-014 — issuer-allowlist equivalence honours
/// <c>alsoKnownAs</c> bidirectionally.
/// </summary>
public class IssuerEquivalenceMatcherTests
{
    private const string CredIss = "did:sorcha:org:abc";
    private const string AllowedDidWeb = "did:web:example.com:orgs:abc";
    private const string OtherIss = "did:sorcha:org:somewhere-else";

    private static IDidResolverRegistry Registry(Action<Mock<IDidResolverRegistry>>? setup = null)
    {
        var m = new Mock<IDidResolverRegistry>();
        setup?.Invoke(m);
        return m.Object;
    }

    [Fact]
    public async Task EmptyAllowlist_ReturnsTrue()
    {
        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, null, Registry());
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task DirectMatch_ReturnsTrue_DoesNotResolve()
    {
        var calls = 0;
        var reg = Registry(m => m.Setup(r => r.ResolveWithAlsoKnownAsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls++)
            .ReturnsAsync((DidDocument?)null));

        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [CredIss], reg);

        ok.Should().BeTrue();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task NoRegistry_DirectMatchOnly()
    {
        (await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [CredIss], null)).Should().BeTrue();
        (await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [AllowedDidWeb], null)).Should().BeFalse();
    }

    [Fact]
    public async Task CredentialIssuerAlsoKnownAsContainsAllowed_ReturnsTrue()
    {
        var reg = Registry(m =>
        {
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(CredIss, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DidDocument { Id = CredIss, AlsoKnownAs = [AllowedDidWeb] });
        });

        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [AllowedDidWeb], reg);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task AllowedDidAlsoKnownAsContainsCredentialIssuer_ReturnsTrue()
    {
        var reg = Registry(m =>
        {
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(CredIss, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DidDocument { Id = CredIss });
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(AllowedDidWeb, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DidDocument { Id = AllowedDidWeb, AlsoKnownAs = [CredIss] });
        });

        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [AllowedDidWeb], reg);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task NoEquivalence_ReturnsFalse()
    {
        var reg = Registry(m =>
        {
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string did, CancellationToken _) => new DidDocument { Id = did });
        });

        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [OtherIss], reg);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task CredentialUnreachable_FallsThroughToStep3()
    {
        var reg = Registry(m =>
        {
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(CredIss, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DidDocument?)null);
            m.Setup(r => r.ResolveWithAlsoKnownAsAsync(AllowedDidWeb, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DidDocument { Id = AllowedDidWeb, AlsoKnownAs = [CredIss] });
        });

        var ok = await IssuerEquivalenceMatcher.IsAcceptedAsync(CredIss, [AllowedDidWeb], reg);
        ok.Should().BeTrue();
    }
}
