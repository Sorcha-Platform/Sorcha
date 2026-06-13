// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Drafts;

/// <summary>
/// Feature 152 US4 — `SubmitConflictClassifier` maps a submission result to a queue outcome:
/// submitted / retry (transient) / hold (stale conflict), never an infinite-retry on a
/// deterministic rejection.
/// </summary>
public sealed class SubmitConflictClassifierTests
{
    private static ApplicationSubmissionResult Result(ApplicationSubmissionStatus status, string? code = null) =>
        new(status, null, code, null);

    [Fact]
    public void Success_IsSubmitted() =>
        SubmitConflictClassifier.Classify(Result(ApplicationSubmissionStatus.Success))
            .Should().Be(SubmitOutcome.Submitted);

    [Fact]
    public void ServerError_IsRetry() =>
        SubmitConflictClassifier.Classify(Result(ApplicationSubmissionStatus.ServerError, "HTTP_503"))
            .Should().Be(SubmitOutcome.Retry);

    [Fact]
    public void SigningFailed_IsRetry() =>
        SubmitConflictClassifier.Classify(Result(ApplicationSubmissionStatus.SigningFailed))
            .Should().Be(SubmitOutcome.Retry);

    [Theory]
    [InlineData("HTTP_409", SubmitOutcome.StepMovedOn)]
    [InlineData("HTTP_410", SubmitOutcome.InstanceClosed)]
    [InlineData("HTTP_404", SubmitOutcome.StepMovedOn)]
    [InlineData("HTTP_408", SubmitOutcome.Retry)]
    [InlineData("HTTP_429", SubmitOutcome.Retry)]
    [InlineData("HTTP_400", SubmitOutcome.StepMovedOn)]
    [InlineData("HTTP_422", SubmitOutcome.StepMovedOn)]
    public void ValidationFailed_MapsByStatusCode(string code, SubmitOutcome expected) =>
        SubmitConflictClassifier.Classify(Result(ApplicationSubmissionStatus.ValidationFailed, code))
            .Should().Be(expected);
}
