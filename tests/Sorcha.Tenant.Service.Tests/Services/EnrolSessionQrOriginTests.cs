// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// The enrolment QR sends the citizen's phone to the <b>wallet PWA</b>, which this installation
/// serves at <c>/wallet/enrol</c> — so the QR must carry this installation's own origin.
/// <para>
/// Found live on n1: <c>EnrolSession:CouncilOrigin</c> was unset, so the QR fell back to the
/// hard-coded <c>https://strathcarron.gov</c> — a fictional demo council — and scanning it went
/// nowhere. Every real deployment already configures its public origin (it is what verification
/// links are built from), so that is now the fallback.
/// </para>
/// </summary>
public sealed class EnrolSessionQrOriginTests
{
    private static EnrolSessionService CreateService(params (string Key, string Value)[] settings)
    {
        var jwt = new JwtConfiguration
        {
            SigningKey = "test-signing-key-please-be-at-least-32-bytes-long-aaaaa",
            Issuer = "sorcha-test",
            Audiences = ["sorcha:citizen-wallet"],
            AccessTokenLifetimeMinutes = 60,
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new EnrolSessionService(
            Options.Create(jwt),
            new InMemoryAtomicDistributedCache(),
            new Mock<IPlatformUserService>().Object,
            new EnrolSessionMetrics(new TestMeterFactory()),
            config,
            new FakeTimeProvider(),
            NullLogger<EnrolSessionService>.Instance);
    }

    [Fact]
    public async Task QrUrl_FallsBackToTheInstallationsPublicOrigin_NotAFictionalCouncil()
    {
        // n1's actual configuration: the public origin is set, the council override is not.
        var service = CreateService((EnrolSessionService.PublicOriginFallbackConfigKey, "https://n1.sorcha.dev"));

        var response = await service.MintAsync(Guid.NewGuid(), EnrolSessionMode.Standalone, CancellationToken.None);

        response.QrUrl.Should().StartWith("https://n1.sorcha.dev/wallet/enrol?session=");
        response.QrUrl.Should().NotContain("strathcarron.gov");
    }

    [Fact]
    public async Task QrUrl_ExplicitCouncilOrigin_WinsOverThePublicOrigin()
    {
        var service = CreateService(
            (EnrolSessionService.CouncilOriginConfigKey, "https://wallet.example.gov"),
            (EnrolSessionService.PublicOriginFallbackConfigKey, "https://n1.sorcha.dev"));

        var response = await service.MintAsync(Guid.NewGuid(), EnrolSessionMode.Standalone, CancellationToken.None);

        response.QrUrl.Should().StartWith("https://wallet.example.gov/wallet/enrol?session=");
    }

    [Fact]
    public async Task QrUrl_TrailingSlashOnTheOrigin_DoesNotDoubleUpTheSeparator()
    {
        var service = CreateService((EnrolSessionService.PublicOriginFallbackConfigKey, "https://n1.sorcha.dev/"));

        var response = await service.MintAsync(Guid.NewGuid(), EnrolSessionMode.Standalone, CancellationToken.None);

        response.QrUrl.Should().StartWith("https://n1.sorcha.dev/wallet/enrol?session=");
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
            => new(options);

        public void Dispose() { }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
    }
}
