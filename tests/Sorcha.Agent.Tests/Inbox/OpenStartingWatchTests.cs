// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Inbox;
using Xunit;

namespace Sorcha.Agent.Tests.Inbox;

/// <summary>
/// Issue #1446 — an agent playing a Feature 103 OPEN participant could not start a workflow at all.
/// Its action is in nobody's pending list (nobody is bound to it yet), so the agent polled an empty
/// inbox until the run timed out. It now watches a second, deliberately blueprint-scoped endpoint.
/// </summary>
public sealed class OpenStartingWatchTests
{
    [Fact]
    public void TheWatchQuery_NamesTheBlueprint_BecauseAnUnscopedOneIsRefused()
    {
        var path = PollingInboxListener.OpenStartingPath("property-inspection", registerId: null);

        path.Should().StartWith("/api/actions/open-starting?blueprintId=property-inspection");
        path.Should().NotContain("registerId", "an omitted register must not become an empty filter");
    }

    [Fact]
    public void TheWatchQuery_NarrowsByRegisterWhenTheActorHasOne()
    {
        var path = PollingInboxListener.OpenStartingPath("property-inspection", "reg-1");

        path.Should().Contain("registerId=reg-1");
    }

    [Fact]
    public void TheWatchQuery_EscapesIdsRatherThanConcatenatingThem()
    {
        var path = PollingInboxListener.OpenStartingPath("bp id&page=99", "r/1");

        path.Should().Contain("blueprintId=bp%20id%26page%3D99")
            .And.Contain("registerId=r%2F1");
        path.Should().EndWith("&registerId=r%2F1");
        path.Split("page=").Should().HaveCount(2, "a blueprint id must not be able to inject a second query parameter");
    }

    [Fact]
    public void ThePendingWatch_IsUnchangedByDefault()
    {
        // The open-starting path is opt-in. An actor that says nothing keeps the exact query it had.
        PollingInboxListener.PendingPath.Should().Be("/api/actions/pending?page=1&pageSize=50");
    }

    [Fact]
    public void AnOpenStartingWatchWithNoBlueprint_IsRejectedByValidation()
    {
        var definition = MinimalDefinition() with
        {
            Inbox = new InboxConfig
            {
                Polling = new PollingConfig { Enabled = true },
                OpenStarting = new OpenStartingConfig { Enabled = true }
            }
        };

        Validate(definition).Should().Contain(OpenStartingConfig.BlueprintIdRequiredError,
            "an agent that silently watched nothing would look exactly like the defect being fixed");
    }

    [Fact]
    public void AnOpenStartingWatchWithABlueprint_Validates()
    {
        var definition = MinimalDefinition() with
        {
            Inbox = new InboxConfig
            {
                Polling = new PollingConfig { Enabled = true },
                OpenStarting = new OpenStartingConfig { Enabled = true, BlueprintId = "property-inspection" }
            }
        };

        Validate(definition).Should().BeEmpty();
    }

    /// <summary>
    /// <c>Validate</c> is private, and deliberately so — it is an implementation detail of loading.
    /// Reflection here rather than widening the surface for a test.
    /// </summary>
    private static List<string> Validate(ActorDefinition definition)
        => (List<string>)typeof(ActorDefinitionLoader)
            .GetMethod("Validate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [definition])!;

    private static ActorDefinition MinimalDefinition() => new()
    {
        Actor = new ActorIdentity { Name = "tenant" },
        Connection = new ConnectionConfig
        {
            GatewayUrl = "http://localhost",
            RegisterId = "reg-1",
            WalletAddress = "ws1qtenant0000000000000000000000000000",
            Credentials = new CredentialsConfig
            {
                Email = "tenant@example.test",
                Password = "hunter2",
                OrganizationId = "00000000-0000-0000-0000-000000000002"
            }
        },
        Inbox = new InboxConfig { Polling = new PollingConfig { Enabled = true } },
        Mode = "rules",
        Rules = [new ActorRule { ActionName = "Report Problem", Decision = "approve" }]
    };
}
