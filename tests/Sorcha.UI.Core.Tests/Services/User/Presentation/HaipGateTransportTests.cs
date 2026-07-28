// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Presentation;

/// <summary>
/// The HAIP gate transport. Drives the poll delay off a <see cref="FakeTimeProvider"/> so the
/// tests do not sit through the real 2 s cadence.
/// </summary>
public class HaipGateTransportTests
{
    private readonly FakeTimeProvider _time = new();

    private HaipGateTransport Build(Mock<IHaipOfferService> haip)
        => new(haip.Object, _time, NullLogger<HaipGateTransport>.Instance);

    /// <summary>Runs the wait while advancing the fake clock so the poll loop makes progress.</summary>
    private async Task<GateOutcome> RunAsync(
        HaipGateTransport sut, IProgress<GateOutcome>? progress = null)
    {
        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid(), progress);

        for (var guard = 0; !waiting.IsCompleted && guard < 500; guard++)
        {
            await Task.Yield();
            _time.Advance(HaipPollingDefaults.PollInterval);
        }

        return await waiting;
    }

    /// <summary>Answers each poll from a sequence, repeating the last entry once exhausted.</summary>
    private static Mock<IHaipOfferService> Returning(params HaipPollOutcome[] sequence)
    {
        var haip = new Mock<IHaipOfferService>();
        var index = 0;
        haip.Setup(h => h.PollVerificationResultAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence[Math.Min(index++, sequence.Length - 1)]);
        return haip;
    }

    private static HaipPollOutcome Result(string state, Dictionary<string, JsonElement>? claims = null)
        => new(new HaipVerificationResult(Guid.NewGuid(), state, true, claims, null), false);

    [Fact]
    public async Task RepeatedNotFoundEndsTheWaitAsUnreachable()
    {
        var outcome = await RunAsync(Build(Returning(HaipPollOutcome.NotFound)));

        outcome.Should().Be(GateOutcome.Unreachable,
            "the verifier holding no such request is permanent, not an expiry");
    }

    [Fact]
    public async Task ASingleNotFoundDoesNotCondemnTheRequest()
    {
        // A freshly-created request can 404 for a moment.
        var sut = Build(Returning(HaipPollOutcome.NotFound,
                                  Result(HaipVerificationStates.Verified)));

        (await RunAsync(sut)).Should().Be(GateOutcome.Success);
    }

    [Theory]
    [InlineData(HaipVerificationStates.Verified, GateOutcome.Success)]
    [InlineData(HaipVerificationStates.Denied, GateOutcome.Declined)]
    [InlineData(HaipVerificationStates.Expired, GateOutcome.Expired)]
    [InlineData(HaipVerificationStates.Cancelled, GateOutcome.Abandoned)]
    public async Task TerminalStateMapsOntoGateOutcome(string state, GateOutcome expected)
        => (await RunAsync(Build(Returning(Result(state))))).Should().Be(expected);

    [Fact]
    public async Task SubmittedIsReportedAsProgressNotAsTheResult()
    {
        var seen = new List<GateOutcome>();
        var sut = Build(Returning(Result(HaipVerificationStates.Submitted),
                                  Result(HaipVerificationStates.Verified)));

        var outcome = await RunAsync(sut, new Progress<GateOutcome>(seen.Add));

        outcome.Should().Be(GateOutcome.Success,
            "Submitted is a waypoint — ending the wait there would strand the card mid-verification");
    }

    [Fact]
    public async Task TransientFailureIsRetriedNotTreatedAsUnreachable()
    {
        var sut = Build(Returning(HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  HaipPollOutcome.Transient,
                                  Result(HaipVerificationStates.Verified)));

        (await RunAsync(sut)).Should().Be(GateOutcome.Success);
    }

    [Fact]
    public async Task ClaimsComeFromTheOutcomeWithoutASecondCall()
    {
        var claims = new Dictionary<string, JsonElement>
        {
            ["givenName"] = JsonSerializer.Deserialize<JsonElement>("\"Stuart\"")
        };
        var haip = Returning(Result(HaipVerificationStates.Verified, claims));
        var sut = Build(haip);

        await RunAsync(sut);
        var fetched = await sut.FetchClaimsAsync(Guid.NewGuid(), claimsFetchToken: null);

        fetched.Should().NotBeNull();
        fetched!.Should().ContainKey("givenName");
    }

    [Fact]
    public async Task ClaimsAreNullBeforeAnyOutcome()
        => (await Build(Returning(HaipPollOutcome.Transient))
                .FetchClaimsAsync(Guid.NewGuid(), null))
            .Should().BeNull();

    [Fact]
    public async Task CancellationEndsTheWaitAsAbandoned()
    {
        var sut = Build(Returning(Result(HaipVerificationStates.Pending)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        (await sut.WaitForOutcomeAsync(Guid.NewGuid(), null, cts.Token))
            .Should().Be(GateOutcome.Abandoned);
    }

    [Fact]
    public void SourceIsHaipExternalWallet()
        => Build(Returning(HaipPollOutcome.Transient)).Source
            .Should().Be(PresentationSource.HaipExternalWallet);
}
