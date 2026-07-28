// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Presentation;

/// <summary>
/// The terminal set decides when a gate stops waiting. Getting it wrong in either direction is
/// silent: too narrow and the card waits forever on a finished request; too wide and it stops
/// while the holder is still mid-presentation.
/// </summary>
public class GateOutcomeTests
{
    [Theory]
    [InlineData(GateOutcome.Success)]
    [InlineData(GateOutcome.Declined)]
    [InlineData(GateOutcome.Expired)]
    [InlineData(GateOutcome.Abandoned)]
    [InlineData(GateOutcome.Unreachable)]
    public void TerminalOutcomesEndTheWait(GateOutcome outcome)
        => GateOutcomes.IsTerminal(outcome).Should().BeTrue();

    [Theory]
    [InlineData(GateOutcome.Pending)]
    [InlineData(GateOutcome.Submitted)]
    public void NonTerminalOutcomesDoNot(GateOutcome outcome)
        => GateOutcomes.IsTerminal(outcome).Should().BeFalse();

    [Fact]
    public void EveryOutcomeIsClassified()
    {
        // A new member added without a decision here would default to non-terminal and hang the
        // card, which looks exactly like an unscanned QR code.
        var all = Enum.GetValues<GateOutcome>();

        all.Should().HaveCount(7,
            "add the new member to one of the two theories above before widening this count");
    }
}
