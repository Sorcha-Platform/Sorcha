// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services.Admin;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for OperationStatusService (get encryption operation status).
/// </summary>
public class OperationStatusServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly OperationStatusService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public OperationStatusServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<OperationStatusService>>();
        _service = new OperationStatusService(_httpClient, logger.Object);
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpRequestMessage? _capturedRequest;

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
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _capturedRequest = req)
            .ReturnsAsync(response);
    }

    private void SetupNetworkError()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
    }

    [Fact]
    public async Task GetStatusAsync_Success_ReturnsOperation()
    {
        var expected = new EncryptionOperationViewModel
        {
            OperationId = "op-123",
            Stage = "encrypting-per-recipient",
            PercentComplete = 50,
            RecipientCount = 10,
            ProcessedRecipients = 5
        };
        SetupResponse(HttpStatusCode.OK, expected);

        var result = await _service.GetStatusAsync("op-123");

        result.Should().NotBeNull();
        result!.OperationId.Should().Be("op-123");
        result.Stage.Should().Be("encrypting-per-recipient");
        result.PercentComplete.Should().Be(50);
        result.RecipientCount.Should().Be(10);
        result.ProcessedRecipients.Should().Be(5);
    }

    [Fact]
    public async Task GetStatusAsync_NotFound_ReturnsNull()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.GetStatusAsync("op-missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_NetworkError_ReturnsNull()
    {
        SetupNetworkError();

        var result = await _service.GetStatusAsync("op-123");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_CompletedOperation_IsCompleteTrue()
    {
        var expected = new EncryptionOperationViewModel
        {
            OperationId = "op-done",
            Stage = "complete",
            PercentComplete = 100,
            RecipientCount = 5,
            ProcessedRecipients = 5
        };
        SetupResponse(HttpStatusCode.OK, expected);

        var result = await _service.GetStatusAsync("op-done");

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ProcessingOperation_IsCompleteFalse()
    {
        var expected = new EncryptionOperationViewModel
        {
            OperationId = "op-wip",
            Stage = "encrypting-per-recipient",
            PercentComplete = 30,
            RecipientCount = 10,
            ProcessedRecipients = 3
        };
        SetupResponse(HttpStatusCode.OK, expected);

        var result = await _service.GetStatusAsync("op-wip");

        result.Should().NotBeNull();
        result!.IsComplete.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_ServerError_ReturnsNull()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.GetStatusAsync("op-123");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListOperationsAsync_ValidResponse_ReturnsMappedPage()
    {
        var page = new OperationHistoryPage
        {
            Items =
            [
                new OperationHistoryItem
                {
                    OperationId = "op-1",
                    Status = "completed",
                    BlueprintId = "bp-001",
                    ActionTitle = "Submit",
                    TransactionHash = "abc123"
                },
                new OperationHistoryItem
                {
                    OperationId = "op-2",
                    Status = "failed",
                    BlueprintId = "bp-002",
                    ActionTitle = "Review",
                    ErrorMessage = "Key not found"
                }
            ],
            Page = 1,
            PageSize = 10,
            TotalCount = 2,
            HasMore = false
        };
        SetupResponse(HttpStatusCode.OK, page);

        var result = await _service.ListOperationsAsync("did:sorcha:w:wallet1");

        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(2);
        result.HasMore.Should().BeFalse();
        result.Items[0].OperationId.Should().Be("op-1");
        result.Items[0].Status.Should().Be("completed");
        result.Items[1].ErrorMessage.Should().Be("Key not found");
    }

    [Fact]
    public async Task ListOperationsAsync_EmptyResults_ReturnsEmptyPage()
    {
        var page = new OperationHistoryPage
        {
            Items = [],
            Page = 1,
            PageSize = 10,
            TotalCount = 0,
            HasMore = false
        };
        SetupResponse(HttpStatusCode.OK, page);

        var result = await _service.ListOperationsAsync("did:sorcha:w:wallet1");

        result.Should().NotBeNull();
        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListOperationsAsync_ServerError_ReturnsNull()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.ListOperationsAsync("did:sorcha:w:wallet1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListOperationsAsync_DefaultPagination_UsesCorrectUrl()
    {
        var page = new OperationHistoryPage { Items = [], Page = 1, PageSize = 10, TotalCount = 0 };
        SetupResponse(HttpStatusCode.OK, page);

        await _service.ListOperationsAsync("did:sorcha:w:wallet1");

        _capturedRequest.Should().NotBeNull();
        var url = _capturedRequest!.RequestUri!.ToString();
        url.Should().Contain("page=1");
        url.Should().Contain("pageSize=10");
        url.Should().Contain("wallet=did%3Asorcha%3Aw%3Awallet1");
    }

    [Fact]
    public async Task ListOperationsAsync_CustomPagination_UsesCorrectUrl()
    {
        var page = new OperationHistoryPage { Items = [], Page = 3, PageSize = 25, TotalCount = 0 };
        SetupResponse(HttpStatusCode.OK, page);

        await _service.ListOperationsAsync("did:sorcha:w:wallet1", page: 3, pageSize: 25);

        _capturedRequest.Should().NotBeNull();
        var url = _capturedRequest!.RequestUri!.ToString();
        url.Should().Contain("page=3");
        url.Should().Contain("pageSize=25");
    }
}
