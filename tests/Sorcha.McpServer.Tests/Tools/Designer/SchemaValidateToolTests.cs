// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;

namespace Sorcha.McpServer.Tests.Tools.Designer;

public class SchemaValidateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<ILogger<SchemaValidateTool>> _loggerMock;

    public SchemaValidateToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _loggerMock = new Mock<ILogger<SchemaValidateTool>>();
    }

    private SchemaValidateTool CreateTool()
    {
        return new SchemaValidateTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateSchemaAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateSchemaAsync("{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ValidateSchemaAsync_EmptySchema_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateSchemaAsync("");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Schema JSON is required");
    }

    [Fact]
    public async Task ValidateSchemaAsync_InvalidJson_ReturnsInvalidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateSchemaAsync("{ invalid }");

        // Assert
        result.Status.Should().Be("Invalid");
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("Invalid JSON");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ValidSchema_ReturnsValidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        var schema = """
        {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "title": "Test Schema",
            "description": "A test schema",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
            },
            "required": ["name"]
        }
        """;

        // Act
        var result = await tool.ValidateSchemaAsync(schema);

        // Assert
        result.Status.Should().Be("Valid");
        result.IsValid.Should().BeTrue();
        result.Message.Should().Contain("valid");
        result.SchemaInfo.Should().NotBeNull();
        result.SchemaInfo!.Type.Should().Be("object");
        result.SchemaInfo.Title.Should().Be("Test Schema");
        result.SchemaInfo.Description.Should().Be("A test schema");
        result.SchemaInfo.PropertyCount.Should().Be(2);
        result.SchemaInfo.PropertyNames.Should().Contain("name");
        result.SchemaInfo.PropertyNames.Should().Contain("age");
        result.SchemaInfo.RequiredFields.Should().Contain("name");
    }

    [Fact]
    public async Task ValidateSchemaAsync_SchemaWithDefinitions_ReportsDefinitions()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        var schema = """
        {
            "type": "object",
            "properties": {
                "address": { "$ref": "#/$defs/Address" }
            },
            "$defs": {
                "Address": {
                    "type": "object",
                    "properties": {
                        "street": { "type": "string" },
                        "city": { "type": "string" }
                    }
                }
            }
        }
        """;

        // Act
        var result = await tool.ValidateSchemaAsync(schema);

        // Assert
        result.Status.Should().Be("Valid");
        result.SchemaInfo.Should().NotBeNull();
        result.SchemaInfo!.HasDefinitions.Should().BeTrue();
        result.SchemaInfo.DefinitionCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MinimalSchema_ReturnsValidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        // A minimal valid JSON Schema is just an empty object
        var schema = "{}";

        // Act
        var result = await tool.ValidateSchemaAsync(schema);

        // Assert
        result.Status.Should().Be("Valid");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ArraySchema_ReturnsValidResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        var schema = """
        {
            "type": "array",
            "items": {
                "type": "string"
            }
        }
        """;

        // Act
        var result = await tool.ValidateSchemaAsync(schema);

        // Assert
        result.Status.Should().Be("Valid");
        result.SchemaInfo.Should().NotBeNull();
        result.SchemaInfo!.Type.Should().Be("array");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_validate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.ValidateSchemaAsync("{}");

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
