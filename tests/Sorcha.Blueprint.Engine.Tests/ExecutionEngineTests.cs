// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Engine.Implementation;
using EngineModels = Sorcha.Blueprint.Engine.Models;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Tests for ExecutionEngine - the main facade for blueprint execution.
/// </summary>
public class ExecutionEngineTests
{
    private readonly ExecutionEngine _engine;

    public ExecutionEngineTests()
    {
        // Create real implementations (not mocks) for integration-style testing
        var schemaValidator = new SchemaValidator();
        var jsonLogicEvaluator = new JsonLogicEvaluator();
        var disclosureProcessor = new DisclosureProcessor();
        var routingEngine = new RoutingEngine(jsonLogicEvaluator);
        var actionProcessor = new ActionProcessor(
            schemaValidator,
            jsonLogicEvaluator,
            disclosureProcessor,
            routingEngine);

        _engine = new ExecutionEngine(
            actionProcessor,
            schemaValidator,
            jsonLogicEvaluator,
            disclosureProcessor,
            routingEngine);
    }

    #region ExecuteActionAsync Tests

    [Fact]
    public async Task ExecuteActionAsync_WithValidData_ReturnsSuccessResult()
    {
        // Arrange
        var blueprint = CreateSimpleBlueprint();
        var action = blueprint.Actions.First();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe",
            ["age"] = 30
        };

        var context = new EngineModels.ExecutionContext
        {
            Blueprint = blueprint,
            Action = action,
            ActionData = data,
            ParticipantId = "participant1",
            WalletAddress = "wallet1"
        };

        // Act
        var result = await _engine.ExecuteActionAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Validation.IsValid.Should().BeTrue();
        result.ProcessedData.Should().ContainKey("name");
        result.ProcessedData.Should().ContainKey("age");
    }

    [Fact]
    public async Task ExecuteActionAsync_WithInvalidData_ReturnsFailureResult()
    {
        // Arrange
        var blueprint = CreateSimpleBlueprint();
        var action = blueprint.Actions.First();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe"
            // Missing required "age" field
        };

        var context = new EngineModels.ExecutionContext
        {
            Blueprint = blueprint,
            Action = action,
            ActionData = data,
            ParticipantId = "participant1",
            WalletAddress = "wallet1"
        };

        // Act
        var result = await _engine.ExecuteActionAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Validation.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteActionAsync_WithCalculations_AppliesCalculations()
    {
        // Arrange
        var blueprint = CreateBlueprintWithCalculations();
        var action = blueprint.Actions.First();
        var data = new Dictionary<string, object>
        {
            ["quantity"] = 10,
            ["price"] = 5.5
        };

        var context = new EngineModels.ExecutionContext
        {
            Blueprint = blueprint,
            Action = action,
            ActionData = data,
            ParticipantId = "participant1",
            WalletAddress = "wallet1"
        };

        // Act
        var result = await _engine.ExecuteActionAsync(context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ProcessedData.Should().ContainKey("total");
        result.ProcessedData["total"].Should().Be(55.0);
        result.CalculatedValues.Should().ContainKey("total");
    }

    [Fact]
    public async Task ExecuteActionAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.ExecuteActionAsync(null!));
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WithValidData_ReturnsValidResult()
    {
        // Arrange
        var action = CreateActionWithSchema();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe",
            ["age"] = 30
        };

        // Act
        var result = await _engine.ValidateAsync(data, action);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidData_ReturnsInvalidResult()
    {
        // Arrange
        var action = CreateActionWithSchema();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe"
            // Missing required "age"
        };

        // Act
        var result = await _engine.ValidateAsync(data, action);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithNoSchema_ReturnsValidResult()
    {
        // Arrange
        var action = new BpModels.Action { Id = 1, Title = "Test" };
        var data = new Dictionary<string, object>
        {
            ["anything"] = "goes"
        };

        // Act
        var result = await _engine.ValidateAsync(data, action);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithLegacyFormSchemaOnly_StillValidates()
    {
        // The retained fallback for actions built in code rather than published. Named so that its
        // shape is understood as legacy, not as the shape new fixtures should copy.
        var action = new BpModels.Action
        {
            Id = 1,
            Title = "Legacy Action",
            Form = new BpModels.Control
            {
                Schema = JsonNode.Parse("""
                { "type": "object", "required": ["name", "age"] }
                """)
            }
        };

        var result = await _engine.ValidateAsync(new Dictionary<string, object> { ["name"] = "John" }, action);

        result.IsValid.Should().BeFalse("the legacy Form.Schema path must keep enforcing");
    }

    [Fact]
    public async Task ValidateAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var action = CreateActionWithSchema();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.ValidateAsync(null!, action));
    }

    [Fact]
    public async Task ValidateAsync_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var data = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.ValidateAsync(data, null!));
    }

    #endregion

    #region ApplyCalculationsAsync Tests

    [Fact]
    public async Task ApplyCalculationsAsync_WithCalculations_AppliesCorrectly()
    {
        // Arrange
        var action = CreateActionWithCalculations();
        var data = new Dictionary<string, object>
        {
            ["quantity"] = 10,
            ["price"] = 5.5
        };

        // Act
        var result = await _engine.ApplyCalculationsAsync(data, action);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("quantity");
        result.Should().ContainKey("price");
        result.Should().ContainKey("total");
        result["total"].Should().Be(55.0);
    }

    [Fact]
    public async Task ApplyCalculationsAsync_WithNoCalculations_ReturnsOriginalData()
    {
        // Arrange
        var action = new BpModels.Action { Id = 1, Title = "Test" };
        var data = new Dictionary<string, object>
        {
            ["field1"] = "value1",
            ["field2"] = 42
        };

        // Act
        var result = await _engine.ApplyCalculationsAsync(data, action);

        // Assert
        result.Should().NotBeNull();
        result.Should().ContainKey("field1");
        result.Should().ContainKey("field2");
        result.Should().NotContainKey("calculated");
    }

    [Fact]
    public async Task ApplyCalculationsAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var action = CreateActionWithCalculations();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.ApplyCalculationsAsync(null!, action));
    }

    [Fact]
    public async Task ApplyCalculationsAsync_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var data = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.ApplyCalculationsAsync(data, null!));
    }

    #endregion

    #region DetermineRoutingAsync Tests

    [Fact]
    public async Task DetermineRoutingAsync_WithUnconditionalRouting_ReturnsNextParticipant()
    {
        // Arrange
        var blueprint = CreateSimpleBlueprint();
        var action = blueprint.Actions.First();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe",
            ["age"] = 30
        };

        // Act
        var result = await _engine.DetermineRoutingAsync(blueprint, action, data);

        // Assert
        result.Should().NotBeNull();
        result.NextParticipantId.Should().Be("participant2");
        result.NextActionId.Should().Be("2"); // Action ID is 2
        result.IsWorkflowComplete.Should().BeFalse();
    }

    [Fact]
    public async Task DetermineRoutingAsync_WithConditionalRouting_EvaluatesConditions()
    {
        // Arrange
        var blueprint = CreateBlueprintWithConditionalRouting();
        var action = blueprint.Actions.First();
        var data = new Dictionary<string, object>
        {
            ["age"] = 25 // Should route to participant2 (age >= 18)
        };

        // Act
        var result = await _engine.DetermineRoutingAsync(blueprint, action, data);

        // Assert
        result.Should().NotBeNull();
        result.NextParticipantId.Should().Be("participant2");
        result.NextActionId.Should().Be("2"); // Action ID is 2
    }

    [Fact]
    public async Task DetermineRoutingAsync_WithNullBlueprint_ThrowsArgumentNullException()
    {
        // Arrange
        var action = new BpModels.Action { Id = 1, Title = "Test" };
        var data = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.DetermineRoutingAsync(null!, action, data));
    }

    [Fact]
    public async Task DetermineRoutingAsync_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var blueprint = CreateSimpleBlueprint();
        var data = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.DetermineRoutingAsync(blueprint, null!, data));
    }

    [Fact]
    public async Task DetermineRoutingAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var blueprint = CreateSimpleBlueprint();
        var action = blueprint.Actions.First();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _engine.DetermineRoutingAsync(blueprint, action, null!));
    }

    #endregion

    #region ApplyDisclosures Tests

    [Fact]
    public void ApplyDisclosures_WithDisclosures_CreatesDisclosureResults()
    {
        // Arrange
        var action = CreateActionWithDisclosures();
        var data = new Dictionary<string, object>
        {
            ["name"] = "John Doe",
            ["age"] = 30,
            ["ssn"] = "123-45-6789"
        };

        // Act
        var results = _engine.ApplyDisclosures(data, action);

        // Assert
        results.Should().NotBeNull();
        results.Should().HaveCount(2);

        var participant1Disclosure = results.First(d => d.ParticipantId == "participant1");
        participant1Disclosure.DisclosedData.Should().ContainKey("name");
        participant1Disclosure.DisclosedData.Should().ContainKey("age");
        participant1Disclosure.DisclosedData.Should().NotContainKey("ssn");

        var participant2Disclosure = results.First(d => d.ParticipantId == "participant2");
        participant2Disclosure.DisclosedData.Should().ContainKey("name");
        participant2Disclosure.DisclosedData.Should().NotContainKey("age");
        participant2Disclosure.DisclosedData.Should().NotContainKey("ssn");
    }

    [Fact]
    public void ApplyDisclosures_WithNoDisclosures_ReturnsEmptyList()
    {
        // Arrange
        var action = new BpModels.Action { Id = 1, Title = "Test" };
        var data = new Dictionary<string, object>
        {
            ["field1"] = "value1"
        };

        // Act
        var results = _engine.ApplyDisclosures(data, action);

        // Assert
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Fact]
    public void ApplyDisclosures_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        var action = CreateActionWithDisclosures();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => _engine.ApplyDisclosures(null!, action));
    }

    [Fact]
    public void ApplyDisclosures_WithNullAction_ThrowsArgumentNullException()
    {
        // Arrange
        var data = new Dictionary<string, object>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => _engine.ApplyDisclosures(data, null!));
    }

    #endregion

    #region Helper Methods

    private BpModels.Blueprint CreateSimpleBlueprint()
    {
        return new BpModels.Blueprint
        {
            Id = "blueprint1",
            Title = "Test Blueprint",
            Version = 1,
            Actions = new List<BpModels.Action>
            {
                new()
                {
                    Id = 1,
                    Title = "Action 1",
                    Sender = "participant1",
                    Form = new BpModels.Control
                    {
                        Schema = JsonNode.Parse("""
                        {
                            "type": "object",
                            "properties": {
                                "name": { "type": "string" },
                                "age": { "type": "integer" }
                            },
                            "required": ["name", "age"]
                        }
                        """)
                    },
                    // Add routing condition to route to participant2
                    Participants = new List<BpModels.Condition>
                    {
                        new("participant2", true) // Always route to participant2
                    }
                },
                new()
                {
                    Id = 2,
                    Title = "Action 2",
                    Sender = "participant2"
                }
            },
            Participants = new List<BpModels.Participant>
            {
                new()
                {
                    Id = "participant1",
                    WalletAddress = "wallet1"
                },
                new()
                {
                    Id = "participant2",
                    WalletAddress = "wallet2"
                }
            }
        };
    }

    private BpModels.Blueprint CreateBlueprintWithCalculations()
    {
        var blueprint = CreateSimpleBlueprint();
        var action = blueprint.Actions.First();

        action.Form!.Schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "quantity": { "type": "integer" },
                "price": { "type": "number" }
            },
            "required": ["quantity", "price"]
        }
        """);

        action.Calculations = new Dictionary<string, JsonNode>
        {
            ["total"] = JsonNode.Parse("""
            {
                "*": [
                    { "var": "quantity" },
                    { "var": "price" }
                ]
            }
            """)!
        };

        return blueprint;
    }

    private BpModels.Blueprint CreateBlueprintWithConditionalRouting()
    {
        var blueprint = CreateSimpleBlueprint();

        // Update first action to have conditional routing based on age
        var firstAction = blueprint.Actions.First();
        firstAction.Participants = new List<BpModels.Condition>
        {
            // Route to participant2 if age >= 18
            new("participant2", new List<string> { """{">=": [{"var": "age"}, 18]}""" })
        };

        return blueprint;
    }

    /// <summary>
    /// An action shaped the way a PUBLISHED blueprint actually is: the schema on
    /// <c>dataSchemas</c>, <c>form</c> left at its layout-only default.
    /// </summary>
    /// <remarks>
    /// This fixture used to hand-build <c>Form.Schema</c> — a shape that 0 of the 14
    /// blueprint-shaped JSON files in the repository produce. Every test below therefore proved a
    /// code path production never took, which is why issue #1573 (schema validation passing
    /// vacuously for every real payload) survived a green suite. Keep this built from the real
    /// shape; a fixture that can only pass on a synthetic shape is not a guard.
    /// </remarks>
    private BpModels.Action CreateActionWithSchema()
    {
        return new BpModels.Action
        {
            Id = 1,
            Title = "Test Action",
            DataSchemas =
            [
                System.Text.Json.JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "age": { "type": "integer" }
                    },
                    "required": ["name", "age"]
                }
                """)
            ]
        };
    }

    private BpModels.Action CreateActionWithCalculations()
    {
        return new BpModels.Action
        {
            Id = 1,
            Title = "Test Action",
            Calculations = new Dictionary<string, JsonNode>
            {
                ["total"] = JsonNode.Parse("""
                {
                    "*": [
                        { "var": "quantity" },
                        { "var": "price" }
                    ]
                }
                """)!
            }
        };
    }

    private BpModels.Action CreateActionWithDisclosures()
    {
        return new BpModels.Action
        {
            Id = 1,
            Title = "Test Action",
            Disclosures = new List<BpModels.Disclosure>
            {
                new()
                {
                    ParticipantAddress = "participant1",
                    DataPointers = new List<string> { "/name", "/age" }
                },
                new()
                {
                    ParticipantAddress = "participant2",
                    DataPointers = new List<string> { "/name" }
                }
            }
        };
    }

    #endregion
}
