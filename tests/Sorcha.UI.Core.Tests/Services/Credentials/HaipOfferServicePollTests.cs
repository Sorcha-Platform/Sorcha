// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Credentials;

/// <summary>
/// A 404 from the verifier means "this request does not exist here" — a permanent fact, not a
/// transient "the wallet hasn't scanned yet". Collapsing both into null (the previous behaviour)
/// made the QR card poll a doomed request 150 times over five minutes, show the citizen nothing
/// but "Waiting for wallet to scan…", and finally misreport it as Expired.
///
/// Observed live on n1 (2026-07-28): the AIAS Cyber gate is presentationSource "SorchaWallet", so
/// its request lives in Blueprint's F127 lifecycle and HAIP has never heard of it.
/// </summary>
public class HaipOfferServicePollTests
{
    private sealed class StubHandler(HttpStatusCode status, string? body = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? string.Empty, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static HaipOfferService Build(HttpStatusCode status, string? body = null) =>
        new(new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://n1.test/") },
            NullLogger<HaipOfferService>.Instance);

    [Fact]
    public async Task NotFound_IsReportedAsRequestNotFound_NotAsATransientMiss()
    {
        var sut = Build(HttpStatusCode.NotFound);

        var outcome = await sut.PollVerificationResultAsync(Guid.NewGuid());

        outcome.RequestNotFound.Should().BeTrue(
            "a 404 means the verifier has no such request — polling it again cannot succeed");
        outcome.Result.Should().BeNull();
    }

    [Fact]
    public async Task ServerError_IsTransient_NotRequestNotFound()
    {
        // A 500 or a network blip genuinely may succeed on the next tick; it must NOT be
        // mistaken for "this request will never exist".
        var sut = Build(HttpStatusCode.InternalServerError);

        var outcome = await sut.PollVerificationResultAsync(Guid.NewGuid());

        outcome.RequestNotFound.Should().BeFalse();
        outcome.Result.Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulPoll_ReturnsTheResult()
    {
        var sut = Build(HttpStatusCode.OK, """{"state":"Verified"}""");

        var outcome = await sut.PollVerificationResultAsync(Guid.NewGuid());

        outcome.RequestNotFound.Should().BeFalse();
        outcome.Result.Should().NotBeNull();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Verified);
    }

    [Fact]
    public void UnreachableIsTerminal_SoThePollingLoopStopsAndTheCardCanSayWhy()
    {
        HaipVerificationStates.IsTerminal(HaipVerificationStates.Unreachable).Should().BeTrue();
    }

    [Fact]
    public void NotFoundToleranceIsSmallButNonZero()
    {
        // Non-zero: a request can 404 for a moment immediately after creation, so failing on the
        // very first miss would be flaky. Small: five minutes of doomed polling is the bug.
        HaipPollingDefaults.MaxConsecutiveNotFound.Should().BeInRange(2, 5);
    }
}
