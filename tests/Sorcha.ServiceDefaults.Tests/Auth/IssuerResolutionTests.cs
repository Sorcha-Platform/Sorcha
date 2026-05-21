// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Auth;

/// <summary>
/// Tests for <see cref="SorchaIssuer"/> — issuer hardening (spec 136, US3, FR-017/FR-018).
/// No shared default: explicit wins, else <c>urn:sorcha:{installation}</c>, else (non-prod)
/// <c>urn:sorcha:dev-local</c>, else fail-closed.
/// </summary>
public sealed class IssuerResolutionTests
{
    [Fact]
    public void Resolve_ExplicitIssuer_Wins()
    {
        SorchaIssuer.Resolve("https://issuer.acme.example", "acme", allowDevLocalFallback: true)
            .Should().Be("https://issuer.acme.example");
    }

    [Fact]
    public void Resolve_ExplicitIssuer_WinsEvenWhenFallbackDisallowed()
    {
        // An explicit issuer is always honoured — fail-closed only applies when nothing is configured.
        SorchaIssuer.Resolve("urn:custom:issuer", installationName: null, allowDevLocalFallback: false)
            .Should().Be("urn:custom:issuer");
    }

    [Fact]
    public void Resolve_ExplicitIssuer_IsTrimmed()
    {
        SorchaIssuer.Resolve("  urn:custom:issuer  ", null, allowDevLocalFallback: true)
            .Should().Be("urn:custom:issuer");
    }

    [Theory]
    [InlineData("acme", "urn:sorcha:acme")]
    [InlineData(" N1 ", "urn:sorcha:n1")]   // normalised (trimmed + lowercased) to match the audience namespace
    [InlineData("Sorcha", "urn:sorcha:sorcha")]
    public void Resolve_NoExplicit_DerivesFromInstallationName(string installation, string expected)
    {
        SorchaIssuer.Resolve(explicitIssuer: null, installation, allowDevLocalFallback: false)
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_NoExplicit_BlankInstallation_WithFallback_YieldsDevLocal()
    {
        SorchaIssuer.Resolve(explicitIssuer: null, installationName: null, allowDevLocalFallback: true)
            .Should().Be(SorchaIssuer.DevLocalIssuer);

        SorchaIssuer.Resolve("   ", "   ", allowDevLocalFallback: true)
            .Should().Be("urn:sorcha:dev-local");
    }

    [Fact]
    public void Resolve_NoExplicit_NoInstallation_NoFallback_FailsClosed()
    {
        var act = () => SorchaIssuer.Resolve(explicitIssuer: null, installationName: null, allowDevLocalFallback: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Issuer is required*")
            .WithMessage("*InstallationName*");
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Local", true)]
    public void AllowsDevLocalFallback_ProductionAndStagingFailClosed(string environmentName, bool expected)
    {
        var env = new FakeHostEnvironment { EnvironmentName = environmentName };

        SorchaIssuer.AllowsDevLocalFallback(env).Should().Be(expected);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
