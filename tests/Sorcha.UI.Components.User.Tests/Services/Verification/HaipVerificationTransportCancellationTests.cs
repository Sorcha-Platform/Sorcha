// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
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
/// Cancellation tests for <see cref="HaipVerificationTransport"/> — asserts that a cancelled
/// <see cref="CancellationToken"/> causes in-flight operations to throw
/// <see cref="OperationCanceledException"/> without leaking (Feature 164, B3 / contract C8 / FR-012 / SC-006).
/// </summary>
public sealed class HaipVerificationTransportCancellationTests
{
    private static readonly VerificationPreset AgePreset = new(
        Key: "age-over-18",
        Label: "Age over 18",
        Purpose: "Verify holder is over 18",
        RequiredVct: "AgeCredential/v1",
        RequiredClaims: ["age_over_18"],
        OptionalClaims: [],
        KnownCredentialClaims: ["age_over_18"]);

    private readonly Mock<IHaipVerifierClient> _clientMock = new(MockBehavior.Strict);
    private readonly Mock<IVerifierIdentityProvider> _identityMock = new(MockBehavior.Strict);

    private HaipVerificationTransport CreateSut() => new(
        _clientMock.Object,
        _identityMock.Object,
        NullLogger<HaipVerificationTransport>.Instance);

    [Fact]
    public async Task StartAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        // Act
        var act = () => sut.StartAsync(AgePreset, cts.Token);

        // Assert — cancelled token must propagate, not be swallowed (C8 / FR-012)
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "a pre-cancelled token must cause StartAsync to throw OperationCanceledException");
    }

    [Fact]
    public async Task PollAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        // Act
        var act = () => sut.PollAsync("session-id", cts.Token);

        // Assert — cancelled token must propagate (C8 / FR-012)
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "a pre-cancelled token must cause PollAsync to throw OperationCanceledException");
    }

    [Fact]
    public async Task StartAsync_CancelledDuringHttpCall_PropagatesCancellation()
    {
        // Arrange — identity resolves fine but the HTTP call observes cancellation
        using var cts = new CancellationTokenSource();

        _identityMock.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-id");

        _clientMock.Setup(x => x.CreateRequestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, IReadOnlyList<string> _, CancellationToken token) =>
            {
                await Task.Delay(100, token); // will throw if cancelled
                return new HaipCreateResult("r", "openid4vp://");
            });

        cts.Cancel(); // cancel before invoking

        var sut = CreateSut();

        // Act
        var act = () => sut.StartAsync(AgePreset, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "cancellation during the HTTP create-request call must propagate");
    }

    [Fact]
    public async Task PollAsync_CancelledDuringHttpCall_PropagatesCancellation()
    {
        // Arrange — the HTTP poll observes cancellation
        using var cts = new CancellationTokenSource();

        _clientMock.Setup(x => x.PollResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("cancelled"));

        cts.Cancel();

        var sut = CreateSut();

        // Act
        var act = () => sut.PollAsync("session-id", cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "OperationCanceledException from the HTTP layer must not be swallowed");
    }
}
