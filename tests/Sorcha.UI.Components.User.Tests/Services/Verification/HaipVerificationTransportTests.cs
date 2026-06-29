// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.UI.Components.User.Services.Verification;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services.Verification;

/// <summary>
/// Unit tests for <see cref="HaipVerificationTransport"/> — round-trip, state mapping,
/// expiry, and fault handling (Feature 164, B3 US1 / contracts C2–C7).
/// </summary>
public sealed class HaipVerificationTransportTests
{
    private static readonly VerificationPreset AgePreset = new(
        Key: "age-over-18",
        Label: "Age over 18",
        Purpose: "Verify holder is over 18",
        RequiredVct: "AgeCredential/v1",
        RequiredClaims: ["age_over_18"],
        OptionalClaims: [],
        KnownCredentialClaims: ["age_over_18", "portrait"]);

    private readonly Mock<IHaipVerifierClient> _clientMock = new(MockBehavior.Strict);
    private readonly Mock<IVerifierIdentityProvider> _identityMock = new(MockBehavior.Strict);

    private HaipVerificationTransport CreateSut() => new(
        _clientMock.Object,
        _identityMock.Object,
        NullLogger<HaipVerificationTransport>.Instance);

    [Fact]
    public async Task StartAsync_ValidPreset_ReturnsPendingSessionWithSessionIdAndQrDeepLink()
    {
        // Arrange
        _identityMock.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("did:key:test-client-id");

        _clientMock.Setup(x => x.CreateRequestAsync(
                "did:key:test-client-id",
                "AgeCredential/v1",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipCreateResult("req-abc-123", "openid4vp://authorize?request_uri=https://example.com/r/abc"));

        var sut = CreateSut();

        // Act
        var result = await sut.StartAsync(AgePreset);

        // Assert (C2: StartAsync returns non-empty SessionId + QrDeepLink)
        result.SessionId.Should().Be("req-abc-123", because: "the RequestId from the HAIP create result becomes the SessionId");
        result.QrDeepLink.Should().StartWith("openid4vp://", because: "the deep link is the OID4VP authorization request URI");
        result.State.Should().Be(VerificationSessionState.Pending, because: "a newly created session is always Pending");
        result.VpToken.Should().BeNull(because: "no vp_token exists before the holder responds");
    }

    [Fact]
    public async Task StartSessionAsync_MapsToStartedRecord()
    {
        // Arrange
        _identityMock.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-id");

        _clientMock.Setup(x => x.CreateRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipCreateResult("session-42", "openid4vp://deeplink"));

        var sut = CreateSut();

        // Act
        var started = await sut.StartSessionAsync(AgePreset);

        // Assert
        started.SessionId.Should().Be("session-42");
        started.QrDeepLink.Should().Be("openid4vp://deeplink");
        started.Purpose.Should().Be(AgePreset.Purpose);
        started.RequiredVct.Should().Be(AgePreset.RequiredVct);
    }

    [Fact]
    public async Task StartSessionAsync_OnTransportFault_Throws()
    {
        // Arrange — a 401 / network / server fault must surface as a thrown error so the shared
        // VerificationSessionQr component shows its error + retry state, NOT "Verification is not
        // yet configured here." The not-configured state is reserved for the stub transport
        // (Feature 164 — fixes the mis-rendered auth failure).
        _identityMock.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-id");

        _clientMock.Setup(x => x.CreateRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        var sut = CreateSut();

        // Act
        var act = async () => await sut.StartSessionAsync(AgePreset);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a transport fault must not be returned as an empty session that renders as not-configured");
    }

    [Fact]
    public async Task PollAsync_BeforeHolderResponds_ReturnsPendingWithNullVpToken()
    {
        // Arrange — poll result when holder has not yet responded (C3)
        _clientMock.Setup(x => x.PollResultAsync("req-abc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Pending", null, null));

        var sut = CreateSut();

        // Act
        var result = await sut.PollAsync("req-abc-123");

        // Assert
        result.State.Should().Be(VerificationSessionState.Pending, because: "server returned Pending state");
        result.VpToken.Should().BeNull(because: "holder has not submitted yet");
    }

    [Fact]
    public async Task PollAsync_AfterHolderSubmits_ReturnsCompleteWithVpToken()
    {
        // Arrange — poll result after holder direct-posts a vp_token (C4)
        // Server serialises PresentationRequestState.Verified via ToString() → "Verified"
        const string vpToken = "eyJhbGciOiJFUzI1NiJ9.fake-vp-token.signature";
        _clientMock.Setup(x => x.PollResultAsync("req-abc-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Verified", vpToken, null));

        var sut = CreateSut();

        // Act
        var result = await sut.PollAsync("req-abc-123");

        // Assert
        result.State.Should().Be(VerificationSessionState.Complete, because: "server returned Complete state");
        result.VpToken.Should().Be(vpToken, because: "the raw vp_token is returned for client-side verdict computation");
    }

    [Fact]
    public async Task PollSessionAsync_AfterHolderSubmits_IsCompleteAndHasVpToken()
    {
        // Arrange
        const string vpToken = "eyJhbGciOiJFUzI1NiJ9.fake-vp-token.signature";
        _clientMock.Setup(x => x.PollResultAsync("req-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Verified", vpToken, null));

        var sut = CreateSut();

        // Act
        var poll = await sut.PollSessionAsync("req-123");

        // Assert
        poll.IsComplete.Should().BeTrue();
        poll.IsTerminal.Should().BeTrue(because: "Complete is a terminal state");
        poll.VpToken.Should().Be(vpToken);
    }

    [Fact]
    public async Task PollSessionAsync_WhenSessionExpires_IsTerminalButNotComplete()
    {
        // Arrange — expired session must terminate the poll loop without raising OnCompleted (C7)
        _clientMock.Setup(x => x.PollResultAsync("req-expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Expired", null, null));

        var sut = CreateSut();

        // Act
        var poll = await sut.PollSessionAsync("req-expired");

        // Assert
        poll.IsTerminal.Should().BeTrue(because: "Expired is a terminal state that stops the poll loop");
        poll.IsComplete.Should().BeFalse(because: "an expired session did not complete successfully");
        poll.VpToken.Should().BeNull();
    }

    [Fact]
    public async Task PollSessionAsync_Pending_IsNotTerminal()
    {
        // Arrange
        _clientMock.Setup(x => x.PollResultAsync("req-pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Pending", null, null));

        var sut = CreateSut();

        // Act
        var poll = await sut.PollSessionAsync("req-pending");

        // Assert
        poll.IsTerminal.Should().BeFalse(because: "Pending sessions should keep the poll loop running");
        poll.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task PollAsync_WhenSessionExpires_ReturnsExpiredState()
    {
        // Arrange — TTL elapsed (C7)
        _clientMock.Setup(x => x.PollResultAsync("req-expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult("Expired", null, null));

        var sut = CreateSut();

        // Act
        var result = await sut.PollAsync("req-expired");

        // Assert
        result.State.Should().Be(VerificationSessionState.Expired, because: "server returned Expired state");
        result.VpToken.Should().BeNull(because: "no vp_token when session has expired");
    }

    [Fact]
    public async Task PollAsync_OnTransportFault_ReturnsErrorState()
    {
        // Arrange — network/transport fault (C6)
        _clientMock.Setup(x => x.PollResultAsync("req-fault", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var sut = CreateSut();

        // Act
        var result = await sut.PollAsync("req-fault");

        // Assert
        result.State.Should().Be(VerificationSessionState.Error, because: "a transport fault maps to Error state");
        result.Error.Should().NotBeNullOrEmpty(because: "the error message should be populated for diagnostics");
    }

    [Fact]
    public async Task StartAsync_OnTransportFault_ReturnsErrorState()
    {
        // Arrange
        _identityMock.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-id");

        _clientMock.Setup(x => x.CreateRequestAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("service unavailable"));

        var sut = CreateSut();

        // Act
        var result = await sut.StartAsync(AgePreset);

        // Assert
        result.State.Should().Be(VerificationSessionState.Error);
        result.Error.Should().NotBeNullOrEmpty();
        result.SessionId.Should().BeEmpty();
    }

    [Theory]
    // Actual PresentationRequestState.ToString() values emitted by the HAIP server
    [InlineData("Pending", VerificationSessionState.Pending)]
    [InlineData("Submitted", VerificationSessionState.Pending)]  // still waiting for verdict
    [InlineData("Verified", VerificationSessionState.Complete)]
    [InlineData("Denied", VerificationSessionState.Error)]       // credential rejected
    [InlineData("Expired", VerificationSessionState.Expired)]
    [InlineData("Cancelled", VerificationSessionState.Expired)]
    [InlineData("unknown-state", VerificationSessionState.Error)]
    public async Task PollAsync_StateMapping_MapsStateStringsCorrectly(string serverState, VerificationSessionState expectedState)
    {
        // Arrange
        _clientMock.Setup(x => x.PollResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HaipPollResult(serverState, serverState == "Verified" ? "vp" : null, null));

        var sut = CreateSut();

        // Act
        var result = await sut.PollAsync("session-id");

        // Assert
        result.State.Should().Be(expectedState, because: $"server state '{serverState}' should map to {expectedState}");
    }
}
