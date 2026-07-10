// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Fixtures.TrustLists;

/// <summary>
/// Smoke tests for <see cref="TrustListFixture"/> — the ETSI TS 119 612
/// trusted-list test infrastructure used by the Feature 181 trust rail.
/// </summary>
public sealed class TrustListFixtureTests : IDisposable
{
    private static readonly XNamespace Tsl = TrustListFixture.TslNamespace;

    private readonly TrustListFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void BuildSignedTrustList_DefaultOptions_SignatureVerifies()
    {
        var signed = _fixture.BuildSignedTrustList(_fixture.DefaultOptions());

        TrustListFixture.VerifySignature(signed).Should().BeTrue(
            "an untampered enveloped XMLDSig signature must verify via SignedXml.CheckSignature");
    }

    [Fact]
    public void Tamper_SignedTrustList_SignatureFails()
    {
        var signed = _fixture.BuildSignedTrustList(_fixture.DefaultOptions());

        var tampered = _fixture.Tamper(signed);

        tampered.Should().NotBe(signed);
        TrustListFixture.VerifySignature(tampered).Should().BeFalse(
            "flipping a character inside signed content must invalidate the signature");
    }

    [Fact]
    public void BuildSignedTrustList_DefaultOptions_ParsesWithExpectedSequenceAndGrantedCaQcEntry()
    {
        var signed = _fixture.BuildSignedTrustList(_fixture.DefaultOptions(sequenceNumber: 42));

        var doc = XDocument.Parse(signed);
        var root = doc.Root!;
        root.Name.Should().Be(Tsl + "TrustServiceStatusList");

        root.Element(Tsl + "SchemeInformation")!
            .Element(Tsl + "TSLSequenceNumber")!.Value.Should().Be("42");

        var services = root
            .Element(Tsl + "TrustServiceProviderList")!
            .Elements(Tsl + "TrustServiceProvider")
            .Elements(Tsl + "TSPServices")
            .Elements(Tsl + "TSPService")
            .ToList();

        services.Should().HaveCount(1);

        var serviceInformation = services[0].Element(Tsl + "ServiceInformation")!;
        serviceInformation.Element(Tsl + "ServiceTypeIdentifier")!.Value
            .Should().Be(TrustListFixture.CaQcServiceType);
        serviceInformation.Element(Tsl + "ServiceStatus")!.Value
            .Should().Be(TrustListFixture.GrantedServiceStatus);

        var certBase64 = serviceInformation
            .Element(Tsl + "ServiceDigitalIdentity")!
            .Element(Tsl + "DigitalId")!
            .Element(Tsl + "X509Certificate")!.Value;
        Convert.FromBase64String(certBase64).Should().Equal(_fixture.TestCa.RawData);
    }

    [Fact]
    public void IssueLeaf_SignedByTestCa_ChainsToCa()
    {
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var leaf = _fixture.IssueLeaf("Sorcha Test Leaf", leafKey);

        leaf.Subject.Should().Be("CN=Sorcha Test Leaf");
        leaf.Issuer.Should().Be(_fixture.TestCa.Subject);
    }
}
