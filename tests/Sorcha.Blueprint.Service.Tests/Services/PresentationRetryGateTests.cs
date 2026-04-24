// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// T052 — unit tests for the Feature 111 US3 retry-gate exception and message shape.
/// End-to-end integration (ActionExecutionService wiring) is exercised by the
/// integration tests landing in the integration-test harness PR.
/// </summary>
public class PresentationRetryGateTests
{
    [Fact]
    public void PresentationAlreadyCompleteException_CarriesActionIdAndPriorTxId()
    {
        var ex = new PresentationAlreadyCompleteException(3, "tx-abc");

        ex.ActionId.Should().Be(3);
        ex.PriorOutcomeTransactionId.Should().Be("tx-abc");
        ex.Message.Should().Contain("3");
        ex.Message.Should().Contain("tx-abc");
    }

    [Fact]
    public void PresentationAlreadyCompleteException_ActionIdZero_ProducesValidMessage()
    {
        // Regression guard: starting action (actionId=0) is a valid case for retry
        // gating — e.g. the first action of an AssuredIdentity flow. The exception
        // must still produce a non-empty, human-readable message.
        var ex = new PresentationAlreadyCompleteException(0, "tx-start");

        ex.ActionId.Should().Be(0);
        ex.Message.Should().NotBeNullOrWhiteSpace();
        ex.Message.Should().Contain("0");
        ex.Message.Should().Contain("tx-start");
    }

    [Fact]
    public void PresentationAlreadyCompleteException_MessageDoesNotLeakInternalState()
    {
        // The message is HTTP-surfaced as a 409 Problem detail. It must reference
        // the action and the blocking transaction, but must NOT leak internal
        // sentinel state ("success", "abandoned+outcome", etc.) — those are
        // lifecycle-internal and not appropriate for external clients.
        var ex = new PresentationAlreadyCompleteException(7, "tx-success-xyz");

        ex.Message.Should().NotContain("abandoned+outcome");
        ex.Message.Should().NotContain("outcome-pending-write");
    }
}
