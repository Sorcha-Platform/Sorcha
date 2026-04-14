// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Unit tests for <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>
/// evaluation in <see cref="RoutingEngine.DetermineNextWithMappingAsync"/>.
/// Feature 104 wave 14a.
/// </summary>
public class OutputMappingTests
{
    private readonly IRoutingEngine _routingEngine;

    public OutputMappingTests()
    {
        _routingEngine = new RoutingEngine(new JsonLogicEvaluator());
    }

    private static BpModels.Blueprint TwoActionBlueprint(BpModels.Route route)
    {
        return new BpModels.Blueprint
        {
            Id = "BP-OM",
            Title = "OutputMapping fixture",
            Description = "Two-action carry-forward blueprint",
            Participants =
            [
                new() { Id = "author",   Name = "Author" },
                new() { Id = "reviewer", Name = "Reviewer" }
            ],
            Actions =
            [
                new()
                {
                    Id = 0,
                    Title = "Write",
                    Sender = "author",
                    IsStartingAction = true,
                    Routes = [route]
                },
                new()
                {
                    Id = 1,
                    Title = "Ack",
                    Sender = "reviewer"
                }
            ]
        };
    }

    [Fact]
    public async Task NoMappingDeclared_PendingPayloadsIsNull()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true
        });

        var source = new JsonObject { ["payload"] = new JsonObject { ["x"] = "y" } };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        result.PendingPayloads.Should().BeNull();
    }

    [Fact]
    public async Task NullOutputSource_PendingPayloadsIsNull()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/payload/note"] = "/carriedNote"
            }
        });

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), outputSource: null);

        result.PendingPayloads.Should().BeNull();
    }

    [Fact]
    public async Task SingleFieldCarryForward_SeedsNextActionPayload()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/payload/note"] = "/carriedNote"
            }
        });

        var source = new JsonObject
        {
            ["payload"] = new JsonObject { ["note"] = "hello from action 0" }
        };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        result.PendingPayloads.Should().NotBeNull();
        result.PendingPayloads!.Should().ContainKey(1);
        result.PendingPayloads[1]["carriedNote"]!.GetValue<string>().Should().Be("hello from action 0");
    }

    [Fact]
    public async Task NestedTargetPath_CreatesIntermediateObjects()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/haip/credential_offer_uri"] = "/credentialOffer/credential_offer_uri",
                ["/haip/display"]              = "/credentialOffer/display"
            }
        });

        var source = new JsonObject
        {
            ["haip"] = new JsonObject
            {
                ["credential_offer_uri"] = "openid-credential-offer://?credential_offer=abc",
                ["display"] = new JsonObject
                {
                    ["title"] = "Verified Citizen Credential",
                    ["subtitle"] = "Issued by Exampleland"
                }
            }
        };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        result.PendingPayloads.Should().NotBeNull();
        var seed = result.PendingPayloads![1];
        var offer = seed["credentialOffer"]!.AsObject();
        offer["credential_offer_uri"]!.GetValue<string>()
            .Should().Be("openid-credential-offer://?credential_offer=abc");
        offer["display"]!["title"]!.GetValue<string>().Should().Be("Verified Citizen Credential");
    }

    [Fact]
    public async Task AbsentSourcePath_IsSilentlySkipped()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/payload/present"] = "/here",
                ["/payload/missing"] = "/absent"
            }
        });

        var source = new JsonObject
        {
            ["payload"] = new JsonObject { ["present"] = "value" }
        };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        result.PendingPayloads.Should().NotBeNull();
        var seed = result.PendingPayloads![1];
        seed["here"]!.GetValue<string>().Should().Be("value");
        seed.ContainsKey("absent").Should().BeFalse();
    }

    [Fact]
    public async Task AllSourcesAbsent_ReturnsNullPendingPayloads()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/payload/missing"] = "/target"
            }
        });

        var source = new JsonObject { ["payload"] = new JsonObject() };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        // No entries applied — engine returns null rather than an empty dictionary
        result.PendingPayloads.Should().BeNull();
    }

    [Fact]
    public async Task ParallelRoute_EachNextActionGetsIndependentPayload()
    {
        var blueprint = new BpModels.Blueprint
        {
            Id = "BP-OM-PAR",
            Title = "Parallel OutputMapping",
            Description = "Parallel branch carry-forward",
            Participants =
            [
                new() { Id = "sender",    Name = "Sender" },
                new() { Id = "reviewer1", Name = "Reviewer 1" },
                new() { Id = "reviewer2", Name = "Reviewer 2" }
            ],
            Actions =
            [
                new()
                {
                    Id = 0,
                    Title = "Send",
                    Sender = "sender",
                    IsStartingAction = true,
                    Routes =
                    [
                        new BpModels.Route
                        {
                            Id = "fan-out",
                            NextActionIds = [1, 2],
                            IsDefault = true,
                            OutputMapping = new Dictionary<string, string>
                            {
                                ["/payload/summary"] = "/shared"
                            }
                        }
                    ]
                },
                new() { Id = 1, Title = "Review A", Sender = "reviewer1" },
                new() { Id = 2, Title = "Review B", Sender = "reviewer2" }
            ]
        };

        var source = new JsonObject
        {
            ["payload"] = new JsonObject { ["summary"] = "heads up" }
        };

        var result = await _routingEngine.DetermineNextWithMappingAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>(), source);

        result.PendingPayloads.Should().NotBeNull();
        result.PendingPayloads!.Should().ContainKeys(1, 2);
        result.PendingPayloads[1]["shared"]!.GetValue<string>().Should().Be("heads up");
        result.PendingPayloads[2]["shared"]!.GetValue<string>().Should().Be("heads up");

        // Mutating one copy must not affect the other (independent instances)
        result.PendingPayloads[1]["shared"] = "mutated";
        result.PendingPayloads[2]["shared"]!.GetValue<string>().Should().Be("heads up");
    }

    [Fact]
    public async Task DetermineNextAsync_LegacyOverload_BehavesIdentically()
    {
        var blueprint = TwoActionBlueprint(new BpModels.Route
        {
            Id = "r",
            NextActionIds = [1],
            IsDefault = true,
            OutputMapping = new Dictionary<string, string>
            {
                ["/payload/note"] = "/carriedNote"
            }
        });

        // DetermineNextAsync does NOT accept an output source and therefore MUST
        // return null PendingPayloads (backward compatible, non-breaking).
        var result = await _routingEngine.DetermineNextAsync(
            blueprint, blueprint.Actions[0], new Dictionary<string, object>());

        result.NextActionId.Should().Be("1");
        result.PendingPayloads.Should().BeNull();
    }
}
