// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.ServiceClients.Http.Hub;
using Xunit;

namespace Sorcha.ServiceClients.Tests.Hub;

public class SorchaHubConnectionBuilderTests
{
    [Fact]
    public void Build_WithValidParameters_ReturnsNonNullConnection()
    {
        var connection = SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"));

        connection.Should().NotBeNull();
    }

    [Fact]
    public void Build_ReturnsDisconnectedConnection()
    {
        var connection = SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"));

        connection.State.Should().Be(HubConnectionState.Disconnected,
            "connection should not be started automatically");
    }

    [Fact]
    public void Build_WithNullHubUrl_ThrowsArgumentNullException()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            null!,
            () => Task.FromResult<string?>("test-token"));

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("hubUrl");
    }

    [Fact]
    public void Build_WithNullTokenProvider_ThrowsArgumentNullException()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("tokenProvider");
    }

    [Fact]
    public void Build_WithLoggingConfiguration_DoesNotThrow()
    {
        var act = () => SorchaHubConnectionBuilder.Build(
            "https://localhost/hubs/actions",
            () => Task.FromResult<string?>("test-token"),
            logging => { });

        act.Should().NotThrow();
    }

    // --- Feature 118: jittered reconnect schedule (research R-003) -------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RetryPolicy_ReturnsJitteredDelayWithin20PercentOfNominal(int previousRetryCount)
    {
        // Deterministic random — always returns 0.5 (mid-jitter, multiplier == 1.0).
        var policy = new SorchaHubConnectionBuilder.InfiniteRetryPolicy(static () => 0.5);
        var nominal = SorchaHubConnectionBuilder.InfiniteRetryPolicy.NominalDelays[previousRetryCount];

        var actual = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetryCount });

        actual.Should().NotBeNull();
        // At random=0.5, multiplier is exactly 1.0, so jittered == nominal.
        actual!.Value.Should().Be(nominal);
    }

    [Fact]
    public void RetryPolicy_AfterScheduleExhausted_StaysAtTailDelay()
    {
        var policy = new SorchaHubConnectionBuilder.InfiniteRetryPolicy(static () => 0.5);
        var tail = SorchaHubConnectionBuilder.InfiniteRetryPolicy.NominalDelays[^1];

        for (var i = 5; i < 20; i++)
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = i });
            delay.Should().Be(tail, $"attempt {i} should clamp to the tail nominal delay");
        }
    }

    [Fact]
    public void RetryPolicy_SpreadsAttemptsAcross20PercentJitterBand()
    {
        // 1000 simulated attempts at index 4 (30s nominal). Assert the spread
        // sits within ±20% and >=95% of samples are inside ±25% (loose bound to
        // tolerate the uniform distribution's tails).
        var seed = new Random(42);
        var policy = new SorchaHubConnectionBuilder.InfiniteRetryPolicy(() => seed.NextDouble());
        var nominal = TimeSpan.FromSeconds(30);
        const int samples = 1000;
        var insideTightBand = 0;

        for (var i = 0; i < samples; i++)
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 4 })!.Value;
            var ratio = delay.TotalMilliseconds / nominal.TotalMilliseconds;
            ratio.Should().BeInRange(0.80, 1.20, "every sample must be inside ±20% jitter");
            if (ratio is >= 0.75 and <= 1.25)
            {
                insideTightBand++;
            }
        }

        ((double)insideTightBand / samples).Should().BeGreaterThan(0.95);
    }

    [Fact]
    public void RetryPolicy_AtZeroRandom_FloorsAtMinDelay()
    {
        // random=0 produces multiplier 1 - JitterFraction = 0.8 of nominal.
        // At index 0 (1s nominal), 0.8s is well above the 100ms floor — verify
        // the path nonetheless. Use a tiny fake nominal via reflection? Simpler:
        // verify the floor via the static helper directly.
        var floored = SorchaHubConnectionBuilder.InfiniteRetryPolicy.ApplyJitter(
            TimeSpan.FromMilliseconds(50), random: 0.0);
        floored.Should().Be(SorchaHubConnectionBuilder.InfiniteRetryPolicy.MinDelay);
    }

    // --- Perf audit F6: finite jittered reconnect (hub-client sites) -----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void JitteredRetryPolicy_WithinSchedule_ReturnsJitteredNominal(int previousRetryCount)
    {
        var delays = new[]
        {
            TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)
        };
        // random=0.5 → multiplier 1.0, so jittered == nominal (except the 0s slot,
        // which the 100ms floor lifts to MinDelay).
        var policy = new SorchaHubConnectionBuilder.JitteredRetryPolicy(delays, static () => 0.5);

        var actual = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetryCount });

        actual.Should().NotBeNull();
        var expected = delays[previousRetryCount] < SorchaHubConnectionBuilder.InfiniteRetryPolicy.MinDelay
            ? SorchaHubConnectionBuilder.InfiniteRetryPolicy.MinDelay
            : delays[previousRetryCount];
        actual!.Value.Should().Be(expected);
    }

    [Fact]
    public void JitteredRetryPolicy_AfterScheduleExhausted_ReturnsNull()
    {
        // The distinguishing behaviour vs InfiniteRetryPolicy: it gives up after the
        // last delay so the hub client's own background-retry loop can take over.
        var delays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };
        var policy = new SorchaHubConnectionBuilder.JitteredRetryPolicy(delays, static () => 0.5);

        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 2 }).Should().BeNull();
        policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 5 }).Should().BeNull();
    }

    [Fact]
    public void JitteredRetryPolicy_SpreadsWithin20PercentBand()
    {
        var seed = new Random(42);
        var delays = new[] { TimeSpan.FromSeconds(30) };
        var policy = new SorchaHubConnectionBuilder.JitteredRetryPolicy(delays, () => seed.NextDouble());

        for (var i = 0; i < 500; i++)
        {
            var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 })!.Value;
            (delay.TotalMilliseconds / 30_000.0).Should().BeInRange(0.80, 1.20);
        }
    }
}
