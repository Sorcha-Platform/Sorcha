// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;

namespace Sorcha.McpServer.Tests.Tools.Designer;

public class SchemaGenerateToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<ILogger<SchemaGenerateTool>> _loggerMock;

    public SchemaGenerateToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _loggerMock = new Mock<ILogger<SchemaGenerateTool>>();
    }

    private SchemaGenerateTool CreateTool()
    {
        return new SchemaGenerateTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GenerateSchemaAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.GenerateSchemaAsync("{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task GenerateSchemaAsync_EmptySample_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.GenerateSchemaAsync("");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Sample JSON is required");
    }

    [Fact]
    public async Task GenerateSchemaAsync_InvalidJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.GenerateSchemaAsync("{ invalid }");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task GenerateSchemaAsync_SimpleObject_GeneratesObjectSchema()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"name": "John", "age": 30}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().NotBeNullOrEmpty();
        result.Schema.Should().Contain("\"type\": \"object\"");
        result.Schema.Should().Contain("\"name\"");
        result.Schema.Should().Contain("\"age\"");
        result.Schema.Should().Contain("\"string\"");
        result.Schema.Should().Contain("\"integer\"");
        result.PropertyCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerateSchemaAsync_WithMakeRequired_AddsRequiredArray()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"name": "John", "email": "john@example.com"}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample, makeRequired: true);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"required\"");
        result.Schema.Should().Contain("\"name\"");
        result.Schema.Should().Contain("\"email\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_NestedObject_GeneratesNestedSchema()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"person": {"name": "John", "age": 30}}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"person\"");
        result.Schema.Should().Contain("\"name\"");
        result.Schema.Should().Contain("\"age\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_Array_GeneratesArraySchema()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """["apple", "banana", "cherry"]""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"type\": \"array\"");
        result.Schema.Should().Contain("\"items\"");
        result.Schema.Should().Contain("\"string\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_ArrayOfObjects_GeneratesItemSchema()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """[{"id": 1, "name": "Item 1"}, {"id": 2, "name": "Item 2"}]""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"type\": \"array\"");
        result.Schema.Should().Contain("\"items\"");
        result.Schema.Should().Contain("\"id\"");
        result.Schema.Should().Contain("\"name\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_BooleanValue_GeneratesBooleanType()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"active": true, "verified": false}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"boolean\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_NumberValue_GeneratesNumberType()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"price": 19.99, "quantity": 5}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"number\"");
        result.Schema.Should().Contain("\"integer\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_DateTimeString_DetectsDateTimeFormat()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"createdAt": "2025-01-15T10:30:00Z"}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"format\": \"date-time\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_EmailString_DetectsEmailFormat()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"email": "test@example.com"}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"format\": \"email\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_UriString_DetectsUriFormat()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        var sample = """{"website": "https://example.com"}""";

        // Act
        var result = await tool.GenerateSchemaAsync(sample);

        // Assert
        result.Status.Should().Be("Success");
        result.Schema.Should().Contain("\"format\": \"uri\"");
    }

    [Fact]
    public async Task GenerateSchemaAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_schema_generate")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.GenerateSchemaAsync("{}");

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
