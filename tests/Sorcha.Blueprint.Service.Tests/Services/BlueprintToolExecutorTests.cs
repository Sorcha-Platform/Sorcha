// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Templates;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="BlueprintToolExecutor"/>.
/// </summary>
public class BlueprintToolExecutorTests
{
    private readonly BlueprintToolExecutor _executor;
    private readonly Mock<ILogger<BlueprintToolExecutor>> _loggerMock;
    private readonly Mock<ISchemaIndexService> _schemaIndexServiceMock;
    private readonly Mock<IBlueprintTemplateService> _templateServiceMock;

    public BlueprintToolExecutorTests()
    {
        _loggerMock = new Mock<ILogger<BlueprintToolExecutor>>();
        _schemaIndexServiceMock = new Mock<ISchemaIndexService>();
        _templateServiceMock = new Mock<IBlueprintTemplateService>();
        _executor = new BlueprintToolExecutor(_loggerMock.Object, _schemaIndexServiceMock.Object, _templateServiceMock.Object);
    }

    #region GetToolDefinitions Tests

    [Fact]
    public void GetToolDefinitions_ReturnsAllTools()
    {
        // Act
        var tools = _executor.GetToolDefinitions();

        // Assert
        tools.Should().HaveCount(19);
        tools.Select(t => t.Name).Should().BeEquivalentTo(new[]
        {
            "create_blueprint",
            "add_participant",
            "remove_participant",
            "add_action",
            "update_action",
            "set_disclosure",
            "add_routing",
            "validate_blueprint",
            "search_schemas",
            "use_standard_schema",
            "search_templates",
            "require_credential",
            "issue_credential",
            "set_action_schema",
            "set_action_routes",
            "set_action_metadata",
            // Feature 142 / US5 — presentational form-layout authoring tools.
            "set_form_layout",
            "set_field_autofill",
            "set_review_page",
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        // Act
        var tools = _executor.GetToolDefinitions();

        // Assert
        foreach (var tool in tools)
        {
            tool.Description.Should().NotBeNullOrWhiteSpace($"Tool {tool.Name} should have a description");
        }
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveInputSchemas()
    {
        // Act
        var tools = _executor.GetToolDefinitions();

        // Assert
        foreach (var tool in tools)
        {
            tool.InputSchema.Should().NotBeNull($"Tool {tool.Name} should have an input schema");
            tool.InputSchema.RootElement.TryGetProperty("type", out var typeProp).Should().BeTrue();
            typeProp.GetString().Should().Be("object");
        }
    }

    #endregion

    #region create_blueprint Tests

    [Fact]
    public async Task ExecuteAsync_CreateBlueprint_CreatesWithTitleAndDescription()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { title = "Test Blueprint", description = "A test description" });

        // Act
        var result = await _executor.ExecuteAsync("create_blueprint", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Title.Should().Be("Test Blueprint");
        draft.Description.Should().Be("A test description");
    }

    [Fact]
    public async Task ExecuteAsync_CreateBlueprint_SetsDefaultsForMissingFields()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { title = "Minimal", description = "" });

        // Act
        var result = await _executor.ExecuteAsync("create_blueprint", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Title.Should().Be("Minimal");
    }

    #endregion

    #region add_participant Tests

    [Fact]
    public async Task ExecuteAsync_AddParticipant_AddsPersonParticipant()
    {
        // Arrange
        var builder = BlueprintBuilder.Create().WithTitle("Test");
        var args = CreateArgs(new { id = "alice", name = "Alice Smith" });

        // Act
        var result = await _executor.ExecuteAsync("add_participant", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Participants.Should().HaveCount(1);
        draft.Participants[0].Id.Should().Be("alice");
        draft.Participants[0].Name.Should().Be("Alice Smith");
    }

    [Fact]
    public async Task ExecuteAsync_AddParticipant_AddsOrganizationWithRole()
    {
        // Arrange
        var builder = BlueprintBuilder.Create().WithTitle("Test");
        var args = CreateArgs(new { id = "acme", name = "ACME Corp", organisation = "ACME Inc", role = "organization" });

        // Act
        var result = await _executor.ExecuteAsync("add_participant", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Participants.Should().HaveCount(1);
        draft.Participants[0].Organisation.Should().Be("ACME Inc");
    }

    [Fact]
    public async Task ExecuteAsync_AddParticipant_AddsMultipleParticipants()
    {
        // Arrange
        var builder = BlueprintBuilder.Create().WithTitle("Test");

        // Act
        await _executor.ExecuteAsync("add_participant", CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant", CreateArgs(new { id = "bob", name = "Bob" }), builder);

        // Assert
        var draft = builder.BuildDraft();
        draft.Participants.Should().HaveCount(2);
    }

    #endregion

    #region remove_participant Tests

    [Fact]
    public async Task ExecuteAsync_RemoveParticipant_ReturnsNotSupportedForExistingParticipant()
    {
        // Arrange - remove_participant is not fully implemented in the tool executor
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddParticipant("bob", p => p.Named("Bob"));
        var args = CreateArgs(new { id = "alice" });

        // Act
        var result = await _executor.ExecuteAsync("remove_participant", args, builder);

        // Assert - The tool indicates it's not supported
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not yet supported");
    }

    [Fact]
    public async Task ExecuteAsync_RemoveParticipant_ReturnsNotSupported()
    {
        // Arrange
        var builder = BlueprintBuilder.Create().WithTitle("Test");
        var args = CreateArgs(new { id = "nonexistent" });

        // Act
        var result = await _executor.ExecuteAsync("remove_participant", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not yet supported");
    }

    #endregion

    #region add_action Tests

    [Fact]
    public async Task ExecuteAsync_AddAction_CreatesBasicAction()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"));
        var args = CreateArgs(new
        {
            id = 1,  // Integer, not string
            title = "Submit Request",
            sender = "alice",
            isStartingAction = true
        });

        // Act
        var result = await _executor.ExecuteAsync("add_action", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Actions.Should().HaveCount(1);
        draft.Actions[0].Title.Should().Be("Submit Request");
        draft.Actions[0].IsStartingAction.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddAction_CreatesActionWithDataFields()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"));

        var dataFields = new object[]
        {
            new { name = "amount", type = "number", isRequired = true, minimum = 1000, maximum = 50000 },
            new { name = "email", type = "string", isRequired = true, format = "email" },
            new { name = "notes", type = "string", maxLength = 500 }
        };

        var args = CreateArgs(new
        {
            id = 1,  // Integer, not string
            title = "Loan Application",
            sender = "alice",
            dataFields
        });

        // Act
        var result = await _executor.ExecuteAsync("add_action", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Actions[0].DataSchemas.Should().NotBeNull();
        draft.Actions[0].DataSchemas.Should().HaveCount(1);

        var schema = draft.Actions[0].DataSchemas!.First();
        schema.RootElement.TryGetProperty("properties", out var props).Should().BeTrue();
        props.TryGetProperty("amount", out _).Should().BeTrue();
        props.TryGetProperty("email", out _).Should().BeTrue();
        props.TryGetProperty("notes", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddAction_CreatesActionWithEnumField()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"));

        var dataFields = new object[]
        {
            new { name = "status", type = "string", enumValues = new[] { "approved", "rejected", "pending" } }
        };

        var args = CreateArgs(new
        {
            id = 1,  // Integer, not string
            title = "Status Update",
            sender = "alice",
            dataFields
        });

        // Act
        var result = await _executor.ExecuteAsync("add_action", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var schema = draft.Actions[0].DataSchemas!.First();
        schema.RootElement.GetProperty("properties").GetProperty("status")
            .TryGetProperty("enum", out var enumProp).Should().BeTrue();
        enumProp.GetArrayLength().Should().Be(3);
    }

    #endregion

    #region update_action Tests

    [Fact]
    public async Task ExecuteAsync_UpdateAction_ReturnsNotSupported()
    {
        // Arrange - update_action is not fully implemented yet
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Original Title").SentBy("alice"));
        var args = CreateArgs(new { actionId = "1", title = "Updated Title" });

        // Act
        var result = await _executor.ExecuteAsync("update_action", args, builder);

        // Assert - Tool indicates it's not fully supported
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not yet fully supported");
    }

    [Fact]
    public async Task ExecuteAsync_UpdateAction_ReturnsNotSupportedForNonexistent()
    {
        // Arrange
        var builder = BlueprintBuilder.Create().WithTitle("Test");
        var args = CreateArgs(new { actionId = "nonexistent", title = "New Title" });

        // Act
        var result = await _executor.ExecuteAsync("update_action", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not yet fully supported");
    }

    #endregion

    #region set_disclosure Tests

    [Fact]
    public async Task ExecuteAsync_SetDisclosure_AddsDisclosureRule()
    {
        // Arrange - use tool to add action first so the action exists
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "bob", name = "Bob" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 1, title = "Submit", sender = "alice" }), builder);

        var args = CreateArgs(new
        {
            actionId = 1,  // Integer, not string
            participantId = "bob",
            fields = new[] { "/amount", "/description" }  // 'fields' not 'disclosedFields'
        });

        // Act
        var result = await _executor.ExecuteAsync("set_disclosure", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetDisclosure_ReturnsErrorForInvalidAction()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);

        var args = CreateArgs(new
        {
            actionId = 999,  // Non-existent action
            participantId = "alice",
            fields = new[] { "/field" }
        });

        // Act
        var result = await _executor.ExecuteAsync("set_disclosure", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion

    #region add_routing Tests

    [Fact]
    public async Task ExecuteAsync_AddRouting_AddsRoutingRule()
    {
        // Arrange - use tool executor to build blueprint
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "bob", name = "Bob" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 1, title = "Submit", sender = "alice" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 2, title = "Review", sender = "bob" }), builder);

        // add_routing uses conditions array with field, operator, value, routeTo
        var args = CreateArgs(new
        {
            actionId = 1,
            conditions = new[]
            {
                new { field = "amount", @operator = "greaterThan", value = 1000, routeTo = "bob" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("add_routing", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddRouting_SupportsEqualsOperator()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "bob", name = "Bob" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 1, title = "Submit", sender = "alice" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 2, title = "Approved", sender = "bob" }), builder);

        var args = CreateArgs(new
        {
            actionId = 1,
            conditions = new[]
            {
                new { field = "status", @operator = "equals", value = "approved", routeTo = "bob" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("add_routing", args, builder);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddRouting_ReturnsErrorForInvalidAction()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test", description = "Test blueprint" }), builder);

        // actionId must be an integer, not a string - use a non-existent integer ID
        var args = CreateArgs(new
        {
            actionId = 999,  // Non-existent action
            conditions = new[]
            {
                new { field = "x", @operator = "equals", value = "y", routeTo = "someone" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("add_routing", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion

    #region validate_blueprint Tests

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_ReturnsValidForCompleteBlueprint()
    {
        // Arrange - use the tool executor to build a complete blueprint
        var builder = BlueprintBuilder.Create();

        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Valid Blueprint", description = "A complete blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "bob", name = "Bob" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new
            {
                id = 1,  // Integer, not string
                title = "Submit",
                description = "Submit data",
                sender = "alice",
                isStartingAction = true
            }), builder);

        var args = CreateArgs(new { });

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeFalse();

        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.isValid.Should().BeTrue();
        output.errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_ReturnsErrorsForMissingParticipants()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .WithDescription("Test blueprint");
        var args = CreateArgs(new { });

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", args, builder);

        // Assert
        result.Success.Should().BeTrue(); // Tool execution succeeded
        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.isValid.Should().BeFalse();
        output.errors.Should().Contain(e => e.code == "MIN_PARTICIPANTS");
    }

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_ReturnsErrorsForMissingActions()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .WithDescription("Test blueprint")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddParticipant("bob", p => p.Named("Bob"));
        var args = CreateArgs(new { });

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", args, builder);

        // Assert
        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.isValid.Should().BeFalse();
        output.errors.Should().Contain(e => e.code == "MIN_ACTIONS");
    }

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_ReturnsWarningForNoStartingAction()
    {
        // Arrange - use tool to add action without starting flag
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test Blueprint", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "alice", name = "Alice" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "bob", name = "Bob" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new
            {
                id = 1,  // Integer, not string
                title = "Action",
                description = "An action",
                sender = "alice",
                isStartingAction = false  // Not marked as starting
            }), builder);

        var args = CreateArgs(new { });

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", args, builder);

        // Assert
        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.warnings.Should().Contain(w => w.code == "NO_STARTING_ACTION");
    }

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_ReturnsErrorForNonAbsoluteUriVct()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test Blueprint", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "issuer", name = "Issuer" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "holder", name = "Holder" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new
            {
                id = 1,
                title = "Issue",
                description = "Issue a credential",
                sender = "issuer",
                isStartingAction = true
            }), builder);

        var issueResult = await _executor.ExecuteAsync("issue_credential",
            CreateArgs(new
            {
                actionId = 1,
                credentialType = "AssuredIdentity",
                vct = "https://sorcha.dev/vc/assured-identity/v1",
                recipientParticipantId = "holder",
                claimMappings = new[]
                {
                    new { claimName = "name", sourceField = "/name" }
                }
            }), builder);
        issueResult.Success.Should().BeTrue();

        // Set an invalid (non-absolute-URI) vct via the metadata escape-hatch.
        var metadataResult = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new
            {
                actionId = 1,
                credentialIssuanceConfig = new
                {
                    credentialType = "AssuredIdentity",
                    vct = "not a uri",
                    recipientParticipantId = "holder",
                    claimMappings = new[]
                    {
                        new { claimName = "name", sourceField = "/name" }
                    }
                }
            }), builder);
        metadataResult.Success.Should().BeTrue();

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", CreateArgs(new { }), builder);

        // Assert
        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.isValid.Should().BeFalse();
        output.errors.Should().Contain(e => e.code == "INVALID_CREDENTIAL_VCT");
    }

    [Fact]
    public async Task ExecuteAsync_ValidateBlueprint_AllowsAbsoluteUriVct()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Test Blueprint", description = "Test blueprint" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "issuer", name = "Issuer" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "holder", name = "Holder" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new
            {
                id = 1,
                title = "Issue",
                description = "Issue a credential",
                sender = "issuer",
                isStartingAction = true
            }), builder);

        var issueResult = await _executor.ExecuteAsync("issue_credential",
            CreateArgs(new
            {
                actionId = 1,
                credentialType = "AssuredIdentity",
                vct = "https://sorcha.dev/vc/assured-identity/v1",
                recipientParticipantId = "holder",
                claimMappings = new[]
                {
                    new { claimName = "name", sourceField = "/name" }
                }
            }), builder);
        issueResult.Success.Should().BeTrue();

        // Set a valid absolute-URI vct via the metadata escape-hatch.
        var metadataResult = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new
            {
                actionId = 1,
                credentialIssuanceConfig = new
                {
                    credentialType = "AssuredIdentity",
                    vct = "https://sorcha.dev/vc/assured-identity/v1",
                    recipientParticipantId = "holder",
                    claimMappings = new[]
                    {
                        new { claimName = "name", sourceField = "/name" }
                    }
                }
            }), builder);
        metadataResult.Success.Should().BeTrue();

        // Act
        var result = await _executor.ExecuteAsync("validate_blueprint", CreateArgs(new { }), builder);

        // Assert
        var resultJson = result.Result!.RootElement.ToString();
        var output = JsonSerializer.Deserialize<ValidationOutput>(resultJson);
        output!.errors.Should().NotContain(e => e.code == "INVALID_CREDENTIAL_VCT");
    }

    #endregion

    #region Unknown Tool Tests

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsFailed()
    {
        // Arrange
        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { });

        // Act
        var result = await _executor.ExecuteAsync("unknown_tool", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unknown tool");
    }

    #endregion

    #region search_schemas Tests (T014)

    [Fact]
    public async Task ExecuteSearchSchemas_ReturnsMatchingSchemas()
    {
        // Arrange
        var schemas = new List<SchemaIndexEntryDto>
        {
            CreateSchemaIndexEntry("invoice-schema", "Invoice Schema", "finance", fieldCount: 5,
                fieldNames: new[] { "invoiceNumber", "amount", "currency", "date", "vendor" }),
            CreateSchemaIndexEntry("purchase-order", "Purchase Order", "finance", fieldCount: 3,
                fieldNames: new[] { "poNumber", "items", "total" })
        };

        _schemaIndexServiceMock.Setup(s => s.SearchAsync(
                "invoice", null, null, null, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaIndexSearchResponse(schemas.AsReadOnly(), schemas.Count, null, null));

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "invoice" });

        // Act
        var result = await _executor.ExecuteAsync("search_schemas", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeFalse();
        var resultJson = result.Result!.RootElement;
        resultJson.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        resultJson.GetProperty("results").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteSearchSchemas_FiltersByCategory()
    {
        // Arrange — the executor delegates filtering to ISchemaIndexService.SearchAsync.
        // We assert the executor passes the category through as the `sectors` argument and
        // returns whatever the service returns (one filtered entry).
        var filtered = new List<SchemaIndexEntryDto>
        {
            CreateSchemaIndexEntry("health-record", "Health Record", "healthcare")
        };

        _schemaIndexServiceMock.Setup(s => s.SearchAsync(
                "record",
                It.Is<string[]?>(sectors => sectors != null && sectors.Contains("healthcare")),
                null, null, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaIndexSearchResponse(filtered.AsReadOnly(), filtered.Count, null, null));

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "record", category = "healthcare" });

        // Act
        var result = await _executor.ExecuteAsync("search_schemas", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var results = result.Result!.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("identifier").GetString().Should().Be("health-record");
    }

    [Fact]
    public async Task ExecuteSearchSchemas_NoResults_ReturnsEmpty()
    {
        // Arrange
        _schemaIndexServiceMock.Setup(s => s.SearchAsync(
                "nonexistent", null, null, null, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaIndexSearchResponse(new List<SchemaIndexEntryDto>().AsReadOnly(), 0, null, null));

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "nonexistent" });

        // Act
        var result = await _executor.ExecuteAsync("search_schemas", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.Result!.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        result.Result.RootElement.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteSearchSchemas_ReturnsFieldInfo()
    {
        // Arrange
        var schemas = new List<SchemaIndexEntryDto>
        {
            CreateSchemaIndexEntry("invoice-schema", "Invoice Schema", "finance",
                fieldCount: 3, fieldNames: new[] { "invoiceNumber", "amount", "currency" })
        };

        _schemaIndexServiceMock.Setup(s => s.SearchAsync(
                "invoice", null, null, null, 50, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaIndexSearchResponse(schemas.AsReadOnly(), schemas.Count, null, null));

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "invoice" });

        // Act
        var result = await _executor.ExecuteAsync("search_schemas", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var firstResult = result.Result!.RootElement.GetProperty("results")[0];
        firstResult.GetProperty("fieldCount").GetInt32().Should().Be(3);
        firstResult.GetProperty("fieldNames").GetArrayLength().Should().Be(3);
    }

    #endregion

    #region use_standard_schema Tests (T015)

    [Fact]
    public async Task ExecuteUseStandardSchema_AppliesSchemaToAction()
    {
        // Arrange
        var schemaContent = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "invoiceNumber": { "type": "string", "title": "Invoice Number" },
                    "amount": { "type": "number", "title": "Amount" }
                },
                "required": ["invoiceNumber"]
            }
            """);

        _schemaIndexServiceMock.Setup(s => s.GetContentByShortCodeAsync("invoice-schema", It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaContent);

        var builder = BlueprintBuilder.Create()
            .WithTitle("Test Blueprint")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit Invoice").SentBy("alice"));

        var args = CreateArgs(new { schemaId = "invoice-schema", actionId = 1 });

        // Act
        var result = await _executor.ExecuteAsync("use_standard_schema", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var resultJson = result.Result!.RootElement;
        resultJson.GetProperty("fieldsAdded").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ExecuteUseStandardSchema_SchemaNotFound_ReturnsError()
    {
        // Arrange
        _schemaIndexServiceMock.Setup(s => s.GetContentByShortCodeAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonDocument?)null);

        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit").SentBy("alice"));

        var args = CreateArgs(new { schemaId = "unknown", actionId = 1 });

        // Act
        var result = await _executor.ExecuteAsync("use_standard_schema", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteUseStandardSchema_ActionNotFound_ReturnsError()
    {
        // Arrange
        var schemaContent = JsonDocument.Parse("""{ "type": "object", "properties": {} }""");

        _schemaIndexServiceMock.Setup(s => s.GetContentByShortCodeAsync("test-schema", It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaContent);

        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"));

        var args = CreateArgs(new { schemaId = "test-schema", actionId = 999 });

        // Act
        var result = await _executor.ExecuteAsync("use_standard_schema", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion

    #region search_templates Tests (T020)

    [Fact]
    public async Task ExecuteSearchTemplates_ReturnsMatchingTemplates()
    {
        // Arrange
        var templates = new List<BlueprintTemplate>
        {
            new() { Id = "t1", Title = "Loan Application", Description = "A loan workflow", Category = "finance", Published = true },
            new() { Id = "t2", Title = "Invoice Processing", Description = "Invoice flow", Category = "finance", Published = true }
        };

        _templateServiceMock.Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "loan" });

        // Act
        var result = await _executor.ExecuteAsync("search_templates", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeFalse();
        result.Result!.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        result.Result.RootElement.GetProperty("results")[0].GetProperty("title").GetString()
            .Should().Be("Loan Application");
    }

    [Fact]
    public async Task ExecuteSearchTemplates_FiltersByCategory()
    {
        // Arrange
        var templates = new List<BlueprintTemplate>
        {
            new() { Id = "t1", Title = "Loan Application", Description = "Finance loan", Category = "finance", Published = true },
            new() { Id = "t2", Title = "Health Record", Description = "Healthcare record", Category = "healthcare", Published = true }
        };

        _templateServiceMock.Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var builder = BlueprintBuilder.Create();
        // Both templates match "record" in description or title, but category filter narrows it
        var args = CreateArgs(new { query = "Record", category = "healthcare" });

        // Act
        var result = await _executor.ExecuteAsync("search_templates", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var results = result.Result!.RootElement.GetProperty("results");
        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("id").GetString().Should().Be("t2");
    }

    [Fact]
    public async Task ExecuteSearchTemplates_NoResults_ReturnsEmpty()
    {
        // Arrange
        _templateServiceMock.Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BlueprintTemplate>());

        var builder = BlueprintBuilder.Create();
        var args = CreateArgs(new { query = "nonexistent" });

        // Act
        var result = await _executor.ExecuteAsync("search_templates", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.Result!.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        result.Result.RootElement.GetProperty("results").GetArrayLength().Should().Be(0);
    }

    #endregion

    #region require_credential Tests (T024)

    [Fact]
    public async Task ExecuteRequireCredential_AddsRequirementToAction()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit").SentBy("alice"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "DriverLicense"
        });

        // Act
        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();

        var draft = builder.BuildDraft();
        var action = draft.Actions.First(a => a.Id == 1);
        action.CredentialRequirements.Should().HaveCount(1);
        action.CredentialRequirements!.First().Type.Should().Be("DriverLicense");
    }

    [Fact]
    public async Task ExecuteRequireCredential_WithAcceptedIssuers()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit").SentBy("alice"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "DriverLicense",
            acceptedIssuers = new[] { "dmv-authority", "gov-agency" }
        });

        // Act
        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var req = draft.Actions.First(a => a.Id == 1).CredentialRequirements!.First();
        req.TrustPolicy.AllowedIssuerDids().Should().BeEquivalentTo(new[] { "dmv-authority", "gov-agency" });
    }

    [Fact]
    public async Task ExecuteRequireCredential_WithRequiredClaims()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit").SentBy("alice"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "DriverLicense",
            requiredClaims = new object[]
            {
                new { claimName = "licenseClass", expectedValue = "B" },
                new { claimName = "isValid" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var req = draft.Actions.First(a => a.Id == 1).CredentialRequirements!.First();
        req.RequiredClaims.Should().HaveCount(2);
        req.RequiredClaims!.First().ClaimName.Should().Be("licenseClass");
    }

    [Fact]
    public async Task ExecuteRequireCredential_ActionNotFound_ReturnsError()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"));

        var args = CreateArgs(new
        {
            actionId = 999,
            credentialType = "DriverLicense"
        });

        // Act
        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteRequireCredential_MultipleRequirements()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddAction(1, a => a.WithTitle("Submit").SentBy("alice"));

        // Act — add two credentials
        await _executor.ExecuteAsync("require_credential",
            CreateArgs(new { actionId = 1, credentialType = "DriverLicense" }), builder);
        var result = await _executor.ExecuteAsync("require_credential",
            CreateArgs(new { actionId = 1, credentialType = "InsuranceCert" }), builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var action = draft.Actions.First(a => a.Id == 1);
        action.CredentialRequirements.Should().HaveCount(2);
        action.CredentialRequirements!.Select(r => r.Type).Should()
            .BeEquivalentTo(new[] { "DriverLicense", "InsuranceCert" });
    }

    #endregion

    #region issue_credential Tests (T028)

    [Fact]
    public async Task ExecuteIssueCredential_ConfiguresIssuanceOnAction()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue License").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "DriverLicense",
            vct = "https://sorcha.dev/vc/driver-license/v1",
            recipientParticipantId = "holder",
            claimMappings = new[]
            {
                new { claimName = "licenseType", sourceField = "/licenseType" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var draft = builder.BuildDraft();
        var action = draft.Actions.First(a => a.Id == 1);
        action.CredentialIssuanceConfig.Should().NotBeNull();
        action.CredentialIssuanceConfig!.CredentialType.Should().Be("DriverLicense");
        action.CredentialIssuanceConfig.RecipientParticipantId.Should().Be("holder");
    }

    [Fact]
    public async Task ExecuteIssueCredential_WithClaimMappings()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "ProductPassport",
            vct = "https://sorcha.dev/vc/product-passport/v1",
            recipientParticipantId = "holder",
            claimMappings = new[]
            {
                new { claimName = "productName", sourceField = "/name" },
                new { claimName = "batchNumber", sourceField = "/batch" },
                new { claimName = "origin", sourceField = "/countryOfOrigin" }
            }
        });

        // Act
        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var config = draft.Actions.First(a => a.Id == 1).CredentialIssuanceConfig!;
        config.ClaimMappings.Should().HaveCount(3);
        var mappings = config.ClaimMappings.ToList();
        mappings[0].ClaimName.Should().Be("productName");
        mappings[0].SourceField.Should().Be("/name");
        mappings[2].ClaimName.Should().Be("origin");
    }

    [Fact]
    public async Task ExecuteIssueCredential_ValidatesRecipientExists()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "License",
            vct = "https://sorcha.dev/vc/license/v1",
            recipientParticipantId = "nonexistent",
            claimMappings = new[] { new { claimName = "type", sourceField = "/type" } }
        });

        // Act
        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent");
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteIssueCredential_WithExpiryAndUsagePolicy()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "Certificate",
            vct = "https://sorcha.dev/vc/certificate/v1",
            recipientParticipantId = "holder",
            expiryDuration = "P365D",
            usagePolicy = "SingleUse",
            claimMappings = new[] { new { claimName = "cert", sourceField = "/cert" } }
        });

        // Act
        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        // Assert
        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var config = draft.Actions.First(a => a.Id == 1).CredentialIssuanceConfig!;
        config.ExpiryDuration.Should().Be("P365D");
        config.UsagePolicy.Should().Be(UsagePolicy.SingleUse);
    }

    [Fact]
    public async Task ExecuteIssueCredential_ActionNotFound_ReturnsError()
    {
        // Arrange
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"));

        var args = CreateArgs(new
        {
            actionId = 999,
            credentialType = "License",
            vct = "https://sorcha.dev/vc/license/v1",
            recipientParticipantId = "issuer",
            claimMappings = new[] { new { claimName = "type", sourceField = "/type" } }
        });

        // Act
        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    #endregion

    #region DPP Integration Test (T032)

    [Fact]
    public async Task DPP_FullLifecycle_IssueAndRequireProductPassport()
    {
        // Arrange — build a 4-participant, 4-action DPP blueprint
        var builder = BlueprintBuilder.Create();

        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Digital Product Passport", description = "A DPP lifecycle workflow" }), builder);

        // Add 4 participants
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "manufacturer", name = "Manufacturer Corp" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "distributor", name = "Distributor Ltd" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "retailer", name = "Retailer Inc" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "consumer", name = "End Consumer" }), builder);

        // Add 4 actions
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new
            {
                id = 0,
                title = "Manufacture Product",
                sender = "manufacturer",
                isStartingAction = true,
                dataFields = new[]
                {
                    new { name = "productName", type = "string" },
                    new { name = "batchNumber", type = "string" },
                    new { name = "countryOfOrigin", type = "string" }
                }
            }), builder);

        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 1, title = "Distribute Product", sender = "distributor" }), builder);

        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 2, title = "Retail Product", sender = "retailer" }), builder);

        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 3, title = "Consumer Verify", sender = "consumer" }), builder);

        // Issue ProductPassport at action 0 (manufacture)
        var issueResult = await _executor.ExecuteAsync("issue_credential",
            CreateArgs(new
            {
                actionId = 0,
                credentialType = "ProductPassport",
                vct = "https://sorcha.dev/vc/product-passport/v1",
                recipientParticipantId = "distributor",
                claimMappings = new[]
                {
                    new { claimName = "productName", sourceField = "/productName" },
                    new { claimName = "batchNumber", sourceField = "/batchNumber" },
                    new { claimName = "origin", sourceField = "/countryOfOrigin" }
                }
            }), builder);
        issueResult.Success.Should().BeTrue();

        // Require ProductPassport at actions 1, 2, 3
        foreach (var actionId in new[] { 1, 2, 3 })
        {
            var reqResult = await _executor.ExecuteAsync("require_credential",
                CreateArgs(new
                {
                    actionId,
                    credentialType = "ProductPassport",
                    acceptedIssuers = new[] { "manufacturer" }
                }), builder);
            reqResult.Success.Should().BeTrue();
        }

        // Act — validate the blueprint
        var validateResult = await _executor.ExecuteAsync("validate_blueprint",
            CreateArgs(new { }), builder);

        // Assert
        validateResult.Success.Should().BeTrue();
        var output = JsonSerializer.Deserialize<ValidationOutput>(
            validateResult.Result!.RootElement.GetRawText());
        output!.isValid.Should().BeTrue();
        output.errors.Should().BeEmpty();

        // Verify the full structure
        var draft = builder.BuildDraft();
        draft.Participants.Should().HaveCount(4);
        draft.Actions.Should().HaveCount(4);
        draft.Actions.First(a => a.Id == 0).CredentialIssuanceConfig.Should().NotBeNull();
        draft.Actions.First(a => a.Id == 0).CredentialIssuanceConfig!.CredentialType.Should().Be("ProductPassport");
        foreach (var actionId in new[] { 1, 2, 3 })
        {
            draft.Actions.First(a => a.Id == actionId).CredentialRequirements.Should().HaveCount(1);
            draft.Actions.First(a => a.Id == actionId).CredentialRequirements!.First().Type
                .Should().Be("ProductPassport");
        }
    }

    #endregion

    #region Escape-hatch tools

    private async Task<BlueprintBuilder> SeedTwoParticipantOneAction()
    {
        var builder = BlueprintBuilder.Create();
        await _executor.ExecuteAsync("create_blueprint",
            CreateArgs(new { title = "Esc Hatch Test", description = "Coverage for escape hatch tools" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "applicant", name = "Applicant" }), builder);
        await _executor.ExecuteAsync("add_participant",
            CreateArgs(new { id = "reviewer", name = "Reviewer" }), builder);
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 0, title = "Submit", sender = "applicant", isStartingAction = true }), builder);
        return builder;
    }

    [Fact]
    public async Task ExecuteAsync_SetActionSchema_ReplacesWithRawJsonSchemaIncludingExtensions()
    {
        var builder = await SeedTwoParticipantOneAction();

        // Schema with x-pages and x-credential-offer — neither expressible via add_action.
        var rawSchema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["credentialOffer"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["x-credential-offer"] = true,
                    ["properties"] = new
                    {
                        credential_offer_uri = new { type = "string", format = "uri" }
                    },
                    ["required"] = new[] { "credential_offer_uri" }
                }
            },
            required = new[] { "credentialOffer" }
        };

        var result = await _executor.ExecuteAsync("set_action_schema",
            CreateArgs(new { actionId = 0, schema = rawSchema }), builder);

        result.Success.Should().BeTrue();
        result.BlueprintChanged.Should().BeTrue();
        var draft = builder.BuildDraft();
        var action = draft.Actions.First(a => a.Id == 0);
        action.DataSchemas.Should().HaveCount(1);
        var doc = action.DataSchemas!.First();
        doc.RootElement.GetProperty("properties").GetProperty("credentialOffer")
            .GetProperty("x-credential-offer").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetActionSchema_AppendModePreservesExisting()
    {
        var builder = await SeedTwoParticipantOneAction();

        await _executor.ExecuteAsync("set_action_schema",
            CreateArgs(new { actionId = 0, schema = new { type = "object", properties = new { a = new { type = "string" } } } }),
            builder);
        var result = await _executor.ExecuteAsync("set_action_schema",
            CreateArgs(new
            {
                actionId = 0,
                mode = "append",
                schema = new { type = "object", properties = new { b = new { type = "integer" } } }
            }),
            builder);

        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        draft.Actions.First(a => a.Id == 0).DataSchemas.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_SetActionSchema_RejectsNonObjectSchema()
    {
        var builder = await SeedTwoParticipantOneAction();

        var result = await _executor.ExecuteAsync("set_action_schema",
            CreateArgs(new { actionId = 0, schema = "not-an-object" }), builder);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("JSON Schema object");
    }

    [Fact]
    public async Task ExecuteAsync_SetActionRoutes_SetsTerminalAndOutputMappingRoutes()
    {
        var builder = await SeedTwoParticipantOneAction();
        // Add an action 1 so routes can target it
        await _executor.ExecuteAsync("add_action",
            CreateArgs(new { id = 1, title = "Claim", sender = "applicant" }), builder);

        var routes = new object[]
        {
            new
            {
                id = "approved-to-claim",
                nextActionIds = new[] { 1 },
                condition = new Dictionary<string, object>
                {
                    ["=="] = new object[]
                    {
                        new Dictionary<string, object> { ["var"] = "decision" },
                        "approved"
                    }
                },
                description = "Approved",
                outputMapping = new Dictionary<string, string>
                {
                    ["/haip/credential_offer_uri"] = "/credentialOffer/credential_offer_uri"
                }
            },
            new
            {
                id = "rejected-terminal",
                nextActionIds = Array.Empty<int>(),
                isDefault = true,
                description = "Rejected — terminal"
            }
        };

        var result = await _executor.ExecuteAsync("set_action_routes",
            CreateArgs(new { actionId = 0, routes }), builder);

        result.Success.Should().BeTrue();
        var draft = builder.BuildDraft();
        var action = draft.Actions.First(a => a.Id == 0);
        action.Routes.Should().HaveCount(2);
        var approval = action.Routes!.First(r => r.Id == "approved-to-claim");
        approval.NextActionIds.Should().BeEquivalentTo(new[] { 1 });
        approval.Condition.Should().NotBeNull();
        approval.OutputMapping.Should().ContainKey("/haip/credential_offer_uri");
        var terminal = action.Routes!.First(r => r.Id == "rejected-terminal");
        terminal.NextActionIds.Should().BeEmpty();
        terminal.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetActionRoutes_RejectsNonArrayInput()
    {
        var builder = await SeedTwoParticipantOneAction();

        var result = await _executor.ExecuteAsync("set_action_routes",
            CreateArgs(new { actionId = 0, routes = "nope" }), builder);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("array");
    }

    [Fact]
    public async Task ExecuteAsync_SetActionMetadata_SetsRejectionConfigAndCredentialIssuanceWithHaip()
    {
        var builder = await SeedTwoParticipantOneAction();

        var result = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new
            {
                actionId = 0,
                isStartingAction = true,
                requiredPriorActions = new[] { 0 },
                rejectionConfig = new
                {
                    targetActionId = 0,
                    isTerminal = true,
                    requireReason = false
                },
                credentialIssuanceConfig = new
                {
                    credentialType = "AssuredIdentityCredential",
                    claimMappings = new[]
                    {
                        new { claimName = "givenName", sourceField = "/givenName" }
                    },
                    recipientParticipantId = "applicant",
                    targetAudience = "HaipExternalWallet"
                }
            }), builder);

        result.Success.Should().BeTrue();
        var action = builder.BuildDraft().Actions.First(a => a.Id == 0);
        action.IsStartingAction.Should().BeTrue();
        action.RequiredPriorActions.Should().BeEquivalentTo(new[] { 0 });
        action.RejectionConfig.Should().NotBeNull();
        action.RejectionConfig!.IsTerminal.Should().BeTrue();
        action.RejectionConfig.RequireReason.Should().BeFalse();
        action.CredentialIssuanceConfig.Should().NotBeNull();
        action.CredentialIssuanceConfig!.TargetAudience.Should().Be(TargetAudience.HaipExternalWallet);
    }

    [Fact]
    public async Task ExecuteAsync_SetActionMetadata_SetsCredentialRequirementWithHaipPresentationSource()
    {
        var builder = await SeedTwoParticipantOneAction();

        var result = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new
            {
                actionId = 0,
                credentialRequirements = new[]
                {
                    new
                    {
                        type = "AssuredIdentityCredential",
                        presentationSource = "HaipExternalWallet",
                        requiredClaims = new[]
                        {
                            new { claimName = "dateOfBirth" }
                        }
                    }
                }
            }), builder);

        result.Success.Should().BeTrue();
        var action = builder.BuildDraft().Actions.First(a => a.Id == 0);
        action.CredentialRequirements.Should().HaveCount(1);
        action.CredentialRequirements!.First().PresentationSource
            .Should().Be(PresentationSource.HaipExternalWallet);
    }

    [Fact]
    public async Task ExecuteAsync_SetActionMetadata_NullClearsExistingConfig()
    {
        var builder = await SeedTwoParticipantOneAction();
        await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new
            {
                actionId = 0,
                rejectionConfig = new { targetActionId = 0, isTerminal = true }
            }), builder);
        builder.BuildDraft().Actions.First(a => a.Id == 0).RejectionConfig.Should().NotBeNull();

        var clear = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new { actionId = 0, rejectionConfig = (object?)null }), builder);

        clear.Success.Should().BeTrue();
        builder.BuildDraft().Actions.First(a => a.Id == 0).RejectionConfig.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SetActionMetadata_RejectsEmptyUpdate()
    {
        var builder = await SeedTwoParticipantOneAction();

        var result = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new { actionId = 0 }), builder);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("at least one");
    }

    [Fact]
    public async Task ExecuteAsync_EscapeHatchTools_FailGracefullyOnUnknownAction()
    {
        var builder = await SeedTwoParticipantOneAction();

        var schemaResult = await _executor.ExecuteAsync("set_action_schema",
            CreateArgs(new { actionId = 999, schema = new { type = "object" } }), builder);
        var routesResult = await _executor.ExecuteAsync("set_action_routes",
            CreateArgs(new { actionId = 999, routes = Array.Empty<object>() }), builder);
        var metaResult = await _executor.ExecuteAsync("set_action_metadata",
            CreateArgs(new { actionId = 999, isStartingAction = true }), builder);

        schemaResult.Success.Should().BeFalse();
        routesResult.Success.Should().BeFalse();
        metaResult.Success.Should().BeFalse();
        schemaResult.Error.Should().Contain("999");
        routesResult.Error.Should().Contain("999");
        metaResult.Error.Should().Contain("999");
    }

    #endregion

    #region Helpers

    private static JsonDocument CreateArgs(object args)
    {
        var json = JsonSerializer.Serialize(args);
        return JsonDocument.Parse(json);
    }

    private static SchemaIndexEntryDto CreateSchemaIndexEntry(
        string shortCode, string title, string category,
        int fieldCount = 0, string[]? fieldNames = null)
    {
        return new SchemaIndexEntryDto(
            ShortCode: shortCode,
            SourceProvider: "internal",
            SourceUri: $"urn:sorcha:schema:{shortCode}",
            Title: title,
            Description: null,
            SectorTags: new[] { category },
            FieldCount: fieldCount,
            RequiredFieldCount: 0,
            SchemaVersion: "1.0.0",
            Status: "Active",
            LastFetchedAt: DateTimeOffset.UtcNow,
            FieldNames: fieldNames);
    }

    private record ValidationOutput(bool isValid, ValidationError[] errors, ValidationWarning[] warnings);
    private record ValidationError(string code, string message, string? location);
    private record ValidationWarning(string code, string message, string? location);

    #endregion

    // -------------------------------------------------------------------------
    // Capabilities the AI author could not express until now. Each of these is a
    // property the platform has supported for weeks while the tool layer had no
    // parameter for it, so every AI-authored blueprint silently omitted it.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteIssueCredential_WithVct_SetsCanonicalTypeIdentifier()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "TrainingCompletion",
            vct = "https://sorcha.dev/vc/training-completion/v1",
            displayName = "Training Completion",
            recipientParticipantId = "holder",
            claimMappings = new[] { new { claimName = "course", sourceField = "/course" } }
        });

        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        result.Success.Should().BeTrue();
        var cfg = builder.BuildDraft().Actions.First(a => a.Id == 1).CredentialIssuanceConfig!;
        cfg.Vct.Should().Be("https://sorcha.dev/vc/training-completion/v1",
            "vct is the credential's SOLE type claim under SD-JWT VC — without it no conforming verifier can type-match it");
        cfg.DisplayName.Should().Be("Training Completion");
    }

    [Fact]
    public async Task ExecuteIssueCredential_RelativeVct_IsRejected()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        // A bare name is exactly the unmatchable shape the vct parameter exists to prevent.
        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "TrainingCompletion",
            vct = "TrainingCompletion",
            recipientParticipantId = "holder",
            claimMappings = new[] { new { claimName = "course", sourceField = "/course" } }
        });

        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("absolute URI");
    }

    [Fact]
    public async Task ExecuteIssueCredential_WithIssuanceCondition_GatesMinting()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("reviewer", p => p.Named("Reviewer"))
            .AddParticipant("applicant", p => p.Named("Applicant"))
            .AddAction(1, a => a.WithTitle("Decide").SentBy("reviewer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "ApprovalAttestation",
            vct = "https://sorcha.dev/vc/approval/v1",
            recipientParticipantId = "applicant",
            claimMappings = new[] { new { claimName = "outcome", sourceField = "/decision" } },
            issuanceCondition = new Dictionary<string, object>
            {
                ["=="] = new object[] { new Dictionary<string, object> { ["var"] = "decision" }, "approved" }
            }
        });

        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        result.Success.Should().BeTrue();
        var cfg = builder.BuildDraft().Actions.First(a => a.Id == 1).CredentialIssuanceConfig!;
        cfg.IssuanceCondition.Should().NotBeNull(
            "without a condition an approve/reject action issues the credential even on rejection (Feature 176)");
        cfg.IssuanceCondition!.ToJsonString().Should().Contain("decision").And.Contain("approved");
    }

    [Fact]
    public async Task ExecuteIssueCredential_WithoutIssuanceCondition_LeavesItNull()
    {
        // The default must stay "always issue" — Feature 176 made the condition OPTIONAL so that
        // existing blueprints keep their behaviour. Defaulting it to anything else would silently
        // stop credentials being issued.
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("issuer", p => p.Named("Issuer"))
            .AddParticipant("holder", p => p.Named("Holder"))
            .AddAction(1, a => a.WithTitle("Issue").SentBy("issuer"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "Cert",
            vct = "https://sorcha.dev/vc/cert/v1",
            recipientParticipantId = "holder",
            claimMappings = new[] { new { claimName = "a", sourceField = "/a" } }
        });

        var result = await _executor.ExecuteAsync("issue_credential", args, builder);

        result.Success.Should().BeTrue();
        builder.BuildDraft().Actions.First(a => a.Id == 1)
            .CredentialIssuanceConfig!.IssuanceCondition.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteRequireCredential_WithAnyOfGroup_MarksRequirementAsAlternative()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("citizen", p => p.Named("Citizen"))
            .AddAction(1, a => a.WithTitle("Prove identity").SentBy("citizen"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "Passport",
            anyOfGroup = "identity-document",
            acceptedIssuers = Array.Empty<string>()
        });

        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        result.Success.Should().BeTrue();
        var req = builder.BuildDraft().Actions.First(a => a.Id == 1).CredentialRequirements!.First();
        req.AnyOfGroup.Should().Be("identity-document",
            "requirements sharing a tag are alternatives — one of them satisfies the action");
    }

    [Fact]
    public async Task ExecuteRequireCredential_WithoutAnyOfGroup_StaysIndependentlyRequired()
    {
        var builder = BlueprintBuilder.Create()
            .WithTitle("Test")
            .AddParticipant("citizen", p => p.Named("Citizen"))
            .AddAction(1, a => a.WithTitle("Prove identity").SentBy("citizen"));

        var args = CreateArgs(new
        {
            actionId = 1,
            credentialType = "Passport",
            acceptedIssuers = Array.Empty<string>()
        });

        var result = await _executor.ExecuteAsync("require_credential", args, builder);

        result.Success.Should().BeTrue();
        builder.BuildDraft().Actions.First(a => a.Id == 1)
            .CredentialRequirements!.First().AnyOfGroup.Should().BeNull();
    }

    [Fact]
    public void IssueCredentialTool_AdvertisesVctAndIssuanceCondition()
    {
        // The model can only use what the tool schema advertises. These two carry correctness
        // consequences (an unmatchable credential; issuing on rejection), so their absence from
        // the schema is the whole defect — not the prompt wording.
        var tool = _executor.GetToolDefinitions().First(t => t.Name == "issue_credential");
        var json = System.Text.Json.JsonSerializer.Serialize(tool.InputSchema);

        json.Should().Contain("vct");
        json.Should().Contain("issuanceCondition");
        json.Should().Contain("displayName");
    }

    [Fact]
    public void RequireCredentialTool_AdvertisesAnyOfGroup()
    {
        var tool = _executor.GetToolDefinitions().First(t => t.Name == "require_credential");
        System.Text.Json.JsonSerializer.Serialize(tool.InputSchema).Should().Contain("anyOfGroup");
    }
}
