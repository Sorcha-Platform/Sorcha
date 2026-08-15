// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.WorkloadIdentity;

namespace Sorcha.WorkloadIdentity.Tests;

public class SpiffeIdTests
{
    [Fact]
    public void ForService_BuildsCanonicalUri()
    {
        var id = SpiffeId.ForService("sorcha", "service-blueprint");

        id.ToString().Should().Be("spiffe://sorcha/service/service-blueprint");
        id.TrustDomain.Should().Be("sorcha");
        id.ClientId.Should().Be("service-blueprint");
    }

    [Fact]
    public void ForService_LowercasesTrustDomain()
    {
        var id = SpiffeId.ForService("Sorcha-Dev", "service-wallet");

        id.TrustDomain.Should().Be("sorcha-dev");
        id.ToString().Should().Be("spiffe://sorcha-dev/service/service-wallet");
    }

    [Fact]
    public void ForService_PreservesClientIdCase()
    {
        // client_id is an exact ServicePrincipal key — no normalisation is permitted.
        var id = SpiffeId.ForService("sorcha", "Register-Service");

        id.ClientId.Should().Be("Register-Service");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForService_EmptyInstallation_Throws(string? installation)
    {
        var act = () => SpiffeId.ForService(installation!, "service-blueprint");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForService_EmptyClientId_Throws(string? clientId)
    {
        var act = () => SpiffeId.ForService("sorcha", clientId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParse_RoundTripsForServiceOutput()
    {
        var original = SpiffeId.ForService("sorcha", "validator-service");

        SpiffeId.TryParse(original.ToString(), out var parsed).Should().BeTrue();

        parsed.Should().NotBeNull();
        parsed!.Should().Be(original);
    }

    [Theory]
    [InlineData("https://sorcha/service/service-blueprint")]          // wrong scheme
    [InlineData("spiffe://sorcha/workload/service-blueprint")]        // wrong path segment
    [InlineData("spiffe://sorcha/service/")]                          // empty client id
    [InlineData("spiffe://sorcha/service")]                           // missing client id segment
    [InlineData("spiffe://sorcha/service/a/b")]                       // extra path segment
    [InlineData("spiffe:///service/service-blueprint")]               // empty trust domain
    [InlineData("spiffe://sorcha")]                                   // no path
    [InlineData("not-a-uri")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsNonWorkloadShapes(string? value)
    {
        SpiffeId.TryParse(value, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void Equality_IsOrdinalOnClientId()
    {
        // A case-insensitive comparison here would let service-Blueprint impersonate
        // service-blueprint; identity matching MUST be Ordinal.
        var lower = SpiffeId.ForService("sorcha", "service-blueprint");
        var upper = SpiffeId.ForService("sorcha", "service-Blueprint");

        lower.Should().NotBe(upper);
    }

    [Fact]
    public void Equality_DiffersAcrossInstallations()
    {
        var a = SpiffeId.ForService("sorcha", "service-blueprint");
        var b = SpiffeId.ForService("other-install", "service-blueprint");

        a.Should().NotBe(b);
    }
}
