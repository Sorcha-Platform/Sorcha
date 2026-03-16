// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Designer;
using Sorcha.UI.Core.Services;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Services;

public class BlueprintLayoutServiceTests
{
    private readonly BlueprintLayoutService _sut = new();

    private static Blueprint.Models.Blueprint CreateBlueprint(
        List<Participant>? participants = null,
        List<BlueprintAction>? actions = null)
    {
        return new Blueprint.Models.Blueprint
        {
            Id = "test-bp",
            Title = "Test Blueprint",
            Description = "Test blueprint for layout",
            Participants = participants ?? [
                new Participant { Id = "p1", Name = "Alice", Organisation = "Org", WalletAddress = "w1" },
                new Participant { Id = "p2", Name = "Bob", Organisation = "Org", WalletAddress = "w2" }
            ],
            Actions = actions ?? []
        };
    }

    [Fact]
    public void ComputeLayout_EmptyBlueprint_ReturnsEmptyLayout()
    {
        var bp = CreateBlueprint(actions: []);

        var result = _sut.ComputeLayout(bp);

        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Width.Should().Be(0);
        result.Height.Should().Be(0);
    }

    [Fact]
    public void ComputeLayout_LinearTwoActionChain_ProducesTwoLayers()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Ping", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                Routes = [new Route { Id = "r1", NextActionIds = [1] }]
            },
            new()
            {
                Id = 1, Title = "Pong", BlueprintId = "bp",
                Sender = "p2", IsStartingAction = false,
                Routes = [new Route { Id = "r2", NextActionIds = [] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Nodes.Should().HaveCount(2);
        result.Nodes.Should().Contain(n => n.ActionId == 0 && n.Layer == 0);
        result.Nodes.Should().Contain(n => n.ActionId == 1 && n.Layer == 1);
    }

    [Fact]
    public void ComputeLayout_BranchingRoutes_ProducesFanOut()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Start", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                Routes = [new Route { Id = "r1", NextActionIds = [1, 2] }]
            },
            new()
            {
                Id = 1, Title = "Branch A", BlueprintId = "bp",
                Sender = "p1", Routes = [new Route { Id = "r2", NextActionIds = [] }]
            },
            new()
            {
                Id = 2, Title = "Branch B", BlueprintId = "bp",
                Sender = "p2", Routes = [new Route { Id = "r3", NextActionIds = [] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Nodes.Should().HaveCount(3);
        // Both branches should be in same layer (layer 1)
        var branchA = result.Nodes.First(n => n.ActionId == 1);
        var branchB = result.Nodes.First(n => n.ActionId == 2);
        branchA.Layer.Should().Be(1);
        branchB.Layer.Should().Be(1);
        // They should be at different X positions
        branchA.Position.X.Should().NotBe(branchB.Position.X);
    }

    [Fact]
    public void ComputeLayout_CycleDetection_MarksBackEdges()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Ping", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                Routes = [new Route { Id = "r1", NextActionIds = [1] }]
            },
            new()
            {
                Id = 1, Title = "Pong", BlueprintId = "bp",
                Sender = "p2",
                Routes = [new Route { Id = "r2", NextActionIds = [0] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Edges.Should().Contain(e => e.IsBackEdge && e.SourceActionId == 1 && e.TargetActionId == 0);
        result.Edges.Should().Contain(e => e.EdgeType == EdgeType.BackEdge);
        // Pong's target (Ping) should be marked as cycle target
        result.Nodes.First(n => n.ActionId == 0).IsCycleTarget.Should().BeTrue();
    }

    [Fact]
    public void ComputeLayout_SingleStartingAction_NoRoutes_ProducesSingleTerminalNode()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Only Action", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Nodes.Should().HaveCount(1);
        var node = result.Nodes[0];
        node.IsStarting.Should().BeTrue();
        node.IsTerminal.Should().BeTrue();
        node.Layer.Should().Be(0);
    }

    [Fact]
    public void ComputeLayout_ConditionalRoutes_ClassifiesEdgeTypes()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Review", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                Routes =
                [
                    new Route
                    {
                        Id = "approve", NextActionIds = [1],
                        Condition = System.Text.Json.Nodes.JsonNode.Parse("{\"==\":[1,1]}"),
                        Description = "Approved"
                    },
                    new Route
                    {
                        Id = "reject", NextActionIds = [2],
                        IsDefault = true, Description = "Default path"
                    }
                ]
            },
            new()
            {
                Id = 1, Title = "Approved", BlueprintId = "bp",
                Sender = "p2", Routes = [new Route { Id = "end1", NextActionIds = [] }]
            },
            new()
            {
                Id = 2, Title = "Rejected", BlueprintId = "bp",
                Sender = "p2", Routes = [new Route { Id = "end2", NextActionIds = [] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Edges.Should().Contain(e =>
            e.SourceActionId == 0 && e.TargetActionId == 1 && e.EdgeType == EdgeType.Conditional);
        result.Edges.Should().Contain(e =>
            e.SourceActionId == 0 && e.TargetActionId == 2 && e.EdgeType == EdgeType.Default);
    }

    [Fact]
    public void ComputeLayout_ParticipantLegend_MatchesBlueprint()
    {
        var participants = new List<Participant>
        {
            new() { Id = "p1", Name = "Alice", Organisation = "Org", WalletAddress = "w1" },
            new() { Id = "p2", Name = "Bob", Organisation = "Org", WalletAddress = "w2" },
            new() { Id = "p3", Name = "Charlie", Organisation = "Org", WalletAddress = "w3" }
        };
        var actions = new List<BlueprintAction>
        {
            new() { Id = 0, Title = "Step 1", BlueprintId = "bp", Sender = "p1", IsStartingAction = true }
        };
        var bp = CreateBlueprint(participants: participants, actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.ParticipantLegend.Should().HaveCount(3);
        result.ParticipantLegend[0].Name.Should().Be("Alice");
        result.ParticipantLegend[1].Name.Should().Be("Bob");
        result.ParticipantLegend[2].Name.Should().Be("Charlie");
        // Each should have distinct colours
        result.ParticipantLegend.Select(p => p.Colour).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ComputeLayout_TerminalRoutes_ClassifiedAsTerminal()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Start", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                Routes = [new Route { Id = "r1", NextActionIds = [1] }]
            },
            new()
            {
                Id = 1, Title = "End", BlueprintId = "bp",
                Sender = "p2",
                Routes = [new Route { Id = "end", NextActionIds = [] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Edges.Should().Contain(e => e.EdgeType == EdgeType.Terminal && e.SourceActionId == 1);
        result.Nodes.First(n => n.ActionId == 1).IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void ComputeLayout_DetailSummary_IncludesSchemaAndDisclosureCounts()
    {
        var actions = new List<BlueprintAction>
        {
            new()
            {
                Id = 0, Title = "Action", BlueprintId = "bp",
                Sender = "p1", IsStartingAction = true,
                DataSchemas = [System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}")],
                Disclosures = [
                    new Disclosure("p1", ["/*"]),
                    new Disclosure("p2", ["/name"])
                ],
                Routes = [new Route { Id = "r1", NextActionIds = [] }]
            }
        };
        var bp = CreateBlueprint(actions: actions);

        var result = _sut.ComputeLayout(bp);

        var node = result.Nodes.First();
        node.DetailSummary.Should().Contain("1 schema");
        node.DetailSummary.Should().Contain("2 disclosures");
        node.DetailSummary.Should().Contain("1 route");
    }

    [Fact]
    public void ComputeLayout_MultiParticipant_AssignsSwimlaneColumns()
    {
        var participants = new List<Participant>
        {
            new() { Id = "p1", Name = "Alice", Organisation = "Org", WalletAddress = "w1" },
            new() { Id = "p2", Name = "Bob", Organisation = "Org", WalletAddress = "w2" },
            new() { Id = "p3", Name = "Charlie", Organisation = "Org", WalletAddress = "w3" }
        };
        var actions = new List<BlueprintAction>
        {
            new() { Id = 0, Title = "Submit", BlueprintId = "bp", Sender = "p1", IsStartingAction = true,
                     Routes = [new Route { Id = "r1", NextActionIds = [1] }] },
            new() { Id = 1, Title = "Review", BlueprintId = "bp", Sender = "p2",
                     Routes = [new Route { Id = "r2", NextActionIds = [2] }] },
            new() { Id = 2, Title = "Approve", BlueprintId = "bp", Sender = "p3",
                     Routes = [new Route { Id = "r3", NextActionIds = [] }] }
        };
        var bp = CreateBlueprint(participants: participants, actions: actions);

        var result = _sut.ComputeLayout(bp);

        var submitNode = result.Nodes.First(n => n.ActionId == 0);
        var reviewNode = result.Nodes.First(n => n.ActionId == 1);
        var approveNode = result.Nodes.First(n => n.ActionId == 2);

        // Each participant should be in a different swimlane column
        submitNode.SwimlaneColumn.Should().Be(0, "Alice is first participant");
        reviewNode.SwimlaneColumn.Should().Be(1, "Bob is second participant");
        approveNode.SwimlaneColumn.Should().Be(2, "Charlie is third participant");
    }

    [Fact]
    public void ComputeLayout_SingleParticipant_AllInColumn0()
    {
        var participants = new List<Participant>
        {
            new() { Id = "p1", Name = "Alice", Organisation = "Org", WalletAddress = "w1" }
        };
        var actions = new List<BlueprintAction>
        {
            new() { Id = 0, Title = "Step 1", BlueprintId = "bp", Sender = "p1", IsStartingAction = true,
                     Routes = [new Route { Id = "r1", NextActionIds = [1] }] },
            new() { Id = 1, Title = "Step 2", BlueprintId = "bp", Sender = "p1",
                     Routes = [new Route { Id = "r2", NextActionIds = [] }] }
        };
        var bp = CreateBlueprint(participants: participants, actions: actions);

        var result = _sut.ComputeLayout(bp);

        result.Nodes.Should().AllSatisfy(n => n.SwimlaneColumn.Should().Be(0));
    }

    [Fact]
    public void ComputeLayout_SwimlaneColumns_AlignByParticipantX()
    {
        var participants = new List<Participant>
        {
            new() { Id = "p1", Name = "Alice", Organisation = "Org", WalletAddress = "w1" },
            new() { Id = "p2", Name = "Bob", Organisation = "Org", WalletAddress = "w2" }
        };
        var actions = new List<BlueprintAction>
        {
            new() { Id = 0, Title = "Step 1", BlueprintId = "bp", Sender = "p1", IsStartingAction = true,
                     Routes = [new Route { Id = "r1", NextActionIds = [1] }] },
            new() { Id = 1, Title = "Step 2", BlueprintId = "bp", Sender = "p2",
                     Routes = [new Route { Id = "r2", NextActionIds = [2] }] },
            new() { Id = 2, Title = "Step 3", BlueprintId = "bp", Sender = "p1",
                     Routes = [new Route { Id = "r3", NextActionIds = [] }] }
        };
        var bp = CreateBlueprint(participants: participants, actions: actions);

        var result = _sut.ComputeLayout(bp);

        var step1 = result.Nodes.First(n => n.ActionId == 0);
        var step3 = result.Nodes.First(n => n.ActionId == 2);

        // Same participant (Alice) should have same X position (same column)
        step1.Position.X.Should().Be(step3.Position.X,
            "actions by the same participant should align in the same swimlane column");
    }
}
