// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Shared;

using Xunit;

namespace Sorcha.McpServer.Tests.Tools.Designer;

/// <summary>
/// <c>sorcha_blueprint_get</c> must return each action's routes.
/// </summary>
/// <remarks>
/// Routes decide which action runs next, which is most of what a workflow is. Omitting them made
/// the tool unable to answer "what happens after this action", and cold-start runs #5 and #6 both
/// called <c>sorcha_blueprint_export</c> immediately afterwards purely to read them — two calls
/// and a full definition dump to recover something the summary should carry.
/// </remarks>
public sealed class BlueprintGetReturnsRoutesTests
{
    private const string BlueprintId = "run6-supplier-compliance";

    private static string BlueprintJson() => JsonSerializer.Serialize(new
    {
        id = BlueprintId,
        title = "Supplier compliance",
        description = "Two-party exchange",
        version = 1,
        participants = new[] { new { id = "supplier", name = "Supplier" } },
        actions = new object[]
        {
            new
            {
                id = 1,
                title = "Submit pack",
                description = "",
                sender = "supplier",
                isStartingAction = true,
                routes = new object[]
                {
                    new { id = "to-review", nextActionIds = new[] { 2 }, isDefault = true,
                          description = "Always continue to review" },
                    new { id = "to-reject", nextActionIds = new[] { 3 }, isDefault = false,
                          description = "When the pack is incomplete",
                          condition = new { var = "complete" } },
                },
            },
        },
    });

    [Fact]
    public async Task ItReturnsEachActionsRoutes()
    {
        var result = await Tool(BlueprintJson()).GetBlueprintAsync(BlueprintId);

        result.Status.Should().Be("Success");
        var action = result.Blueprint!.Actions.Should().ContainSingle().Subject;

        action.Routes.Should().HaveCount(2,
            "routes are what decide which action runs next; without them the caller must export "
            + "the whole definition to learn the workflow's shape");
        action.Routes.Select(r => r.Id).Should().Equal("to-review", "to-reject");
    }

    [Fact]
    public async Task ARoutesDestinationAndDefaultFlagSurvive()
    {
        var result = await Tool(BlueprintJson()).GetBlueprintAsync(BlueprintId);
        var routes = result.Blueprint!.Actions[0].Routes;

        routes[0].NextActionIds.Should().Equal(2);
        routes[0].IsDefault.Should().BeTrue();
        routes[1].NextActionIds.Should().Equal(3);
        routes[1].IsDefault.Should().BeFalse();
        routes[1].Description.Should().Be("When the pack is incomplete");
    }

    [Fact]
    public async Task AConditionalRouteIsFlaggedAsConditional()
    {
        // The expression itself stays out — export carries the full definition — but whether a
        // route is conditional at all is the difference between "always" and "sometimes".
        var result = await Tool(BlueprintJson()).GetBlueprintAsync(BlueprintId);
        var routes = result.Blueprint!.Actions[0].Routes;

        routes[0].HasCondition.Should().BeFalse();
        routes[1].HasCondition.Should().BeTrue();
    }

    [Fact]
    public async Task AnActionWithNoRoutesReturnsAnEmptyList()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = BlueprintId,
            title = "T",
            version = 1,
            actions = new object[] { new { id = 1, title = "Only", sender = "supplier" } },
        });

        var result = await Tool(json).GetBlueprintAsync(BlueprintId);

        result.Blueprint!.Actions[0].Routes.Should().BeEmpty();
    }

    private static BlueprintGetTool Tool(string blueprintJson)
    {
        var auth = new Mock<IMcpAuthorizationService>();
        auth.Setup(a => a.CanInvokeTool("sorcha_blueprint_get")).Returns(true);

        var availability = new Mock<IServiceAvailabilityTracker>();
        availability.Setup(a => a.IsServiceAvailable(It.IsAny<string>())).Returns(true);

        var client = new Mock<IBlueprintServiceClient>();
        client.Setup(c => c.GetBlueprintAsync(BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprintJson);

        return new BlueprintGetTool(
            auth.Object, availability.Object, client.Object, Mock.Of<ILogger<BlueprintGetTool>>());
    }
}
