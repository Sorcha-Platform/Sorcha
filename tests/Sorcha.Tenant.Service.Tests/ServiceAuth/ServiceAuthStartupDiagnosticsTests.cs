// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.ServiceAuth;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.Tests.ServiceAuth;

/// <summary>
/// F191 US3 (#1420) — the startup-log requirement: a deployment that has retired shared-secret
/// service authentication must say so at startup, so a mis-flipped node is diagnosable.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the integration-test assertion that read a log sink attached to a
/// <c>WebApplicationFactory</c> host. That sink received <b>no events at all</b> once other
/// Serilog-configured hosts existed in the same test process, so the test failed the whole suite
/// while asserting a security claim — "shared secrets are not disabled" — that its own sibling
/// tests disproved (#1507).
/// </para>
/// <para>
/// The log line is operator diagnostics. It does not need a Kestrel socket, a TLS handshake or a
/// process-wide logging pipeline to be verified — only a logger the test owns. Asserting it here
/// is both deterministic and STRICTLY STRONGER than what it replaced, because it also pins the
/// silent case, which nothing previously covered.
/// </para>
/// </remarks>
public class ServiceAuthStartupDiagnosticsTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static IConfiguration ConfigWith(string? disableSharedSecrets) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(disableSharedSecrets is null
                ? []
                : new Dictionary<string, string?>
                {
                    [WorkloadIdentityConfig.DisableSharedSecrets] = disableSharedSecrets,
                })
            .Build();

    [Fact]
    public void WhenSecretsAreRetired_ItWarnsAndNamesTheConfigurationKey()
    {
        var logger = new CapturingLogger();

        var emitted = ServiceAuthStartupDiagnostics.LogSharedSecretPosture(logger, ConfigWith("true"));

        emitted.Should().BeTrue();
        logger.Entries.Should().ContainSingle();

        var (level, message) = logger.Entries[0];
        level.Should().Be(LogLevel.Warning,
            "an operator scanning startup output filters on severity — an Information line about a " +
            "platform-wide authentication change would be scrolled past");
        message.Should().Contain("DisableSharedSecrets",
            "the line must name the configuration key an operator would have to change to undo it; " +
            "a message describing the effect without the key sends them searching");
        message.ToLowerInvariant().Should().Contain("disabled");
        message.Should().Contain("workload-certificate",
            "it must also say what DOES still work, or the line reads as an outage rather than a posture");
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void WhenSecretsAreNotRetired_ItSaysNothing(string? configured)
    {
        var logger = new CapturingLogger();

        var emitted = ServiceAuthStartupDiagnostics.LogSharedSecretPosture(logger, ConfigWith(configured));

        emitted.Should().BeFalse();
        logger.Entries.Should().BeEmpty(
            "the ordinary posture is not news; a line on every start is noise an operator learns to " +
            "skip, which is what makes the retired-secrets line worth reading when it does appear");
    }

    [Fact]
    public void TheOperatorFacingWording_IsPinned()
    {
        // The message is what an operator greps for and what runbooks quote, so it is a contract in
        // the same way an error code is. Changing it is allowed; changing it silently is not.
        ServiceAuthStartupDiagnostics.SharedSecretsDisabledMessage.Should().Be(
            "ServiceAuth:DisableSharedSecrets is ENABLED — shared-secret service authentication is disabled; " +
            "only workload-certificate credentials mint service tokens (F191/#1420)");
    }
}
