// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
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
    public void PresentationAlreadyCompleteException_InheritsFromException()
    {
        var ex = new PresentationAlreadyCompleteException(0, "tx");

        ex.Should().BeAssignableTo<Exception>();
    }
}
