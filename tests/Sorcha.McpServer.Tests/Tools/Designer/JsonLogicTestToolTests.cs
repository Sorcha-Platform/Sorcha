// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;

namespace Sorcha.McpServer.Tests.Tools.Designer;

public class JsonLogicTestToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock;
    private readonly Mock<IMcpErrorHandler> _errorHandlerMock;
    private readonly Mock<ILogger<JsonLogicTestTool>> _loggerMock;

    public JsonLogicTestToolTests()
    {
        _authServiceMock = new Mock<IMcpAuthorizationService>();
        _errorHandlerMock = new Mock<IMcpErrorHandler>();
        _loggerMock = new Mock<ILogger<JsonLogicTestTool>>();
    }

    private JsonLogicTestTool CreateTool()
    {
        return new JsonLogicTestTool(
            _authServiceMock.Object,
            _errorHandlerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task TestJsonLogicAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(false);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("{}", "{}");

        // Assert
        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
    }

    [Fact]
    public async Task TestJsonLogicAsync_EmptyRule_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Rule JSON is required");
    }

    [Fact]
    public async Task TestJsonLogicAsync_EmptyData_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("{}", "");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Data JSON is required");
    }

    [Fact]
    public async Task TestJsonLogicAsync_InvalidRuleJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("{ invalid }", "{}");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid rule JSON");
    }

    [Fact]
    public async Task TestJsonLogicAsync_InvalidDataJson_ReturnsErrorResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("{}", "{ invalid }");

        // Assert
        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid data JSON");
    }

    [Fact]
    public async Task TestJsonLogicAsync_EqualityCheck_ReturnsBooleanResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        var rule = """{"==": [{"var": "status"}, "approved"]}""";
        var data = """{"status": "approved"}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("true");
        result.ResultType.Should().Be("boolean");
        result.IsTruthy.Should().BeTrue();
    }

    [Fact]
    public async Task TestJsonLogicAsync_ComparisonCheck_ReturnsBooleanResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        var rule = """{">": [{"var": "age"}, 18]}""";
        var data = """{"age": 25}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("true");
        result.IsTruthy.Should().BeTrue();
    }

    [Fact]
    public async Task TestJsonLogicAsync_FalseCondition_ReturnsFalseResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        var rule = """{">": [{"var": "age"}, 18]}""";
        var data = """{"age": 15}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("false");
        result.IsTruthy.Should().BeFalse();
    }

    [Fact]
    public async Task TestJsonLogicAsync_ArithmeticOperation_ReturnsNumberResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        var rule = """{"+": [{"var": "a"}, {"var": "b"}]}""";
        var data = """{"a": 10, "b": 5}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("15");
        result.ResultType.Should().Be("number");
    }

    [Fact]
    public async Task TestJsonLogicAsync_IfThenElse_ReturnsCorrectBranch()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        var rule = """{"if": [{">=": [{"var": "score"}, 60]}, "pass", "fail"]}""";
        var data = """{"score": 75}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("pass");
        result.ResultType.Should().Be("string");
    }

    [Fact]
    public async Task TestJsonLogicAsync_CatOperation_ReturnsConcatenatedString()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Test cat operation - concatenate strings
        var rule = """{"cat": [{"var": "first"}, " ", {"var": "last"}]}""";
        var data = """{"first": "John", "last": "Doe"}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("John Doe");
    }

    [Fact]
    public async Task TestJsonLogicAsync_MultipleComparisons_ReturnsResult()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Test chained comparison: 18 <= age <= 65
        var rule = """{"<=": [18, {"var": "age"}, 65]}""";
        var data = """{"age": 25}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("true");
        result.IsTruthy.Should().BeTrue();
    }

    [Fact]
    public async Task TestJsonLogicAsync_VarAccess_ReturnsValue()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Nested object access
        var rule = """{"var": ["user", "name"]}""";
        var data = """{"user": {"name": "John Doe"}}""";

        // Act
        var result = await tool.TestJsonLogicAsync(rule, data);

        // Assert
        result.Status.Should().Be("Success");
        result.Result.Should().Contain("John Doe");
    }

    [Fact]
    public async Task TestJsonLogicAsync_ResponseTimeIsRecorded()
    {
        // Arrange
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_jsonlogic_test")).Returns(true);
        var tool = CreateTool();

        // Act
        var result = await tool.TestJsonLogicAsync("true", "{}");

        // Assert
        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
