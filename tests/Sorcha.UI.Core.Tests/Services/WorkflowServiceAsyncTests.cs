// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Workflows;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for WorkflowService async response mapping (Feature 052).
/// </summary>
public class WorkflowServiceAsyncTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly WorkflowService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public WorkflowServiceAsyncTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<WorkflowService>>();
        _service = new WorkflowService(_httpClient, logger.Object);
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetupResponse(HttpStatusCode statusCode, object? content = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent("")
        };
        _responses.Add(response);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private static ActionExecuteRequest CreateRequest() => new()
    {
        BlueprintId = "bp-001",
        ActionId = "1",
        InstanceId = "inst-001",
        SenderWallet = "did:sorcha:w:abc123",
        RegisterAddress = "reg-001",
        PayloadData = new Dictionary<string, object> { ["field1"] = "value1" }
    };

    [Fact]
    public async Task SubmitActionExecuteAsync_AsyncResponse_MapsOperationIdAndIsAsync()
    {
        SetupResponse(HttpStatusCode.Accepted, new
        {
            transactionId = "",
            instanceId = "inst-001",
            operationId = "abc123def456",
            isAsync = true,
            isComplete = false,
            nextActions = Array.Empty<object>()
        });

        var result = await _service.SubmitActionExecuteAsync(CreateRequest());

        result.Should().NotBeNull();
        result!.IsAsync.Should().BeTrue();
        result.OperationId.Should().Be("abc123def456");
        result.HasAsyncOperation.Should().BeTrue();
        result.TransactionId.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitActionExecuteAsync_SyncResponse_NoOperationId()
    {
        SetupResponse(HttpStatusCode.OK, new
        {
            transactionId = "a1b2c3d4e5f6",
            instanceId = "inst-001",
            operationId = (string?)null,
            isAsync = false,
            isComplete = false,
            nextActions = new[] { new { actionId = 2, actionTitle = "Review", participantId = "reviewer-01" } }
        });

        var result = await _service.SubmitActionExecuteAsync(CreateRequest());

        result.Should().NotBeNull();
        result!.IsAsync.Should().BeFalse();
        result.OperationId.Should().BeNull();
        result.HasAsyncOperation.Should().BeFalse();
        result.TransactionId.Should().Be("a1b2c3d4e5f6");
    }

    [Fact]
    public async Task SubmitActionExecuteAsync_ServerError_ReturnsNull()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.SubmitActionExecuteAsync(CreateRequest());

        result.Should().BeNull();
    }
}
