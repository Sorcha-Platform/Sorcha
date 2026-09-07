// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.McpServer.Prompts;

namespace Sorcha.McpServer.Tests.Prompts;

public class SorchaPromptsTests
{
    [Fact]
    public void TwoPartyExchange_NamesTheLifecycleInOrder()
    {
        var text = SorchaPrompts.TwoPartyExchange("Acme", "Beta", "invoice totals");

        text.Should().Contain("sorcha_register_create");
        text.Should().Contain("sorcha_blueprint_publish");
        text.Should().Contain("sorcha_instance_create");
        text.IndexOf("sorcha_register_create", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("sorcha_instance_create", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoPartyExchange_PointsAtTheSchemaResource()
    {
        SorchaPrompts.TwoPartyExchange("Acme", "Beta", "invoice totals")
            .Should().Contain("sorcha://schema/blueprint");
    }

    [Fact]
    public void EveryPrompt_WarnsThatSomeStepsNeedAHuman()
    {
        foreach (var text in new[]
                 {
                     SorchaPrompts.TwoPartyExchange("A", "B", "x"),
                     SorchaPrompts.IssueCredential("A", "B", "x"),
                     SorchaPrompts.ProveToRegulator("reg-1", "tx-1")
                 })
        {
            // Ruling 4 (task-8): Exactly.Once() is not a substring-count assertion FluentAssertions
            // offers for strings — use the lowercase Contain form instead.
            text.ToLowerInvariant().Should().Contain("confirm");
        }
    }
}
